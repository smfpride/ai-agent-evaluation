using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Request model for starting an evaluation run
/// </summary>
public class StartEvaluationRunRequest
{
    [Required]
    [JsonPropertyName("evaluationRunId")]
    public Guid EvaluationRunId { get; set; }

    [JsonPropertyName("timeoutMinutes")]
    [Range(1, 30)]
    public int TimeoutMinutes { get; set; } = 5;
}

/// <summary>
/// Response model for starting an evaluation run
/// </summary>
public class StartEvaluationRunResponse
{
    [JsonPropertyName("evaluationRunId")]
    public Guid EvaluationRunId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("totalTestCases")]
    public int TotalTestCases { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("estimatedCompletionTime")]
    public DateTime? EstimatedCompletionTime { get; set; }

    [JsonPropertyName("statusUrl")]
    public string? StatusUrl { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Service Bus message for processing individual test cases
/// </summary>
public class ProcessTestCaseMessage
{
    [Required]
    [JsonPropertyName("evaluationRunId")]
    public Guid EvaluationRunId { get; set; }

    [Required]
    [JsonPropertyName("testCaseId")]
    public Guid TestCaseId { get; set; }

    [JsonPropertyName("directLineUrl")]
    public string DirectLineUrl { get; set; } = string.Empty;

    [JsonPropertyName("userInput")]
    public string UserInput { get; set; } = string.Empty;

    [JsonPropertyName("expectedResponse")]
    public string ExpectedResponse { get; set; } = string.Empty;

    /// <summary>
    /// List of evaluator type IDs to use for evaluation.
    /// The Copilot Studio agent will be called once, and the response will be evaluated by all evaluators.
    /// </summary>
    [JsonPropertyName("evaluatorTypeIds")]
    public List<Guid> EvaluatorTypeIds { get; set; } = new();

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("passingThreshold")]
    public decimal PassingThreshold { get; set; } = 0.7m;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Response from Copilot Studio Direct Line API
/// </summary>
public class CopilotDirectLineResponse
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("rawActivityJson")]
    public string? RawActivityJson { get; set; }

    /// <summary>
    /// DirectLine token used for this conversation. Required for multi-turn conversations
    /// as each conversation is bound to the token that created it.
    /// </summary>
    [JsonPropertyName("directLineToken")]
    public string? DirectLineToken { get; set; }
}

/// <summary>
/// LLM Judge evaluation request
/// </summary>
public class LLMJudgeRequest
{
    [JsonPropertyName("evaluatorTypeId")]
    public Guid? EvaluatorTypeId { get; set; }

    [Required]
    [JsonPropertyName("userInput")]
    public string UserInput { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("expectedResponse")]
    public string ExpectedResponse { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("actualResponse")]
    public string ActualResponse { get; set; } = string.Empty;

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }
}

/// <summary>
/// LLM Judge evaluation response
/// </summary>
public class LLMJudgeResponse
{
    [JsonPropertyName("score")]
    [Range(0, 1)]
    public decimal Score { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }
}

/// <summary>
/// Service Bus message for agent call processing (Phase 1 - New Architecture)
/// Queued to agent-call-queue by OrchestratorFunction
/// </summary>
public class AgentCallMessage
{
    [Required]
    [JsonPropertyName("evaluationRunId")]
    public Guid EvaluationRunId { get; set; }

    [Required]
    [JsonPropertyName("testCaseId")]
    public Guid TestCaseId { get; set; }

    [JsonPropertyName("directLineUrl")]
    public string DirectLineUrl { get; set; } = string.Empty;

    [JsonPropertyName("userInput")]
    public string UserInput { get; set; } = string.Empty;

    [JsonPropertyName("expectedResponse")]
    public string ExpectedResponse { get; set; } = string.Empty;

    /// <summary>
    /// List of evaluator type IDs to use for evaluation.
    /// AgentWorker will create pending results for each evaluator and queue evaluator jobs.
    /// </summary>
    [JsonPropertyName("evaluatorTypeIds")]
    public List<Guid> EvaluatorTypeIds { get; set; } = new();

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("directLineToken")]
    public string? DirectLineToken { get; set; }

    [JsonPropertyName("passingThreshold")]
    public decimal PassingThreshold { get; set; } = 0.7m;

    /// <summary>
    /// Turn ID for multi-turn tests (null for single-turn)
    /// </summary>
    [JsonPropertyName("testCaseTurnId")]
    public Guid? TestCaseTurnId { get; set; }
}

/// <summary>
/// Service Bus message for evaluator job processing (Phase 1 - New Architecture)
/// Queued to evaluator-processing-queue by AgentWorkerFunction
/// </summary>
public class EvaluatorJobMessage
{
    [Required]
    [JsonPropertyName("evaluationResultId")]
    public Guid EvaluationResultId { get; set; }

    [Required]
    [JsonPropertyName("evaluationRunId")]
    public Guid EvaluationRunId { get; set; }

    /// <summary>
    /// Number of retry attempts for this message (used for exponential backoff)
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;
}

/// <summary>
/// Data Transfer Object for updating evaluation results (Phase 1 - New Architecture)
/// Used by EvaluatorWorkerFunction to update pending results to Completed or Errored
/// </summary>
public class UpdateEvaluationResultRequest
{
    [Required]
    public int StatusCode { get; set; }

    public decimal? Score { get; set; }

    public bool? Passed { get; set; }

    public string? Reasoning { get; set; }

    public int? ExecutionTimeMs { get; set; }

    public string? ErrorMessage { get; set; }
}