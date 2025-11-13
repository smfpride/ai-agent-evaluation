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
    private readonly ILLMJudgeService _llmJudgeService;

    public MultiTurnDurableActivities(
        ILogger<MultiTurnDurableActivities> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        ILLMJudgeService llmJudgeService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _llmJudgeService = llmJudgeService;
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
    public async Task<TurnExecutionResult> ExecuteSingleTurn(
        [ActivityTrigger] ExecuteTurnInput input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation("Executing turn {Sequence} for test case {TestCaseId}",
            input.Turn.Sequence, input.Turn.TestCaseId);

        try
        {
            // 1. Send message to agent (can take minutes)
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
                
                return CreateFailureResult(input, copilotResponse.ErrorMessage ?? "Copilot Studio request failed", stopwatch);
            }

            _logger.LogInformation("Received response from Copilot Studio for turn {Sequence} in {Ms}ms",
                input.Turn.Sequence, copilotResponse.ExecutionTimeMs);

            // 2. Evaluate with LLM Judge (can take minutes, has retry logic)
            _logger.LogInformation("Evaluating response with LLM Judge for turn {Sequence}", input.Turn.Sequence);
            
            var judgeResponse = await _llmJudgeService.EvaluateResponseAsync(
                new LLMJudgeRequest
                {
                    EvaluatorTypeId = input.EvaluatorTypeId,
                    UserInput = input.Turn.PromptText,
                    ExpectedResponse = input.Turn.ExpectedResponse ?? string.Empty,
                    ActualResponse = copilotResponse.Response,
                    ConversationId = copilotResponse.ConversationId
                },
                cancellationToken);

            if (!judgeResponse.Success)
            {
                _logger.LogError("LLM Judge error for turn {Sequence}: {Error}",
                    input.Turn.Sequence, judgeResponse.ErrorMessage);
                
                return CreateFailureResult(input, judgeResponse.ErrorMessage ?? "LLM Judge evaluation failed", stopwatch);
            }

            _logger.LogInformation("LLM Judge evaluation for turn {Sequence}: Score={Score}, Passed={Passed}",
                input.Turn.Sequence, judgeResponse.Score, judgeResponse.Score >= input.PassingThreshold);

            stopwatch.Stop();

            // 3. Save result to Dataverse immediately
            _logger.LogInformation("Saving evaluation result for turn {Sequence}", input.Turn.Sequence);
            
            var createResultRequest = new CreateEvaluationResultRequest
            {
                EvaluationRunId = input.TestRunId,
                TestCaseId = input.Turn.TestCaseId,
                TestCaseTurnId = input.Turn.Id,
                ConversationId = copilotResponse.ConversationId,
                ActualResponse = copilotResponse.Response,
                Score = judgeResponse.Score,
                Passed = judgeResponse.Score >= input.PassingThreshold,
                Reasoning = judgeResponse.Reasoning,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                IsAggregate = false,
                StatusCode = 778760002  // Completed
            };

            var resultId = await _dataverseService.CreateEvaluationResultAsync(createResultRequest, cancellationToken);

            _logger.LogInformation("Successfully executed turn {Sequence} for test case {TestCaseId}",
                input.Turn.Sequence, input.Turn.TestCaseId);

            return new TurnExecutionResult
            {
                ConversationId = copilotResponse.ConversationId,
                DirectLineToken = copilotResponse.DirectLineToken,
                Success = true,
                Score = judgeResponse.Score,
                Passed = judgeResponse.Score >= input.PassingThreshold,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                EvaluationResultId = resultId
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error executing turn {Sequence} for test case {TestCaseId}",
                input.Turn.Sequence, input.Turn.TestCaseId);
            
            return CreateFailureResult(input, $"Unexpected error: {ex.Message}", stopwatch);
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
            // Calculate aggregate metrics
            var totalExecutionTime = context.TurnResults.Values.Sum(r => r.ExecutionTimeMs ?? 0);
            var averageScore = context.TurnResults.Values.Any()
                ? (decimal)context.TurnResults.Values.Average(r => (double)(r.Score ?? 0))
                : 0m;
            var overallPassed = context.PassRate >= (double)context.PassingThreshold;

            // Create reasoning summary
            var reasoning = $"Multi-turn test case: {context.PassedTurns}/{context.TotalTurns} turns passed ({context.PassRate:P0})";

            // All successfully completed tests get StatusCode = 778760002 (Completed)
            // The Passed field indicates whether it met the threshold
            // StatusCode = 778760003 (Errored) is only for execution failures/exceptions
            var statusCode = 778760002;  // Completed

            // Update the placeholder aggregate result
            await _dataverseService.UpdateAggregateResultAsync(
                context.AggregateResultId,
                averageScore,
                overallPassed,
                reasoning,
                totalExecutionTime,
                statusCode,
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
    /// Helper method to create a failure result
    /// </summary>
    private TurnExecutionResult CreateFailureResult(ExecuteTurnInput input, string errorMessage, Stopwatch stopwatch)
    {
        return new TurnExecutionResult
        {
            ConversationId = input.ConversationId ?? string.Empty,
            DirectLineToken = input.DirectLineToken,
            Success = false,
            Score = 0,
            Passed = false,
            ErrorMessage = errorMessage,
            ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
    }
}
