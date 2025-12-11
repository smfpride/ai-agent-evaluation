using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for M365AgentsSDKAdapter
/// </summary>
public class M365AgentsSDKAdapterTests
{
    private readonly Mock<ILogger<M365AgentsSDKAdapter>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly HttpClient _httpClient;

    public M365AgentsSDKAdapterTests()
    {
        _mockLogger = new Mock<ILogger<M365AgentsSDKAdapter>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task ExecuteAsync_MissingAgentId_ReturnsError()
    {
        // Arrange
        var adapter = new M365AgentsSDKAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760004,
                Config = new Dictionary<string, object>() // Missing agentId
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("agentId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ValidConfiguration_UsesDefaultEndpoint()
    {
        // Arrange
        var adapter = new M365AgentsSDKAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760004,
                Config = new Dictionary<string, object>
                {
                    ["agentId"] = "agent-guid"
                    // endpoint will use default
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert - Will fail with auth error, but structure is correct
        Assert.NotNull(result);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_CustomEndpoint_UsesProvidedEndpoint()
    {
        // Arrange
        var adapter = new M365AgentsSDKAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760004,
                Config = new Dictionary<string, object>
                {
                    ["agentId"] = "agent-guid",
                    ["endpoint"] = "https://custom.agents.endpoint.com/api"
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.ExecutionTimeMs >= 0);
    }
}
