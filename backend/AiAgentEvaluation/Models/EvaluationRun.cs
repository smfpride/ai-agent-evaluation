using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Represents an evaluation run entity from Dataverse (mcicaps_evaluationruns)
/// </summary>
public class EvaluationRun
{
    [JsonPropertyName("mcicaps_evaluationrunid")]
    public Guid Id { get; set; }

    [JsonPropertyName("mcicaps_runname")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_description")]
    public string? Description { get; set; }

    [JsonPropertyName("mcicaps_testset")]
    public Guid? TestSetId { get; set; }

    [JsonPropertyName("mcicaps_agenttoeval")]
    public Guid? AgentToEvalId { get; set; }

    [JsonPropertyName("statecode")]
    public int StateCode { get; set; } = 1; // Default: Created

    [JsonPropertyName("statuscode")]
    public int StatusCode { get; set; } = 1; // Default: Created

    [JsonPropertyName("mcicaps_starttime")]
    public DateTime? StartTime { get; set; }

    [JsonPropertyName("mcicaps_endtime")]
    public DateTime? EndTime { get; set; }

    [JsonPropertyName("mcicaps_lastprogressupdate")]
    public DateTime? LastProgressUpdate { get; set; }

    [JsonPropertyName("mcicaps_totaltestcases")]
    public int TotalTestCases { get; set; }

    [JsonPropertyName("mcicaps_completedtestcases")]
    public int CompletedTestCases { get; set; }

    [JsonPropertyName("mcicaps_failedtestcases")]
    public int FailedTestCases { get; set; }

    [JsonPropertyName("mcicaps_progresspercentage")]
    public decimal? ProgressPercentage { get; set; }

    [JsonPropertyName("mcicaps_overallscore")]
    public decimal? OverallScore { get; set; }

    [JsonPropertyName("mcicaps_overallpass")]
    public bool? OverallPass { get; set; }

    [JsonPropertyName("mcicaps_passingthreshold")]
    public decimal? PassingThreshold { get; set; }

    [JsonPropertyName("mcicaps_similarityscore")]
    public decimal? SimilarityScore { get; set; }

    [JsonPropertyName("mcicaps_accuracyscore")]
    public decimal? AccuracyScore { get; set; }

    [JsonPropertyName("mcicaps_relevancyscore")]
    public decimal? RelevancyScore { get; set; }

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
/// Status codes for evaluation runs
/// </summary>
public static class EvaluationRunStatus
{
    public const int Created = 1;
    public const int Execute = 778760001;
    public const int Running = 778760002;
    public const int Errored = 778760003;
    public const int Completed = 778760004;
}