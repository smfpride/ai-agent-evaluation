namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Factory for creating agent adapters based on agent type
/// </summary>
public interface IAgentAdapterFactory
{
    /// <summary>
    /// Get appropriate adapter for the specified agent type
    /// </summary>
    IAgentAdapter GetAdapter(int agentType);
}
