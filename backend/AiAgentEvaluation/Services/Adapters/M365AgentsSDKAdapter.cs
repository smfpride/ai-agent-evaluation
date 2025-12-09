using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for M365 Agents SDK
/// TODO: Phase 5 implementation
/// </summary>
public class M365AgentsSDKAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public M365AgentsSDKAdapter(
        ILogger<M365AgentsSDKAdapter> logger,
        HttpClient httpClient,
        IDataverseService dataverseService)
        : base(logger, dataverseService)
    {
        _httpClient = httpClient;
    }
    
    public override Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "M365AgentsSDKAdapter is not yet implemented. This will be available in Phase 5. " +
            "Please use DirectLine adapter (agent type 778760000) for now.");
    }
}
