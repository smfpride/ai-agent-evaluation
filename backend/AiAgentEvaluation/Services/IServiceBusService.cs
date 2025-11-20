using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for Azure Service Bus messaging
/// </summary>
public interface IServiceBusService
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends agent call message to agent-call-queue (Phase 1 - New Architecture)
    /// </summary>
    Task SendAgentCallMessageAsync(AgentCallMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends evaluator job message to evaluator-processing-queue (Phase 1 - New Architecture)
    /// </summary>
    Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends evaluator job message to evaluator-processing-queue with scheduled enqueue time (Phase 1 - Retry with exponential backoff)
    /// </summary>
    Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multi-turn agent call message to multi-turn-agent-call-queue (Story 11 - Service Bus Migration)
    /// </summary>
    Task SendMultiTurnAgentCallMessageAsync(MultiTurnAgentCallMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multi-turn agent call message to multi-turn-agent-call-queue with scheduled enqueue time (Story 11 - Retry with exponential backoff)
    /// </summary>
    Task SendMultiTurnAgentCallMessageAsync(MultiTurnAgentCallMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken cancellationToken = default);
}