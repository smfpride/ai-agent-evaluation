using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents a test case entity from Dataverse (mcicaps_testcases)
/// </summary>
public class TestCase
{
    [JsonPropertyName("mcicaps_testcaseid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_testcasename")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_mcicaps_evaluationrun_value")]
    [Required]
    public Guid EvaluationRunId { get; set; }

    [JsonPropertyName("mcicaps_testtype")]
    [Required]
    public int TestType { get; set; }

    [JsonPropertyName("mcicaps_prompt")]
    [Required]
    public string UserInput { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_expectedresponse")]
    [Required]
    public string ExpectedResponse { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_conversationid")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("mcicaps_testorder")]
    public int? TestOrder { get; set; }

    [JsonPropertyName("mcicaps_metadata")]
    public string? Metadata { get; set; }

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

/// <summary>
/// Test types available for evaluation
/// </summary>
public static class TestType
{
    public const int Similarity = 778760000;
    public const int Accuracy = 778760001;
    public const int Relevancy = 778760002;

    public static string GetName(int testType) => testType switch
    {
        Similarity => "Similarity",
        Accuracy => "Accuracy", 
        Relevancy => "Relevancy",
        _ => "Unknown"
    };
}