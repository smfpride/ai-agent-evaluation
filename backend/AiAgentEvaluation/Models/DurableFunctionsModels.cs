using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiAgentEvaluation.Models;

/// <summary>
/// Input model for the multi-turn orchestrator
/// </summary>
public class MultiTurnOrchestratorInput
{
    [Required]
    [JsonPropertyName("testCaseId")]
    public Guid TestCaseId { get; set; }

    [Required]
    [JsonPropertyName("testRunId")]
    public Guid TestRunId { get; set; }

    [Required]
    [JsonPropertyName("directLineUrl")]
    public string DirectLineUrl { get; set; } = string.Empty;

    [JsonPropertyName("evaluatorTypeId")]
    public Guid? EvaluatorTypeId { get; set; }

    [JsonPropertyName("passingThreshold")]
    public decimal PassingThreshold { get; set; } = 0.7m;

    /// <summary>
    /// ID of the placeholder aggregate result to update upon completion
    /// </summary>
    [JsonPropertyName("aggregateResultId")]
    public Guid AggregateResultId { get; set; }
}

/// <summary>
/// Input for executing a single turn
/// </summary>
public class ExecuteTurnInput
{
    [Required]
    [JsonPropertyName("turn")]
    public TestCaseTurn Turn { get; set; } = null!;

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; set; }

    /// <summary>
    /// DirectLine token for the conversation. Required for multi-turn conversations
    /// as DirectLine binds conversations to the token that created them.
    /// </summary>
    [JsonPropertyName("directLineToken")]
    public string? DirectLineToken { get; set; }

    [Required]
    [JsonPropertyName("directLineUrl")]
    public string DirectLineUrl { get; set; } = string.Empty;

    [JsonPropertyName("evaluatorTypeId")]
    public Guid? EvaluatorTypeId { get; set; }

    [JsonPropertyName("passingThreshold")]
    public decimal PassingThreshold { get; set; } = 0.7m;

    [JsonPropertyName("testRunId")]
    public Guid TestRunId { get; set; }
}

/// <summary>
/// Result from executing a single turn
/// </summary>
public class TurnExecutionResult
{
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>
    /// DirectLine token for the conversation. Must be preserved for multi-turn conversations.
    /// </summary>
    [JsonPropertyName("directLineToken")]
    public string? DirectLineToken { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("score")]
    public decimal Score { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    [JsonPropertyName("evaluationResultId")]
    public Guid? EvaluationResultId { get; set; }

    /// <summary>
    /// StatusCode for tracking result lifecycle: 1=Pending, 778760002=Completed, 778760003=Errored, 778760004=NotApplicable
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}

/// <summary>
/// Input for creating aggregate result
/// </summary>
public class CreateAggregateResultInput
{
    [Required]
    [JsonPropertyName("context")]
    public MultiTurnExecutionContext Context { get; set; } = null!;

    [Required]
    [JsonPropertyName("testRunId")]
    public Guid TestRunId { get; set; }
}
