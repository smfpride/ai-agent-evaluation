using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents an evaluator type entity from Dataverse (mcicaps_evaluatortype)
/// </summary>
public class EvaluatorType
{
    [JsonPropertyName("mcicaps_evaluatortypeid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_evaluatorprompt")]
    [Required]
    public string EvaluatorPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Lookup to Judge Agent for evaluation backend configuration
    /// Replaces the deprecated mcicaps_deploymentname field
    /// </summary>
    [JsonPropertyName("mcicaps_judgeagentid")]
    public Guid? JudgeAgentId { get; set; }

    /// <summary>
    /// DEPRECATED: Use JudgeAgentId instead
    /// Kept for backward compatibility during migration
    /// </summary>
    [JsonPropertyName("mcicaps_deploymentname")]
    [Obsolete("Use JudgeAgentId to reference a Judge Agent record instead")]
    public string? DeploymentName { get; set; }

    [JsonPropertyName("statecode")]
    public int StateCode { get; set; } = 0; // Default: Active

    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; } = 1; // Default: Active

    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }

    [JsonPropertyName("_createdby_value")]
    public Guid? CreatedBy { get; set; }

    [JsonPropertyName("_modifiedby_value")]
    public Guid? ModifiedBy { get; set; }
}
