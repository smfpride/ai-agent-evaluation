using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for DirectLineAdapter
/// </summary>
public class DirectLineAdapterTests
{
    private readonly Mock<ILogger<DirectLineAdapter>> _mockLogger;
    private readonly Mock<ICopilotStudioService> _mockCopilotService;
    private readonly DirectLineAdapter _adapter;

    public DirectLineAdapterTests()
    {
        _mockLogger = new Mock<ILogger<DirectLineAdapter>>();
        _mockCopilotService = new Mock<ICopilotStudioService>();
        _adapter = new DirectLineAdapter(_mockLogger.Object, _mockCopilotService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Test response",
            ExecutionTimeMs = 123,
            ConversationId = "conv-123",
            RawActivityJson = "{\"test\":\"data\"}"
        };

        _mockCopilotService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var request = new AgentCallRequest
        {
            UserInput = "Test input",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>
                {
                    ["directLineUrl"] = "https://example.com/directline"
                }
            }
        };

        // Act
        var result = await _adapter.ExecuteAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test response", result.ResponseText);
        Assert.Equal(123, result.ExecutionTimeMs);
        Assert.Equal("conv-123", result.ConversationId);
        Assert.Equal("{\"test\":\"data\"}", result.RawActivityJson);
        
        _mockCopilotService.Verify(x => x.SendMessageAsync(
            "https://example.com/directline",
            "Test input",
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingDirectLineUrl_ReturnsError()
    {
        // Arrange
        var request = new AgentCallRequest
        {
            UserInput = "Test input",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>() // Empty config
            }
        };

        // Act
        var result = await _adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("directLineUrl", result.ErrorMessage);
        
        _mockCopilotService.Verify(x => x.SendMessageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CopilotServiceFailure_ReturnsError()
    {
        // Arrange
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = false,
            Response = string.Empty,
            ErrorMessage = "Service unavailable",
            ExecutionTimeMs = 50
        };

        _mockCopilotService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var request = new AgentCallRequest
        {
            UserInput = "Test input",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>
                {
                    ["directLineUrl"] = "https://example.com/directline"
                }
            }
        };

        // Act
        var result = await _adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Service unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithConversationContext_PassesContextToCopilotService()
    {
        // Arrange
        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Continued response",
            ConversationId = "conv-456",
            ExecutionTimeMs = 100
        };

        _mockCopilotService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "conv-456",
                "token-123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        var request = new AgentCallRequest
        {
            UserInput = "Follow-up input",
            ConversationId = "conv-456",
            DirectLineToken = "token-123",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>
                {
                    ["directLineUrl"] = "https://example.com/directline"
                }
            }
        };

        // Act
        var result = await _adapter.ExecuteAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Continued response", result.ResponseText);
        Assert.Equal("conv-456", result.ConversationId);
        
        _mockCopilotService.Verify(x => x.SendMessageAsync(
            "https://example.com/directline",
            "Follow-up input",
            "conv-456",
            "token-123",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
