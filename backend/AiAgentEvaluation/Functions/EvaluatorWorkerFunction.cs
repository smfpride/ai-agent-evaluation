using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Text.Json;
using System.Diagnostics;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Service Bus Worker Function for processing evaluator jobs (Phase 1 - New Architecture)
/// Triggered by messages on evaluator-processing-queue
/// Retrieves pending results, calls LLM Judge, and updates results to Completed or Errored
/// Implements exponential backoff retry by re-queuing messages with scheduled enqueue time
/// </summary>
public class EvaluatorWorkerFunction
{
    private readonly ILogger<EvaluatorWorkerFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ILLMJudgeServiceFactory _llmJudgeServiceFactory;
    private readonly IServiceBusService _serviceBusService;
    private const int MaxRetryCount = 3; // Max application-level retries before marking as errored
    private static readonly TimeSpan[] RetryDelays = new[]
    {
        TimeSpan.FromSeconds(5),   // 1st retry after 5 seconds
        TimeSpan.FromSeconds(15),  // 2nd retry after 15 seconds
        TimeSpan.FromSeconds(45)   // 3rd retry after 45 seconds
    };

    public EvaluatorWorkerFunction(
        ILogger<EvaluatorWorkerFunction> logger,
        IDataverseService dataverseService,
        ILLMJudgeServiceFactory llmJudgeServiceFactory,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _llmJudgeServiceFactory = llmJudgeServiceFactory;
        _serviceBusService = serviceBusService;
    }

    [Function("ProcessEvaluatorJob")]
    public async Task ProcessEvaluatorJob(
        [ServiceBusTrigger("evaluator-processing-queue", Connection = "ServiceBusNamespace")] 
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing evaluator job message: {MessageBody}", messageBody);

        EvaluatorJobMessage? message = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse the message
            message = JsonSerializer.Deserialize<EvaluatorJobMessage>(messageBody);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize evaluator job message: {MessageBody}", messageBody);
                return;
            }

            _logger.LogInformation(
                "Processing evaluator job: Result={EvaluationResultId}, Run={EvaluationRunId}",
                message.EvaluationResultId,
                message.EvaluationRunId);

            // Retrieve pending result from Dataverse
            var result = await _dataverseService.GetEvaluationResultAsync(
                message.EvaluationResultId,
                cancellationToken);

            if (result == null)
            {
                _logger.LogError(
                    "Evaluation result {ResultId} not found",
                    message.EvaluationResultId);
                return;
            }

            // Check if already completed (idempotency)
            if (result.StatusCode == EvaluationResultStatus.Completed)
            {
                _logger.LogInformation(
                    "Result {ResultId} already completed, skipping evaluation",
                    result.Id);
                return;
            }

            if (result.StatusCode == EvaluationResultStatus.Errored)
            {
                _logger.LogInformation(
                    "Result {ResultId} already errored, skipping evaluation",
                    result.Id);
                return;
            }

            if (result.StatusCode != EvaluationResultStatus.Pending)
            {
                _logger.LogWarning(
                    "Result {ResultId} has unexpected status {StatusCode}, expected Pending",
                    result.Id,
                    result.StatusCode);
                return;
            }

            // Load related data
            var testCase = await _dataverseService.GetTestCaseAsync(result.TestCaseId, cancellationToken);
            if (testCase == null)
            {
                _logger.LogError(
                    "Test case {TestCaseId} not found for result {ResultId}",
                    result.TestCaseId,
                    result.Id);
                await UpdateResultToErrored(
                    message.EvaluationResultId,
                    message.EvaluationRunId,
                    "Test case not found",
                    stopwatch,
                    cancellationToken);
                return;
            }

            var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(
                result.EvaluatorTypeId!.Value,
                cancellationToken);

            if (evaluatorType == null)
            {
                _logger.LogError(
                    "Evaluator type {EvaluatorTypeId} not found for result {ResultId}",
                    result.EvaluatorTypeId,
                    result.Id);
                await UpdateResultToErrored(
                    message.EvaluationResultId,
                    message.EvaluationRunId,
                    "Evaluator type not found",
                    stopwatch,
                    cancellationToken);
                return;
            }

            // Determine user input and expected response (may come from test case or turn)
            string userInput;
            string expectedResponse;

            if (result.TestCaseTurnId.HasValue)
            {
                // Multi-turn: Load turn data
                var turns = await _dataverseService.GetTestCaseTurnsAsync(result.TestCaseId, cancellationToken);
                var turn = turns.FirstOrDefault(t => t.Id == result.TestCaseTurnId.Value);

                if (turn == null)
                {
                    _logger.LogError(
                        "Test case turn {TurnId} not found for result {ResultId}",
                        result.TestCaseTurnId.Value,
                        result.Id);
                    await UpdateResultToErrored(
                        message.EvaluationResultId,
                        message.EvaluationRunId,
                        "Test case turn not found",
                        stopwatch,
                        cancellationToken);
                    return;
                }

                userInput = turn.PromptText;
                expectedResponse = turn.ExpectedResponse ?? string.Empty;
            }
            else
            {
                // Single-turn: Use test case data
                userInput = testCase.UserInput;
                expectedResponse = testCase.ExpectedResponse ?? string.Empty;
            }

            _logger.LogInformation(
                "Calling LLM Judge for result {ResultId} with evaluator {EvaluatorType}",
                result.Id,
                evaluatorType.Name);

            // Call LLM Judge with retry (exponential backoff handled by ResilientService)
            var llmJudgeService = await _llmJudgeServiceFactory.CreateJudgeServiceAsync(
                result.EvaluatorTypeId.Value,
                cancellationToken);

            var judgeResponse = await llmJudgeService.EvaluateResponseAsync(new LLMJudgeRequest
            {
                EvaluatorTypeId = result.EvaluatorTypeId.Value,
                UserInput = userInput,
                ExpectedResponse = expectedResponse,
                ActualResponse = result.ActualResponse ?? string.Empty,
                ConversationId = result.ConversationId
            }, cancellationToken);

            stopwatch.Stop();

            if (!judgeResponse.Success)
            {
                _logger.LogWarning(
                    "LLM Judge call failed for result {ResultId}: {ErrorMessage}, RetryCount={RetryCount}",
                    result.Id,
                    judgeResponse.ErrorMessage,
                    message.RetryCount);

                // Implement exponential backoff retry
                if (message.RetryCount < MaxRetryCount)
                {
                    var delay = RetryDelays[message.RetryCount];
                    var scheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay);
                    
                    _logger.LogInformation(
                        "Re-queuing evaluator job for result {ResultId} with retry count {RetryCount}, scheduled for {ScheduledTime}",
                        result.Id,
                        message.RetryCount + 1,
                        scheduledEnqueueTime);

                    // Re-queue the message with incremented retry count
                    await _serviceBusService.SendEvaluatorJobMessageAsync(
                        new EvaluatorJobMessage
                        {
                            EvaluationResultId = message.EvaluationResultId,
                            EvaluationRunId = message.EvaluationRunId,
                            RetryCount = message.RetryCount + 1
                        },
                        scheduledEnqueueTime,
                        cancellationToken);
                    
                    return;
                }

                // Max retries exceeded, mark as errored
                _logger.LogError(
                    "LLM Judge call failed for result {ResultId} after {MaxRetries} retries: {ErrorMessage}",
                    result.Id,
                    MaxRetryCount,
                    judgeResponse.ErrorMessage);

                await UpdateResultToErrored(
                    message.EvaluationResultId,
                    message.EvaluationRunId,
                    $"LLM Judge call failed after {MaxRetryCount} retries: {judgeResponse.ErrorMessage ?? "Unknown error"}",
                    stopwatch,
                    cancellationToken);
                return;
            }

            // Get passing threshold from evaluation run
            var evaluationRun = await _dataverseService.GetEvaluationRunAsync(
                message.EvaluationRunId,
                cancellationToken);
            
            var passingThreshold = evaluationRun?.PassingThreshold ?? 0.7m;
            
            _logger.LogInformation(
                "Using passing threshold {PassingThreshold} for result {ResultId}",
                passingThreshold,
                result.Id);

            // Update result to completed
            await _dataverseService.UpdateEvaluationResultAsync(result.Id, new UpdateEvaluationResultRequest
            {
                StatusCode = EvaluationResultStatus.Completed,
                Score = judgeResponse.Score,
                Passed = judgeResponse.Score >= passingThreshold,
                Reasoning = judgeResponse.Reasoning,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            // Increment completion counter
            await _dataverseService.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId,
                failed: false,
                cancellationToken);

            _logger.LogInformation(
                "Evaluator job completed: Result={ResultId}, Score={Score}, Passed={Passed}, Time={TotalTime}ms",
                result.Id,
                judgeResponse.Score,
                judgeResponse.Score >= passingThreshold,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled error processing evaluator job: Result={ResultId}",
                message?.EvaluationResultId);

            if (message != null)
            {
                await UpdateResultToErrored(
                    message.EvaluationResultId,
                    message.EvaluationRunId,
                    $"Unhandled error: {ex.Message}",
                    stopwatch,
                    cancellationToken);
            }

            // Re-throw to trigger Service Bus retry logic
            throw;
        }
    }

    /// <summary>
    /// Updates a result to Errored status
    /// </summary>
    private async Task UpdateResultToErrored(
        Guid resultId,
        Guid evaluationRunId,
        string errorMessage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dataverseService.UpdateEvaluationResultAsync(resultId, new UpdateEvaluationResultRequest
            {
                StatusCode = EvaluationResultStatus.Errored,
                Score = 0.0m,
                Passed = false,
                ErrorMessage = errorMessage,
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
            }, cancellationToken);

            // Increment completion counter (mark as failed)
            await _dataverseService.IncrementCompletedTestCasesAsync(
                evaluationRunId,
                failed: true,
                cancellationToken);

            _logger.LogInformation(
                "Updated result {ResultId} to Errored status",
                resultId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating result {ResultId} to Errored status",
                resultId);
        }
    }
}
