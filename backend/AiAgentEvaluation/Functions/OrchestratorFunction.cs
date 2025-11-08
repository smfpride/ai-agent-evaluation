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
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "evaluations/start")] HttpRequestData req,
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

            // Create Service Bus messages for all test cases
            var messages = testCases.Select(testCase => new ProcessTestCaseMessage
            {
                EvaluationRunId = request.EvaluationRunId,
                TestCaseId = testCase.Id,
                AgentId = request.AgentId ?? evaluationRun.AgentId,
                UserInput = testCase.UserInput,
                ExpectedResponse = testCase.ExpectedResponse,
                TestType = testCase.TestType,
                ConversationId = testCase.ConversationId,
                RetryCount = 0,
                MaxRetries = 3
            }).ToList();

            // Send messages to Service Bus in batches
            await _serviceBusService.SendBatchTestCaseMessagesAsync(messages, cancellationToken);

            // Create response
            var response = new StartEvaluationRunResponse
            {
                EvaluationRunId = request.EvaluationRunId,
                Status = "Running",
                TotalTestCases = testCases.Count,
                StartTime = DateTime.UtcNow,
                Message = $"Evaluation run started successfully with {testCases.Count} test cases"
            };

            _logger.LogInformation("Successfully started evaluation run {EvaluationRunId} with {TestCaseCount} test cases", 
                request.EvaluationRunId, testCases.Count);

            return await CreateJsonResponse(req, HttpStatusCode.OK, response);
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