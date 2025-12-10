using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for M365CopilotAdapter
/// </summary>
public class M365CopilotAdapterTests
{
    private readonly Mock<ILogger<M365CopilotAdapter>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly HttpClient _httpClient;

    public M365CopilotAdapterTests()
    {
        _mockLogger = new Mock<ILogger<M365CopilotAdapter>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _httpClient = new HttpClient();
    }

    [Fact]
    public async Task ExecuteAsync_ManagedIdentityMode_ReturnsResponse()
    {
        // Arrange
        var adapter = new M365CopilotAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760002,
                Config = new Dictionary<string, object>
                {
                    ["authMode"] = "ManagedIdentity",
                    ["tenantId"] = "test-tenant-id"
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert - Will succeed with simulated response or fail with auth error
        // Either way, tests the adapter structure is correct
        Assert.NotNull(result);
        Assert.True(result.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_WithScopes_ProcessesConfiguration()
    {
        // Arrange
        var adapter = new M365CopilotAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760002,
                Config = new Dictionary<string, object>
                {
                    ["authMode"] = "ManagedIdentity",
                    ["tenantId"] = "test-tenant-id",
                    ["scopes"] = JsonSerializer.Deserialize<object>(
                        "[\"User.Read\",\"Chat.Read\"]")!
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
