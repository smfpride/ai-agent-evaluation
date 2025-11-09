using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents a judge prompt template entity from Dataverse (mcicaps_judgeprompt)
/// </summary>
public class JudgePrompt
{
    [JsonPropertyName("mcicaps_judgepromptid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_prompttemplate")]
    [Required]
    public string PromptTemplate { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_testtype")]
    [Required]
    public int TestType { get; set; }

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
