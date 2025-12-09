using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for M365 Copilot using Microsoft Graph API
/// Supports ManagedIdentity, UserCredentials, and ClientCredentials authentication modes
/// TODO: Phase 3 implementation
/// </summary>
public class M365CopilotAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public M365CopilotAdapter(
        ILogger<M365CopilotAdapter> logger,
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
            "M365CopilotAdapter is not yet implemented. This will be available in Phase 3. " +
            "Please use DirectLine adapter (agent type 778760000) for now.");
    }
}
