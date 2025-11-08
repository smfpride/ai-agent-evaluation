using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents an evaluation result entity from Dataverse (mcicaps_evaluationresults)
/// </summary>
public class EvaluationResult
{
    [JsonPropertyName("mcicaps_evaluationresultid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_mcicaps_evaluationrun_value")]
    [Required]
    public Guid EvaluationRunId { get; set; }

    [JsonPropertyName("_mcicaps_testcase_value")]
    [Required]
    public Guid TestCaseId { get; set; }

    [JsonPropertyName("mcicaps_actualresponse")]
    public string? ActualResponse { get; set; }

    [JsonPropertyName("mcicaps_score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("mcicaps_passed")]
    public bool? Passed { get; set; }

    [JsonPropertyName("mcicaps_reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("mcicaps_conversationid")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("mcicaps_executiontime")]
    public int? ExecutionTimeMs { get; set; }

    [JsonPropertyName("mcicaps_errormessage")]
    public string? ErrorMessage { get; set; }

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
/// Data Transfer Object for creating evaluation results
/// </summary>
public class CreateEvaluationResultRequest
{
    [Required]
    public Guid EvaluationRunId { get; set; }

    [Required]
    public Guid TestCaseId { get; set; }

    public string? ActualResponse { get; set; }

    public decimal? Score { get; set; }

    public bool? Passed { get; set; }

    public string? Reasoning { get; set; }

    public string? ConversationId { get; set; }

    public int? ExecutionTimeMs { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Metadata { get; set; }
}