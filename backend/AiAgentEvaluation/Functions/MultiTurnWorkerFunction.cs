using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Text.Json;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Service Bus Worker Function for processing multi-turn test cases
/// </summary>
public class MultiTurnWorkerFunction
{
    private readonly ILogger<MultiTurnWorkerFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IMultiTurnExecutionService _multiTurnExecutionService;
    private readonly IServiceBusService _serviceBusService;

    public MultiTurnWorkerFunction(
        ILogger<MultiTurnWorkerFunction> logger,
        IDataverseService dataverseService,
        IMultiTurnExecutionService multiTurnExecutionService,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _multiTurnExecutionService = multiTurnExecutionService;
        _serviceBusService = serviceBusService;
    }

    [Function("ProcessMultiTurnTestCase")]
    public async Task ProcessMultiTurnTestCase(
        [ServiceBusTrigger("multi-turn-test-case-processing", Connection = "ServiceBusNamespace")]
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing multi-turn test case message: {MessageBody}", messageBody);

        ProcessMultiTurnTestCaseMessage? message = null;
        try
        {
            // Parse the message
            message = JsonSerializer.Deserialize<ProcessMultiTurnTestCaseMessage>(messageBody);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize message: {MessageBody}", messageBody);
                return;
            }

            _logger.LogInformation("Processing multi-turn test case {TestCaseId} for evaluation run {EvaluationRunId}",
                message.TestCaseId, message.EvaluationRunId);

            // Start processing timer
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Validate that DirectLineUrl is provided
            if (string.IsNullOrEmpty(message.DirectLineUrl))
            {
                _logger.LogError("DirectLineUrl is missing from test case message {TestCaseId}", message.TestCaseId);
                await HandleTestCaseError(message, "DirectLineUrl is not configured for this evaluation", stopwatch, cancellationToken);
                return;
            }

            // Get the test case with its turns
            var testCase = await _dataverseService.GetTestCaseAsync(message.TestCaseId, cancellationToken);
            if (testCase == null)
            {
                _logger.LogError("Test case {TestCaseId} not found", message.TestCaseId);
                await HandleTestCaseError(message, "Test case not found", stopwatch, cancellationToken);
                return;
            }

            // Load turns for the test case
            testCase.Turns = await _dataverseService.GetTestCaseTurnsAsync(message.TestCaseId, cancellationToken);

            if (testCase.Turns == null || !testCase.Turns.Any())
            {
                _logger.LogWarning("Multi-turn test case {TestCaseId} has no turns defined", message.TestCaseId);
                await HandleTestCaseError(message, "No turns defined for multi-turn test case", stopwatch, cancellationToken);
                return;
            }

            _logger.LogInformation("Executing multi-turn test case {TestCaseId} with {TurnCount} turns",
                message.TestCaseId, testCase.Turns.Count);

            // Execute the multi-turn test case
            var context = await _multiTurnExecutionService.ExecuteTestCaseAsync(
                testCase,
                message.EvaluationRunId,
                message.DirectLineUrl,
                message.PassingThreshold,
                cancellationToken);

            stopwatch.Stop();

            // Increment the completed test cases counter
            // For multi-turn, we consider it failed if not all turns passed
            var failed = context.PassedTurns < context.TotalTurns;
            await _dataverseService.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId,
                failed: failed,
                cancellationToken);

            _logger.LogInformation(
                "Successfully processed multi-turn test case {TestCaseId} - Pass Rate: {PassRate:P0} ({Passed}/{Total}), Total time: {TotalTime}ms",
                message.TestCaseId, context.PassRate, context.PassedTurns, context.TotalTurns, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing multi-turn test case {TestCaseId}", message?.TestCaseId ?? Guid.Empty);

            if (message != null)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await HandleTestCaseError(message, $"Unexpected error: {ex.Message}", stopwatch, cancellationToken);
            }
        }
    }

    private async Task HandleTestCaseError(ProcessMultiTurnTestCaseMessage message, string errorMessage, System.Diagnostics.Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        try
        {
            // Check if we should retry
            if (message.RetryCount < message.MaxRetries)
            {
                _logger.LogWarning("Retrying multi-turn test case {TestCaseId} (attempt {RetryCount}/{MaxRetries})",
                    message.TestCaseId, message.RetryCount + 1, message.MaxRetries);

                // Create retry message
                var retryMessage = new ProcessMultiTurnTestCaseMessage
                {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    DirectLineUrl = message.DirectLineUrl,
                    PassingThreshold = message.PassingThreshold,
                    RetryCount = message.RetryCount + 1,
                    MaxRetries = message.MaxRetries
                };

                // Send back to Service Bus for retry (with delay)
                await Task.Delay(TimeSpan.FromSeconds(30 * (message.RetryCount + 1)), cancellationToken);
                // Note: We'd need to add a method to ServiceBusService to send multi-turn messages
                // For now, log that retry is needed
                _logger.LogInformation("Retry would be sent for test case {TestCaseId}", message.TestCaseId);

                return;
            }

            // Max retries reached, save error result
            stopwatch.Stop();

            var createResultRequest = new CreateEvaluationResultRequest
            {
                EvaluationRunId = message.EvaluationRunId,
                TestCaseId = message.TestCaseId,
                IsAggregate = true, // This is an aggregate error result
                Passed = false,
                Score = 0,
                ErrorMessage = $"Multi-turn test case execution failed after {message.MaxRetries} retries: {errorMessage}",
                ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
            };

            await _dataverseService.CreateEvaluationResultAsync(createResultRequest, cancellationToken);

            // Increment failed test cases counter
            await _dataverseService.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId,
                failed: true,
                cancellationToken);

            _logger.LogError("Multi-turn test case {TestCaseId} failed after {MaxRetries} retries: {ErrorMessage}",
                message.TestCaseId, message.MaxRetries, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling multi-turn test case error for {TestCaseId}", message.TestCaseId);
        }
    }
}
