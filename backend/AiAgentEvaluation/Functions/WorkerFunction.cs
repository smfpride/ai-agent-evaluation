using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Text.Json;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Service Bus Worker Function for processing individual test cases
/// </summary>
public class WorkerFunction
{
    private readonly ILogger<WorkerFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly ILLMJudgeServiceFactory _llmJudgeServiceFactory;
    private readonly IServiceBusService _serviceBusService;

    public WorkerFunction(
        ILogger<WorkerFunction> logger,
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

    [Function("ProcessTestCase")]
    public async Task ProcessTestCase(
        [ServiceBusTrigger("test-case-processing", Connection = "ServiceBusNamespace")] 
        string messageBody,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing test case message: {MessageBody}", messageBody);

        ProcessTestCaseMessage? message = null;
        try
        {
            // Parse the message
            message = JsonSerializer.Deserialize<ProcessTestCaseMessage>(messageBody);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize message: {MessageBody}", messageBody);
                return;
            }

            _logger.LogInformation("Processing test case {TestCaseId} for evaluation run {EvaluationRunId}", 
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

            // Step 1: Get the agent response from Copilot Studio
            var copilotResponse = await _copilotStudioService.SendMessageAsync(
                message.DirectLineUrl,
                message.UserInput, 
                message.ConversationId,
                null, // directLineToken - not used in single-turn scenario
                cancellationToken);

            if (!copilotResponse.Success)
            {
                _logger.LogWarning("Copilot Studio call failed for test case {TestCaseId}: {ErrorMessage}", 
                    message.TestCaseId, copilotResponse.ErrorMessage);
                
                await HandleTestCaseError(message, copilotResponse.ErrorMessage ?? "Copilot Studio call failed", stopwatch, cancellationToken);
                return;
            }

            _logger.LogInformation("Received response from Copilot Studio for test case {TestCaseId} in {ExecutionTime}ms (MCS ExecutionTime: {McsExecutionTime}ms)", 
                message.TestCaseId, copilotResponse.ExecutionTimeMs, copilotResponse.ExecutionTimeMs);
            
            // Log the actual response for debugging
            _logger.LogInformation("Copilot Response Details - TestCaseId: {TestCaseId}, UserInput: '{UserInput}', ActualResponse: '{ActualResponse}', ConversationId: {ConversationId}", 
                message.TestCaseId, message.UserInput, copilotResponse.Response, copilotResponse.ConversationId);

            // Step 2: Evaluate the response with ALL evaluators CONCURRENTLY
            // The Copilot Studio agent was called once, now we fan out to multiple evaluators in parallel
            _logger.LogInformation("Evaluating test case {TestCaseId} with {EvaluatorCount} evaluators (concurrent)", 
                message.TestCaseId, message.EvaluatorTypeIds.Count);

            // Create evaluation tasks for all evaluators
            var evaluationTasks = message.EvaluatorTypeIds.Select(async evaluatorTypeId =>
            {
                try
                {
                    var evaluatorStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    
                    // Use factory to get the appropriate judge service based on evaluator configuration
                    var llmJudgeService = await _llmJudgeServiceFactory.CreateJudgeServiceAsync(
                        evaluatorTypeId, cancellationToken);
                    
                    var judgeRequest = new LLMJudgeRequest
                    {
                        EvaluatorTypeId = evaluatorTypeId,
                        UserInput = message.UserInput,
                        ExpectedResponse = message.ExpectedResponse,
                        ActualResponse = copilotResponse.Response,
                        ConversationId = copilotResponse.ConversationId
                    };

                    var judgeResponse = await llmJudgeService.EvaluateResponseAsync(judgeRequest, cancellationToken);

                    if (!judgeResponse.Success)
                    {
                        _logger.LogWarning("LLM Judge evaluation failed for test case {TestCaseId} with evaluator {EvaluatorTypeId}: {ErrorMessage}", 
                            message.TestCaseId, evaluatorTypeId, judgeResponse.ErrorMessage);
                        
                        // Create error result for this specific evaluator
                        await _dataverseService.CreateEvaluationResultAsync(
                            new CreateEvaluationResultRequest
                            {
                                EvaluationRunId = message.EvaluationRunId,
                                TestCaseId = message.TestCaseId,
                                EvaluatorTypeId = evaluatorTypeId,
                                Score = null,
                                ErrorMessage = judgeResponse.ErrorMessage ?? "LLM Judge evaluation failed",
                                ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                                McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                                Metadata = copilotResponse.RawActivityJson,
                                StatusCode = EvaluationResultStatus.Errored
                            }, 
                            cancellationToken);

                        // Increment failed counter
                        await _dataverseService.IncrementCompletedTestCasesAsync(
                            message.EvaluationRunId, 
                            failed: true, 
                            cancellationToken);
                        
                        return; // Exit this evaluator's task
                    }

                    evaluatorStopwatch.Stop();
                    
                    _logger.LogInformation("LLM Judge evaluation completed for test case {TestCaseId} with evaluator {EvaluatorTypeId}: Score={Score} in {ExecutionTime}ms", 
                        message.TestCaseId, evaluatorTypeId, judgeResponse.Score, judgeResponse.ExecutionTimeMs);

                    // Step 3: Save the evaluation result to Dataverse
                    var passed = judgeResponse.Score >= message.PassingThreshold;

                    var createResultRequest = new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        EvaluatorTypeId = evaluatorTypeId,
                        ActualResponse = copilotResponse.Response,
                        Score = judgeResponse.Score,
                        Passed = passed,
                        Reasoning = judgeResponse.Reasoning,
                        ConversationId = copilotResponse.ConversationId,
                        ExecutionTimeMs = (int)evaluatorStopwatch.ElapsedMilliseconds,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        Metadata = copilotResponse.RawActivityJson,  // Store raw Direct Line activity for debugging
                        StatusCode = EvaluationResultStatus.Completed
                    };
                    
                    _logger.LogInformation("Saving evaluation result - TestCaseId: {TestCaseId}, EvaluatorTypeId: {EvaluatorTypeId}, Score: {Score}", 
                        message.TestCaseId, evaluatorTypeId, judgeResponse.Score);

                    var resultId = await _dataverseService.CreateEvaluationResultAsync(createResultRequest, cancellationToken);

                    // Increment the completed test cases counter for immediate progress feedback
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        message.EvaluationRunId, 
                        failed: false, 
                        cancellationToken);

                    _logger.LogInformation("Successfully processed test case {TestCaseId} with evaluator {EvaluatorTypeId} - Result ID: {ResultId}, Score: {Score}, Passed: {Passed}", 
                        message.TestCaseId, evaluatorTypeId, resultId, judgeResponse.Score, passed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error evaluating test case {TestCaseId} with evaluator {EvaluatorTypeId}", 
                        message.TestCaseId, evaluatorTypeId);
                    
                    // Create error result for this evaluator
                    await _dataverseService.CreateEvaluationResultAsync(
                        new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = message.EvaluationRunId,
                            TestCaseId = message.TestCaseId,
                            EvaluatorTypeId = evaluatorTypeId,
                            Score = null,
                            ErrorMessage = $"Unexpected error: {ex.Message}",
                            McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                            Metadata = copilotResponse.RawActivityJson,
                            StatusCode = EvaluationResultStatus.Errored
                        }, 
                        cancellationToken);

                    // Increment failed counter
                    await _dataverseService.IncrementCompletedTestCasesAsync(
                        message.EvaluationRunId, 
                        failed: true, 
                        cancellationToken);
                }
            }).ToList();

            // Wait for all evaluations to complete
            await Task.WhenAll(evaluationTasks);

            stopwatch.Stop();
            _logger.LogInformation("Completed processing test case {TestCaseId} with {EvaluatorCount} evaluators in {TotalTime}ms", 
                message.TestCaseId, message.EvaluatorTypeIds.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing test case {TestCaseId}", message?.TestCaseId ?? Guid.Empty);
            
            if (message != null)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await HandleTestCaseError(message, $"Unexpected error: {ex.Message}", stopwatch, cancellationToken);
            }
        }
    }

    private async Task HandleTestCaseError(ProcessTestCaseMessage message, string errorMessage, System.Diagnostics.Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        try
        {
            // Check if we should retry
            if (message.RetryCount < message.MaxRetries)
            {
                _logger.LogWarning("Retrying test case {TestCaseId} (attempt {RetryCount}/{MaxRetries})", 
                    message.TestCaseId, message.RetryCount + 1, message.MaxRetries);

                // Create retry message with all evaluator IDs
                var retryMessage = new ProcessTestCaseMessage
                {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    DirectLineUrl = message.DirectLineUrl,
                    UserInput = message.UserInput,
                    ExpectedResponse = message.ExpectedResponse,
                    EvaluatorTypeIds = message.EvaluatorTypeIds,
                    ConversationId = message.ConversationId,
                    PassingThreshold = message.PassingThreshold,
                    RetryCount = message.RetryCount + 1,
                    MaxRetries = message.MaxRetries
                };
                
                // Send back to Service Bus for retry (with delay)
                await Task.Delay(TimeSpan.FromSeconds(30 * (message.RetryCount + 1)), cancellationToken);
                await _serviceBusService.SendTestCaseMessageAsync(retryMessage, cancellationToken);
                
                return;
            }

            // Max retries reached, save error result for ALL evaluators
            stopwatch.Stop();

            foreach (var evaluatorTypeId in message.EvaluatorTypeIds)
            {
                var createResultRequest = new CreateEvaluationResultRequest
                {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    EvaluatorTypeId = evaluatorTypeId,
                    Score = null,
                    ErrorMessage = errorMessage,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    StatusCode = EvaluationResultStatus.Errored
                };

                await _dataverseService.CreateEvaluationResultAsync(createResultRequest, cancellationToken);

                // Increment the completed test cases counter (as failed) for immediate progress feedback
                await _dataverseService.IncrementCompletedTestCasesAsync(
                    message.EvaluationRunId, 
                    failed: true, 
                    cancellationToken);
            }

            _logger.LogError("Failed to process test case {TestCaseId} after {MaxRetries} retries for {EvaluatorCount} evaluators. Error: {ErrorMessage}", 
                message.TestCaseId, message.MaxRetries, message.EvaluatorTypeIds.Count, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling test case failure for {TestCaseId}", message.TestCaseId);
        }
    }
}