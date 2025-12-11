using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

public class AgentAdapterFactory : IAgentAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentAdapterFactory> _logger;
    
    public AgentAdapterFactory(
        IServiceProvider serviceProvider,
        ILogger<AgentAdapterFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public IAgentAdapter GetAdapter(int agentType)
    {
        _logger.LogInformation("Creating adapter for agent type {AgentType}", agentType);
        
        return agentType switch
        {
            778760000 => _serviceProvider.GetRequiredService<DirectLineAdapter>(),
            778760001 => _serviceProvider.GetRequiredService<TeamsAdapter>(),
            778760002 => _serviceProvider.GetRequiredService<M365CopilotAdapter>(),
            778760003 => _serviceProvider.GetRequiredService<SharePointAdapter>(),
            778760004 => _serviceProvider.GetRequiredService<M365AgentsSDKAdapter>(),
            778760005 => _serviceProvider.GetRequiredService<SlackAdapter>(),
            _ => throw new NotSupportedException($"Agent type {agentType} is not supported")
        };
    }
}
