using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Slack bots using Slack Web API
/// TODO: Phase 6 implementation
/// </summary>
public class SlackAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public SlackAdapter(
        ILogger<SlackAdapter> logger,
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
            "SlackAdapter is not yet implemented. This will be available in Phase 6. " +
            "Please use DirectLine adapter (agent type 778760000) for now.");
    }
}
