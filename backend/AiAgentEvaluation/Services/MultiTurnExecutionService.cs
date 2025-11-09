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
    private readonly ILLMJudgeService _llmJudgeService;
    private readonly ILogger<MultiTurnExecutionService> _logger;

    public MultiTurnExecutionService(
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        ILLMJudgeService llmJudgeService,
        ILogger<MultiTurnExecutionService> logger)
    {
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _llmJudgeService = llmJudgeService;
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
                var copilotResponse = await _copilotStudioService.SendMessageAsync(
                    directLineUrl,
                    turn.PromptText,
                    context.ConversationId,
                    cancellationToken);

                // 2. Store conversation ID after first turn
                if (turn.Sequence == 1 && !string.IsNullOrEmpty(copilotResponse.ConversationId))
                {
                    context.ConversationId = copilotResponse.ConversationId;
                    _logger.LogInformation(
                        "Stored conversation ID {ConversationId} for test case {TestCaseId}",
                        context.ConversationId, testCase.Id);
                }

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
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs
                    };

                    failureResult.Id = await _dataverseService.CreateEvaluationResultAsync(
                        failureResultRequest, cancellationToken);

                    context.TurnResults[turn.Sequence] = failureResult;
                    continue;
                }

                // 3. Evaluate response using LLM Judge
                var judgeRequest = new LLMJudgeRequest
                {
                    EvaluatorTypeId = testCase.EvaluatorTypeId,
                    UserInput = turn.PromptText,
                    ExpectedResponse = turn.ExpectedResponse ?? string.Empty,
                    ActualResponse = copilotResponse.Response,
                    ConversationId = copilotResponse.ConversationId
                };

                var judgeResponse = await _llmJudgeService.EvaluateResponseAsync(
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
                        ExecutionTimeMs = copilotResponse.ExecutionTimeMs
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
                    Metadata = copilotResponse.RawActivityJson
                };

                result.Id = await _dataverseService.CreateEvaluationResultAsync(
                    createResultRequest, cancellationToken);

                context.TurnResults[turn.Sequence] = result;

                _logger.LogInformation(
                    "Turn {Sequence} completed: {Status} (Score: {Score})",
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
