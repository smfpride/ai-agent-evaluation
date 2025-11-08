using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for LLM Judge evaluation using Azure OpenAI
/// </summary>
public interface ILLMJudgeService
{
    Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default);
    Task<string> GetPromptTemplateAsync(int testType, CancellationToken cancellationToken = default);
}