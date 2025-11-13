using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents an evaluation result entity from Dataverse (mcicaps_evaluationresults)
/// </summary>
public class EvaluationResult
{
    [JsonPropertyName("mcicaps_evaluationresultsid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_evaluationresultname")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("_mcicaps_evaluation_value")]
    [Required]
    public Guid EvaluationRunId { get; set; }

    [JsonPropertyName("_mcicaps_testcase_value")]
    [Required]
    public Guid TestCaseId { get; set; }

    /// <summary>
    /// Lookup to the evaluator type used for this result
    /// </summary>
    [JsonPropertyName("_mcicaps_evaluatortype_value")]
    public Guid? EvaluatorTypeId { get; set; }

    /// <summary>
    /// Lookup to specific turn (for multi-turn tests)
    /// </summary>
    [JsonPropertyName("_mcicaps_testcaseturnid_value")]
    public Guid? TestCaseTurnId { get; set; }

    /// <summary>
    /// Indicates if this is an aggregate result for the overall multi-turn test
    /// </summary>
    [JsonPropertyName("mcicaps_isaggregate")]
    public bool IsAggregate { get; set; }

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

    public Guid? EvaluatorTypeId { get; set; }

    public Guid? TestCaseTurnId { get; set; }

    public bool IsAggregate { get; set; }

    public string? ActualResponse { get; set; }

    public decimal? Score { get; set; }

    public bool? Passed { get; set; }

    public string? Reasoning { get; set; }

    public string? ConversationId { get; set; }

    public int? ExecutionTimeMs { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Metadata { get; set; }

    /// <summary>
    /// StatusCode for tracking result lifecycle: 1=Pending, 778760002=Completed, 778760003=Errored
    /// </summary>
    public int? StatusCode { get; set; }
}

/// <summary>
/// Status codes for evaluation results
/// </summary>
public static class EvaluationResultStatus
{
    public const int Pending = 1;
    public const int Completed = 778760002;
    public const int Errored = 778760003;
    public const int NotApplicable = 778760004;

    public static string GetName(int statusCode) => statusCode switch
    {
        Pending => "Pending",
        Completed => "Completed",
        Errored => "Errored",
        NotApplicable => "Not Applicable",
        _ => "Unknown"
    };
}