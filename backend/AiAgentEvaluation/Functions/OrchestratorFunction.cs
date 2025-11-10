using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Net;
using System.Text.Json;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// HTTP Orchestrator Function for starting evaluation runs
/// </summary>
public class OrchestratorFunction
{
    private readonly ILogger<OrchestratorFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IServiceBusService _serviceBusService;

    public OrchestratorFunction(
        ILogger<OrchestratorFunction> logger,
        IDataverseService dataverseService,
        IServiceBusService serviceBusService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _serviceBusService = serviceBusService;
    }

    [Function("StartEvaluationRun")]
    public async Task<HttpResponseData> StartEvaluationRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "evaluations/start")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting evaluation run request received");

        try
        {
            // Parse the request
            var requestBody = await req.ReadAsStringAsync();
            if (string.IsNullOrEmpty(requestBody))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is required");
            }

            var request = JsonSerializer.Deserialize<StartEvaluationRunRequest>(requestBody);
            if (request == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid request format");
            }

            // Validate the request
            if (request.EvaluationRunId == Guid.Empty)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "EvaluationRunId is required");
            }

            _logger.LogInformation("Processing evaluation run {EvaluationRunId}", request.EvaluationRunId);

            // Get the evaluation run from Dataverse
            var evaluationRun = await _dataverseService.GetEvaluationRunAsync(request.EvaluationRunId, cancellationToken);
            if (evaluationRun == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, $"Evaluation run {request.EvaluationRunId} not found");
            }

            // Check if the evaluation run is in the correct state
            if (evaluationRun.StatusCode != EvaluationRunStatus.Execute)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                    $"Evaluation run is not in Execute state. Current status: {evaluationRun.StatusCode}");
            }

            // Update status to Running
            await _dataverseService.UpdateEvaluationRunStatusAsync(request.EvaluationRunId, EvaluationRunStatus.Running, cancellationToken);
            await _dataverseService.SetEvaluationRunStartTimeAsync(request.EvaluationRunId, DateTime.UtcNow, cancellationToken);

            // Get all test cases for this evaluation run
            var testCases = await _dataverseService.GetTestCasesAsync(request.EvaluationRunId, cancellationToken);
            if (!testCases.Any())
            {
                await _dataverseService.UpdateEvaluationRunStatusAsync(request.EvaluationRunId, EvaluationRunStatus.Completed, cancellationToken);
                await _dataverseService.SetEvaluationRunEndTimeAsync(request.EvaluationRunId, DateTime.UtcNow, cancellationToken);
                
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "No test cases found for this evaluation run");
            }

            _logger.LogInformation("Found {TestCaseCount} test cases for evaluation run {EvaluationRunId}", 
                testCases.Count, request.EvaluationRunId);

            // Set the total test cases count in Dataverse to initialize counters
            await _dataverseService.SetEvaluationRunTotalTestCasesAsync(request.EvaluationRunId, testCases.Count, cancellationToken);

            // Get the Direct Line URL from the AgentToEval lookup
            string directLineUrl;
            if (evaluationRun.AgentToEvalId.HasValue)
            {
                var agentToEval = await _dataverseService.GetAgentToEvalAsync(evaluationRun.AgentToEvalId.Value, cancellationToken);
                if (agentToEval == null || string.IsNullOrEmpty(agentToEval.DirectLineSpeechUrl))
                {
                    await _dataverseService.UpdateEvaluationRunStatusAsync(request.EvaluationRunId, EvaluationRunStatus.Errored, cancellationToken);
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                        "Agent to evaluate not found or Direct Line URL is not configured");
                }
                directLineUrl = agentToEval.DirectLineSpeechUrl;
                _logger.LogInformation("Using Direct Line URL from AgentToEval: {DirectLineUrl}", directLineUrl);
            }
            else
            {
                await _dataverseService.UpdateEvaluationRunStatusAsync(request.EvaluationRunId, EvaluationRunStatus.Errored, cancellationToken);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                    "Evaluation run does not have an AgentToEval lookup configured");
            }

            // Separate single-turn and multi-turn test cases
            var passingThreshold = evaluationRun.PassingThreshold ?? 0.7m;
            var singleTurnTestCases = testCases.Where(tc => !tc.IsMultiTurn).ToList();
            var multiTurnTestCases = testCases.Where(tc => tc.IsMultiTurn).ToList();

            _logger.LogInformation("Test case breakdown: {SingleTurn} single-turn, {MultiTurn} multi-turn",
                singleTurnTestCases.Count, multiTurnTestCases.Count);

            // Create Service Bus messages for single-turn test cases
            var singleTurnMessages = singleTurnTestCases.Select(testCase => new ProcessTestCaseMessage
            {
                EvaluationRunId = request.EvaluationRunId,
                TestCaseId = testCase.Id,
                DirectLineUrl = directLineUrl,
                UserInput = testCase.UserInput,
                ExpectedResponse = testCase.ExpectedResponse,
                EvaluatorTypeId = testCase.EvaluatorTypeId,
                ConversationId = testCase.ConversationId,
                PassingThreshold = passingThreshold,
                RetryCount = 0,
                MaxRetries = 3
            }).ToList();

            // Create Service Bus messages for multi-turn test cases
            var multiTurnMessages = multiTurnTestCases.Select(testCase => new ProcessMultiTurnTestCaseMessage
            {
                EvaluationRunId = request.EvaluationRunId,
                TestCaseId = testCase.Id,
                DirectLineUrl = directLineUrl,
                PassingThreshold = passingThreshold,
                RetryCount = 0,
                MaxRetries = 3
            }).ToList();

            // Send messages to Service Bus in batches
            if (singleTurnMessages.Any())
            {
                await _serviceBusService.SendBatchTestCaseMessagesAsync(singleTurnMessages, cancellationToken);
                _logger.LogInformation("Sent {Count} single-turn test case messages", singleTurnMessages.Count);
            }

            // Send multi-turn messages to separate queue
            if (multiTurnMessages.Any())
            {
                await _serviceBusService.SendBatchMultiTurnTestCaseMessagesAsync(multiTurnMessages, cancellationToken);
                _logger.LogInformation("Sent {Count} multi-turn test case messages", multiTurnMessages.Count);
            }

            // Calculate estimated completion time
            // Estimate: ~30 seconds per test case on average (copilot + LLM judge)
            // With concurrency, divide by max concurrent (assume 100 concurrent by default)
            var startTime = DateTime.UtcNow;
            var estimatedSecondsPerTest = 30;
            var estimatedConcurrency = Math.Min(100, request.MaxConcurrency);
            var estimatedTotalSeconds = (testCases.Count * estimatedSecondsPerTest) / estimatedConcurrency;
            var estimatedCompletionTime = startTime.AddSeconds(estimatedTotalSeconds);

            // Build status URL
            var hostUrl = req.Url.GetLeftPart(UriPartial.Authority);
            var statusUrl = $"{hostUrl}/api/evaluations/{request.EvaluationRunId}/status";

            // Create response
            var response = new StartEvaluationRunResponse
            {
                EvaluationRunId = request.EvaluationRunId,
                Status = "Running",
                TotalTestCases = testCases.Count,
                StartTime = startTime,
                EstimatedCompletionTime = estimatedCompletionTime,
                StatusUrl = statusUrl,
                Message = $"Evaluation run started successfully with {testCases.Count} test cases"
            };

            _logger.LogInformation("Successfully started evaluation run {EvaluationRunId} with {TestCaseCount} test cases", 
                request.EvaluationRunId, testCases.Count);

            return await CreateJsonResponse(req, HttpStatusCode.Accepted, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting evaluation run");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred");
        }
    }

    [Function("GetEvaluationRunStatus")]
    public async Task<HttpResponseData> GetEvaluationRunStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/{evaluationRunId}/status")] HttpRequestData req,
        string evaluationRunId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting status for evaluation run {EvaluationRunId}", evaluationRunId);

        try
        {
            if (!Guid.TryParse(evaluationRunId, out var runId))
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid evaluation run ID format");
            }

            var evaluationRun = await _dataverseService.GetEvaluationRunAsync(runId, cancellationToken);
            if (evaluationRun == null)
            {
                return await CreateErrorResponse(req, HttpStatusCode.NotFound, $"Evaluation run {evaluationRunId} not found");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, evaluationRun);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting evaluation run status for {EvaluationRunId}", evaluationRunId);
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred");
        }
    }

    private static async Task<HttpResponseData> CreateJsonResponse<T>(HttpRequestData req, HttpStatusCode statusCode, T data)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
        
        await response.WriteStringAsync(json);
        return response;
    }

    private static async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        
        var errorResponse = new { error = message, timestamp = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });
        
        await response.WriteStringAsync(json);
        return response;
    }
}