using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Copilot Studio agents using Direct Line API
/// </summary>
public class DirectLineAdapter : IAgentAdapter
{
    private readonly ILogger<DirectLineAdapter> _logger;
    private readonly ICopilotStudioService _copilotStudioService;
    
    public DirectLineAdapter(
        ILogger<DirectLineAdapter> logger,
        ICopilotStudioService copilotStudioService)
    {
        _logger = logger;
        _copilotStudioService = copilotStudioService;
    }
    
    public async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get Direct Line URL from configuration
            var directLineUrl = request.Configuration.GetValue<string>("directLineUrl");
            
            if (string.IsNullOrEmpty(directLineUrl))
            {
                throw new InvalidOperationException(
                    "DirectLineAdapter requires 'directLineUrl' in configuration");
            }
            
            _logger.LogInformation(
                "DirectLineAdapter: Calling Direct Line API for test case {TestCaseId}",
                request.TestCaseId);
            
            // Use existing CopilotStudioService (unchanged)
            var response = await _copilotStudioService.SendMessageAsync(
                directLineUrl,
                request.UserInput,
                request.ConversationId,
                request.DirectLineToken,
                cancellationToken);
            
            return new AgentResponse
            {
                Success = response.Success,
                ResponseText = response.Response,
                ExecutionTimeMs = response.ExecutionTimeMs,
                ConversationId = response.ConversationId,
                ErrorMessage = response.ErrorMessage,
                RawActivityJson = response.RawActivityJson
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "DirectLineAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
