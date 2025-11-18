using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for Azure Service Bus messaging
/// </summary>
public interface IServiceBusService
{
    Task SendTestCaseMessageAsync(ProcessTestCaseMessage message, CancellationToken cancellationToken = default);
    Task SendBatchTestCaseMessagesAsync(IEnumerable<ProcessTestCaseMessage> messages, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends agent call message to agent-call-queue (Phase 1 - New Architecture)
    /// </summary>
    Task SendAgentCallMessageAsync(AgentCallMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends evaluator job message to evaluator-processing-queue (Phase 1 - New Architecture)
    /// </summary>
    Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, CancellationToken cancellationToken = default);
}