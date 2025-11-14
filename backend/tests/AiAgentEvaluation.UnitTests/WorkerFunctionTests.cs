using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for WorkerFunction
/// Covers test cases TC-WRK-001 through TC-WRK-019
/// </summary>
public class WorkerFunctionTests
{
    private readonly Mock<ILogger<WorkerFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<ILLMJudgeService> _mockLLMJudgeService;
    private readonly Mock<ILLMJudgeServiceFactory> _mockLLMJudgeServiceFactory;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly WorkerFunction _workerFunction;

    public WorkerFunctionTests()
    {
        _mockLogger = new Mock<ILogger<WorkerFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockLLMJudgeService = new Mock<ILLMJudgeService>();
        _mockLLMJudgeServiceFactory = new Mock<ILLMJudgeServiceFactory>();
        _mockServiceBusService = new Mock<IServiceBusService>();

        // Setup factory to return mock judge service
        _mockLLMJudgeServiceFactory
            .Setup(x => x.CreateJudgeServiceAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLLMJudgeService.Object);

        _workerFunction = new WorkerFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeServiceFactory.Object,
            _mockServiceBusService.Object);
    }

    #region TC-WRK-001: Similarity Test Case Processing

    [Fact]
    public async Task ProcessTestCase_WithSimilarityTest_SuccessfullyProcesses()
    {
        // Arrange
        var message = new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = "What are the features of Product X?",
            ExpectedResponse = "Product X includes AI capabilities, cloud integration, and mobile support.",
            RetryCount = 0,
            MaxRetries = 3
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X offers advanced AI features, seamless cloud integration, and comprehensive mobile app support.",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.92m,
            Reasoning = "The actual response accurately captures all key features mentioned in the expected response with similar meaning.",
            ExecutionTimeMs = 800
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        VerifySuccessfulProcessing(message);
    }

    #endregion

    #region TC-WRK-002: Accuracy Test Case Processing

    [Fact]
    public async Task ProcessTestCase_WithAccuracyTest_SuccessfullyProcesses()
    {
        // Arrange
        var message = new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = "What is the capital of France?",
            ExpectedResponse = "Paris",
            RetryCount = 0,
            MaxRetries = 3
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "The capital of France is Paris.",
            ConversationId = "conv-456",
            ExecutionTimeMs = 1200
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 1.0m,
            Reasoning = "The answer is factually correct and accurate.",
            ExecutionTimeMs = 750
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        VerifySuccessfulProcessing(message);
    }

    #endregion

    #region TC-WRK-003: Relevancy Test Case Processing

    [Fact]
    public async Task ProcessTestCase_WithRelevancyTest_SuccessfullyProcesses()
    {
        // Arrange
        var message = new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = "How do I reset my password?",
            ExpectedResponse = "Instructions for password reset",
            RetryCount = 0,
            MaxRetries = 3
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "To reset your password, go to Settings > Security > Change Password and follow the on-screen instructions.",
            ConversationId = "conv-789",
            ExecutionTimeMs = 1400
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.95m,
            Reasoning = "The response is highly relevant and directly addresses the user's question about password reset.",
            ExecutionTimeMs = 820
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        VerifySuccessfulProcessing(message);
    }

    #endregion

    #region TC-WRK-004: High Score Test Case

    [Fact]
    public async Task ProcessTestCase_WithHighScore_CorrectlyStoresResult()
    {
        // Arrange
        var message = CreateTestMessage();
        var copilotResponse = CreateSuccessfulCopilotResponse();
        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.95m, // High score
            Reasoning = "Excellent response matching all criteria.",
            ExecutionTimeMs = 800
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.Score >= 0.8m), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-005: Low Score Test Case

    [Fact]
    public async Task ProcessTestCase_WithLowScore_CorrectlyStoresResult()
    {
        // Arrange
        var message = CreateTestMessage();
        var copilotResponse = CreateSuccessfulCopilotResponse();
        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.25m, // Low score
            Reasoning = "Response does not meet expected criteria and lacks key information.",
            ExecutionTimeMs = 800
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.Score < 0.3m), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-006: Copilot Studio API Failure

    [Fact]
    public async Task ProcessTestCase_WhenCopilotStudioFails_RetriesMessage()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 0;
        message.MaxRetries = 3;

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Direct Line API unavailable",
            ExecutionTimeMs = 500
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should retry
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.Is<ProcessTestCaseMessage>(m => m.RetryCount == 1), 
            It.IsAny<CancellationToken>()), Times.Once);
        
        // Should not create result yet
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessTestCase_WhenCopilotStudioFailsMaxRetries_SavesErrorResult()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 3; // Already at max retries
        message.MaxRetries = 3;

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Direct Line API unavailable",
            ExecutionTimeMs = 500
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should save error result
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.Score == null && !string.IsNullOrEmpty(r.ErrorMessage)), 
            It.IsAny<CancellationToken>()), Times.Once);
        
        // Should not retry again
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.IsAny<ProcessTestCaseMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region TC-WRK-007: Azure OpenAI Rate Limiting

    [Fact]
    public async Task ProcessTestCase_WhenLLMJudgeFails_RetriesMessage()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 0;
        message.MaxRetries = 3;

        var copilotResponse = CreateSuccessfulCopilotResponse();
        
        var judgeResponse = new LLMJudgeResponse
        {
            Success = false,
            ErrorMessage = "Rate limit exceeded",
            ExecutionTimeMs = 200
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should retry
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.Is<ProcessTestCaseMessage>(m => m.RetryCount == 1), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-010: Large Response Processing

    [Fact]
    public async Task ProcessTestCase_WithLargeResponse_ProcessesSuccessfully()
    {
        // Arrange
        var message = CreateTestMessage();
        
        // Create a large response (simulate near 2MB limit)
        var largeResponse = new string('A', 500000); // 500KB as example
        
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = largeResponse,
            ConversationId = "conv-large",
            ExecutionTimeMs = 2000
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.8m,
            Reasoning = "Response is comprehensive and addresses all requirements.",
            ExecutionTimeMs = 1500
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.ActualResponse == largeResponse), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-011: Empty Agent Response

    [Fact]
    public async Task ProcessTestCase_WithEmptyResponse_HandlesGracefully()
    {
        // Arrange
        var message = CreateTestMessage();
        
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = string.Empty, // Empty response
            ConversationId = "conv-empty",
            ExecutionTimeMs = 800
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.0m,
            Reasoning = "Response is empty and provides no information.",
            ExecutionTimeMs = 500
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.ActualResponse == string.Empty && r.Score == 0.0m), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-012: Malformed LLM Judge Response

    [Fact]
    public async Task ProcessTestCase_WithMalformedJudgeResponse_HandlesError()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 3; // At max retries
        message.MaxRetries = 3;
        
        var copilotResponse = CreateSuccessfulCopilotResponse();

        var judgeResponse = new LLMJudgeResponse
        {
            Success = false,
            ErrorMessage = "Failed to parse LLM response",
            ExecutionTimeMs = 600
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should save error result
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.Score == null && !string.IsNullOrEmpty(r.ErrorMessage)), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-013: Unicode and Special Character Handling

    [Fact]
    public async Task ProcessTestCase_WithUnicodeCharacters_ProcessesCorrectly()
    {
        // Arrange
        var message = CreateTestMessage();
        message.UserInput = "What is the product name? 🚀 €100";
        message.ExpectedResponse = "The product is called '智能助手' and costs €100 💰";

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "The product name is '智能助手' (Smart Assistant) priced at €100 💰",
            ConversationId = "conv-unicode",
            ExecutionTimeMs = 1100
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.88m,
            Reasoning = "Response correctly includes Unicode characters and special symbols.",
            ExecutionTimeMs = 750
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => r.ActualResponse!.Contains("智能助手") && r.ActualResponse.Contains("€")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-017: Message Deserialization

    [Fact]
    public async Task ProcessTestCase_WithInvalidMessageFormat_HandlesGracefully()
    {
        // Arrange
        var invalidMessageBody = "{ invalid json }";

        // Act
        await _workerFunction.ProcessTestCase(invalidMessageBody, CancellationToken.None);

        // Assert - Should log error but not throw
        // Verify no service calls were made
        _mockCopilotStudioService.Verify(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region TC-WRK-018: Retry Counter Tracking

    [Fact]
    public async Task ProcessTestCase_TracksRetryCount_CorrectlyIncrementsOnFailure()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 1; // Second attempt
        message.MaxRetries = 3;

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Temporary failure",
            ExecutionTimeMs = 500
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should increment retry count
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.Is<ProcessTestCaseMessage>(m => m.RetryCount == 2), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-WRK-009: Dataverse Write Failure

    [Fact]
    public async Task ProcessTestCase_WhenDataverseFails_HandlesErrorGracefully()
    {
        // Arrange
        var message = CreateTestMessage();
        var copilotResponse = CreateSuccessfulCopilotResponse();
        var judgeResponse = CreateSuccessfulJudgeResponse();

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        // Dataverse fails
        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse connection failed"));

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act - Should not throw exception
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert - Should attempt to handle error
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region StatusCode Tests (Story 08)

    [Fact]
    public async Task ProcessTestCase_Success_SetsStatusCodeToCompleted()
    {
        // Arrange
        var message = CreateTestMessage();
        
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Test response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1000
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.85m,
            Reasoning = "Good match",
            ExecutionTimeMs = 500
        };

        SetupSuccessfulTestCaseProcessing(message, copilotResponse, judgeResponse);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => 
                r.StatusCode == 778760002),  // Completed
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessTestCase_CopilotFails_SetsStatusCodeToErrored()
    {
        // Arrange
        var message = CreateTestMessage();
        message.RetryCount = 3; // At max retries
        message.MaxRetries = 3;
        
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Copilot service unavailable",
            ExecutionTimeMs = 500
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), 
            It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);
        
        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        
        _mockDataverseService.Setup(x => x.IncrementCompletedTestCasesAsync(
            It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        await _workerFunction.ProcessTestCase(messageBody, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => 
                r.StatusCode == 778760003 &&  // Errored
                r.ErrorMessage != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private ProcessTestCaseMessage CreateTestMessage()
    {
        return new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = "Test input",
            ExpectedResponse = "Expected output",
            RetryCount = 0,
            MaxRetries = 3
        };
    }

    private CopilotDirectLineResponse CreateSuccessfulCopilotResponse()
    {
        return new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Test response from agent",
            ConversationId = "conv-test",
            ExecutionTimeMs = 1200
        };
    }

    private LLMJudgeResponse CreateSuccessfulJudgeResponse()
    {
        return new LLMJudgeResponse
        {
            Success = true,
            Score = 0.85m,
            Reasoning = "Response meets evaluation criteria.",
            ExecutionTimeMs = 800
        };
    }

    private void SetupSuccessfulTestCaseProcessing(
        ProcessTestCaseMessage message, 
        CopilotDirectLineResponse copilotResponse, 
        LLMJudgeResponse judgeResponse)
    {
        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(
            message.DirectLineUrl, message.UserInput, message.ConversationId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    private void VerifySuccessfulProcessing(ProcessTestCaseMessage message)
    {
        _mockCopilotStudioService.Verify(x => x.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockLLMJudgeService.Verify(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => 
                r.EvaluationRunId == message.EvaluationRunId && 
                r.TestCaseId == message.TestCaseId), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}




