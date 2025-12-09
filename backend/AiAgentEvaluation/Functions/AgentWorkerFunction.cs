using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using System.Text.Json;
using System.Diagnostics;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Service Bus Worker Function for processing agent calls (Phase 1 - New Architecture)
/// Triggered by messages on agent-call-queue
/// Calls Copilot Studio, creates pending results, and queues evaluator jobs
/// </summary>
public class AgentWorkerFunction
{
    private readonly ILogger<AgentWorkerFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService; // Kept for backward compatibility
    private readonly IAgentAdapterFactory _agentAdapterFactory;
    private readonly IServiceBusService _serviceBusService;
    private readonly AppSettings _settings;

    public AgentWorkerFunction(
        ILogger<AgentWorkerFunction> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService, // Kept for backward compatibility
        IAgentAdapterFactory agentAdapterFactory,
        IServiceBusService serviceBusService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _agentAdapterFactory = agentAdapterFactory;
        _serviceBusService = serviceBusService;
        _settings = settings.Value;
    }

    [Function("ProcessAgentCall")]
    public async Task ProcessAgentCall(
        [ServiceBusTrigger("agent-call-queue", Connection = "ServiceBusNamespace")] 
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing agent call message: {MessageBody}", messageBody);

        // Apply random delay to distribute load across instances (configurable)
        if (_settings.Evaluation.RandomDelayMaxMs > 0)
        {
            var delayMs = Random.Shared.Next(
                _settings.Evaluation.RandomDelayMinMs,
                _settings.Evaluation.RandomDelayMaxMs);
            
            if (delayMs > 0)
            {
                _logger.LogDebug("Applying random startup delay: {DelayMs}ms", delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        AgentCallMessage? message = null;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse the message
            message = JsonSerializer.Deserialize<AgentCallMessage>(messageBody);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize agent call message: {MessageBody}", messageBody);
                return;
            }

            _logger.LogInformation(
                "Processing agent call: TestCase={TestCaseId}, Run={EvaluationRunId}, Evaluators={EvaluatorCount}",
                message.TestCaseId,
                message.EvaluationRunId,
                message.EvaluatorTypeIds.Count);

            // Get agent configuration (new adapter pattern)
            AgentConfiguration configuration;
            AgentResponse agentResponse;
            
            if (message.AgentToEvalId.HasValue)
            {
                // NEW: Use adapter pattern
                var agentToEval = await _dataverseService.GetAgentToEvalAsync(message.AgentToEvalId.Value, cancellationToken);
                if (agentToEval == null)
                {
                    _logger.LogError("AgentToEval {AgentToEvalId} not found for test case {TestCaseId}", 
                        message.AgentToEvalId.Value, message.TestCaseId);
                    await HandleAgentCallError(message, "Agent configuration not found", cancellationToken);
                    return;
                }
                
                configuration = agentToEval.GetConfiguration();
                var adapter = _agentAdapterFactory.GetAdapter(configuration.AgentType);
                
                _logger.LogInformation(
                    "Using adapter for agent type {AgentType} for test case {TestCaseId}",
                    configuration.AgentType, message.TestCaseId);
                
                var request = new AgentCallRequest
                {
                    UserInput = message.UserInput,
                    ConversationId = message.ConversationId,
                    DirectLineToken = message.DirectLineToken,
                    TestCaseId = message.TestCaseId,
                    EvaluationRunId = message.EvaluationRunId,
                    Configuration = configuration
                };
                
                agentResponse = await adapter.ExecuteAsync(request, cancellationToken);
            }
            else
            {
                // BACKWARD COMPATIBILITY: Use DirectLineUrl directly (legacy path)
                if (string.IsNullOrEmpty(message.DirectLineUrl))
                {
                    _logger.LogError("DirectLineUrl is missing for test case {TestCaseId}", message.TestCaseId);
                    await HandleAgentCallError(message, "DirectLineUrl is not configured", cancellationToken);
                    return;
                }
                
                _logger.LogInformation(
                    "Using legacy DirectLine path for test case {TestCaseId}",
                    message.TestCaseId);
                
                var copilotResponse = await _copilotStudioService.SendMessageAsync(
                    message.DirectLineUrl,
                    message.UserInput,
                    message.ConversationId,
                    message.DirectLineToken,
                    cancellationToken);
                
                // Convert to AgentResponse format
                agentResponse = new AgentResponse
                {
                    Success = copilotResponse.Success,
                    ResponseText = copilotResponse.Response,
                    ExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                    ConversationId = copilotResponse.ConversationId,
                    ErrorMessage = copilotResponse.ErrorMessage,
                    RawActivityJson = copilotResponse.RawActivityJson
                };
            }

            if (!agentResponse.Success)
            {
                _logger.LogWarning(
                    "Agent call failed for test case {TestCaseId}: {ErrorMessage}",
                    message.TestCaseId,
                    agentResponse.ErrorMessage);

                // Check if this is a user error (should NOT be retried)
                bool isUserError = agentResponse.ErrorMessage != null && (
                    agentResponse.ErrorMessage.Contains("PSA-1009") ||
                    agentResponse.ErrorMessage.Contains("PSA-1010") ||
                    agentResponse.ErrorMessage.Contains("PSA-1011") ||
                    agentResponse.ErrorMessage.Contains("Status: 400") ||
                    agentResponse.ErrorMessage.Contains("Status: 404"));

                if (isUserError)
                {
                    // User errors indicate bad test data - mark as failed immediately without retry
                    _logger.LogWarning(
                        "User error detected for test case {TestCaseId} - marking as failed without retry",
                        message.TestCaseId);
                    await HandleAgentCallError(message, agentResponse.ErrorMessage ?? "User error", cancellationToken);
                    return;
                }

                // Retry logic with exponential backoff (Service Bus-level retry in addition to ResilientService retries)
                var maxRetries = _settings.Evaluation?.MaxRetryAttempts ?? 3;
                if (message.RetryCount < maxRetries)
                {
                    var nextRetryCount = message.RetryCount + 1;
                    var retryDelaySeconds = (_settings.Evaluation?.RetryDelaySeconds ?? 30) * nextRetryCount;
                    var scheduledEnqueueTime = DateTimeOffset.UtcNow.AddSeconds(retryDelaySeconds);

                    _logger.LogInformation(
                        "Retrying agent call for test case {TestCaseId} in {DelaySeconds} seconds (attempt {RetryCount}/{MaxRetries})",
                        message.TestCaseId,
                        retryDelaySeconds,
                        nextRetryCount,
                        maxRetries);

                    // Re-queue with incremented retry count and scheduled time
                    var retryMessage = new AgentCallMessage
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        AgentToEvalId = message.AgentToEvalId,
                        DirectLineUrl = message.DirectLineUrl,
                        UserInput = message.UserInput,
                        ExpectedResponse = message.ExpectedResponse,
                        EvaluatorTypeIds = message.EvaluatorTypeIds,
                        ConversationId = message.ConversationId,
                        DirectLineToken = message.DirectLineToken,
                        PassingThreshold = message.PassingThreshold,
                        TestCaseTurnId = message.TestCaseTurnId,
                        RetryCount = nextRetryCount
                    };

                    await _serviceBusService.SendAgentCallMessageAsync(retryMessage, scheduledEnqueueTime, cancellationToken);
                    return;
                }

                // Max retries exceeded - create error results
                _logger.LogError(
                    "Max retries exceeded for test case {TestCaseId}",
                    message.TestCaseId);
                await HandleAgentCallError(message, agentResponse.ErrorMessage ?? "Agent call failed after retries", cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Agent response received: TestCase={TestCaseId}, ExecutionTime={ExecutionTime}ms, ConversationId={ConversationId}",
                message.TestCaseId,
                agentResponse.ExecutionTimeMs,
                agentResponse.ConversationId);

            // Create pending results for each evaluator and queue evaluator jobs
            var evaluatorJobsTasks = message.EvaluatorTypeIds.Select(async evaluatorTypeId =>
            {
                try
                {
                    // Check if evaluator requires expected response (for NA handling)
                    var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId, cancellationToken);

                    // Handle NA scenario: If evaluator requires expected response but none provided
                    if (evaluatorType?.RequiresExpectedResponse == true && string.IsNullOrWhiteSpace(message.ExpectedResponse))
                    {
                        _logger.LogInformation(
                            "Creating NA result for evaluator {EvaluatorTypeId} - no expected response provided",
                            evaluatorTypeId);

                        // Create NA result immediately (don't queue)
                        await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = message.EvaluationRunId,
                            TestCaseId = message.TestCaseId,
                            EvaluatorTypeId = evaluatorTypeId,
                            TestCaseTurnId = message.TestCaseTurnId,
                            StatusCode = EvaluationResultStatus.NotApplicable,
                            Score = 0.0m,
                            Passed = false,
                            Reasoning = $"Evaluator '{evaluatorType?.Name}' requires expected response but none provided",
                            ActualResponse = agentResponse.ResponseText,
                            ConversationId = agentResponse.ConversationId,
                            McsExecutionTimeMs = agentResponse.ExecutionTimeMs,
                            Metadata = agentResponse.RawActivityJson
                        }, cancellationToken);

                        // Increment completion counter
                        await _dataverseService.IncrementCompletedTestCasesAsync(
                            message.EvaluationRunId,
                            failed: false,
                            cancellationToken);

                        return; // Skip queue for NA results
                    }

                    // Create pending result
                    var resultId = await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeId = evaluatorTypeId,
                        TestCaseTurnId = message.TestCaseTurnId,
                        StatusCode = EvaluationResultStatus.Pending,
                        ActualResponse = agentResponse.ResponseText,
                        ConversationId = agentResponse.ConversationId,
                        McsExecutionTimeMs = agentResponse.ExecutionTimeMs,
                        Metadata = agentResponse.RawActivityJson
                    }, cancellationToken);

                    _logger.LogInformation(
                        "Created pending result {ResultId} for evaluator {EvaluatorTypeId}",
                        resultId,
                        evaluatorTypeId);

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
                        "Error processing evaluator {EvaluatorTypeId} for test case {TestCaseId}",
                        evaluatorTypeId,
                        message.TestCaseId);
                    throw;
                }
            });

            // Wait for all evaluator jobs to be queued
            await Task.WhenAll(evaluatorJobsTasks);

            stopwatch.Stop();
            _logger.LogInformation(
                "Agent call processing completed: TestCase={TestCaseId}, TotalTime={TotalTime}ms, EvaluatorsQueued={EvaluatorCount}",
                message.TestCaseId,
                stopwatch.ElapsedMilliseconds,
                message.EvaluatorTypeIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled error processing agent call: TestCase={TestCaseId}",
                message?.TestCaseId);

            if (message != null)
            {
                await HandleAgentCallError(message, $"Unhandled error: {ex.Message}", cancellationToken);
            }

            // Re-throw to trigger Service Bus retry logic
            throw;
        }
    }

    /// <summary>
    /// Handles agent call errors by creating error results for all evaluators
    /// </summary>
    private async Task HandleAgentCallError(AgentCallMessage message, string errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogError(
                "Handling agent call error for test case {TestCaseId}: {ErrorMessage}",
                message.TestCaseId,
                errorMessage);

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
                        TestCaseTurnId = message.TestCaseTurnId,
                        StatusCode = EvaluationResultStatus.Errored,
                        Score = 0.0m,
                        Passed = false,
                        Reasoning = "Agent call failed",
                        ErrorMessage = errorMessage
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
                        "Error creating error result for evaluator {EvaluatorTypeId}",
                        evaluatorTypeId);
                }
            });

            await Task.WhenAll(errorResultTasks);

            _logger.LogInformation(
                "Created error results for {Count} evaluators",
                message.EvaluatorTypeIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling agent call error for test case {TestCaseId}",
                message.TestCaseId);
        }
    }
}
