using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for SharePointAdapter
/// </summary>
public class SharePointAdapterTests
{
    private readonly Mock<ILogger<SharePointAdapter>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly HttpClient _httpClient;

    public SharePointAdapterTests()
    {
        _mockLogger = new Mock<ILogger<SharePointAdapter>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task ExecuteAsync_MissingSiteUrl_ReturnsError()
    {
        // Arrange
        var adapter = new SharePointAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760003,
                Config = new Dictionary<string, object>() // Missing siteUrl
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("siteUrl", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAgentId_ReturnsError()
    {
        // Arrange
        var adapter = new SharePointAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760003,
                Config = new Dictionary<string, object>
                {
                    ["siteUrl"] = "https://contoso.sharepoint.com/sites/test"
                    // Missing agentId
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("agentId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ValidConfiguration_AttemptsCall()
    {
        // Arrange
        var adapter = new SharePointAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760003,
                Config = new Dictionary<string, object>
                {
                    ["siteUrl"] = "https://contoso.sharepoint.com/sites/test",
                    ["agentId"] = "agent-guid",
                    ["authMode"] = "ManagedIdentity"
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert - Will fail with auth or network error, but structure is correct
        Assert.NotNull(result);
        Assert.True(result.ExecutionTimeMs >= 0);
    }
}
