using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents a single turn within a multi-turn test case (mcicaps_testcaseturn)
/// </summary>
public class TestCaseTurn
{
    /// <summary>
    /// Unique identifier for the turn
    /// </summary>
    [JsonPropertyName("mcicaps_testcaseturnid")]
    public Guid Id { get; set; }

    /// <summary>
    /// Name of the turn (auto-generated with format "Turn-{SEQNUM:5}")
    /// </summary>
    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent test case ID
    /// </summary>
    [JsonPropertyName("_mcicaps_testcaseid_value")]
    [Required]
    public Guid TestCaseId { get; set; }

    /// <summary>
    /// Order of execution within the test case (1-based)
    /// THIS is the field used for sorting turns, NOT mcicaps_name
    /// </summary>
    [JsonPropertyName("mcicaps_sequence")]
    [Required]
    public int Sequence { get; set; }

    /// <summary>
    /// User prompt/input for this turn
    /// </summary>
    [JsonPropertyName("mcicaps_prompttext")]
    [Required]
    public string PromptText { get; set; } = string.Empty;

    /// <summary>
    /// Expected agent response for this turn
    /// </summary>
    [JsonPropertyName("mcicaps_expectedresponse")]
    public string? ExpectedResponse { get; set; }

    /// <summary>
    /// State code of the record
    /// </summary>
    [JsonPropertyName("statecode")]
    public int StateCode { get; set; } = 0; // Default: Active

    /// <summary>
    /// Status code of the record
    /// </summary>
    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; } = 1; // Default: Active

    /// <summary>
    /// Date when the record was created
    /// </summary>
    [JsonPropertyName("createdon")]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Date when the record was last modified
    /// </summary>
    [JsonPropertyName("modifiedon")]
    public DateTime ModifiedOn { get; set; }

    /// <summary>
    /// User who created the record
    /// </summary>
    [JsonPropertyName("_createdby_value")]
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// User who last modified the record
    /// </summary>
    [JsonPropertyName("_modifiedby_value")]
    public Guid? ModifiedBy { get; set; }
}
