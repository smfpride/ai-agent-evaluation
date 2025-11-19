using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Diagnostics;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Activity functions for multi-turn test case execution (Phase 2: Updated for async architecture)
/// Each activity can run for extended periods without timeout
/// </summary>
public class MultiTurnDurableActivities
{
    private readonly ILogger<MultiTurnDurableActivities> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly ILLMJudgeServiceFactory _llmJudgeServiceFactory;
    private readonly IServiceBusService _serviceBusService;

    public MultiTurnDurableActivities(
        ILogger<MultiTurnDurableActivities> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        ILLMJudgeServiceFactory llmJudgeServiceFactory,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _llmJudgeServiceFactory = llmJudgeServiceFactory;
        _serviceBusService = serviceBusService;
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
    /// Executes a single turn using async architecture (Phase 2)
    /// 1. Calls Copilot Studio
    /// 2. Creates pending results for each evaluator
    /// 3. Queues evaluator jobs to evaluator-processing-queue
    /// 4. Polls Dataverse until all evaluators complete (with 10-minute timeout)
    /// </summary>
    [Function("ExecuteSingleTurn")]
    public async Task<List<TurnExecutionResult>> ExecuteSingleTurn(
        [ActivityTrigger] ExecuteTurnInput input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<TurnExecutionResult>();
        
        _logger.LogInformation("Executing turn {Sequence} for test case {TestCaseId} with {EvaluatorCount} evaluators (async architecture)",
            input.Turn.Sequence, input.Turn.TestCaseId, input.EvaluatorTypeIds.Count);

        try
        {
            // Step 1: Call Copilot Studio
            _logger.LogInformation("Sending message to Copilot Studio for turn {Sequence}, promptText='{PromptText}', conversationId={ConversationId}", 
                input.Turn.Sequence, input.Turn.PromptText, input.ConversationId);
            
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
                
                // Create error results for all evaluators
                return await CreateErrorResultsForAllEvaluators(
                    input, 
                    copilotResponse.ErrorMessage ?? "Copilot Studio request failed", 
                    stopwatch, 
                    cancellationToken);
            }

            _logger.LogInformation("Received response from Copilot Studio for turn {Sequence} in {Ms}ms",
                input.Turn.Sequence, copilotResponse.ExecutionTimeMs);

            // Step 2 & 3: Create pending results and queue evaluator jobs
            var pendingResultIds = new List<Guid>();
            
            foreach (var evaluatorTypeId in input.EvaluatorTypeIds)
            {
                var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId, cancellationToken);
                
                // Check for NA scenario
                if (evaluatorType != null && evaluatorType.RequiresExpectedResponse && 
                    string.IsNullOrWhiteSpace(input.Turn.ExpectedResponse))
                {
                    _logger.LogInformation("Turn {Sequence} marked NA for evaluator {EvaluatorId} (missing expected response)",
                        input.Turn.Sequence, evaluatorTypeId);
                    
                    // Create NA result immediately (don't queue)
                    var naResultId = await _dataverseService.CreateEvaluationResultAsync(
                        new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = input.TestRunId,
                            TestCaseId = input.Turn.TestCaseId,
                            TestCaseTurnId = input.Turn.Id,
                            EvaluatorTypeId = evaluatorTypeId,
                            ConversationId = copilotResponse.ConversationId,
                            ActualResponse = copilotResponse.Response,
                            Score = 0.0m,
                            Passed = false,
                            Reasoning = $"Evaluator '{evaluatorType.Name}' requires expected response but turn {input.Turn.Sequence} has none provided",
                            McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                            Metadata = copilotResponse.RawActivityJson,
                            IsAggregate = false,
                            StatusCode = EvaluationResultStatus.NotApplicable
                        }, 
                        cancellationToken);
                    
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        input.TestRunId, 
                        failed: false, 
                        cancellationToken);

                    results.Add(new TurnExecutionResult
                    {
                        ConversationId = copilotResponse.ConversationId,
                        DirectLineToken = copilotResponse.DirectLineToken,
                        Success = true,
                        Score = 0.0m,
                        Passed = false,
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        EvaluationResultId = naResultId,
                        StatusCode = EvaluationResultStatus.NotApplicable
                    });
                    
                    continue; // Skip queuing for NA results
                }
                
                // Create pending result
                var resultId = await _dataverseService.CreateEvaluationResultAsync(
                    new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = input.TestRunId,
                        TestCaseId = input.Turn.TestCaseId,
                        TestCaseTurnId = input.Turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        ConversationId = copilotResponse.ConversationId,
                        ActualResponse = copilotResponse.Response,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        Metadata = copilotResponse.RawActivityJson,
                        IsAggregate = false,
                        StatusCode = EvaluationResultStatus.Pending
                    }, 
                    cancellationToken);
                
                pendingResultIds.Add(resultId);
                
                _logger.LogInformation("Created pending result {ResultId} for evaluator {EvaluatorId}",
                    resultId, evaluatorTypeId);
                
                // Queue evaluator job
                await _serviceBusService.SendEvaluatorJobMessageAsync(
                    new EvaluatorJobMessage
                    {
                        EvaluationResultId = resultId,
                        EvaluationRunId = input.TestRunId
                    }, 
                    cancellationToken);
                
                _logger.LogInformation("Queued evaluator job for result {ResultId}", resultId);
            }
            
            // Step 4: Poll Dataverse until all evaluators complete (with timeout)
            if (pendingResultIds.Any())
            {
                _logger.LogInformation("Polling for {Count} evaluator results to complete", pendingResultIds.Count);
                
                var completedResults = await PollForEvaluationCompletion(
                    pendingResultIds, 
                    TimeSpan.FromMinutes(10), 
                    cancellationToken);
                
                // Build TurnExecutionResult from completed results
                foreach (var completedResult in completedResults)
                {
                    results.Add(new TurnExecutionResult
                    {
                        ConversationId = completedResult.ConversationId ?? copilotResponse.ConversationId,
                        DirectLineToken = copilotResponse.DirectLineToken,
                        Success = completedResult.StatusCode == EvaluationResultStatus.Completed,
                        Score = completedResult.Score ?? 0m,
                        Passed = completedResult.Passed ?? false,
                        ExecutionTimeMs = completedResult.ExecutionTimeMs ?? 0,
                        EvaluationResultId = completedResult.Id,
                        StatusCode = completedResult.StatusCode,
                        ErrorMessage = completedResult.ErrorMessage
                    });
                }
            }

            stopwatch.Stop();
            _logger.LogInformation("Successfully executed turn {Sequence} for test case {TestCaseId} with {EvaluatorCount} evaluators in {Ms}ms",
                input.Turn.Sequence, input.Turn.TestCaseId, results.Count, stopwatch.ElapsedMilliseconds);

            return results;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error executing turn {Sequence} for test case {TestCaseId}",
                input.Turn.Sequence, input.Turn.TestCaseId);
            
            return await CreateErrorResultsForAllEvaluators(
                input, 
                $"Unexpected error: {ex.Message}", 
                stopwatch, 
                cancellationToken);
        }
    }
    
    /// <summary>
    /// Polls Dataverse until all evaluation results complete or timeout occurs
    /// </summary>
    private async Task<List<EvaluationResult>> PollForEvaluationCompletion(
        List<Guid> resultIds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pollStopwatch = Stopwatch.StartNew();
        var pollInterval = TimeSpan.FromSeconds(2); // Start with 2 seconds
        var completedResults = new List<EvaluationResult>();
        var remainingResultIds = new HashSet<Guid>(resultIds);
        
        while (remainingResultIds.Any() && pollStopwatch.Elapsed < timeout)
        {
            _logger.LogInformation("Polling for {RemainingCount} results, elapsed: {Elapsed}s",
                remainingResultIds.Count, pollStopwatch.Elapsed.TotalSeconds);
            
            // Check each remaining result
            var currentBatch = remainingResultIds.ToList();
            foreach (var resultId in currentBatch)
            {
                try
                {
                    var result = await _dataverseService.GetEvaluationResultAsync(resultId, cancellationToken);
                    
                    if (result != null && result.StatusCode != EvaluationResultStatus.Pending)
                    {
                        // Result is complete (either Completed, Errored, or NotApplicable)
                        completedResults.Add(result);
                        remainingResultIds.Remove(resultId);
                        
                        _logger.LogInformation("Result {ResultId} completed with status {Status}",
                            resultId, EvaluationResultStatus.GetName(result.StatusCode));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking result {ResultId}, will retry", resultId);
                }
            }
            
            if (remainingResultIds.Any())
            {
                // Wait before next poll (exponential backoff up to 10 seconds)
                await Task.Delay(pollInterval, cancellationToken);
                pollInterval = TimeSpan.FromSeconds(Math.Min(pollInterval.TotalSeconds * 1.5, 10));
            }
        }
        
        // Handle timeout
        if (remainingResultIds.Any())
        {
            _logger.LogWarning("Polling timed out after {Elapsed}s with {RemainingCount} results still pending",
                pollStopwatch.Elapsed.TotalSeconds, remainingResultIds.Count);
            
            // Mark remaining results as errored due to timeout
            foreach (var resultId in remainingResultIds)
            {
                try
                {
                    await _dataverseService.UpdateEvaluationResultAsync(
                        resultId,
                        new UpdateEvaluationResultRequest
                        {
                            StatusCode = EvaluationResultStatus.Errored,
                            ErrorMessage = $"Evaluation timed out after {timeout.TotalMinutes} minutes",
                            Score = 0m,
                            Passed = false
                        },
                        cancellationToken);
                    
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        Guid.Empty, // Will be set by update
                        failed: true,
                        cancellationToken);
                    
                    // Retrieve the updated result
                    var result = await _dataverseService.GetEvaluationResultAsync(resultId, cancellationToken);
                    if (result != null)
                    {
                        completedResults.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error marking result {ResultId} as timed out", resultId);
                }
            }
        }
        
        return completedResults;
    }
    
    /// <summary>
    /// Creates error results for all evaluators when agent call fails
    /// </summary>
    private async Task<List<TurnExecutionResult>> CreateErrorResultsForAllEvaluators(
        ExecuteTurnInput input,
        string errorMessage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var results = new List<TurnExecutionResult>();
        
        foreach (var evaluatorTypeId in input.EvaluatorTypeIds)
        {
            try
            {
                var errorResultId = await _dataverseService.CreateEvaluationResultAsync(
                    new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = input.TestRunId,
                        TestCaseId = input.Turn.TestCaseId,
                        TestCaseTurnId = input.Turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        ConversationId = input.ConversationId,
                        ActualResponse = null,
                        Score = 0m,
                        Passed = false,
                        ErrorMessage = errorMessage,
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        IsAggregate = false,
                        StatusCode = EvaluationResultStatus.Errored
                    }, 
                    cancellationToken);
                
                await _dataverseService.IncrementCompletedTestCasesAsync(
                    input.TestRunId, 
                    failed: true, 
                    cancellationToken);

                results.Add(new TurnExecutionResult
                {
                    ConversationId = input.ConversationId ?? string.Empty,
                    DirectLineToken = input.DirectLineToken,
                    Success = false,
                    Score = 0,
                    Passed = false,
                    ErrorMessage = errorMessage,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    EvaluationResultId = errorResultId,
                    StatusCode = EvaluationResultStatus.Errored
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating error result for evaluator {EvaluatorId}", evaluatorTypeId);
            }
        }
        
        return results;
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
