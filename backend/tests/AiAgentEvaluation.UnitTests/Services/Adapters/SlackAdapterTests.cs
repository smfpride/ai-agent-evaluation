using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for SlackAdapter
/// </summary>
public class SlackAdapterTests
{
    private readonly Mock<ILogger<SlackAdapter>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;

    public SlackAdapterTests()
    {
        _mockLogger = new Mock<ILogger<SlackAdapter>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Fact]
    public async Task ExecuteAsync_MissingChannelId_ReturnsError()
    {
        // Arrange
        var adapter = new SlackAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760005,
                Config = new Dictionary<string, object>() // Missing channelId
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("channelId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ValidConfiguration_CallsSlackAPI()
    {
        // Arrange
        _mockDataverseService
            .Setup(x => x.GetEnvironmentVariableValueAsync("Slack_BotToken", It.IsAny<CancellationToken>()))
            .ReturnsAsync("xoxb-test-token");

        // Mock chat.postMessage
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.RequestUri!.ToString().Contains("chat.postMessage")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new 
                { 
                    ok = true, 
                    ts = "1234567890.123456" 
                }))
            });

        // Mock conversations.history
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.RequestUri!.ToString().Contains("conversations.history")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    ok = true,
                    messages = new[]
                    {
                        new { bot_id = "B123", text = "Slack bot response" }
                    }
                }))
            });

        var adapter = new SlackAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760005,
                Config = new Dictionary<string, object>
                {
                    ["channelId"] = "C1234567890",
                    ["environmentVariables"] = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        "{\"botToken\":\"Slack_BotToken\"}")!
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Slack bot response", result.ResponseText);
    }
}
