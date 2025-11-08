namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for Azure Blob Storage operations
/// </summary>
public interface IBlobStorageService
{
    Task<string> GetPromptTemplateAsync(string templateName, CancellationToken cancellationToken = default);
    Task<bool> PromptTemplateExistsAsync(string templateName, CancellationToken cancellationToken = default);
    Task<List<string>> ListPromptTemplatesAsync(CancellationToken cancellationToken = default);
    Task UploadPromptTemplateAsync(string templateName, string content, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}