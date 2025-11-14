using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
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
        [DurableClient] DurableTaskClient durableClient,
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

            // Fetch evaluator types for this run (N:N lookup)
            var evaluatorTypes = await _dataverseService.GetEvaluatorTypesForRunAsync(
                request.EvaluationRunId, 
                cancellationToken);
            
            if (!evaluatorTypes.Any())
            {
                await _dataverseService.UpdateEvaluationRunStatusAsync(request.EvaluationRunId, EvaluationRunStatus.Errored, cancellationToken);
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, 
                    "Evaluation run must have at least one evaluator type configured");
            }

            _logger.LogInformation("Found {EvaluatorCount} evaluator types for evaluation run {EvaluationRunId}",
                evaluatorTypes.Count, request.EvaluationRunId);

            // Calculate total evaluation units (TestCase × EvaluatorType pairs)
            int totalUnits = CalculateTotalEvaluationUnits(testCases, evaluatorTypes.Count);
            await _dataverseService.SetEvaluationRunTotalTestCasesAsync(
                request.EvaluationRunId, 
                totalUnits, 
                cancellationToken);

            _logger.LogInformation("Total evaluation units: {TotalUnits} ({TestCaseCount} test cases × {EvaluatorCount} evaluators)",
                totalUnits, testCases.Count, evaluatorTypes.Count);

            // Separate single-turn and multi-turn test cases
            var passingThreshold = evaluationRun.PassingThreshold ?? 0.7m;
            var singleTurnTestCases = testCases.Where(tc => !tc.IsMultiTurn).ToList();
            var multiTurnTestCases = testCases.Where(tc => tc.IsMultiTurn).ToList();

            _logger.LogInformation("Test case breakdown: {SingleTurn} single-turn, {MultiTurn} multi-turn",
                singleTurnTestCases.Count, multiTurnTestCases.Count);

            // Process single-turn test cases
            int naResultsCreated = 0;
            
            foreach (var testCase in singleTurnTestCases)
            {
                // Determine which evaluators are applicable for this test case
                var applicableEvaluators = new List<Guid>();
                
                foreach (var evaluatorType in evaluatorTypes)
                {
                    // Check if Not Applicable (NA) scenario
                    bool isNA = evaluatorType.RequiresExpectedResponse && 
                                string.IsNullOrWhiteSpace(testCase.ExpectedResponse);
                    
                    if (isNA)
                    {
                        // Short-circuit: Create NA result immediately
                        await _dataverseService.CreateEvaluationResultAsync(
                            new CreateEvaluationResultRequest
                            {
                                EvaluationRunId = request.EvaluationRunId,
                                TestCaseId = testCase.Id,
                                EvaluatorTypeId = evaluatorType.Id,
                                StatusCode = EvaluationResultStatus.NotApplicable,
                                Score = 0.0m,
                                Passed = false,
                                Reasoning = $"Evaluator '{evaluatorType.Name}' requires expected response but none provided",
                                ActualResponse = null,
                                ConversationId = null
                            }, 
                            cancellationToken);
                        
                        // Increment completion counter (NA counts as completed)
                        await _dataverseService.IncrementCompletedTestCasesAsync(
                            request.EvaluationRunId, 
                            failed: false,
                            cancellationToken);
                        
                        naResultsCreated++;
                        
                        _logger.LogInformation(
                            "Test case {TestCaseId} marked NA for evaluator {EvaluatorId} (missing expected response)",
                            testCase.Id, evaluatorType.Id);
                    }
                    else
                    {
                        // This evaluator is applicable
                        applicableEvaluators.Add(evaluatorType.Id);
                    }
                }
                
                // If there are applicable evaluators, queue ONE message with all evaluator IDs
                if (applicableEvaluators.Any())
                {
                    var message = new ProcessTestCaseMessage
                    {
                        EvaluationRunId = request.EvaluationRunId,
                        TestCaseId = testCase.Id,
                        DirectLineUrl = directLineUrl,
                        UserInput = testCase.UserInput,
                        ExpectedResponse = testCase.ExpectedResponse,
                        EvaluatorTypeIds = applicableEvaluators,
                        ConversationId = testCase.ConversationId,
                        PassingThreshold = passingThreshold,
                        RetryCount = 0,
                        MaxRetries = 3
                    };
                    
                    await _serviceBusService.SendTestCaseMessageAsync(message, cancellationToken);
                }
            }
            
            if (singleTurnTestCases.Any())
            {
                _logger.LogInformation("Sent {Count} single-turn test case messages to Service Bus ({NACount} marked NA)",
                    singleTurnTestCases.Count, naResultsCreated);
            }

            // Start Durable Functions orchestrations for multi-turn test cases
            if (multiTurnTestCases.Any())
            {
                foreach (var testCase in multiTurnTestCases)
                {
                    // Check if orchestration already started (idempotency)
                    var existingResult = await _dataverseService.GetAggregateResultForTestCaseAsync(
                        request.EvaluationRunId, testCase.Id, cancellationToken);
                    
                    if (existingResult != null && 
                        existingResult.StatusCode != EvaluationResultStatus.Errored)
                    {
                        _logger.LogInformation(
                            "Test case {TestCaseId} already has aggregate result {ResultId} with status {Status}, skipping",
                            testCase.Id, existingResult.Id, existingResult.StatusCode);
                        continue; // Idempotent - don't start duplicate
                    }
                    
                    // Create placeholder aggregate results for ALL evaluators
                    var aggregateResultIds = new Dictionary<Guid, Guid>();
                    var applicableEvaluators = new List<Guid>();
                    
                    foreach (var evaluatorType in evaluatorTypes)
                    {
                        // Create placeholder aggregate result with Pending status
                        var placeholderRequest = new CreateEvaluationResultRequest
                        {
                            EvaluationRunId = request.EvaluationRunId,
                            TestCaseId = testCase.Id,
                            EvaluatorTypeId = evaluatorType.Id,
                            IsAggregate = true,
                            StatusCode = EvaluationResultStatus.Pending,
                            // No score, no response yet
                        };
                        
                        var resultId = await _dataverseService.CreateEvaluationResultAsync(
                            placeholderRequest, cancellationToken);
                        
                        aggregateResultIds[evaluatorType.Id] = resultId;
                        applicableEvaluators.Add(evaluatorType.Id);
                        
                        _logger.LogInformation(
                            "Created placeholder aggregate result {ResultId} for test case {TestCaseId} with evaluator {EvaluatorId}",
                            resultId, testCase.Id, evaluatorType.Id);
                    }
                    
                    // Generate unique instance ID
                    string instanceId = $"multiturn-{testCase.Id}";
                    
                    var orchestratorInput = new MultiTurnOrchestratorInput
                    {
                        TestCaseId = testCase.Id,
                        TestRunId = request.EvaluationRunId,
                        DirectLineUrl = directLineUrl,
                        EvaluatorTypeIds = applicableEvaluators,
                        PassingThreshold = passingThreshold,
                        AggregateResultIds = aggregateResultIds // Map of evaluator -> aggregate result
                    };
                    
                    try
                    {
                        await durableClient.ScheduleNewOrchestrationInstanceAsync(
                            "MultiTurnTestCaseOrchestrator",
                            orchestratorInput,
                            new StartOrchestrationOptions 
                            { 
                                InstanceId = instanceId 
                            });

                        _logger.LogInformation("Started Durable Functions orchestration {InstanceId} for test case {TestCaseId} with {EvaluatorCount} evaluators",
                            instanceId, testCase.Id, applicableEvaluators.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start orchestration for test case {TestCaseId}", 
                            testCase.Id);
                        // Continue with other test cases
                    }
                }

                _logger.LogInformation("Started {Count} Durable Functions orchestrations for multi-turn test cases",
                    multiTurnTestCases.Count);
            }

            // Calculate estimated completion time
            // Estimate: ~30 seconds per evaluation unit on average (copilot + LLM judge)
            // With concurrency of 100 (from host.json maxConcurrentActivityFunctions)
            var startTime = DateTime.UtcNow;
            var estimatedSecondsPerTest = 30;
            var estimatedConcurrency = 100;
            var estimatedTotalSeconds = (totalUnits * estimatedSecondsPerTest) / estimatedConcurrency;
            var estimatedCompletionTime = startTime.AddSeconds(estimatedTotalSeconds);

            // Build status URL
            var hostUrl = req.Url.GetLeftPart(UriPartial.Authority);
            var statusUrl = $"{hostUrl}/api/evaluations/{request.EvaluationRunId}/status";

            // Create response
            var response = new StartEvaluationRunResponse
            {
                EvaluationRunId = request.EvaluationRunId,
                Status = "Running",
                TotalTestCases = totalUnits,
                StartTime = startTime,
                EstimatedCompletionTime = estimatedCompletionTime,
                StatusUrl = statusUrl,
                Message = $"Evaluation run started successfully with {testCases.Count} test cases and {evaluatorTypes.Count} evaluators ({totalUnits} total evaluation units)"
            };

            _logger.LogInformation("Successfully started evaluation run {EvaluationRunId} with {TestCaseCount} test cases and {EvaluatorCount} evaluators ({TotalUnits} units)", 
                request.EvaluationRunId, testCases.Count, evaluatorTypes.Count, totalUnits);

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

    /// <summary>
    /// Calculates total evaluation units accounting for multi-evaluator support
    /// Formula: Σ(single-turn: evaluatorCount) + Σ(multi-turn: (turnCount × evaluatorCount) + evaluatorCount for aggregates)
    /// </summary>
    private static int CalculateTotalEvaluationUnits(List<TestCase> testCases, int evaluatorCount)
    {
        int total = 0;
        
        foreach (var testCase in testCases)
        {
            if (testCase.IsMultiTurn)
            {
                int turnCount = testCase.Turns?.Count ?? 0;
                // Multi-turn: (turns × evaluators) + (1 aggregate per evaluator)
                total += (turnCount * evaluatorCount) + evaluatorCount;
            }
            else
            {
                // Single-turn: 1 result per evaluator
                total += evaluatorCount;
            }
        }
        
        return total;
    }
}