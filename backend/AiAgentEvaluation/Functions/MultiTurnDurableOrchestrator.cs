using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using System.Net;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Durable Functions orchestrator for multi-turn test case execution
/// Eliminates the 5-minute Service Bus timeout limitation
/// </summary>
public class MultiTurnDurableOrchestrator
{
    private readonly ILogger<MultiTurnDurableOrchestrator> _logger;

    public MultiTurnDurableOrchestrator(ILogger<MultiTurnDurableOrchestrator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// HTTP starter function to initiate multi-turn orchestration
    /// Called by OrchestratorFunction.StartEvaluationRun for multi-turn test cases
    /// </summary>
    [Function("StartMultiTurnTestCase")]
    public async Task<HttpResponseData> StartMultiTurnTestCase(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient starter)
    {
        _logger.LogInformation("Starting multi-turn test case orchestration");

        try
        {
            var input = await req.ReadFromJsonAsync<MultiTurnOrchestratorInput>();
            if (input == null)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid input");
                return badResponse;
            }

            // Use test case ID as instance ID for idempotency
            string instanceId = $"multiturn-{input.TestCaseId}";
            
            // Start the orchestration
            await starter.ScheduleNewOrchestrationInstanceAsync(
                "MultiTurnTestCaseOrchestrator",
                input,
                new StartOrchestrationOptions { InstanceId = instanceId });

            _logger.LogInformation("Started orchestration with ID = '{InstanceId}'", instanceId);

            // Return the management URLs for checking status
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                instanceId,
                testCaseId = input.TestCaseId,
                message = "Multi-turn orchestration started"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting multi-turn orchestration");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    /// <summary>
    /// Orchestrator function for multi-turn test case execution
    /// Executes turns sequentially, maintaining conversation state
    /// Can run for hours without timeout
    /// </summary>
    [Function("MultiTurnTestCaseOrchestrator")]
    public async Task<MultiTurnExecutionContext> RunOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<MultiTurnOrchestratorInput>()!;
        var logger = context.CreateReplaySafeLogger<MultiTurnDurableOrchestrator>();

        logger.LogInformation("Starting orchestration for test case {TestCaseId}", input.TestCaseId);

        // Create execution contexts for each evaluator
        var executionContexts = new Dictionary<Guid, MultiTurnExecutionContext>();
        foreach (var evaluatorTypeId in input.EvaluatorTypeIds)
        {
            executionContexts[evaluatorTypeId] = new MultiTurnExecutionContext
            {
                TestRunId = input.TestRunId,
                PassingThreshold = input.PassingThreshold,
                AggregateResultId = input.AggregateResultIds[evaluatorTypeId],
                EvaluatorTypeId = evaluatorTypeId
            };
        }

        try
        {
            // Get test case with turns
            var testCase = await context.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                input.TestCaseId);

            if (testCase == null || testCase.Turns == null || !testCase.Turns.Any())
            {
                logger.LogError("Test case {TestCaseId} not found or has no turns", input.TestCaseId);
                return executionContexts.Values.First();
            }

            // Initialize all contexts with test case info
            foreach (var ctx in executionContexts.Values)
            {
                ctx.TestCase = testCase;
                ctx.Turns = testCase.Turns.OrderBy(t => t.Sequence).ToList();
            }

            logger.LogInformation("Executing {TurnCount} turns for test case {TestCaseId} with {EvaluatorCount} evaluators",
                testCase.Turns.Count, input.TestCaseId, input.EvaluatorTypeIds.Count);

            // Log turn details for debugging
            foreach (var t in testCase.Turns.OrderBy(x => x.Sequence))
            {
                logger.LogInformation("Turn {Sequence}: PromptText='{PromptText}', ExpectedResponse='{ExpectedResponse}'",
                    t.Sequence, t.PromptText, t.ExpectedResponse);
            }

            string? conversationId = null;
            string? directLineToken = null;

            // Execute each turn sequentially
            foreach (var turn in testCase.Turns.OrderBy(t => t.Sequence))
            {
                var turnInput = new ExecuteTurnInput
                {
                    Turn = turn,
                    ConversationId = conversationId,
                    DirectLineToken = directLineToken,
                    DirectLineUrl = input.DirectLineUrl,
                    EvaluatorTypeIds = input.EvaluatorTypeIds,
                    PassingThreshold = input.PassingThreshold,
                    TestRunId = input.TestRunId
                };

                logger.LogInformation("Executing turn {Sequence} of {Total} for test case {TestCaseId} with {EvaluatorCount} evaluators",
                    turn.Sequence, testCase.Turns.Count, input.TestCaseId, input.EvaluatorTypeIds.Count);

                // Execute turn once - will evaluate with all evaluators
                // Returns a list of results (one per evaluator)
                var turnResults = await context.CallActivityAsync<List<TurnExecutionResult>>(
                    "ExecuteSingleTurn",
                    turnInput);

                // Store conversation ID and DirectLine token from first turn
                // Find the first result with a non-empty conversation ID (some evaluators may return NA without calling Copilot)
                if (turn.Sequence == 1 && turnResults.Any())
                {
                    var resultWithConversation = turnResults.FirstOrDefault(r => !string.IsNullOrEmpty(r.ConversationId));
                    if (resultWithConversation != null)
                    {
                        conversationId = resultWithConversation.ConversationId;
                        directLineToken = resultWithConversation.DirectLineToken;
                        
                        foreach (var ctx in executionContexts.Values)
                        {
                            ctx.ConversationId = conversationId;
                        }
                        
                        logger.LogInformation("Captured conversation ID and token from turn 1: {ConversationId}", conversationId);
                    }
                    else
                    {
                        logger.LogWarning("Turn 1 completed but no conversation ID was captured - all evaluators may have returned NA");
                    }
                }

                // Store results for each evaluator
                foreach (var turnResult in turnResults)
                {
                    if (turnResult.EvaluationResultId.HasValue)
                    {
                        // Find which evaluator this result belongs to by matching the result ID
                        // We need to get the EvaluatorTypeId from the result
                        // For now, we'll match by index (assumes results are returned in same order as EvaluatorTypeIds)
                        var evaluatorIndex = turnResults.IndexOf(turnResult);
                        if (evaluatorIndex < input.EvaluatorTypeIds.Count)
                        {
                            var evaluatorTypeId = input.EvaluatorTypeIds[evaluatorIndex];
                            var ctx = executionContexts[evaluatorTypeId];
                            
                            var evaluationResult = new EvaluationResult
                            {
                                Id = turnResult.EvaluationResultId.Value,
                                Passed = turnResult.Passed,
                                Score = turnResult.Score,
                                ExecutionTimeMs = turnResult.ExecutionTimeMs,
                                ErrorMessage = turnResult.ErrorMessage,
                                StatusCode = turnResult.StatusCode
                            };

                            ctx.TurnResults[turn.Sequence] = evaluationResult;
                        }
                    }
                }

                logger.LogInformation("Completed turn {Sequence} for {EvaluatorCount} evaluators",
                    turn.Sequence, turnResults.Count);

                // Optional: Add small delay between turns for rate limiting
                if (turn.Sequence < testCase.Turns.Count)
                {
                    await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(2), CancellationToken.None);
                }
            }

            // Create aggregate results for each evaluator
            logger.LogInformation("Creating {Count} aggregate results for test case {TestCaseId}", 
                input.EvaluatorTypeIds.Count, input.TestCaseId);
            
            foreach (var evaluatorTypeId in input.EvaluatorTypeIds)
            {
                var ctx = executionContexts[evaluatorTypeId];
                
                await context.CallActivityAsync(
                    "CreateAggregateResult",
                    new CreateAggregateResultInput
                    {
                        Context = ctx,
                        TestRunId = input.TestRunId
                });
            }

            logger.LogInformation(
                "Completed multi-turn test case {TestCaseId} with {EvaluatorCount} evaluators",
                input.TestCaseId, input.EvaluatorTypeIds.Count);

            // Return the first context (for backward compatibility)
            return executionContexts.Values.First();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration for test case {TestCaseId}", input.TestCaseId);
            throw;
        }
    }
}
