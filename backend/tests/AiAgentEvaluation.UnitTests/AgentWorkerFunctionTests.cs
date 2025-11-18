using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Text.Json;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for AgentWorkerFunction (Phase 1 - New Architecture)
/// Tests agent call processing, pending result creation, and evaluator job queuing
/// </summary>
public class AgentWorkerFunctionTests
{
    private readonly Mock<ILogger<AgentWorkerFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly AgentWorkerFunction _agentWorker;

    public AgentWorkerFunctionTests()
    {
        _mockLogger = new Mock<ILogger<AgentWorkerFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockServiceBusService = new Mock<IServiceBusService>();

        _agentWorker = new AgentWorkerFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockServiceBusService.Object);
    }

    [Fact]
    public async Task ProcessAgentCall_SuccessfulCall_CreatesPendingResultsAndQueuesJobs()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        
        var message = new AgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),
            DirectLineUrl = "https://test.directline.com",
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution",
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2 }
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X is an advanced AI solution",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500,
            RawActivityJson = "{\"test\":\"data\"}"
        };

        var resultId1 = Guid.NewGuid();
        var resultId2 = Guid.NewGuid();

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                message.DirectLineUrl,
                message.UserInput,
                message.ConversationId,
                message.DirectLineToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Relevancy" });

        _mockDataverseService
            .SetupSequence(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultId1)
            .ReturnsAsync(resultId2);

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _agentWorker.ProcessAgentCall(messageBody, CancellationToken.None);

        // Assert
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                message.DirectLineUrl,
                message.UserInput,
                message.ConversationId,
                message.DirectLineToken,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r => 
                    r.StatusCode == EvaluationResultStatus.Pending &&
                    r.ActualResponse == copilotResponse.Response &&
                    r.ConversationId == copilotResponse.ConversationId),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.Is<EvaluatorJobMessage>(m => 
                    m.EvaluationRunId == message.EvaluationRunId &&
                    (m.EvaluationResultId == resultId1 || m.EvaluationResultId == resultId2)),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAgentCall_NAScenario_CreatesNAResultAndSkipsQueue()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        
        var message = new AgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),
            DirectLineUrl = "https://test.directline.com",
            UserInput = "What is Product X?",
            ExpectedResponse = "", // Empty expected response
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId }
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X is an advanced AI solution",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500
        };

        _mockCopilotStudioService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        // Setup evaluator type that requires expected response (Accuracy)
        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluatorType { Id = evaluatorTypeId, Name = "Accuracy" });

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _agentWorker.ProcessAgentCall(messageBody, CancellationToken.None);

        // Assert - NA result created
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r => 
                    r.StatusCode == EvaluationResultStatus.NotApplicable &&
                    r.Reasoning!.Contains("expected response")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - No evaluator job queued for NA result
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - Completion counter incremented
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(message.EvaluationRunId, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAgentCall_CopilotFailure_CreatesErrorResultsForAllEvaluators()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        
        var message = new AgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),
            DirectLineUrl = "https://test.directline.com",
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution",
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2 }
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            ErrorMessage = "Service unavailable"
        };

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
        await _agentWorker.ProcessAgentCall(messageBody, CancellationToken.None);

        // Assert - Error results created for all evaluators
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r => 
                    r.StatusCode == EvaluationResultStatus.Errored &&
                    r.ErrorMessage == copilotResponse.ErrorMessage),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert - Completion counter incremented as failed for each evaluator
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(message.EvaluationRunId, true, It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Assert - No evaluator jobs queued
        _mockServiceBusService.Verify(
            x => x.SendEvaluatorJobMessageAsync(
                It.IsAny<EvaluatorJobMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAgentCall_MissingDirectLineUrl_CreatesErrorResults()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        
        var message = new AgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),
            DirectLineUrl = "", // Empty DirectLineUrl
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution",
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId }
        };

        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _agentWorker.ProcessAgentCall(messageBody, CancellationToken.None);

        // Assert - Copilot Studio never called
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - Error result created
        _mockDataverseService.Verify(
            x => x.CreateEvaluationResultAsync(
                It.Is<CreateEvaluationResultRequest>(r => 
                    r.StatusCode == EvaluationResultStatus.Errored &&
                    r.ErrorMessage!.Contains("DirectLineUrl")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAgentCall_InvalidJson_ThrowsException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act & Assert - Should throw JsonException
        await Assert.ThrowsAsync<JsonException>(() => 
            _agentWorker.ProcessAgentCall(invalidJson, CancellationToken.None));

        // Verify no services were called
        _mockCopilotStudioService.Verify(
            x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAgentCall_MultipleEvaluators_HandlesPartialSuccess()
    {
        // Arrange
        var evaluatorTypeId1 = Guid.NewGuid();
        var evaluatorTypeId2 = Guid.NewGuid();
        var evaluatorTypeId3 = Guid.NewGuid();
        
        var message = new AgentCallMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),
            DirectLineUrl = "https://test.directline.com",
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution",
            EvaluatorTypeIds = new List<Guid> { evaluatorTypeId1, evaluatorTypeId2, evaluatorTypeId3 }
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Product X is an advanced AI solution",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500
        };

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
            .ReturnsAsync(new EvaluatorType { Id = Guid.NewGuid(), Name = "Relevancy" });

        var callCount = 0;
        _mockDataverseService
            .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                // Simulate failure on second evaluator
                if (callCount == 2)
                    throw new Exception("Dataverse unavailable");
                return Task.FromResult(Guid.NewGuid());
            });

        _mockServiceBusService
            .Setup(x => x.SendEvaluatorJobMessageAsync(It.IsAny<EvaluatorJobMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act & Assert - Should throw to trigger Service Bus retry
        await Assert.ThrowsAsync<Exception>(() => 
            _agentWorker.ProcessAgentCall(messageBody, CancellationToken.None));
    }
}
