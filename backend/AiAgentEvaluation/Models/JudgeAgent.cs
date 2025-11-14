using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents a Judge Agent entity from Dataverse (mcicaps_judgeagents)
/// Configures either Azure OpenAI or Copilot Studio as the LLM judge
/// </summary>
public class JudgeAgent
{
    [JsonPropertyName("mcicaps_judgeagentid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Judge type: 778760000 = Azure OpenAI, 778760001 = Copilot Studio
    /// </summary>
    [JsonPropertyName("mcicaps_judgetype")]
    [Required]
    public int JudgeType { get; set; }

    /// <summary>
    /// Azure OpenAI endpoint URL (required if JudgeType = Azure OpenAI)
    /// Example: https://my-openai.openai.azure.com/
    /// </summary>
    [JsonPropertyName("mcicaps_aoaiendpoint")]
    public string? AoaiEndpoint { get; set; }

    /// <summary>
    /// Azure OpenAI deployment name (required if JudgeType = Azure OpenAI)
    /// Example: gpt-4o, gpt-4-turbo
    /// </summary>
    [JsonPropertyName("mcicaps_aoaideploymentname")]
    public string? AoaiDeploymentName { get; set; }

    /// <summary>
    /// Copilot Studio Direct Line token endpoint (required if JudgeType = Copilot Studio)
    /// Example: https://xxx.powerplatform.com/powervirtualagents/.../directline/token
    /// </summary>
    [JsonPropertyName("mcicaps_directlineurl")]
    public string? DirectLineUrl { get; set; }

    /// <summary>
    /// Model name for Copilot Studio topic routing (optional)
    /// Injected at start of prompt to allow MCS agent to route to correct topic/model
    /// If empty/null, agent routes to default topic
    /// Example: gpt-4o, gpt-4-turbo, gpt-35-turbo
    /// </summary>
    [JsonPropertyName("mcicaps_modelname")]
    public string? ModelName { get; set; }

    [JsonPropertyName("mcicaps_description")]
    public string? Description { get; set; }

    [JsonPropertyName("statecode")]
    public int StateCode { get; set; } = 0;

    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; } = 1;

    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }
}

/// <summary>
/// Judge type constants
/// </summary>
public static class JudgeType
{
    public const int AzureOpenAI = 778760000;
    public const int CopilotStudio = 778760001;

    public static string GetName(int judgeType) => judgeType switch
    {
        AzureOpenAI => "Azure OpenAI",
        CopilotStudio => "Copilot Studio",
        _ => "Unknown"
    };
}
