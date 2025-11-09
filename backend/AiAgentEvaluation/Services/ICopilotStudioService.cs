using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for interacting with Copilot Studio via Direct Line API
/// </summary>
public interface ICopilotStudioService
{
    Task<CopilotDirectLineResponse> SendMessageAsync(string directLineSpeechUrl, string userInput, string? conversationId = null, CancellationToken cancellationToken = default);
    Task<string> StartConversationAsync(string directLineSpeechUrl, CancellationToken cancellationToken = default);
    Task EndConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}