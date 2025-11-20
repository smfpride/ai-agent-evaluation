using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Text.Json;
using System.Diagnostics;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Service Bus Worker Function for processing multi-turn agent calls (Story 11 - Service Bus Migration)
/// Triggered by messages on multi-turn-agent-call-queue
/// Calls Copilot Studio with conversation context, creates pending results, queues evaluator jobs, and queues next turn
/// </summary>
public class MultiTurnAgentWorkerFunction
{
    private readonly ILogger<MultiTurnAgentWorkerFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly IServiceBusService _serviceBusService;
    private readonly AppSettings _settings;

    public MultiTurnAgentWorkerFunction(
        ILogger<MultiTurnAgentWorkerFunction> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        IServiceBusService serviceBusService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _serviceBusService = serviceBusService;
        _settings = settings.Value;
    }

    [Function("ProcessMultiTurnAgentCall")]
    public async Task ProcessMultiTurnAgentCall(
        [ServiceBusTrigger("multi-turn-agent-call-queue", Connection = "ServiceBusNamespace")] 
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing multi-turn agent call message: {MessageBody}", messageBody);

        MultiTurnAgentCallMessage? message = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse the message
            message = JsonSerializer.Deserialize<MultiTurnAgentCallMessage>(messageBody);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize multi-turn agent call message: {MessageBody}", messageBody);
                return;
            }

            _logger.LogInformation(
                "Processing multi-turn agent call: TestCase={TestCaseId}, Turn={TurnSequence}/{TotalTurns}, Run={EvaluationRunId}, Evaluators={EvaluatorCount}, RetryCount={RetryCount}",
                message.TestCaseId,
                message.TurnSequence,
                message.TotalTurns,
                message.EvaluationRunId,
                message.EvaluatorTypeIds.Count,
                message.RetryCount);

            // Validate DirectLineUrl
            if (string.IsNullOrEmpty(message.DirectLineUrl))
            {
                _logger.LogError("DirectLineUrl is missing for test case {TestCaseId}", message.TestCaseId);
                await HandleMultiTurnAgentCallError(message, "DirectLineUrl is not configured", cancellationToken);
                return;
            }

            // Get the turn details from Dataverse
            var turns = await _dataverseService.GetTestCaseTurnsAsync(message.TestCaseId, cancellationToken);
            var currentTurn = turns.FirstOrDefault(t => t.Sequence == message.TurnSequence);
            
            if (currentTurn == null)
            {
                _logger.LogError(
                    "Turn {TurnSequence} not found for test case {TestCaseId}",
                    message.TurnSequence,
                    message.TestCaseId);
                await HandleMultiTurnAgentCallError(message, $"Turn {message.TurnSequence} not found", cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Retrieved turn {TurnSequence}: Prompt='{PromptText}', ExpectedResponse='{ExpectedResponse}'",
                message.TurnSequence,
                currentTurn.PromptText,
                currentTurn.ExpectedResponse);

            // Call Copilot Studio with conversation context (conversationId for Turn 2+)
            var copilotResponse = await _copilotStudioService.SendMessageAsync(
                message.DirectLineUrl,
                currentTurn.PromptText,
                message.ConversationId, // null for Turn 1, populated for Turn 2+
                message.DirectLineToken, // null for Turn 1, populated for Turn 2+
                cancellationToken);

            if (!copilotResponse.Success)
            {
                _logger.LogWarning(
                    "Copilot Studio call failed for test case {TestCaseId}, turn {TurnSequence}: {ErrorMessage}",
                    message.TestCaseId,
                    message.TurnSequence,
                    copilotResponse.ErrorMessage);

                // Retry logic with exponential backoff
                if (message.RetryCount < (_settings.AzureOpenAI?.MaxRetryAttempts ?? 3))
                {
                    var nextRetryCount = message.RetryCount + 1;
                    var retryDelaySeconds = (_settings.Evaluation?.RetryDelaySeconds ?? 30) * nextRetryCount;
                    var scheduledEnqueueTime = DateTimeOffset.UtcNow.AddSeconds(retryDelaySeconds);

                    _logger.LogInformation(
                        "Retrying turn {TurnSequence} for test case {TestCaseId} in {DelaySeconds} seconds (attempt {RetryCount}/{MaxRetries})",
                        message.TurnSequence,
                        message.TestCaseId,
                        retryDelaySeconds,
                        nextRetryCount,
                        _settings.AzureOpenAI?.MaxRetryAttempts ?? 3);

                    // Re-queue with incremented retry count and scheduled time
                    var retryMessage = new MultiTurnAgentCallMessage
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeIds = message.EvaluatorTypeIds,
                        TurnSequence = message.TurnSequence,
                        TotalTurns = message.TotalTurns,
                        DirectLineUrl = message.DirectLineUrl,
                        ConversationId = message.ConversationId,
                        DirectLineToken = message.DirectLineToken,
                        PassingThreshold = message.PassingThreshold,
                        RetryCount = nextRetryCount
                    };

                    await _serviceBusService.SendMultiTurnAgentCallMessageAsync(retryMessage, scheduledEnqueueTime, cancellationToken);
                    return;
                }

                // Max retries exceeded - create error results
                _logger.LogError(
                    "Max retries exceeded for test case {TestCaseId}, turn {TurnSequence}",
                    message.TestCaseId,
                    message.TurnSequence);
                await HandleMultiTurnAgentCallError(message, copilotResponse.ErrorMessage ?? "Copilot Studio call failed after retries", cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Copilot Studio response received: TestCase={TestCaseId}, Turn={TurnSequence}, ExecutionTime={ExecutionTime}ms, ConversationId={ConversationId}",
                message.TestCaseId,
                message.TurnSequence,
                copilotResponse.ExecutionTimeMs,
                copilotResponse.ConversationId);

            // Create pending results for each evaluator and queue evaluator jobs
            var evaluatorJobsTasks = message.EvaluatorTypeIds.Select(async evaluatorTypeId =>
            {
                try
                {
                    // Check if evaluator requires expected response (for NA handling)
                    var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId, cancellationToken);

                    // Handle NA scenario: If evaluator requires expected response but none provided for this turn
                    if (evaluatorType?.RequiresExpectedResponse == true && string.IsNullOrWhiteSpace(currentTurn.ExpectedResponse))
                    {
                        _logger.LogInformation(
                            "Creating NA result for evaluator {EvaluatorTypeId}, turn {TurnSequence} - no expected response provided",
                            evaluatorTypeId,
                            message.TurnSequence);

                        // Create NA result immediately (don't queue)
                        await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = message.EvaluationRunId,
                            TestCaseId = message.TestCaseId,
                            EvaluatorTypeId = evaluatorTypeId,
                            TestCaseTurnId = currentTurn.Id,
                            StatusCode = EvaluationResultStatus.NotApplicable,
                            Score = 0.0m,
                            Passed = false,
                            Reasoning = $"Evaluator '{evaluatorType?.Name}' requires expected response but none provided for turn {message.TurnSequence}",
                            ActualResponse = copilotResponse.Response,
                            ConversationId = copilotResponse.ConversationId,
                            McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                            Metadata = copilotResponse.RawActivityJson
                        }, cancellationToken);

                        // Increment completion counter
                        await _dataverseService.IncrementCompletedTestCasesAsync(
                            message.EvaluationRunId,
                            failed: false,
                            cancellationToken);

                        return; // Skip queue for NA results
                    }

                    // Create pending result for this turn
                    var resultId = await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeId = evaluatorTypeId,
                        TestCaseTurnId = currentTurn.Id,
                        StatusCode = EvaluationResultStatus.Pending,
                        ActualResponse = copilotResponse.Response,
                        ConversationId = copilotResponse.ConversationId,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        Metadata = copilotResponse.RawActivityJson
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Created pending result {ResultId} for evaluator {EvaluatorTypeId}, turn {TurnSequence}",
                        resultId,
                        evaluatorTypeId,
                        message.TurnSequence);

                    // Queue evaluator job
                    await _serviceBusService.SendEvaluatorJobMessageAsync(new EvaluatorJobMessage
                    {
                        EvaluationResultId = resultId,
                        EvaluationRunId = message.EvaluationRunId
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Queued evaluator job for result {ResultId}",
                        resultId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error processing evaluator {EvaluatorTypeId} for test case {TestCaseId}, turn {TurnSequence}",
                        evaluatorTypeId,
                        message.TestCaseId,
                        message.TurnSequence);
                    throw;
                }
            });

            // Wait for all evaluator jobs to be queued
            await Task.WhenAll(evaluatorJobsTasks);

            // Queue next turn if not last turn
            if (message.TurnSequence < message.TotalTurns)
            {
                var nextTurnMessage = new MultiTurnAgentCallMessage
                {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    EvaluatorTypeIds = message.EvaluatorTypeIds,
                    TurnSequence = message.TurnSequence + 1,
                    TotalTurns = message.TotalTurns,
                    DirectLineUrl = message.DirectLineUrl,
                    ConversationId = copilotResponse.ConversationId, // Pass conversation context
                    DirectLineToken = copilotResponse.DirectLineToken ?? message.DirectLineToken, // Preserve token
                    PassingThreshold = message.PassingThreshold,
                    RetryCount = 0 // Reset retry count for next turn
                };

                await _serviceBusService.SendMultiTurnAgentCallMessageAsync(nextTurnMessage, cancellationToken);

                _logger.LogInformation(
                    "Queued next turn {NextTurn}/{TotalTurns} for test case {TestCaseId}",
                    message.TurnSequence + 1,
                    message.TotalTurns,
                    message.TestCaseId);
            }
            else
            {
                _logger.LogInformation(
                    "Completed last turn {TurnSequence}/{TotalTurns} for test case {TestCaseId}",
                    message.TurnSequence,
                    message.TotalTurns,
                    message.TestCaseId);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Multi-turn agent call processing completed: TestCase={TestCaseId}, Turn={TurnSequence}/{TotalTurns}, TotalTime={TotalTime}ms, EvaluatorsQueued={EvaluatorCount}",
                message.TestCaseId,
                message.TurnSequence,
                message.TotalTurns,
                stopwatch.ElapsedMilliseconds,
                message.EvaluatorTypeIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled error processing multi-turn agent call: TestCase={TestCaseId}, Turn={TurnSequence}",
                message?.TestCaseId,
                message?.TurnSequence);

            if (message != null)
            {
                await HandleMultiTurnAgentCallError(message, $"Unhandled error: {ex.Message}", cancellationToken);
            }

            // Re-throw to trigger Service Bus retry logic
            throw;
        }
    }

    /// <summary>
    /// Handles multi-turn agent call errors by creating error results for all evaluators for this turn
    /// Does NOT queue next turn when error occurs
    /// </summary>
    private async Task HandleMultiTurnAgentCallError(MultiTurnAgentCallMessage message, string errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogError(
                "Handling multi-turn agent call error for test case {TestCaseId}, turn {TurnSequence}: {ErrorMessage}",
                message.TestCaseId,
                message.TurnSequence,
                errorMessage);

            // Get the turn details to get the turn ID
            var turns = await _dataverseService.GetTestCaseTurnsAsync(message.TestCaseId, cancellationToken);
            var currentTurn = turns.FirstOrDefault(t => t.Sequence == message.TurnSequence);
            var turnId = currentTurn?.Id;

            // Create error results for ALL evaluators (since agent call failed, all evaluations fail)
            var errorResultTasks = message.EvaluatorTypeIds.Select(async evaluatorTypeId =>
            {
                try
                {
                    await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeId = evaluatorTypeId,
                        TestCaseTurnId = turnId,
                        StatusCode = EvaluationResultStatus.Errored,
                        Score = 0.0m,
                        Passed = false,
                        Reasoning = $"Agent call failed for turn {message.TurnSequence}",
                        ErrorMessage = $"{errorMessage} (RetryCount={message.RetryCount})"
                    }, cancellationToken);

                    // Increment completion counter (mark as failed)
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        message.EvaluationRunId,
                        failed: true,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error creating error result for evaluator {EvaluatorTypeId}, turn {TurnSequence}",
                        evaluatorTypeId,
                        message.TurnSequence);
                }
            });

            await Task.WhenAll(errorResultTasks);

            _logger.LogInformation(
                "Created error results for {Count} evaluators for turn {TurnSequence}. Next turn will NOT be queued.",
                message.EvaluatorTypeIds.Count,
                message.TurnSequence);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling multi-turn agent call error for test case {TestCaseId}, turn {TurnSequence}",
                message.TestCaseId,
                message.TurnSequence);
        }
    }
}
