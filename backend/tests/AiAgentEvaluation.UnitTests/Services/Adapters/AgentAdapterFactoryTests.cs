using AiAgentEvaluation.Services;
using AiAgentEvaluation.Services.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for AgentAdapterFactory
/// </summary>
public class AgentAdapterFactoryTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AgentAdapterFactory _factory;

    public AgentAdapterFactoryTests()
    {
        var services = new ServiceCollection();
        
        // Mock services needed by adapters
        services.AddSingleton(Mock.Of<ILogger<DirectLineAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<TeamsAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<M365CopilotAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<SharePointAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<M365AgentsSDKAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<SlackAdapter>>());
        services.AddSingleton(Mock.Of<ILogger<AgentAdapterFactory>>());
        services.AddSingleton(Mock.Of<ICopilotStudioService>());
        services.AddSingleton(Mock.Of<IDataverseService>());
        services.AddSingleton<HttpClient>();
        
        // Register adapters
        services.AddScoped<DirectLineAdapter>();
        services.AddScoped<TeamsAdapter>();
        services.AddScoped<M365CopilotAdapter>();
        services.AddScoped<SharePointAdapter>();
        services.AddScoped<M365AgentsSDKAdapter>();
        services.AddScoped<SlackAdapter>();
        
        _serviceProvider = services.BuildServiceProvider();
        _factory = new AgentAdapterFactory(_serviceProvider, Mock.Of<ILogger<AgentAdapterFactory>>());
    }

    [Theory]
    [InlineData(778760000, typeof(DirectLineAdapter))]
    [InlineData(778760001, typeof(TeamsAdapter))]
    [InlineData(778760002, typeof(M365CopilotAdapter))]
    [InlineData(778760003, typeof(SharePointAdapter))]
    [InlineData(778760004, typeof(M365AgentsSDKAdapter))]
    [InlineData(778760005, typeof(SlackAdapter))]
    public void GetAdapter_ValidAgentType_ReturnsCorrectAdapter(int agentType, Type expectedType)
    {
        // Act
        var adapter = _factory.GetAdapter(agentType);

        // Assert
        Assert.NotNull(adapter);
        Assert.IsType(expectedType, adapter);
    }

    [Fact]
    public void GetAdapter_InvalidAgentType_ThrowsNotSupportedException()
    {
        // Arrange
        int invalidAgentType = 999999;

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() => _factory.GetAdapter(invalidAgentType));
        Assert.Contains("999999", exception.Message);
        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void GetAdapter_DirectLineType_ReturnsDirectLineAdapter()
    {
        // Act
        var adapter = _factory.GetAdapter(778760000);

        // Assert
        Assert.IsType<DirectLineAdapter>(adapter);
    }
}
