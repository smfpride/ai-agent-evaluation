using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
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
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly IServiceBusService _serviceBusService;

    public AgentWorkerFunction(
        ILogger<AgentWorkerFunction> logger,
        IDataverseService dataverseService,
        ICopilotStudioService copilotStudioService,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _copilotStudioService = copilotStudioService;
        _serviceBusService = serviceBusService;
    }

    [Function("ProcessAgentCall")]
    public async Task ProcessAgentCall(
        [ServiceBusTrigger("agent-call-queue", Connection = "ServiceBusNamespace")] 
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing agent call message: {MessageBody}", messageBody);

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

            // Validate DirectLineUrl
            if (string.IsNullOrEmpty(message.DirectLineUrl))
            {
                _logger.LogError("DirectLineUrl is missing for test case {TestCaseId}", message.TestCaseId);
                await HandleAgentCallError(message, "DirectLineUrl is not configured", cancellationToken);
                return;
            }

            // Call Copilot Studio with retry (exponential backoff handled by ResilientService)
            var copilotResponse = await _copilotStudioService.SendMessageAsync(
                message.DirectLineUrl,
                message.UserInput,
                message.ConversationId,
                message.DirectLineToken,
                cancellationToken);

            if (!copilotResponse.Success)
            {
                _logger.LogWarning(
                    "Copilot Studio call failed for test case {TestCaseId}: {ErrorMessage}",
                    message.TestCaseId,
                    copilotResponse.ErrorMessage);

                await HandleAgentCallError(message, copilotResponse.ErrorMessage ?? "Copilot Studio call failed", cancellationToken);
                return;
            }

            _logger.LogInformation(
                "Copilot Studio response received: TestCase={TestCaseId}, ExecutionTime={ExecutionTime}ms, ConversationId={ConversationId}",
                message.TestCaseId,
                copilotResponse.ExecutionTimeMs,
                copilotResponse.ConversationId);

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

                    // Create pending result
                    var resultId = await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeId = evaluatorTypeId,
                        TestCaseTurnId = message.TestCaseTurnId,
                        StatusCode = EvaluationResultStatus.Pending,
                        ActualResponse = copilotResponse.Response,
                        ConversationId = copilotResponse.ConversationId,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        Metadata = copilotResponse.RawActivityJson
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
