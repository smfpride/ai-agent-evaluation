using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for LLM Judge evaluation using Azure OpenAI
/// </summary>
public interface ILLMJudgeService
{
    Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default);
    Task<string> GetPromptTemplateAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<(string promptTemplate, string deploymentName)> GetPromptTemplateAndDeploymentAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}