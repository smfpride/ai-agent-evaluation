using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for SharePoint Embedded Agents
/// TODO: Phase 4 implementation
/// </summary>
public class SharePointAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public SharePointAdapter(
        ILogger<SharePointAdapter> logger,
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
            "SharePointAdapter is not yet implemented. This will be available in Phase 4. " +
            "Please use DirectLine adapter (agent type 778760000) for now.");
    }
}
