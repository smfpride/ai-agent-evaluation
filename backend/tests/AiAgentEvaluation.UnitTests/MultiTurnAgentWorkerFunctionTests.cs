using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Text.Json;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for MultiTurnAgentWorkerFunction (Story 11 - Service Bus Migration)
/// Tests multi-turn agent call processing with conversation context, retry logic, and error handling
/// </summary>
public class MultiTurnAgentWorkerFunctionTests
{
    private readonly Mock<ILogger<MultiTurnAgentWorkerFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly Mock<IOptions<AppSettings>> _mockSettings;
    private readonly MultiTurnAgentWorkerFunction _multiTurnWorker;
    private readonly AppSettings _appSettings;

    public MultiTurnAgentWorkerFunctionTests()
    {
        _mockLogger = new Mock<ILogger<MultiTurnAgentWorkerFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockServiceBusService = new Mock<IServiceBusService>();
        _mockSettings = new Mock<IOptions<AppSettings>>();

        _appSettings = new AppSettings
        {
            AzureOpenAI = new AzureOpenAISettings
            {
            },
            Evaluation = new EvaluationSettings
            {
                MaxRetryAttempts = 3,
                RetryDelaySeconds = 30
            }
        };

        _mockSettings.Setup(x => x.Value).Returns(_appSettings);

        _multiTurnWorker = new MultiTurnAgentWorkerFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockServiceBusService.Object,
            _mockSettings.Object);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_Turn1_Success()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2 },
            TurnSequence = 1,
            TotalTurns = 3,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = null, // Turn 1 starts with null
            DirectLineToken = null,
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn1 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 1,
            PromptText = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X is an advanced AI solution",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 1500,
            RawActivityJson = "{\"test\":\"data\"}"
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn1 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                message.DirectLineUrl,
                turn1.PromptText,
                null, // Turn 1 has null conversationId
                null, // Turn 1 has null token
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Relevancy", RequiresExpectedResponse = true });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockServiceBusService
            .Setup(x => x.SendMultiTurnAgentCallMessageAsync(It.IsAny<MultiTurnAgentCallMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Copilot called with null conversation context for Turn 1
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                message.DirectLineUrl,
                turn1.PromptText,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Pending results created for both evaluators
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Pending &&
                    r.TestCaseTurnId == turn1.Id &&
                    r.ConversationId == copilotResponse.ConversationId),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert - Evaluator jobs queued for both evaluators
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert - Turn 2 queued with conversation context from Turn 1
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.Is<MultiTurnAgentCallMessage>(m =>
                    m.TurnSequence == 2 &&
                    m.ConversationId == copilotResponse.ConversationId &&
                    m.DirectLineToken == copilotResponse.DirectLineToken &&
                    m.RetryCount == 0), // Reset retry count for next turn
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_Turn2_WithConversationId()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId },
            TurnSequence = 2,
            TotalTurns = 3,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = "conv-123", // Populated from Turn 1
            DirectLineToken = "token-abc", // Populated from Turn 1
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn2 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 2,
            PromptText = "Tell me more",
            ExpectedResponse = "More details about Product X"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X includes advanced features...",
            ConversationId = "conv-123", // Same conversation
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 1200
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn2 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                message.DirectLineUrl,
                turn2.PromptText,
                "conv-123", // Turn 2 uses conversation context
                "token-abc",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Relevancy", RequiresExpectedResponse = true });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockServiceBusService
            .Setup(x => x.SendMultiTurnAgentCallMessageAsync(It.IsAny<MultiTurnAgentCallMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Copilot called with conversation context from Turn 1
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                message.DirectLineUrl,
                turn2.PromptText,
                "conv-123",
                "token-abc",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Turn 3 queued
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.Is<MultiTurnAgentCallMessage>(m =>
                    m.TurnSequence == 3 &&
                    m.ConversationId == "conv-123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_LastTurn_NoNextTurnQueued()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId },
            TurnSequence = 3,
            TotalTurns = 3, // Last turn
            DirectLineUrl = "https://test.directline.com",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn3 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 3,
            PromptText = "Thank you",
            ExpectedResponse = "You're welcome"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "You're welcome! Have a great day.",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 800
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn3 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Relevancy", RequiresExpectedResponse = true });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - No next turn queued (last turn)
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.IsAny<MultiTurnAgentCallMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - Evaluator job still queued
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_CopilotFailure_RetriesWithBackoff()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId },
            TurnSequence = 1,
            TotalTurns = 2,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = null,
            DirectLineToken = null,
            PassingThreshold = 0.7m,
            RetryCount = 0 // First attempt
        };

        var turn1 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 1,
            PromptText = "Test prompt",
            ExpectedResponse = "Expected response"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "429 Rate Limit Exceeded"
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn1 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Retry message queued with scheduled time (using overload with DateTimeOffset)
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.Is<MultiTurnAgentCallMessage>(m =>
                    m.RetryCount == 1 &&
                    m.TurnSequence == 1), // Same turn
                It.Is<DateTimeOffset>(dt => dt > DateTimeOffset.UtcNow), // Scheduled in future
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - No evaluator jobs queued (retry pending)
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - No immediate next turn queued (using overload without DateTimeOffset)
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.IsAny<MultiTurnAgentCallMessage>(),
                It.Is<CancellationToken>(ct => true)), // Match the signature without optional parameter
            Times.Never);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_MaxRetriesExceeded_CreatesErrorResults()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2 },
            TurnSequence = 2,
            TotalTurns = 3,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            PassingThreshold = 0.7m,
            RetryCount = 3 // Max retries reached
        };

        var turn2 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 2,
            PromptText = "Test prompt",
            ExpectedResponse = "Expected response"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Service unavailable"
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn2 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Error results created for ALL evaluators
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Errored &&
                    r.Score == 0.0m &&
                    r.Passed == false &&
                    r.TestCaseTurnId == turn2.Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2)); // One for each evaluator

        // Assert - Failed test counter incremented for each evaluator
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId,
                true, // Failed
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert - No retry queued (using overload with DateTimeOffset)
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.IsAny<MultiTurnAgentCallMessage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - Next turn IS queued (Problem #1 fix: allow subsequent turns even after errors with conversation reset)
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.Is<MultiTurnAgentCallMessage>(m =>
                    m.TurnSequence == 3 &&
                    m.ConversationId == null &&  // Conversation reset
                    m.DirectLineToken == null),  // Token reset
                It.Is<CancellationToken>(ct => true)),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_NAScenario_SkipsEvaluatorQueue()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId },
            TurnSequence = 2,
            TotalTurns = 3,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn2 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 2,
            PromptText = "Tell me more",
            ExpectedResponse = null // No expected response
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "More details...",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 1000
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn2 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        // Evaluator requires expected response but none provided
        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType 
            { 
                Id = evaluatorTypeId, 
                Name = "Similarity", 
                RequiresExpectedResponse = true 
            });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockServiceBusService
            .Setup(x => x.SendMultiTurnAgentCallMessageAsync(It.IsAny<MultiTurnAgentCallMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - NA result created immediately
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.NotApplicable &&
                    r.Score == 0.0m &&
                    r.Passed == false &&
                    r.Reasoning != null && r.Reasoning.Contains("requires expected response")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Completion counter incremented (not failed)
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId,
                false, // Not failed
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - NO evaluator job queued for NA result
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - Next turn still queued
        _mockServiceBusService.Verify(
            x => x.SendMultiTurnAgentCallMessageAsync(
                It.Is<MultiTurnAgentCallMessage>(m => m.TurnSequence == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_MultipleEvaluators_FansOutToAll()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        var evaluatorTypeId3 = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2, evaluatorTypeId3 },
            TurnSequence = 1,
            TotalTurns = 2,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = null,
            DirectLineToken = null,
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn1 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 1,
            PromptText = "Test prompt",
            ExpectedResponse = "Expected response"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Agent response",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 1200
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn1 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Evaluator", RequiresExpectedResponse = true });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockServiceBusService
            .Setup(x => x.SendMultiTurnAgentCallMessageAsync(It.IsAny<MultiTurnAgentCallMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Agent called ONCE (not once per evaluator)
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Pending results created for ALL 3 evaluators
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Assert - Evaluator jobs queued for ALL 3 evaluators
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessMultiTurnAgentCall_SetsMultiTurnMetadataInEvaluatorMessage()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();

        var message = new MultiTurnAgentCallMessage
        {
            EvaluationRunId = evaluationRunId,
            TestCaseId = testCaseId,
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId },
            TurnSequence = 2,
            TotalTurns = 3,
            DirectLineUrl = "https://test.directline.com",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            PassingThreshold = 0.7m,
            RetryCount = 0
        };

        var turn2 = new TestCaseTurn
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Sequence = 2,
            PromptText = "Tell me more",
            ExpectedResponse = "More details"
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "More details about the product...",
            ConversationId = "conv-123",
            DirectLineToken = "token-abc",
            ExecutionTimeMs = 1000
        };

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn2 });

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType 
            { 
                Id = evaluatorTypeId, 
                Name = "Accuracy", 
                RequiresExpectedResponse = true 
            });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockServiceBusService
            .Setup(x => x.SendMultiTurnAgentCallMessageAsync(It.IsAny<MultiTurnAgentCallMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _multiTurnWorker.ProcessMultiTurnAgentCall(messageBody, CancellationToken.None);

        // Assert - Evaluator job message contains multi-turn metadata
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.Is<EvaluatorJobMessage>(m =>
                    m.IsMultiTurn &&
                    m.TestCaseId == testCaseId &&
                    m.EvaluatorTypeId == evaluatorTypeId &&
                    m.TurnSequence == 2 &&
                    m.TotalTurns == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
