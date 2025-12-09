using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Microsoft Teams bots using Bot Framework
/// TODO: Phase 2 implementation
/// </summary>
public class TeamsAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public TeamsAdapter(
        ILogger<TeamsAdapter> logger,
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
            "TeamsAdapter is not yet implemented. This will be available in Phase 2. " +
            "Please use DirectLine adapter (agent type 778760000) for now.");
    }
}
