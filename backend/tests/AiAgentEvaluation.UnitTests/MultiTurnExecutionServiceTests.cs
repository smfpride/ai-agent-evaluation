using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for MultiTurnExecutionService
/// </summary>
public class MultiTurnExecutionServiceTests
{
    private readonly Mock<ILogger<MultiTurnExecutionService>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<ILLMJudgeService> _mockLLMJudgeService;
    private readonly Mock<ILLMJudgeServiceFactory> _mockLLMJudgeServiceFactory;
    private readonly MultiTurnExecutionService _service;

    public MultiTurnExecutionServiceTests()
    {
        _mockLogger = new Mock<ILogger<MultiTurnExecutionService>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockLLMJudgeService = new Mock<ILLMJudgeService>();
        _mockLLMJudgeServiceFactory = new Mock<ILLMJudgeServiceFactory>();

        // Setup factory to return mock judge service
        _mockLLMJudgeServiceFactory
            .Setup(x => x.CreateJudgeServiceAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLLMJudgeService.Object);

        _service = new MultiTurnExecutionService(
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeServiceFactory.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteTestCaseAsync_WithTwoTurns_ExecutesBothTurnsSuccessfully()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var conversationId = "conv-123";
        var directLineUrl = "https://test.directline.com";
        var passingThreshold = 0.7m;

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Blue Shirts Shopping",
            EvaluationRunId = testRunId,
            IsMultiTurn = true,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 1,
                    PromptText = "I would like to see blue shirts for men",
                    ExpectedResponse = "Opulent blue mens button down, cool sky mens t-shirt"
                },
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 2,
                    PromptText = "I'm a size medium",
                    ExpectedResponse = "Opulent blue mens button down"
                }
            }
        };

        // Setup Turn 1
        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[0].PromptText, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                Response = "Here are blue shirts: Opulent blue mens button down, cool sky mens t-shirt, Royal navy henley",
                ConversationId = conversationId,
                ExecutionTimeMs = 1500
            });

        _mockLLMJudgeService
            .Setup(s => s.EvaluateResponseAsync(It.Is<LLMJudgeRequest>(r => r.UserInput == testCase.Turns[0].PromptText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMJudgeResponse
            {
                Success = true,
                Score = 0.95m,
                Reasoning = "Response contains expected items"
            });

        // Setup Turn 2
        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[1].PromptText, conversationId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                Response = "For size medium: Opulent blue mens button down",
                ConversationId = conversationId,
                ExecutionTimeMs = 1200
            });

        _mockLLMJudgeService
            .Setup(s => s.EvaluateResponseAsync(It.Is<LLMJudgeRequest>(r => r.UserInput == testCase.Turns[1].PromptText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMJudgeResponse
            {
                Success = true,
                Score = 0.90m,
                Reasoning = "Response matches expected"
            });

        _mockDataverseService
            .Setup(s => s.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _service.ExecuteTestCaseAsync(testCase, testRunId, directLineUrl, passingThreshold, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.TotalTurns);
        Assert.Equal(2, result.CompletedTurns);
        Assert.Equal(2, result.PassedTurns);
        Assert.Equal(0, result.FailedTurns);
        Assert.Equal(1.0, result.PassRate);
        Assert.Equal(conversationId, result.ConversationId);

        // Verify conversation ID was passed to second turn
        _mockCopilotStudioService.Verify(
            s => s.SendMessageAsync(directLineUrl, testCase.Turns[1].PromptText, conversationId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify aggregate result was created
        _mockDataverseService.Verify(
            s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteTestCaseAsync_WithFailingTurn_ContinuesExecution()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var conversationId = "conv-456";
        var directLineUrl = "https://test.directline.com";
        var passingThreshold = 0.7m;

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test with Failing Turn",
            EvaluationRunId = testRunId,
            IsMultiTurn = true,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 1,
                    PromptText = "First prompt",
                    ExpectedResponse = "Expected 1"
                },
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 2,
                    PromptText = "Second prompt",
                    ExpectedResponse = "Expected 2"
                },
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 3,
                    PromptText = "Third prompt",
                    ExpectedResponse = "Expected 3"
                }
            }
        };

        // Turn 1: Success
        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[0].PromptText, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                Response = "Response 1",
                ConversationId = conversationId,
                ExecutionTimeMs = 1000
            });

        _mockLLMJudgeService
            .Setup(s => s.EvaluateResponseAsync(It.Is<LLMJudgeRequest>(r => r.UserInput == testCase.Turns[0].PromptText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMJudgeResponse { Success = true, Score = 0.9m, Reasoning = "Good" });

        // Turn 2: Copilot fails
        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[1].PromptText, conversationId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = false,
                ErrorMessage = "Service unavailable",
                ExecutionTimeMs = 500
            });

        // Turn 3: Success (should still execute)
        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[2].PromptText, conversationId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                Response = "Response 3",
                ConversationId = conversationId,
                ExecutionTimeMs = 1100
            });

        _mockLLMJudgeService
            .Setup(s => s.EvaluateResponseAsync(It.Is<LLMJudgeRequest>(r => r.UserInput == testCase.Turns[2].PromptText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMJudgeResponse { Success = true, Score = 0.85m, Reasoning = "Good" });

        _mockDataverseService
            .Setup(s => s.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _service.ExecuteTestCaseAsync(testCase, testRunId, directLineUrl, passingThreshold, CancellationToken.None);

        // Assert
        Assert.Equal(3, result.TotalTurns);
        Assert.Equal(3, result.CompletedTurns); // All turns completed (including the failed one)
        Assert.Equal(2, result.PassedTurns);
        Assert.Equal(1, result.FailedTurns);
        Assert.Equal(2.0 / 3.0, result.PassRate, 2);

        // Verify all three turns were attempted
        _mockCopilotStudioService.Verify(
            s => s.SendMessageAsync(directLineUrl, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Verify all results were saved (3 turn results + 1 aggregate)
        _mockDataverseService.Verify(
            s => s.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteTestCaseAsync_WithLowScores_MarksAsFailedBasedOnThreshold()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var conversationId = "conv-789";
        var directLineUrl = "https://test.directline.com";
        var passingThreshold = 0.75m; // High threshold

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test with Low Scores",
            EvaluationRunId = testRunId,
            IsMultiTurn = true,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 1,
                    PromptText = "Test prompt",
                    ExpectedResponse = "Expected response"
                }
            }
        };

        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(directLineUrl, testCase.Turns[0].PromptText, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                Response = "Actual response",
                ConversationId = conversationId,
                ExecutionTimeMs = 1000
            });

        _mockLLMJudgeService
            .Setup(s => s.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMJudgeResponse
            {
                Success = true,
                Score = 0.60m, // Below threshold
                Reasoning = "Partially correct"
            });

        _mockDataverseService
            .Setup(s => s.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _service.ExecuteTestCaseAsync(testCase, testRunId, directLineUrl, passingThreshold, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalTurns);
        Assert.Equal(0, result.PassedTurns); // Score 0.60 < threshold 0.75
        Assert.Equal(1, result.FailedTurns);
        Assert.Equal(0.0, result.PassRate);

        // Verify the result was marked as failed
        _mockDataverseService.Verify(
            s => s.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r => r.Passed == false && r.Score == 0.60m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteTestCaseAsync_WithException_RecordsFailureAndContinues()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var directLineUrl = "https://test.directline.com";
        var passingThreshold = 0.7m;

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test with Exception",
            EvaluationRunId = testRunId,
            IsMultiTurn = true,
            Turns = new List<TestCaseTurn>
            {
                new TestCaseTurn
                {
                    Id = Guid.NewGuid(),
                    Sequence = 1,
                    PromptText = "Test prompt",
                    ExpectedResponse = "Expected"
                }
            }
        };

        _mockCopilotStudioService
            .Setup(s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        _mockDataverseService
            .Setup(s => s.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _service.ExecuteTestCaseAsync(testCase, testRunId, directLineUrl, passingThreshold, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalTurns);
        Assert.Equal(1, result.CompletedTurns);
        Assert.Equal(0, result.PassedTurns);
        Assert.Equal(1, result.FailedTurns);

        // Verify error was recorded
        _mockDataverseService.Verify(
            s => s.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.Passed == false &&
                    r.ErrorMessage != null &&
                    r.ErrorMessage.Contains("Network error")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteTestCaseAsync_WithNoTurns_ReturnsEmptyContext()
    {
        // Arrange
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var directLineUrl = "https://test.directline.com";
        var passingThreshold = 0.7m;

        var testCase = new TestCase
        {
            Id = testCaseId,
            Name = "Test with No Turns",
            EvaluationRunId = testRunId,
            IsMultiTurn = true,
            Turns = new List<TestCaseTurn>() // Empty list
        };

        _mockDataverseService
            .Setup(s => s.CreateAggregateEvaluationResultAsync(It.IsAny<MultiTurnExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        var result = await _service.ExecuteTestCaseAsync(testCase, testRunId, directLineUrl, passingThreshold, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.TotalTurns);
        Assert.Equal(0, result.CompletedTurns);
        Assert.Equal(0, result.PassedTurns);
        Assert.Equal(0, result.FailedTurns);
        Assert.Equal(0.0, result.PassRate);

        // No Copilot calls should have been made
        _mockCopilotStudioService.Verify(
            s => s.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
