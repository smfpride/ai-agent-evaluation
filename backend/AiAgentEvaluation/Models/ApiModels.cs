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

    [JsonPropertyName("evaluatorTypeId")]
    public Guid? EvaluatorTypeId { get; set; }

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