using AiAgentEvaluation.Models;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for executing multi-turn test cases
/// </summary>
public class MultiTurnExecutionService : IMultiTurnExecutionService
{
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly ILLMJudgeServiceFactory _llmJudgeServiceFactory;
    private readonly ILogger<MultiTurnExecutionService> _logger;

    public MultiTurnExecutionService(
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        ILLMJudgeServiceFactory llmJudgeServiceFactory,
        ILogger<MultiTurnExecutionService> logger)
    {
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _llmJudgeServiceFactory = llmJudgeServiceFactory;
        _logger = logger;
    }

    public async Task<MultiTurnExecutionContext> ExecuteTestCaseAsync(
        TestCase testCase,
        Guid testRunId,
        string directLineUrl,
        decimal passingThreshold,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing multi-turn test case {TestCaseId} with {TurnCount} turns",
            testCase.Id, testCase.Turns?.Count ?? 0);

        var context = new MultiTurnExecutionContext
        {
            TestCase = testCase,
            TestRunId = testRunId,
            PassingThreshold = passingThreshold,
            Turns = testCase.Turns?.OrderBy(t => t.Sequence).ToList() ?? new()
        };

        // Execute each turn in sequence
        foreach (var turn in context.Turns)
        {
            try
            {
                _logger.LogInformation(
                    "Executing turn {Sequence}/{Total} for test case {TestCaseId}",
                    turn.Sequence, context.TotalTurns, testCase.Id);

                // 1. Send message to Copilot Studio with conversation context
                _logger.LogWarning(
                    "[MULTITURN] Turn {Sequence}/{Total}: SENDING PROMPT='{Prompt}' ConversationId='{ConversationId}'",
                    turn.Sequence, context.TotalTurns, turn.PromptText?.Substring(0, Math.Min(100, turn.PromptText?.Length ?? 0)), context.ConversationId ?? "NULL-WILL-CREATE");
                
                // Add delay before sending subsequent turns to ensure bot has processed previous turn
                if (turn.Sequence > 1)
                {
                    _logger.LogInformation("Waiting 3 seconds before sending turn {Sequence} to ensure bot is ready...", turn.Sequence);
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
                
                var copilotResponse = await _copilotStudioService.SendMessageAsync(
                    directLineUrl,
                    turn.PromptText ?? string.Empty,
                    context.ConversationId,
                    null, // directLineToken - not used, this is deprecated code
                    cancellationToken);
                
                _logger.LogWarning(
                    "[MULTITURN] Turn {Sequence}: RECEIVED RESPONSE='{Response}' ConversationId='{ConvId}'",
                    turn.Sequence, copilotResponse.Response?.Substring(0, Math.Min(100, copilotResponse.Response?.Length ?? 0)) ?? "NULL", copilotResponse.ConversationId);

                // 2. Store conversation ID after first turn
                if (turn.Sequence == 1 && !string.IsNullOrEmpty(copilotResponse.ConversationId))
                {
                    context.ConversationId = copilotResponse.ConversationId;
                    _logger.LogInformation(
                        "Turn 1: Stored conversation ID {ConversationId} for test case {TestCaseId}",
                        context.ConversationId, testCase.Id);
                }
                else if (turn.Sequence > 1)
                {
                    _logger.LogInformation(
                        "Turn {Sequence}: Reusing conversation ID {ConversationId}",
                        turn.Sequence, context.ConversationId);
                }
                
                _logger.LogInformation(
                    "Turn {Sequence}: Received response: '{Response}'",
                    turn.Sequence, copilotResponse.Response?.Substring(0, Math.Min(100, copilotResponse.Response?.Length ?? 0)) ?? "(null)");

                if (!copilotResponse.Success)
                {
                    _logger.LogWarning(
                        "Copilot Studio call failed for turn {Sequence}: {ErrorMessage}",
                        turn.Sequence, copilotResponse.ErrorMessage);

                    // Create failure result and continue
                    var failureResult = new EvaluationResult
                    {
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        EvaluationRunId = testRunId,
                        ConversationId = context.ConversationId,
                        IsAggregate = false,
                        Passed = false,
                        ErrorMessage = copilotResponse.ErrorMessage ?? "Copilot Studio call failed",
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        CreatedOn = DateTime.UtcNow
                    };

                    var failureResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = testRunId,
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        IsAggregate = false,
                        ConversationId = context.ConversationId,
                        Passed = false,
                        ErrorMessage = copilotResponse.ErrorMessage ?? "Copilot Studio call failed",
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs
                    };

                    failureResult.Id = await _dataverseService.CreateEvaluationResultAsync(
                        failureResultRequest, cancellationToken);

                    context.TurnResults[turn.Sequence] = failureResult;
                    continue;
                }

                // 3. Evaluate response using LLM Judge
                // Use factory to get the appropriate judge service based on evaluator configuration
                var llmJudgeService = await _llmJudgeServiceFactory.CreateJudgeServiceAsync(
                    testCase.EvaluatorTypeId, cancellationToken);
                
                var judgeRequest = new LLMJudgeRequest
                {
                    EvaluatorTypeId = testCase.EvaluatorTypeId,
                    UserInput = turn.PromptText ?? string.Empty,
                    ExpectedResponse = turn.ExpectedResponse ?? string.Empty,
                    ActualResponse = copilotResponse.Response ?? string.Empty,
                    ConversationId = copilotResponse.ConversationId
                };

                var judgeResponse = await llmJudgeService.EvaluateResponseAsync(
                    judgeRequest, cancellationToken);

                if (!judgeResponse.Success)
                {
                    _logger.LogWarning(
                        "LLM Judge evaluation failed for turn {Sequence}: {ErrorMessage}",
                        turn.Sequence, judgeResponse.ErrorMessage);

                    // Create failure result and continue
                    var failureResult = new EvaluationResult
                    {
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        EvaluationRunId = testRunId,
                        ConversationId = context.ConversationId,
                        IsAggregate = false,
                        ActualResponse = copilotResponse.Response,
                        Passed = false,
                        ErrorMessage = judgeResponse.ErrorMessage ?? "LLM Judge evaluation failed",
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        CreatedOn = DateTime.UtcNow
                    };

                    var failureResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = testRunId,
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        IsAggregate = false,
                        ConversationId = context.ConversationId,
                        ActualResponse = copilotResponse.Response,
                        Passed = false,
                        ErrorMessage = judgeResponse.ErrorMessage ?? "LLM Judge evaluation failed",
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs
                    };

                    failureResult.Id = await _dataverseService.CreateEvaluationResultAsync(
                        failureResultRequest, cancellationToken);

                    context.TurnResults[turn.Sequence] = failureResult;
                    continue;
                }

                // 4. Create and save successful evaluation result
                // Calculate passed status based on threshold
                var passed = judgeResponse.Score >= passingThreshold;

                var result = new EvaluationResult
                {
                    TestCaseId = testCase.Id,
                    TestCaseTurnId = turn.Id,
                    EvaluationRunId = testRunId,
                    ConversationId = context.ConversationId,
                    IsAggregate = false,
                    ActualResponse = copilotResponse.Response,
                    Passed = passed,
                    Score = judgeResponse.Score,
                    Reasoning = judgeResponse.Reasoning,
                    ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                    Metadata = copilotResponse.RawActivityJson,
                    CreatedOn = DateTime.UtcNow
                };

                _logger.LogInformation(
                    "Turn {Sequence}: Creating result - Prompt='{Prompt}', Response='{Response}', Score={Score}",
                    turn.Sequence, turn.PromptText?.Substring(0, Math.Min(50, turn.PromptText?.Length ?? 0)) ?? "(null)",
                    copilotResponse.Response?.Substring(0, Math.Min(50, copilotResponse.Response?.Length ?? 0)) ?? "(null)",
                    judgeResponse.Score);

                var createResultRequest = new CreateEvaluationResultRequest
                {
                    EvaluationRunId = testRunId,
                    TestCaseId = testCase.Id,
                    TestCaseTurnId = turn.Id,
                    IsAggregate = false,
                    ConversationId = context.ConversationId,
                    ActualResponse = copilotResponse.Response,
                    Passed = passed,
                    Score = judgeResponse.Score,
                    Reasoning = judgeResponse.Reasoning,
                    ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                    McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                    Metadata = copilotResponse.RawActivityJson
                };

                result.Id = await _dataverseService.CreateEvaluationResultAsync(
                    createResultRequest, cancellationToken);

                context.TurnResults[turn.Sequence] = result;

                _logger.LogWarning(
                    "[MULTITURN] Turn {Sequence}: SAVED ResultId={ResultId} WITH RESPONSE='{Response}'",
                    turn.Sequence, result.Id, createResultRequest.ActualResponse?.Substring(0, Math.Min(100, createResultRequest.ActualResponse?.Length ?? 0)) ?? "NULL");

                _logger.LogInformation(
                    "Turn {Sequence}: {Result} - Score: {Score}",
                    turn.Sequence, result.Passed == true ? "PASSED" : "FAILED", result.Score);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing turn {Sequence}", turn.Sequence);

                // Create failure result and continue
                var failureResult = new EvaluationResult
                {
                    TestCaseId = testCase.Id,
                    TestCaseTurnId = turn.Id,
                    EvaluationRunId = testRunId,
                    ConversationId = context.ConversationId,
                    IsAggregate = false,
                    Passed = false,
                    ErrorMessage = $"Exception during turn execution: {ex.Message}",
                    CreatedOn = DateTime.UtcNow
                };

                var failureResultRequest = new CreateEvaluationResultRequest
                {
                    EvaluationRunId = testRunId,
                    TestCaseId = testCase.Id,
                    TestCaseTurnId = turn.Id,
                    IsAggregate = false,
                    ConversationId = context.ConversationId,
                    Passed = false,
                    ErrorMessage = $"Exception during turn execution: {ex.Message}"
                };

                failureResult.Id = await _dataverseService.CreateEvaluationResultAsync(
                    failureResultRequest, cancellationToken);

                context.TurnResults[turn.Sequence] = failureResult;
            }
        }

        // 5. Save aggregate result
        try
        {
            await _dataverseService.CreateAggregateEvaluationResultAsync(
                context, cancellationToken);

            _logger.LogInformation(
                "Multi-turn test case {TestCaseId} completed: {PassRate:P0} pass rate ({Passed}/{Total})",
                testCase.Id, context.PassRate, context.PassedTurns, context.TotalTurns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error creating aggregate result for test case {TestCaseId}",
                testCase.Id);
            // Don't fail the entire execution if aggregate creation fails
        }

        return context;
    }
}
