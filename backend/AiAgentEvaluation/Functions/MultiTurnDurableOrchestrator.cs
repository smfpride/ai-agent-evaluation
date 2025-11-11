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

        var executionContext = new MultiTurnExecutionContext
        {
            TestRunId = input.TestRunId,
            PassingThreshold = input.PassingThreshold
        };

        try
        {
            // Get test case with turns
            var testCase = await context.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                input.TestCaseId);

            if (testCase == null || testCase.Turns == null || !testCase.Turns.Any())
            {
                logger.LogError("Test case {TestCaseId} not found or has no turns", input.TestCaseId);
                return executionContext;
            }

            executionContext.TestCase = testCase;
            executionContext.Turns = testCase.Turns.OrderBy(t => t.Sequence).ToList();

            logger.LogInformation("Executing {TurnCount} turns for test case {TestCaseId}",
                executionContext.TotalTurns, input.TestCaseId);

            string? conversationId = null;

            // Execute each turn sequentially
            foreach (var turn in executionContext.Turns)
            {
                var turnInput = new ExecuteTurnInput
                {
                    Turn = turn,
                    ConversationId = conversationId,
                    DirectLineUrl = input.DirectLineUrl,
                    EvaluatorTypeId = input.EvaluatorTypeId,
                    PassingThreshold = input.PassingThreshold,
                    TestRunId = input.TestRunId
                };

                logger.LogInformation("Executing turn {Sequence} of {Total} for test case {TestCaseId}",
                    turn.Sequence, executionContext.TotalTurns, input.TestCaseId);

                // Execute turn (can take minutes - no timeout)
                var turnResult = await context.CallActivityAsync<TurnExecutionResult>(
                    "ExecuteSingleTurn",
                    turnInput);

                // Store conversation ID from first turn
                if (turn.Sequence == 1 && !string.IsNullOrEmpty(turnResult.ConversationId))
                {
                    conversationId = turnResult.ConversationId;
                    executionContext.ConversationId = conversationId;
                }

                // Create an EvaluationResult object to store in the context
                var evaluationResult = new EvaluationResult
                {
                    Id = turnResult.EvaluationResultId ?? Guid.Empty,
                    Passed = turnResult.Passed,
                    Score = turnResult.Score,
                    ExecutionTimeMs = turnResult.ExecutionTimeMs,
                    ErrorMessage = turnResult.ErrorMessage
                };

                executionContext.TurnResults[turn.Sequence] = evaluationResult;

                logger.LogInformation("Completed turn {Sequence}: Passed={Passed}, Score={Score}",
                    turn.Sequence, turnResult.Passed, turnResult.Score);

                // Optional: Add small delay between turns for rate limiting
                if (turn.Sequence < executionContext.TotalTurns)
                {
                    await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(2), CancellationToken.None);
                }
            }

            // Create aggregate result
            logger.LogInformation("Creating aggregate result for test case {TestCaseId}", input.TestCaseId);
            
            await context.CallActivityAsync(
                "CreateAggregateResult",
                new CreateAggregateResultInput
                {
                    Context = executionContext,
                    TestRunId = input.TestRunId
                });

            logger.LogInformation(
                "Completed multi-turn test case {TestCaseId} - Pass Rate: {PassRate:P0} ({Passed}/{Total})",
                input.TestCaseId, executionContext.PassRate, executionContext.PassedTurns, executionContext.TotalTurns);

            return executionContext;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in orchestration for test case {TestCaseId}", input.TestCaseId);
            throw;
        }
    }
}
