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
/// Unit tests for TeamsAdapter
/// </summary>
public class TeamsAdapterTests
{
    private readonly Mock<ILogger<TeamsAdapter>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;

    public TeamsAdapterTests()
    {
        _mockLogger = new Mock<ILogger<TeamsAdapter>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Fact]
    public async Task ExecuteAsync_MissingBotId_ReturnsError()
    {
        // Arrange
        var adapter = new TeamsAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760001,
                Config = new Dictionary<string, object>() // Missing botId
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("botId", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ValidConfiguration_CallsBotFramework()
    {
        // Arrange
        _mockDataverseService
            .Setup(x => x.GetEnvironmentVariableValueAsync("TeamsBot_AppId", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-app-id");
        
        _mockDataverseService
            .Setup(x => x.GetEnvironmentVariableValueAsync("TeamsBot_AppSecret", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-app-secret");

        // Mock token response
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("login.microsoftonline.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "test-token" }))
            });

        // Mock conversation start
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.RequestUri!.ToString().Contains("directline.botframework.com") &&
                    req.RequestUri.ToString().Contains("conversations") &&
                    req.Method == HttpMethod.Post &&
                    !req.RequestUri.ToString().Contains("activities")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { conversationId = "test-conv-id" }))
            });

        // Mock message send
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.RequestUri!.ToString().Contains("activities") &&
                    req.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        // Mock get activities
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.RequestUri!.ToString().Contains("activities") &&
                    req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    activities = new[]
                    {
                        new { from = new { id = "bot1" }, text = "Bot response" }
                    }
                }))
            });

        var adapter = new TeamsAdapter(_mockLogger.Object, _httpClient, _mockDataverseService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test message",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760001,
                Config = new Dictionary<string, object>
                {
                    ["botId"] = "test-bot-id",
                    ["environmentVariables"] = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        "{\"appId\":\"TeamsBot_AppId\",\"appSecret\":\"TeamsBot_AppSecret\"}")!
                }
            }
        };

        // Act
        var result = await adapter.ExecuteAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Bot response", result.ResponseText);
        Assert.Equal("test-conv-id", result.ConversationId);
    }
}
