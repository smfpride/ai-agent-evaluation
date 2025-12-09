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

            // Queue orchestration message for async processing
            var orchestrationMessage = new StartOrchestrationMessage
            {
                EvaluationRunId = request.EvaluationRunId
            };
            
            await _serviceBusService.SendOrchestrationMessageAsync(orchestrationMessage, cancellationToken);
            
            _logger.LogInformation("Queued orchestration message for evaluation run {EvaluationRunId}", request.EvaluationRunId);

            // Build status URL
            var hostUrl = req.Url.GetLeftPart(UriPartial.Authority);
            var statusUrl = $"{hostUrl}/api/evaluations/{request.EvaluationRunId}/status";

            // Create response
            var response = new StartEvaluationRunResponse
            {
                EvaluationRunId = request.EvaluationRunId,
                Status = "Queued",
                TotalTestCases = 0, // Will be updated by orchestration processor
                StartTime = DateTime.UtcNow,
                EstimatedCompletionTime = null,
                StatusUrl = statusUrl,
                Message = "Evaluation run queued for processing"
            };

            _logger.LogInformation("Successfully queued evaluation run {EvaluationRunId}", request.EvaluationRunId);

            return await CreateJsonResponse(req, HttpStatusCode.Accepted, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting evaluation run");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Internal server error occurred");
        }
    }

    /// <summary>
    /// Processes orchestration messages from Service Bus to execute evaluation runs in the background
    /// </summary>
    [Function("ProcessOrchestration")]
    public async Task ProcessOrchestration(
        [ServiceBusTrigger("orchestration-queue", Connection = "ServiceBusNamespace")] StartOrchestrationMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing orchestration for evaluation run {EvaluationRunId}", message.EvaluationRunId);

        try
        {
            // Get the evaluation run from Dataverse
            var evaluationRun = await _dataverseService.GetEvaluationRunAsync(message.EvaluationRunId, CancellationToken.None);
            if (evaluationRun == null)
            {
                _logger.LogError("Evaluation run {EvaluationRunId} not found", message.EvaluationRunId);
                return;
            }

            // Update status to Running
            await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Running, CancellationToken.None);
            await _dataverseService.SetEvaluationRunStartTimeAsync(message.EvaluationRunId, DateTime.UtcNow, CancellationToken.None);

            // Get all test cases for this evaluation run
            var testCases = await _dataverseService.GetTestCasesAsync(message.EvaluationRunId, CancellationToken.None);
            if (!testCases.Any())
            {
                _logger.LogWarning("No test cases found for evaluation run {EvaluationRunId}", message.EvaluationRunId);
                await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Completed, CancellationToken.None);
                await _dataverseService.SetEvaluationRunEndTimeAsync(message.EvaluationRunId, DateTime.UtcNow, CancellationToken.None);
                return;
            }

            _logger.LogInformation("Found {TestCaseCount} test cases for evaluation run {EvaluationRunId}", 
                testCases.Count, message.EvaluationRunId);

            // Get the Direct Line URL from the AgentToEval lookup
            string directLineUrl;
            if (evaluationRun.AgentToEvalId.HasValue)
            {
                var agentToEval = await _dataverseService.GetAgentToEvalAsync(evaluationRun.AgentToEvalId.Value, CancellationToken.None);
                if (agentToEval == null || string.IsNullOrEmpty(agentToEval.DirectLineSpeechUrl))
                {
                    _logger.LogError("Agent to evaluate not found or Direct Line URL is not configured for evaluation run {EvaluationRunId}", message.EvaluationRunId);
                    await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Errored, CancellationToken.None);
                    return;
                }
                directLineUrl = agentToEval.DirectLineSpeechUrl;
                _logger.LogInformation("Using Direct Line URL from AgentToEval: {DirectLineUrl}", directLineUrl);
            }
            else
            {
                _logger.LogError("Evaluation run {EvaluationRunId} does not have an AgentToEval lookup configured", message.EvaluationRunId);
                await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Errored, CancellationToken.None);
                return;
            }

            // Fetch evaluator types for this run (N:N lookup)
            var evaluatorTypes = await _dataverseService.GetEvaluatorTypesForRunAsync(
                message.EvaluationRunId, 
                CancellationToken.None);
            
            if (!evaluatorTypes.Any())
            {
                _logger.LogError("Evaluation run {EvaluationRunId} must have at least one evaluator type configured", message.EvaluationRunId);
                await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Errored, CancellationToken.None);
                return;
            }

            _logger.LogInformation("Found {EvaluatorCount} evaluator types for evaluation run {EvaluationRunId}",
                evaluatorTypes.Count, message.EvaluationRunId);

            // Calculate total evaluation units (TestCase × EvaluatorType pairs)
            int totalUnits = CalculateTotalEvaluationUnits(testCases, evaluatorTypes.Count);
            await _dataverseService.SetEvaluationRunTotalTestCasesAsync(
                message.EvaluationRunId, 
                totalUnits, 
                CancellationToken.None);

            _logger.LogInformation("Total evaluation units: {TotalUnits} ({TestCaseCount} test cases × {EvaluatorCount} evaluators)",
                totalUnits, testCases.Count, evaluatorTypes.Count);

            // Separate single-turn and multi-turn test cases
            var passingThreshold = evaluationRun.PassingThreshold ?? 0.7m;
            var singleTurnTestCases = testCases.Where(tc => !tc.IsMultiTurn).ToList();
            var multiTurnTestCases = testCases.Where(tc => tc.IsMultiTurn).ToList();

            _logger.LogInformation("Test case breakdown: {SingleTurn} single-turn, {MultiTurn} multi-turn",
                singleTurnTestCases.Count, multiTurnTestCases.Count);

            // Process single-turn test cases (Phase 1: New Architecture)
            // Queue to agent-call-queue in parallel for performance
            if (singleTurnTestCases.Any())
            {
                _logger.LogInformation("Queuing {Count} single-turn test cases in parallel", singleTurnTestCases.Count);
                var singleTurnStopwatch = Stopwatch.StartNew();
                
                var evaluatorTypeIds = evaluatorTypes.Select(e => e.Id).ToList();
                
                var queueTasks = singleTurnTestCases.Select(async testCase =>
                {
                    var agentCallMessage = new AgentCallMessage
                    {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = testCase.Id,
                        AgentToEvalId = evaluationRun.AgentToEvalId,
                        DirectLineUrl = directLineUrl,
                        UserInput = testCase.UserInput,
                        ExpectedResponse = testCase.ExpectedResponse ?? string.Empty,
                        EvaluatorTypeIds = evaluatorTypeIds,
                        ConversationId = testCase.ConversationId,
                        PassingThreshold = passingThreshold
                    };
                    
                    await _serviceBusService.SendAgentCallMessageAsync(agentCallMessage, CancellationToken.None);
                });
                
                await Task.WhenAll(queueTasks);
                singleTurnStopwatch.Stop();
                
                _logger.LogInformation("Sent {Count} single-turn agent call messages to agent-call-queue in {Ms}ms",
                    singleTurnTestCases.Count, singleTurnStopwatch.ElapsedMilliseconds);
            }

            // Process multi-turn test cases (Story 11: Service Bus Migration with Performance Optimization)
            // Queue Turn 1 messages to multi-turn-agent-call-queue
            if (multiTurnTestCases.Any())
            {
                _logger.LogInformation("Processing {Count} multi-turn test cases with {EvaluatorCount} evaluators each", 
                    multiTurnTestCases.Count, evaluatorTypes.Count);
                
                var multiTurnOverallStopwatch = Stopwatch.StartNew();
                
                // PERFORMANCE: Fetch all existing aggregates for this evaluation run in one batch query
                _logger.LogInformation("Fetching all existing aggregate results for evaluation run {EvaluationRunId} to check idempotency",
                    message.EvaluationRunId);
                var allExistingAggregatesStopwatch = Stopwatch.StartNew();
                var allExistingAggregates = await _dataverseService.GetAggregateResultsForEvaluationRunAsync(
                    message.EvaluationRunId, CancellationToken.None);
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
                
                // PERFORMANCE: Batch-fetch ALL turns for ALL multi-turn test cases in one query
                // Use testSetId to avoid IN operator issues with large test case lists
                _logger.LogInformation("Batch-fetching turns for {Count} multi-turn test cases from test set {TestSetId}", 
                    multiTurnTestCases.Count, evaluationRun.TestSetId);
                var allTurnsStopwatch = Stopwatch.StartNew();
                var allTurnsByTestCase = await _dataverseService.GetAllTurnsForTestCasesAsync(
                    evaluationRun.TestSetId!.Value, 
                    CancellationToken.None);
                allTurnsStopwatch.Stop();
                _logger.LogInformation("Fetched turns for {TestCaseCount} test cases in {Ms}ms", 
                    allTurnsByTestCase.Count, allTurnsStopwatch.ElapsedMilliseconds);
                
                // Filter test cases that need processing
                var testCasesToProcess = new List<(TestCase TestCase, int TurnCount)>();
                
                foreach (var testCase in multiTurnTestCases)
                {
                    _logger.LogInformation("[DIAGNOSTIC] Processing test case {TestCaseId} ({Name})", 
                        testCase.Id, testCase.Name ?? "unnamed");
                    
                    // Check if all evaluators have completed aggregates
                    var allEvaluatorsCompleted = evaluatorTypes.All(evaluatorType =>
                    {
                        var lookupKey = (testCase.Id, evaluatorType.Id);
                        if (aggregateLookup.TryGetValue(lookupKey, out var existingAggregate))
                        {
                            var isCompleted = existingAggregate.StatusCode != EvaluationResultStatus.Errored &&
                                   existingAggregate.StatusCode != EvaluationResultStatus.Pending;
                            _logger.LogInformation("[DIAGNOSTIC] Test case {TestCaseId}, Evaluator {EvaluatorId}: Aggregate exists with status {Status}, isCompleted={IsCompleted}",
                                testCase.Id, evaluatorType.Id, existingAggregate.StatusCode, isCompleted);
                            return isCompleted;
                        }
                        _logger.LogInformation("[DIAGNOSTIC] Test case {TestCaseId}, Evaluator {EvaluatorId}: No aggregate found",
                            testCase.Id, evaluatorType.Id);
                        return false;
                    });
                    
                    _logger.LogInformation("[DIAGNOSTIC] Test case {TestCaseId}: allEvaluatorsCompleted={AllCompleted}",
                        testCase.Id, allEvaluatorsCompleted);
                    
                    if (allEvaluatorsCompleted)
                    {
                        _logger.LogInformation(
                            "Test case {TestCaseId} already has completed aggregate results for all evaluators, skipping",
                            testCase.Id);
                        continue;
                    }
                    
                    // Check if turns exist for this test case
                    if (!allTurnsByTestCase.TryGetValue(testCase.Id, out var turns))
                    {
                        _logger.LogWarning("[DIAGNOSTIC] Multi-turn test case {TestCaseId} not found in allTurnsByTestCase dictionary (keys: {Keys})", 
                            testCase.Id, string.Join(", ", allTurnsByTestCase.Keys));
                        continue;
                    }
                    
                    if (!turns.Any())
                    {
                        _logger.LogWarning("[DIAGNOSTIC] Multi-turn test case {TestCaseId} has empty turns list", testCase.Id);
                        continue;
                    }
                    
                    _logger.LogInformation("[DIAGNOSTIC] Test case {TestCaseId} has {TurnCount} turns, adding to processing queue",
                        testCase.Id, turns.Count);
                    testCasesToProcess.Add((testCase, turns.Count));
                }
                
                _logger.LogInformation("{ProcessCount} multi-turn test cases need processing (out of {TotalCount})",
                    testCasesToProcess.Count, multiTurnTestCases.Count);
                
                if (testCasesToProcess.Any())
                {
                    // PERFORMANCE: Create aggregate placeholders in parallel with throttling to respect Dataverse rate limits
                    _logger.LogInformation("Creating aggregate placeholders for {Count} test cases with {EvaluatorCount} evaluators (parallel, throttled)",
                        testCasesToProcess.Count, evaluatorTypes.Count);
                    var createPlaceholdersStopwatch = Stopwatch.StartNew();
                    
                    // Use SemaphoreSlim to throttle concurrent Dataverse writes (limit: 10)
                    var dataverseSemaphore = new SemaphoreSlim(10, 10);
                    
                    var createTasks = testCasesToProcess.SelectMany(tuple =>
                    {
                        var testCase = tuple.TestCase;
                        
                        return evaluatorTypes.Select(async evaluatorType =>
                        {
                            var lookupKey = (testCase.Id, evaluatorType.Id);
                            if (!aggregateLookup.ContainsKey(lookupKey))
                            {
                                await dataverseSemaphore.WaitAsync(CancellationToken.None);
                                try
                                {
                                    await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
                                    {
                                        EvaluationRunId = message.EvaluationRunId,
                                        TestCaseId = testCase.Id,
                                        EvaluatorTypeId = evaluatorType.Id,
                                        TestCaseTurnId = null,
                                        StatusCode = EvaluationResultStatus.Pending,
                                        IsAggregate = true,
                                        Score = 0.0m,
                                        Passed = false,
                                        Reasoning = "Multi-turn aggregate - awaiting turn completion"
                                    }, CancellationToken.None);
                                }
                                finally
                                {
                                    dataverseSemaphore.Release();
                                }
                            }
                        });
                    });
                    
                    await Task.WhenAll(createTasks);
                    createPlaceholdersStopwatch.Stop();
                    _logger.LogInformation("Created aggregate placeholders in {Ms}ms", createPlaceholdersStopwatch.ElapsedMilliseconds);
                    
                    // PERFORMANCE: Queue all Turn 1 messages in parallel
                    _logger.LogInformation("Queuing Turn 1 messages for {Count} test cases in parallel", testCasesToProcess.Count);
                    var queueMessagesStopwatch = Stopwatch.StartNew();
                    
                    var queueTasks = testCasesToProcess.Select(async tuple =>
                    {
                        var (testCase, totalTurns) = tuple;
                        
                        var multiTurnMessage = new MultiTurnAgentCallMessage
                        {
                            EvaluationRunId = message.EvaluationRunId,
                            TestCaseId = testCase.Id,
                            EvaluatorTypeIds = evaluatorTypeIds,
                            TurnSequence = 1,
                            TotalTurns = totalTurns,
                            DirectLineUrl = directLineUrl,
                            ConversationId = null,
                            DirectLineToken = null,
                            PassingThreshold = passingThreshold,
                            RetryCount = 0
                        };
                        
                        await _serviceBusService.SendMultiTurnAgentCallMessageAsync(multiTurnMessage, CancellationToken.None);
                    });
                    
                    await Task.WhenAll(queueTasks);
                    queueMessagesStopwatch.Stop();
                    
                    multiTurnOverallStopwatch.Stop();
                    
                    _logger.LogInformation(
                        "Sent {Count} multi-turn Turn 1 messages to multi-turn-agent-call-queue in {QueueMs}ms (total multi-turn processing: {TotalMs}ms)",
                        testCasesToProcess.Count,
                        queueMessagesStopwatch.ElapsedMilliseconds,
                        multiTurnOverallStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation("No multi-turn test cases need processing (all already completed or have no turns)");
                }
            }
            else
            {
                _logger.LogInformation("No multi-turn test cases found");
            }

            _logger.LogInformation("Successfully queued all test cases for evaluation run {EvaluationRunId} with {TestCaseCount} test cases and {EvaluatorCount} evaluators ({TotalUnits} units)", 
                message.EvaluationRunId, testCases.Count, evaluatorTypes.Count, totalUnits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing orchestration for evaluation run {EvaluationRunId}", message.EvaluationRunId);
            await _dataverseService.UpdateEvaluationRunStatusAsync(message.EvaluationRunId, EvaluationRunStatus.Errored, CancellationToken.None);
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