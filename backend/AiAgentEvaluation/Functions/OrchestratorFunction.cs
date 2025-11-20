using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using System.Diagnostics;
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

            _logger.LogWarning("[DIAGNOSTIC] Test case breakdown: {SingleTurn} single-turn, {MultiTurn} multi-turn",
                singleTurnTestCases.Count, multiTurnTestCases.Count);
            
            // Log each test case type for debugging
            foreach (var tc in testCases)
            {
                _logger.LogWarning("[DIAGNOSTIC] Test case {TestCaseId} ({Name}): IsMultiTurn={IsMultiTurn}",
                    tc.Id, tc.Name ?? "unnamed", tc.IsMultiTurn);
            }

            // Process single-turn test cases (Phase 1: New Architecture)
            // Queue to agent-call-queue instead of test-case-processing queue
            foreach (var testCase in singleTurnTestCases)
            {
                // Get all evaluator type IDs for this test case
                var evaluatorTypeIds = evaluatorTypes.Select(e => e.Id).ToList();
                
                // Queue agent call message (NA handling moved to AgentWorkerFunction)
                var message = new AgentCallMessage
                {
                    EvaluationRunId = request.EvaluationRunId,
                    TestCaseId = testCase.Id,
                    DirectLineUrl = directLineUrl,
                    UserInput = testCase.UserInput,
                    ExpectedResponse = testCase.ExpectedResponse ?? string.Empty,
                    EvaluatorTypeIds = evaluatorTypeIds,
                    ConversationId = testCase.ConversationId,
                    PassingThreshold = passingThreshold
                };
                
                await _serviceBusService.SendAgentCallMessageAsync(message, cancellationToken);
            }
            
            if (singleTurnTestCases.Any())
            {
                _logger.LogInformation("Sent {Count} single-turn agent call messages to agent-call-queue",
                    singleTurnTestCases.Count);
            }

            // Process multi-turn test cases (Story 11: Service Bus Migration)
            // Queue Turn 1 messages to multi-turn-agent-call-queue
            if (multiTurnTestCases.Any())
            {
                _logger.LogInformation("Processing {Count} multi-turn test cases with {EvaluatorCount} evaluators each", 
                    multiTurnTestCases.Count, evaluatorTypes.Count);
                
                // PERFORMANCE: Fetch all existing aggregates for this evaluation run in one batch query
                // instead of querying per test case (for idempotency checks)
                _logger.LogInformation("Fetching all existing aggregate results for evaluation run {EvaluationRunId} to check idempotency",
                    request.EvaluationRunId);
                var allExistingAggregatesStopwatch = Stopwatch.StartNew();
                var allExistingAggregates = await _dataverseService.GetAggregateResultsForEvaluationRunAsync(
                    request.EvaluationRunId, cancellationToken);
                allExistingAggregatesStopwatch.Stop();
                _logger.LogInformation("Fetched {Count} existing aggregate results in {Ms}ms", 
                    allExistingAggregates.Count, allExistingAggregatesStopwatch.ElapsedMilliseconds);
                
                // Build lookup dictionary for fast idempotency checks
                var aggregateLookup = allExistingAggregates
                    .Where(a => a.TestCaseId != Guid.Empty && a.EvaluatorTypeId.HasValue)
                    .GroupBy(a => (a.TestCaseId, a.EvaluatorTypeId!.Value))
                    .ToDictionary(g => g.Key, g => g.First());
                
                // Get all evaluator type IDs for this run
                var evaluatorTypeIds = evaluatorTypes.Select(e => e.Id).ToList();
                
                foreach (var testCase in multiTurnTestCases)
                {
                    // Check if any aggregate result already exists for this test case + evaluator combination
                    // If all evaluators have completed aggregates, skip this test case
                    var allEvaluatorsCompleted = evaluatorTypes.All(evaluatorType =>
                    {
                        var lookupKey = (testCase.Id, evaluatorType.Id);
                        if (aggregateLookup.TryGetValue(lookupKey, out var existingAggregate))
                        {
                            return existingAggregate.StatusCode != EvaluationResultStatus.Errored &&
                                   existingAggregate.StatusCode != EvaluationResultStatus.Pending;
                        }
                        return false;
                    });
                    
                    if (allEvaluatorsCompleted)
                    {
                        _logger.LogInformation(
                            "Test case {TestCaseId} already has completed aggregate results for all evaluators, skipping",
                            testCase.Id);
                        continue;
                    }
                    
                    // Get turn count from test case
                    var turns = await _dataverseService.GetTestCaseTurnsAsync(testCase.Id, cancellationToken);
                    if (!turns.Any())
                    {
                        _logger.LogWarning("Multi-turn test case {TestCaseId} has no turns defined, skipping", testCase.Id);
                        continue;
                    }
                    
                    var totalTurns = turns.Count;
                    
                    // Create aggregate placeholder results for each evaluator
                    foreach (var evaluatorType in evaluatorTypes)
                    {
                        var lookupKey = (testCase.Id, evaluatorType.Id);
                        if (!aggregateLookup.ContainsKey(lookupKey))
                        {
                            // Create aggregate placeholder
                            await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                            {
                                EvaluationRunId = request.EvaluationRunId,
                                TestCaseId = testCase.Id,
                                EvaluatorTypeId = evaluatorType.Id,
                                TestCaseTurnId = null, // Aggregate has no specific turn
                                StatusCode = EvaluationResultStatus.Pending,
                                IsAggregate = true,
                                Score = 0.0m,
                                Passed = false,
                                Reasoning = "Multi-turn aggregate - awaiting turn completion"
                            }, cancellationToken);
                            
                            _logger.LogInformation(
                                "Created aggregate placeholder for test case {TestCaseId}, evaluator {EvaluatorId}",
                                testCase.Id,
                                evaluatorType.Id);
                        }
                    }
                    
                    // Queue Turn 1 message (one message per test case, includes ALL evaluators)
                    var message = new MultiTurnAgentCallMessage
                    {
                        EvaluationRunId = request.EvaluationRunId,
                        TestCaseId = testCase.Id,
                        EvaluatorTypeIds = evaluatorTypeIds,
                        TurnSequence = 1,
                        TotalTurns = totalTurns,
                        DirectLineUrl = directLineUrl,
                        ConversationId = null, // Turn 1 starts new conversation
                        DirectLineToken = null, // Will be populated from Turn 1 response
                        PassingThreshold = passingThreshold,
                        RetryCount = 0
                    };
                    
                    await _serviceBusService.SendMultiTurnAgentCallMessageAsync(message, cancellationToken);
                    
                    _logger.LogInformation(
                        "Queued Turn 1/{TotalTurns} for multi-turn test case {TestCaseId} with {EvaluatorCount} evaluators",
                        totalTurns,
                        testCase.Id,
                        evaluatorTypeIds.Count);
                }

                _logger.LogInformation("Sent {Count} multi-turn Turn 1 messages to multi-turn-agent-call-queue",
                    multiTurnTestCases.Count);
            }
            else
            {
                _logger.LogInformation("No multi-turn test cases found");
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