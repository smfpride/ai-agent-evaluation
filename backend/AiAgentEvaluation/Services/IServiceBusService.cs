using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for Azure Service Bus messaging
/// </summary>
public interface IServiceBusService
{
    Task SendTestCaseMessageAsync(ProcessTestCaseMessage message, CancellationToken cancellationToken = default);
    Task SendBatchTestCaseMessagesAsync(IEnumerable<ProcessTestCaseMessage> messages, CancellationToken cancellationToken = default);
    Task SendMultiTurnTestCaseMessageAsync(ProcessMultiTurnTestCaseMessage message, CancellationToken cancellationToken = default);
    Task SendBatchMultiTurnTestCaseMessagesAsync(IEnumerable<ProcessMultiTurnTestCaseMessage> messages, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}