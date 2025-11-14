using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for Multi-Turn Durable Functions Orchestrator
/// </summary>
public class MultiTurnDurableOrchestratorTests
{
    private readonly Mock<ILogger<MultiTurnDurableOrchestrator>> _mockLogger;
    private readonly MultiTurnDurableOrchestrator _orchestrator;

    public MultiTurnDurableOrchestratorTests()
    {
        _mockLogger = new Mock<ILogger<MultiTurnDurableOrchestrator>>();
        _orchestrator = new MultiTurnDurableOrchestrator(_mockLogger.Object);
    }

    // Note: HTTP trigger tests are skipped due to complexity of mocking extension methods
    // These are better covered by integration tests

    #region RunOrchestrator Tests

    [Fact]
    public async Task RunOrchestrator_TwoTurns_ExecutesBothTurnsSequentially()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();

        var evaluatorId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        
        var input = new MultiTurnOrchestratorInput
        {
            TestCaseId = testCaseId,
            TestRunId = testRunId,
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeIds = new List<Guid> { evaluatorId },
            AggregateResultIds = new Dictionary<Guid, Guid> { { evaluatorId, aggregateId } },
            PassingThreshold = 0.7m
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test Case",
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 1, PromptText = "Turn 1" },
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 2, PromptText = "Turn 2" }
            }
        };

        var mockContext = new Mock<TaskOrchestrationContext>();
        
        mockContext.Setup(x => x.GetInput<MultiTurnOrchestratorInput>())
            .Returns(input);

        mockContext.Setup(x => x.CreateReplaySafeLogger<MultiTurnDurableOrchestrator>())
            .Returns(_mockLogger.Object);

        // Mock GetTestCaseWithTurns activity
        mockContext.Setup(x => x.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                testCaseId,
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(testCase);

        // Mock ExecuteSingleTurn activity for turn 1 - returns list of results (one per evaluator)
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 1),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult>
            {
                new TurnExecutionResult
                {
                    ConversationId = "conv-123",
                    Success = true,
                    Score = 0.85m,
                    Passed = true,
                    EvaluationResultId = Guid.NewGuid()
                }
            });

        // Mock ExecuteSingleTurn activity for turn 2 - returns list of results (one per evaluator)
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 2),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult>
            {
                new TurnExecutionResult
                {
                    ConversationId = "conv-123",
                    Success = true,
                    Score = 0.90m,
                    Passed = true,
                    EvaluationResultId = Guid.NewGuid()
                }
            });

        // Mock CreateAggregateResult activity
        mockContext.Setup(x => x.CallActivityAsync("CreateAggregateResult", It.IsAny<CreateAggregateResultInput>(), It.IsAny<TaskOptions>())).Returns(Task.CompletedTask);

        // Mock CreateTimer for delay between turns
        mockContext.Setup(x => x.CurrentUtcDateTime)
            .Returns(DateTime.UtcNow);

        mockContext.Setup(x => x.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _orchestrator.RunOrchestrator(mockContext.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalTurns);
        Assert.Equal(2, result.PassedTurns);
        Assert.Equal(0, result.FailedTurns);
        Assert.Equal(1.0, result.PassRate);
        Assert.Equal("conv-123", result.ConversationId);

        // Verify activity calls
        mockContext.Verify(
            x => x.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                testCaseId,
                It.IsAny<TaskOptions>()),
            Times.Once);

        mockContext.Verify(
            x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.IsAny<ExecuteTurnInput>(),
                It.IsAny<TaskOptions>()),
            Times.Exactly(2));

        mockContext.Verify(
            x => x.CallActivityAsync(
                "CreateAggregateResult",
                It.IsAny<CreateAggregateResultInput>(),
                It.IsAny<TaskOptions>()),
            Times.Once);

        // Verify timer for delay between turns (called once after turn 1)
        mockContext.Verify(
            x => x.CreateTimer(
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOrchestrator_TestCaseNotFound_ReturnsEmptyContext()
    {
        // Arrange
        var input = new MultiTurnOrchestratorInput
        {
            TestCaseId = Guid.NewGuid(),
            TestRunId = Guid.NewGuid(),
            DirectLineUrl = "https://test.example.com",
            PassingThreshold = 0.7m
        };

        var mockContext = new Mock<TaskOrchestrationContext>();
        
        mockContext.Setup(x => x.GetInput<MultiTurnOrchestratorInput>())
            .Returns(input);

        mockContext.Setup(x => x.CreateReplaySafeLogger<MultiTurnDurableOrchestrator>())
            .Returns(_mockLogger.Object);

        mockContext.Setup(x => x.CallActivityAsync<TestCase?>(
                "GetTestCaseWithTurns",
                input.TestCaseId,
                It.IsAny<TaskOptions>()))
            .ReturnsAsync((TestCase?)null);

        // Act
        var result = await _orchestrator.RunOrchestrator(mockContext.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalTurns);
        Assert.Null(result.TestCase);

        // Verify no turn execution happened
        mockContext.Verify(
            x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.IsAny<ExecuteTurnInput>(),
                It.IsAny<TaskOptions>()),
            Times.Never);

        mockContext.Verify(
            x => x.CallActivityAsync(
                "CreateAggregateResult",
                It.IsAny<CreateAggregateResultInput>(),
                It.IsAny<TaskOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task RunOrchestrator_ThreeTurnsOneFailure_ContinuesExecutionAndRecordsFailure()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();

        var input = new MultiTurnOrchestratorInput
        {
            TestCaseId = testCaseId,
            TestRunId = testRunId,
            DirectLineUrl = "https://test.example.com",
            PassingThreshold = 0.7m
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 1 },
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 2 },
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 3 }
            }
        };

        var mockContext = new Mock<TaskOrchestrationContext>();
        
        mockContext.Setup(x => x.GetInput<MultiTurnOrchestratorInput>())
            .Returns(input);

        mockContext.Setup(x => x.CreateReplaySafeLogger<MultiTurnDurableOrchestrator>())
            .Returns(_mockLogger.Object);

        mockContext.Setup(x => x.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                testCaseId,
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(testCase);

        // Turn 1 passes
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 1),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult> { new TurnExecutionResult {
                ConversationId = "conv-123",
                Success = true,
                Score = 0.85m,
                Passed = true,
                EvaluationResultId = Guid.NewGuid()
            } });

        // Turn 2 fails
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 2),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult> { new TurnExecutionResult {
                ConversationId = "conv-123",
                Success = true,
                Score = 0.45m,
                Passed = false, // Below threshold
                EvaluationResultId = Guid.NewGuid()
            } });

        // Turn 3 passes
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 3),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult> { new TurnExecutionResult {
                ConversationId = "conv-123",
                Success = true,
                Score = 0.90m,
                Passed = true,
                EvaluationResultId = Guid.NewGuid()
            } });

        mockContext.Setup(x => x.CallActivityAsync("CreateAggregateResult", It.IsAny<CreateAggregateResultInput>(), It.IsAny<TaskOptions>())).Returns(Task.CompletedTask);

        mockContext.Setup(x => x.CurrentUtcDateTime)
            .Returns(DateTime.UtcNow);

        mockContext.Setup(x => x.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _orchestrator.RunOrchestrator(mockContext.Object);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalTurns);
        Assert.Equal(2, result.PassedTurns);
        Assert.Equal(1, result.FailedTurns);
        Assert.Equal(2.0 / 3.0, result.PassRate, precision: 2);

        // Verify all 3 turns were executed
        mockContext.Verify(
            x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.IsAny<ExecuteTurnInput>(),
                It.IsAny<TaskOptions>()),
            Times.Exactly(3));

        // Verify aggregate was created
        mockContext.Verify(
            x => x.CallActivityAsync(
                "CreateAggregateResult",
                It.IsAny<CreateAggregateResultInput>(),
                It.IsAny<TaskOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOrchestrator_MaintainsConversationId_AcrossAllTurns()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();
        var testCase = new TestCase
        {
            Id = testCaseId,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 1 },
                new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 2 }
            }
        };

        var input = new MultiTurnOrchestratorInput
        {
            TestCaseId = testCaseId,
            TestRunId = Guid.NewGuid(),
            DirectLineUrl = "https://test.example.com",
            PassingThreshold = 0.7m
        };

        var mockContext = new Mock<TaskOrchestrationContext>();

        mockContext.Setup(x => x.GetInput<MultiTurnOrchestratorInput>())
            .Returns(input);

        mockContext.Setup(x => x.CreateReplaySafeLogger<MultiTurnDurableOrchestrator>())
            .Returns(_mockLogger.Object);

        mockContext.Setup(x => x.CallActivityAsync<TestCase>(
                "GetTestCaseWithTurns",
                testCaseId,
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(testCase);

        // Turn 1 - should have null conversation ID input
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 1 && i.ConversationId == null),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult> { new TurnExecutionResult {
                ConversationId = "conv-123",
                Success = true,
                Score = 0.85m,
                Passed = true,
                EvaluationResultId = Guid.NewGuid()
            } });

        // Turn 2 - should have conversation ID from turn 1
        mockContext.Setup(x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 2 && i.ConversationId == "conv-123"),
                It.IsAny<TaskOptions>()))
            .ReturnsAsync(new List<TurnExecutionResult> { new TurnExecutionResult {
                ConversationId = "conv-123",
                Success = true,
                Score = 0.85m,
                Passed = true,
                EvaluationResultId = Guid.NewGuid()
            } });

        mockContext.Setup(x => x.CallActivityAsync("CreateAggregateResult", It.IsAny<CreateAggregateResultInput>(), It.IsAny<TaskOptions>())).Returns(Task.CompletedTask);

        mockContext.Setup(x => x.CurrentUtcDateTime)
            .Returns(DateTime.UtcNow);

        mockContext.Setup(x => x.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _orchestrator.RunOrchestrator(mockContext.Object);

        // Assert
        // Verify turn 1 was called with null conversation ID
        mockContext.Verify(
            x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 1 && i.ConversationId == null),
                It.IsAny<TaskOptions>()),
            Times.Once);
        
        // Verify turn 2 was called with conversation ID from turn 1
        mockContext.Verify(
            x => x.CallActivityAsync<List<TurnExecutionResult>>(
                "ExecuteSingleTurn",
                It.Is<ExecuteTurnInput>(i => i.Turn.Sequence == 2 && i.ConversationId == "conv-123"),
                It.IsAny<TaskOptions>()),
            Times.Once);
        
        // Result should have conversation ID
        Assert.Equal("conv-123", result.ConversationId);
    }

    #endregion

    #region Helper Methods

    private Mock<HttpRequestData> CreateMockHttpRequestData(MultiTurnOrchestratorInput? input)
    {
        var mockRequest = new Mock<HttpRequestData>(Mock.Of<FunctionContext>());
        
        mockRequest.Setup(x => x.ReadFromJsonAsync<MultiTurnOrchestratorInput>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(input);

        var mockResponse = new Mock<HttpResponseData>(Mock.Of<FunctionContext>());
        mockResponse.SetupProperty(r => r.StatusCode);
        
        mockRequest.Setup(x => x.CreateResponse())
            .Returns(mockResponse.Object);

        return mockRequest;
    }

    #endregion
}



