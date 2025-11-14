using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Diagnostics;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Activity functions for multi-turn test case execution
/// Each activity can run for extended periods without timeout
/// </summary>
public class MultiTurnDurableActivities
{
    private readonly ILogger<MultiTurnDurableActivities> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly ILLMJudgeServiceFactory _llmJudgeServiceFactory;

    public MultiTurnDurableActivities(
        ILogger<MultiTurnDurableActivities> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        ILLMJudgeServiceFactory llmJudgeServiceFactory)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _llmJudgeServiceFactory = llmJudgeServiceFactory;
    }

    /// <summary>
    /// Gets a test case with all its turns from Dataverse
    /// </summary>
    [Function("GetTestCaseWithTurns")]
    public async Task<TestCase?> GetTestCaseWithTurns(
        [ActivityTrigger] Guid testCaseId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting test case {TestCaseId} with turns", testCaseId);

        try
        {
            var testCase = await _dataverseService.GetTestCaseAsync(testCaseId, cancellationToken);
            if (testCase == null)
            {
                _logger.LogWarning("Test case {TestCaseId} not found", testCaseId);
                return null;
            }

            // Load turns
            testCase.Turns = await _dataverseService.GetTestCaseTurnsAsync(testCaseId, cancellationToken);
            
            _logger.LogInformation("Retrieved test case {TestCaseId} with {TurnCount} turns",
                testCaseId, testCase.Turns?.Count ?? 0);

            return testCase;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting test case {TestCaseId}", testCaseId);
            throw;
        }
    }

    /// <summary>
    /// Executes a single turn: sends message to agent, evaluates with LLM, saves result
    /// Can take many minutes without timeout
    /// </summary>
    [Function("ExecuteSingleTurn")]
    public async Task<List<TurnExecutionResult>> ExecuteSingleTurn(
        [ActivityTrigger] ExecuteTurnInput input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<TurnExecutionResult>();
        
        _logger.LogInformation("Executing turn {Sequence} for test case {TestCaseId} with {EvaluatorCount} evaluators",
            input.Turn.Sequence, input.Turn.TestCaseId, input.EvaluatorTypeIds.Count);

        try
        {
            // Check which evaluators are applicable (NA check)
            var applicableEvaluators = new List<Guid>();
            
            foreach (var evaluatorTypeId in input.EvaluatorTypeIds)
            {
                var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId, cancellationToken);
                
                if (evaluatorType != null && evaluatorType.RequiresExpectedResponse && 
                    string.IsNullOrWhiteSpace(input.Turn.ExpectedResponse))
                {
                    _logger.LogInformation("Turn {Sequence} marked NA for evaluator {EvaluatorId} (missing expected response)",
                        input.Turn.Sequence, evaluatorTypeId);
                    
                    // Create NA result immediately
                    var naResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = input.TestRunId,
                        TestCaseId = input.Turn.TestCaseId,
                        TestCaseTurnId = input.Turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        ConversationId = input.ConversationId,
                        ActualResponse = null,
                        Score = 0.0m,
                        Passed = false,
                        Reasoning = $"Evaluator '{evaluatorType.Name}' requires expected response but turn {input.Turn.Sequence} has none provided",
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        IsAggregate = false,
                        StatusCode = EvaluationResultStatus.NotApplicable
                    };

                    var naResultId = await _dataverseService.CreateEvaluationResultAsync(naResultRequest, cancellationToken);
                    
                    // Increment completion counter
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        input.TestRunId, 
                        failed: false, 
                        cancellationToken);

                    results.Add(new TurnExecutionResult
                    {
                        ConversationId = input.ConversationId ?? string.Empty,
                        DirectLineToken = input.DirectLineToken,
                        Success = true,
                        Score = 0.0m,
                        Passed = false,
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        EvaluationResultId = naResultId,
                        StatusCode = EvaluationResultStatus.NotApplicable
                    });
                }
                else
                {
                    applicableEvaluators.Add(evaluatorTypeId);
                }
            }
            
            // If all evaluators are NA, return early
            if (applicableEvaluators.Count == 0)
            {
                _logger.LogInformation("All evaluators marked NA for turn {Sequence}", input.Turn.Sequence);
                return results;
            }
            
            // 1. Send message to agent ONCE (can take minutes)
            _logger.LogInformation("Sending message to Copilot Studio for turn {Sequence}", input.Turn.Sequence);
            
            var copilotResponse = await _copilotStudioService.SendMessageAsync(
                input.DirectLineUrl,
                input.Turn.PromptText,
                input.ConversationId,
                input.DirectLineToken,
                cancellationToken);

            if (!copilotResponse.Success)
            {
                _logger.LogError("Copilot Studio error for turn {Sequence}: {Error}",
                    input.Turn.Sequence, copilotResponse.ErrorMessage);
                
                return CreateFailureResults(input, applicableEvaluators, copilotResponse.ErrorMessage ?? "Copilot Studio request failed", stopwatch);
            }

            _logger.LogInformation("Received response from Copilot Studio for turn {Sequence} in {Ms}ms",
                input.Turn.Sequence, copilotResponse.ExecutionTimeMs);

            // 2. Evaluate with ALL evaluators using the same Copilot response CONCURRENTLY
            _logger.LogInformation("Evaluating turn {Sequence} response with {EvaluatorCount} evaluators (concurrent)", 
                input.Turn.Sequence, applicableEvaluators.Count);
            
            // Create evaluation tasks for all applicable evaluators
            var evaluationTasks = applicableEvaluators.Select(async evaluatorTypeId =>
            {
                try
                {
                    var evaluatorStopwatch = Stopwatch.StartNew();
                    
                    // Use factory to get the appropriate judge service
                    var llmJudgeService = await _llmJudgeServiceFactory.CreateJudgeServiceAsync(
                        evaluatorTypeId, cancellationToken);
                    
                    var judgeResponse = await llmJudgeService.EvaluateResponseAsync(
                        new LLMJudgeRequest
                        {
                            EvaluatorTypeId = evaluatorTypeId,
                            UserInput = input.Turn.PromptText,
                            ExpectedResponse = input.Turn.ExpectedResponse ?? string.Empty,
                            ActualResponse = copilotResponse.Response,
                            ConversationId = copilotResponse.ConversationId
                        },
                        cancellationToken);

                    if (!judgeResponse.Success)
                    {
                        _logger.LogError("LLM Judge error for turn {Sequence} with evaluator {EvaluatorId}: {Error}",
                            input.Turn.Sequence, evaluatorTypeId, judgeResponse.ErrorMessage);
                        
                        // Create error result for this evaluator
                        var errorResultRequest = new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = input.TestRunId,
                            TestCaseId = input.Turn.TestCaseId,
                            TestCaseTurnId = input.Turn.Id,
                            EvaluatorTypeId = evaluatorTypeId,
                            ConversationId = copilotResponse.ConversationId,
                            ActualResponse = copilotResponse.Response,
                            Score = null,
                            ErrorMessage = judgeResponse.ErrorMessage,
                            ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                            Metadata = copilotResponse.RawActivityJson,
                            IsAggregate = false,
                            StatusCode = EvaluationResultStatus.Errored
                        };

                        var errorResultId = await _dataverseService.CreateEvaluationResultAsync(errorResultRequest, cancellationToken);
                        
                        await _dataverseService.IncrementCompletedTestCasesAsync(
                            input.TestRunId, 
                            failed: true, 
                            cancellationToken);

                        return new TurnExecutionResult
                        {
                            ConversationId = copilotResponse.ConversationId,
                            DirectLineToken = copilotResponse.DirectLineToken,
                            Success = false,
                            Score = 0,
                            Passed = false,
                            ErrorMessage = judgeResponse.ErrorMessage,
                            ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                            EvaluationResultId = errorResultId,
                            StatusCode = EvaluationResultStatus.Errored
                        };
                    }

                    evaluatorStopwatch.Stop();
                    
                    _logger.LogInformation("LLM Judge evaluation for turn {Sequence} with evaluator {EvaluatorId}: Score={Score}, Passed={Passed}",
                        input.Turn.Sequence, evaluatorTypeId, judgeResponse.Score, judgeResponse.Score >= input.PassingThreshold);

                    // 3. Save result to Dataverse immediately
                    var createResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = input.TestRunId,
                        TestCaseId = input.Turn.TestCaseId,
                        TestCaseTurnId = input.Turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        ConversationId = copilotResponse.ConversationId,
                        ActualResponse = copilotResponse.Response,
                        Score = judgeResponse.Score,
                        Passed = judgeResponse.Score >= input.PassingThreshold,
                        Reasoning = judgeResponse.Reasoning,
                        ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                        Metadata = copilotResponse.RawActivityJson,
                        IsAggregate = false,
                        StatusCode = EvaluationResultStatus.Completed
                    };

                    var resultId = await _dataverseService.CreateEvaluationResultAsync(createResultRequest, cancellationToken);

                    // Increment completion counter
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        input.TestRunId, 
                        failed: false, 
                        cancellationToken);

                    return new TurnExecutionResult
                    {
                        ConversationId = copilotResponse.ConversationId,
                        DirectLineToken = copilotResponse.DirectLineToken,
                        Success = true,
                        Score = judgeResponse.Score,
                        Passed = judgeResponse.Score >= input.PassingThreshold,
                        ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                        EvaluationResultId = resultId,
                        StatusCode = EvaluationResultStatus.Completed
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error evaluating turn {Sequence} with evaluator {EvaluatorId}",
                        input.Turn.Sequence, evaluatorTypeId);
                    
                    // Create error result
                    var errorResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = input.TestRunId,
                        TestCaseId = input.Turn.TestCaseId,
                        TestCaseTurnId = input.Turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        ConversationId = copilotResponse.ConversationId,
                        ActualResponse = copilotResponse.Response,
                        Score = null,
                        ErrorMessage = $"Unexpected error: {ex.Message}",
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        Metadata = copilotResponse.RawActivityJson,
                        IsAggregate = false,
                        StatusCode = EvaluationResultStatus.Errored
                    };

                    var errorResultId = await _dataverseService.CreateEvaluationResultAsync(errorResultRequest, cancellationToken);
                    
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        input.TestRunId, 
                        failed: true, 
                        cancellationToken);

                    return new TurnExecutionResult
                    {
                        ConversationId = copilotResponse.ConversationId,
                        DirectLineToken = copilotResponse.DirectLineToken,
                        Success = false,
                        Score = 0,
                        Passed = false,
                        ErrorMessage = ex.Message,
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        EvaluationResultId = errorResultId,
                        StatusCode = EvaluationResultStatus.Errored
                    };
                }
            }).ToList();

            // Wait for all evaluations to complete and collect results
            var evaluationResults = await Task.WhenAll(evaluationTasks);
            results.AddRange(evaluationResults);

            stopwatch.Stop();
            _logger.LogInformation("Successfully executed turn {Sequence} for test case {TestCaseId} with {EvaluatorCount} evaluators",
                input.Turn.Sequence, input.Turn.TestCaseId, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error executing turn {Sequence} for test case {TestCaseId}",
                input.Turn.Sequence, input.Turn.TestCaseId);
            
            return CreateFailureResults(input, input.EvaluatorTypeIds, $"Unexpected error: {ex.Message}", stopwatch);
        }
    }

    /// <summary>
    /// Updates the aggregate result placeholder for the entire multi-turn test case
    /// </summary>
    [Function("CreateAggregateResult")]
    public async Task CreateAggregateResult(
        [ActivityTrigger] CreateAggregateResultInput input,
        CancellationToken cancellationToken)
    {
        var context = input.Context;
        
        _logger.LogInformation("Updating aggregate result {AggregateResultId} for test case {TestCaseId} - Pass Rate: {PassRate:P0}",
            context.AggregateResultId, context.TestCase.Id, context.PassRate);

        try
        {
            // Filter out NA results (statuscode 778760004) from aggregate calculations
            var validResults = context.TurnResults.Values
                .Where(r => r.StatusCode != EvaluationResultStatus.NotApplicable)
                .ToList();
            
            var naResults = context.TurnResults.Values
                .Where(r => r.StatusCode == EvaluationResultStatus.NotApplicable)
                .ToList();

            int naCount = naResults.Count;
            int validCount = validResults.Count;
            int totalTurns = context.TotalTurns;

            _logger.LogInformation("Aggregate calculation: {ValidCount} valid results, {NACount} NA results out of {TotalTurns} total turns",
                validCount, naCount, totalTurns);

            // Check if all turns are NA
            if (validCount == 0)
            {
                _logger.LogInformation("All turns are Not Applicable for aggregate result {AggregateResultId}",
                    context.AggregateResultId);
                
                // All NA -> Aggregate also NA
                await _dataverseService.UpdateAggregateResultAsync(
                    context.AggregateResultId,
                    0.0m,
                    false,
                    EvaluationResultStatus.NotApplicable,
                    "All turns Not Applicable (missing expected responses)",
                    0,
                    cancellationToken);

                // Increment completed test cases counter (NA counts as completed)
                await _dataverseService.IncrementCompletedTestCasesAsync(
                    input.TestRunId,
                    failed: false,
                    cancellationToken);

                _logger.LogInformation("Aggregate result {AggregateResultId} marked as Not Applicable for test case {TestCaseId}", 
                    context.AggregateResultId, context.TestCase.Id);
                return;
            }

            // Calculate aggregate metrics from valid (non-NA) results only
            var totalExecutionTime = validResults.Sum(r => r.ExecutionTimeMs ?? 0);
            var averageScore = (decimal)validResults.Average(r => (double)(r.Score ?? 0));
            
            // Calculate pass rate based on valid results
            var passedCount = validResults.Count(r => r.Passed == true);
            var passRate = validCount > 0 ? (double)passedCount / validCount : 0.0;
            var overallPassed = passRate >= (double)context.PassingThreshold;

            // Create reasoning summary
            var reasoning = naCount > 0
                ? $"Multi-turn test case: {passedCount}/{validCount} valid turns passed ({passRate:P0}), {naCount} turns excluded as NA"
                : $"Multi-turn test case: {passedCount}/{validCount} turns passed ({passRate:P0})";

            // All successfully completed tests get StatusCode = 778760002 (Completed)
            // The Passed field indicates whether it met the threshold
            // StatusCode = 778760003 (Errored) is only for execution failures/exceptions
            var statusCode = EvaluationResultStatus.Completed;

            // Update the placeholder aggregate result
            await _dataverseService.UpdateAggregateResultAsync(
                context.AggregateResultId,
                averageScore,
                overallPassed,
                statusCode,
                reasoning,
                totalExecutionTime,
                cancellationToken);

            // Increment completed test cases counter
            await _dataverseService.IncrementCompletedTestCasesAsync(
                input.TestRunId,
                failed: !overallPassed,
                cancellationToken);

            _logger.LogInformation("Aggregate result {AggregateResultId} updated for test case {TestCaseId}", 
                context.AggregateResultId, context.TestCase.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating aggregate result {AggregateResultId} for test case {TestCaseId}", 
                context.AggregateResultId, context.TestCase.Id);
            throw;
        }
    }

    /// <summary>
    /// Helper method to create failure results for multiple evaluators
    /// </summary>
    private List<TurnExecutionResult> CreateFailureResults(ExecuteTurnInput input, List<Guid> evaluatorTypeIds, string errorMessage, Stopwatch stopwatch)
    {
        var results = new List<TurnExecutionResult>();
        
        foreach (var evaluatorTypeId in evaluatorTypeIds)
        {
            results.Add(new TurnExecutionResult
            {
                ConversationId = input.ConversationId ?? string.Empty,
                DirectLineToken = input.DirectLineToken,
                Success = false,
                Score = 0,
                Passed = false,
                ErrorMessage = errorMessage,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                StatusCode = EvaluationResultStatus.Errored
            });
        }
        
        return results;
    }
}
