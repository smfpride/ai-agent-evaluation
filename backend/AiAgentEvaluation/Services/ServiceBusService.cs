using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Azure.Messaging.ServiceBus;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of Service Bus service using Azure Service Bus SDK
/// </summary>
public class ServiceBusService : IServiceBusService, IDisposable
{
    private readonly ILogger<ServiceBusService> _logger;
    private readonly AppSettings _settings;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly ServiceBusSender? _sender;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public ServiceBusService(ILogger<ServiceBusService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        try
        {
            // Check if Service Bus is configured
            if (string.IsNullOrEmpty(_settings.ServiceBus.Namespace) && 
                string.IsNullOrEmpty(_settings.ServiceBus.ConnectionString))
            {
                _logger.LogWarning(
                    "Service Bus is not configured. Set either 'ServiceBusNamespace' (for managed identity) " +
                    "or 'ServiceBusConnectionString' (for local development). Service Bus operations will fail until configured.");
                
                // Don't throw - allow the service to be created but mark as not initialized
                return;
            }
            
            // Use managed identity for authentication - no connection strings needed
            var credential = new DefaultAzureCredential();
            
            if (!string.IsNullOrEmpty(_settings.ServiceBus.Namespace))
            {
                // Use managed identity with Service Bus namespace
                _serviceBusClient = new ServiceBusClient(_settings.ServiceBus.Namespace, credential);
                _sender = _serviceBusClient.CreateSender(_settings.ServiceBus.QueueName);
                
                _logger.LogInformation("Initialized Service Bus client with managed identity for namespace {Namespace}, queue {QueueName}", 
                    _settings.ServiceBus.Namespace, _settings.ServiceBus.QueueName);
            }
            else if (!string.IsNullOrEmpty(_settings.ServiceBus.ConnectionString))
            {
                // Fallback to connection string for local development
                _serviceBusClient = new ServiceBusClient(_settings.ServiceBus.ConnectionString);
                _sender = _serviceBusClient.CreateSender(_settings.ServiceBus.QueueName);
                
                _logger.LogWarning("Using connection string for Service Bus - this should only be used for local development");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Service Bus service. Please verify your Service Bus configuration.");
            throw;
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_serviceBusClient == null || _sender == null)
            {
                _logger.LogWarning("Service Bus client is not initialized");
                return false;
            }

            // Create a temporary receiver to peek a message (verifies connectivity without consuming)
            await using var receiver = _serviceBusClient.CreateReceiver(_settings.ServiceBus.QueueName);
            await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
            
            _logger.LogDebug("Service Bus health check passed");
            return true;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            // Queue exists but is empty - this is still healthy
            _logger.LogDebug("Service Bus health check passed (queue is empty)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service Bus health check failed");
            return false;
        }
    }

    /// <summary>
    /// Sends agent call message to agent-call-queue (Phase 1 - New Architecture)
    /// </summary>
    public async Task SendAgentCallMessageAsync(AgentCallMessage message, CancellationToken cancellationToken = default)
    {
        await _resilientService.ExecuteAsync(
            async (ct) =>
            {
                if (_serviceBusClient == null)
                {
                    throw new InvalidOperationException("Service Bus client not initialized. Check configuration.");
                }

                await using var sender = _serviceBusClient.CreateSender("agent-call-queue");
                var messageBody = JsonSerializer.Serialize(message, _jsonOptions);
                var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
                {
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString()
                };

                await sender.SendMessageAsync(serviceBusMessage, ct);
                
                _logger.LogInformation(
                    "Sent agent call message to queue: TestCase={TestCaseId}, Run={EvaluationRunId}",
                    message.TestCaseId,
                    message.EvaluationRunId);
            },
            "SendAgentCallMessage",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Sends evaluator job message to evaluator-processing-queue (Phase 1 - New Architecture)
    /// </summary>
    public async Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, CancellationToken cancellationToken = default)
    {
        await _resilientService.ExecuteAsync(
            async (ct) =>
            {
                if (_serviceBusClient == null)
                {
                    throw new InvalidOperationException("Service Bus client not initialized. Check configuration.");
                }

                await using var sender = _serviceBusClient.CreateSender("evaluator-processing-queue");
                var messageBody = JsonSerializer.Serialize(message, _jsonOptions);
                var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
                {
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString()
                };

                await sender.SendMessageAsync(serviceBusMessage, ct);
                
                _logger.LogInformation(
                    "Sent evaluator job message to queue: Result={EvaluationResultId}, Run={EvaluationRunId}",
                    message.EvaluationResultId,
                    message.EvaluationRunId);
            },
            "SendEvaluatorJobMessage",
            null,
            cancellationToken);
    }

    public async Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken cancellationToken = default)
    {
        await _resilientService.ExecuteAsync(
            async (ct) =>
            {
                if (_serviceBusClient == null)
                {
                    throw new InvalidOperationException("Service Bus client not initialized. Check configuration.");
                }

                await using var sender = _serviceBusClient.CreateSender("evaluator-processing-queue");
                var messageBody = JsonSerializer.Serialize(message, _jsonOptions);
                var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
                {
                    ContentType = "application/json",
                    MessageId = Guid.NewGuid().ToString(),
                    ScheduledEnqueueTime = scheduledEnqueueTime
                };

                await sender.SendMessageAsync(serviceBusMessage, ct);
                
                _logger.LogInformation(
                    "Sent evaluator job message to queue with scheduled enqueue time {ScheduledTime}: Result={EvaluationResultId}, Run={EvaluationRunId}, RetryCount={RetryCount}",
                    scheduledEnqueueTime,
                    message.EvaluationResultId,
                    message.EvaluationRunId,
                    message.RetryCount);
            },
            "SendEvaluatorJobMessageScheduled",
            null,
            cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                _sender?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                _serviceBusClient?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing Service Bus resources");
            }
            _disposed = true;
        }
    }
}