using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for Multi-Turn Durable Functions Activity Functions
/// </summary>
public class MultiTurnDurableActivitiesTests
{
    private readonly Mock<ILogger<MultiTurnDurableActivities>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<ILLMJudgeService> _mockLLMJudgeService;
    private readonly MultiTurnDurableActivities _activities;

    public MultiTurnDurableActivitiesTests()
    {
        _mockLogger = new Mock<ILogger<MultiTurnDurableActivities>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockLLMJudgeService = new Mock<ILLMJudgeService>();

        _activities = new MultiTurnDurableActivities(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeService.Object);
    }

    #region GetTestCaseWithTurns Tests

    [Fact]
    public async Task GetTestCaseWithTurns_ValidTestCase_ReturnsTestCaseWithTurns()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();
        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test Case",
            IsMultiTurn = true
        };

        var turns = new List<TestCaseTurn>
        {
            new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 1, PromptText = "Turn 1" },
            new TestCaseTurn { Id = Guid.NewGuid(), Sequence = 2, PromptText = "Turn 2" }
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCase);

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(turns);

        // Act
        var result = await _activities.GetTestCaseWithTurns(testCaseId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testCaseId, result.Id);
        Assert.NotNull(result.Turns);
        Assert.Equal(2, result.Turns.Count);
        Assert.Equal("Turn 1", result.Turns[0].PromptText);
        Assert.Equal("Turn 2", result.Turns[1].PromptText);
    }

    [Fact]
    public async Task GetTestCaseWithTurns_TestCaseNotFound_ReturnsNull()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestCase?)null);

        // Act
        var result = await _activities.GetTestCaseWithTurns(testCaseId, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTestCaseWithTurns_DataverseThrowsException_PropagatesException()
    {
        // Arrange
        var testCaseId = Guid.NewGuid();

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dataverse error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => _activities.GetTestCaseWithTurns(testCaseId, CancellationToken.None));
    }

    #endregion

    #region ExecuteSingleTurn Tests

    [Fact]
    public async Task ExecuteSingleTurn_SuccessfulExecution_ReturnsTurnExecutionResult()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var resultId = Guid.NewGuid();

        var input = new ExecuteTurnInput
        {
            Turn = new TestCaseTurn
            {
                Id = turnId,
                TestCaseId = testCaseId,
                Sequence = 1,
                PromptText = "Test prompt",
                ExpectedResponse = "Expected response"
            },
            ConversationId = null,
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestRunId = testRunId
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Actual response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1000
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.85m,
            Reasoning = "Good match"
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                input.DirectLineUrl,
                input.Turn.PromptText,
                input.ConversationId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(
                It.IsAny<LLMJudgeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultId);

        // Act
        var result = await _activities.ExecuteSingleTurn(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("conv-123", result.ConversationId);
        Assert.Equal(0.85m, result.Score);
        Assert.True(result.Passed);
        Assert.Equal(resultId, result.EvaluationResultId);
        Assert.Null(result.ErrorMessage);

        // Verify all services were called
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                input.DirectLineUrl,
                input.Turn.PromptText,
                input.ConversationId,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLLMJudgeService.Verify(
            x => x.EvaluateResponseAsync(
                It.Is<LLMJudgeRequest>(r =>
                    r.UserInput == input.Turn.PromptText &&
                    r.ExpectedResponse == input.Turn.ExpectedResponse &&
                    r.ActualResponse == copilotResponse.Response &&
                    r.ConversationId == copilotResponse.ConversationId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.EvaluationRunId == testRunId &&
                    r.TestCaseId == testCaseId &&
                    r.TestCaseTurnId == turnId &&
                    r.Score == 0.85m &&
                    r.Passed == true &&
                    r.IsAggregate == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteSingleTurn_CopilotStudioFailure_ReturnsFailureResult()
    {
        // Arrange
        var input = new ExecuteTurnInput
        {
            Turn = new TestCaseTurn
            {
                Id = Guid.NewGuid(),
                TestCaseId = Guid.NewGuid(),
                Sequence = 1,
                PromptText = "Test prompt",
                ExpectedResponse = "Expected response"
            },
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestRunId = Guid.NewGuid()
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Copilot timeout"
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        // Act
        var result = await _activities.ExecuteSingleTurn(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(0, result.Score);
        Assert.False(result.Passed);
        Assert.Contains("Copilot timeout", result.ErrorMessage);

        // Verify LLM Judge was NOT called
        _mockLLMJudgeService.Verify(
            x => x.EvaluateResponseAsync(
                It.IsAny<LLMJudgeRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify Dataverse CreateEvaluationResult was NOT called (failures not saved)
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteSingleTurn_LLMJudgeFailure_ReturnsFailureResult()
    {
        // Arrange
        var input = new ExecuteTurnInput
        {
            Turn = new TestCaseTurn
            {
                Id = Guid.NewGuid(),
                TestCaseId = Guid.NewGuid(),
                Sequence = 1,
                PromptText = "Test prompt",
                ExpectedResponse = "Expected response"
            },
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestRunId = Guid.NewGuid()
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Actual response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1000
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = false,
            ErrorMessage = "Rate limit exceeded"
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(
                It.IsAny<LLMJudgeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _activities.ExecuteSingleTurn(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(0, result.Score);
        Assert.False(result.Passed);
        Assert.Contains("Rate limit exceeded", result.ErrorMessage);

        // Verify Dataverse CreateEvaluationResult was NOT called (failures not saved)
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteSingleTurn_ScoreBelowThreshold_ReturnsFailedResult()
    {
        // Arrange
        var input = new ExecuteTurnInput
        {
            Turn = new TestCaseTurn
            {
                Id = Guid.NewGuid(),
                TestCaseId = Guid.NewGuid(),
                Sequence = 1,
                PromptText = "Test prompt",
                ExpectedResponse = "Expected response"
            },
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestRunId = Guid.NewGuid()
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Actual response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1000
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.5m, // Below threshold
            Reasoning = "Not a good match"
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(
                It.IsAny<LLMJudgeRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _activities.ExecuteSingleTurn(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success); // Execution succeeded
        Assert.Equal(0.5m, result.Score);
        Assert.False(result.Passed); // But test failed due to low score

        // Verify result was saved with Passed = false
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.Score == 0.5m && r.Passed == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteSingleTurn_UnexpectedException_ReturnsFailureResult()
    {
        // Arrange
        var input = new ExecuteTurnInput
        {
            Turn = new TestCaseTurn
            {
                Id = Guid.NewGuid(),
                TestCaseId = Guid.NewGuid(),
                Sequence = 1,
                PromptText = "Test prompt",
                ExpectedResponse = "Expected response"
            },
            DirectLineUrl = "https://test.example.com",
            EvaluatorTypeId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestRunId = Guid.NewGuid()
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _activities.ExecuteSingleTurn(input, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("Unexpected error", result.ErrorMessage);
    }

    #endregion

    #region CreateAggregateResult Tests

    [Fact]
    public async Task CreateAggregateResult_AllTurnsPassed_CreatesAggregateWithPassedTrue()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestRunId = testRunId,
            PassingThreshold = 0.7m,
            ConversationId = "conv-123",
            TestCase = new TestCase
            {
                Id = testCaseId,
                Name = "Test Case"
            },
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn { Sequence = 1 },
                new TestCaseTurn { Sequence = 2 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { Passed = true, Score = 0.85m, ExecutionTimeMs = 1000 } },
                { 2, new EvaluationResult { Passed = true, Score = 0.90m, ExecutionTimeMs = 1200 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.EvaluationRunId == testRunId &&
                    r.TestCaseId == testCaseId &&
                    r.ConversationId == "conv-123" &&
                    r.Passed == true && // Should pass (100% pass rate >= 0.7 threshold)
                    r.IsAggregate == true &&
                    r.ExecutionTimeMs == 2200 && // Sum of turn times
                    r.Score == 0.875m), // Average score
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(
                testRunId,
                false, // not failed
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_SomeTurnsFailed_CreatesAggregateWithCorrectMetrics()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestRunId = testRunId,
            PassingThreshold = 0.7m,
            ConversationId = "conv-456",
            TestCase = new TestCase
            {
                Id = testCaseId,
                Name = "Test Case"
            },
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn { Sequence = 1 },
                new TestCaseTurn { Sequence = 2 },
                new TestCaseTurn { Sequence = 3 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { Passed = true, Score = 0.90m, ExecutionTimeMs = 1000 } },
                { 2, new EvaluationResult { Passed = false, Score = 0.50m, ExecutionTimeMs = 1100 } },
                { 3, new EvaluationResult { Passed = true, Score = 0.80m, ExecutionTimeMs = 1200 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert - Pass rate is 66.67% (2/3) which is < 0.7 threshold
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.TestCaseId == testCaseId &&
                    r.Passed == false && // Should fail (66.67% < 0.7 threshold)
                    r.IsAggregate == true &&
                    r.ExecutionTimeMs == 3300 && // Sum of all turn times
                    r.Score.HasValue && Math.Abs((double)(r.Score.Value - 0.733m)) < 0.01), // Average score ~0.733
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(
                testRunId,
                true, // failed
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_DataverseFailure_PropagatesException()
    {
        // Arrange
        var context = new MultiTurnExecutionContext
        {
            TestRunId = Guid.NewGuid(),
            PassingThreshold = 0.7m,
            TestCase = new TestCase { Id = Guid.NewGuid() },
            Turns = new List<TestCaseTurn> { new TestCaseTurn { Sequence = 1 } },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { Passed = true, Score = 0.85m } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = context.TestRunId
        };

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(
                It.IsAny<CreateEvaluationResultRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dataverse error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(
            () => _activities.CreateAggregateResult(input, CancellationToken.None));
    }

    #endregion
}


