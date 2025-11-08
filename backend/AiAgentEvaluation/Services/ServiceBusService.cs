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
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _sender;
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
            else
            {
                throw new InvalidOperationException("Service Bus namespace is not configured. Please set ServiceBus:Namespace for managed identity authentication.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Service Bus service");
            throw;
        }
    }

    public async Task SendTestCaseMessageAsync(ProcessTestCaseMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending test case message for test case {TestCaseId}", message.TestCaseId);
        
        await _resilientService.ExecuteAsync(async ct =>
        {
            // Serialize the message
            var messageJson = JsonSerializer.Serialize(message, _jsonOptions);
            var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageJson))
            {
                MessageId = Guid.NewGuid().ToString(),
                Subject = $"TestCase-{message.TestCaseId}",
                ContentType = "application/json"
            };

            // Add custom properties for routing and monitoring
            serviceBusMessage.ApplicationProperties.Add("EvaluationRunId", message.EvaluationRunId.ToString());
            serviceBusMessage.ApplicationProperties.Add("TestCaseId", message.TestCaseId.ToString());
            serviceBusMessage.ApplicationProperties.Add("TestType", message.TestType);
            serviceBusMessage.ApplicationProperties.Add("RetryCount", message.RetryCount);
            serviceBusMessage.ApplicationProperties.Add("EnqueuedAt", DateTime.UtcNow);

            // Set message time-to-live
            serviceBusMessage.TimeToLive = TimeSpan.FromMinutes(_settings.Evaluation.DefaultTimeoutMinutes);

            // Send the message
            await _sender.SendMessageAsync(serviceBusMessage, ct);
            
            _logger.LogInformation("Successfully sent test case message {MessageId} for test case {TestCaseId}", 
                serviceBusMessage.MessageId, message.TestCaseId);
        }, "SendTestCaseMessage",
        exception => exception is ServiceBusException,
        cancellationToken);
    }

    public async Task SendBatchTestCaseMessagesAsync(IEnumerable<ProcessTestCaseMessage> messages, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        
        await _resilientService.ExecuteAsync(async (ct) =>
        {
            _logger.LogInformation("Sending batch of {MessageCount} test case messages", messageList.Count);
            
            if (messageList.Count == 0)
            {
                _logger.LogWarning("No messages to send in batch");
                return;
            }

            // Create batch of Service Bus messages
            using var messageBatch = await _sender.CreateMessageBatchAsync(ct);
            var batchCount = 0;
            var totalSent = 0;

            foreach (var message in messageList)
            {
                // Serialize the message
                var messageJson = JsonSerializer.Serialize(message, _jsonOptions);
                var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageJson))
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Subject = $"TestCase-{message.TestCaseId}",
                    ContentType = "application/json"
                };

                // Add custom properties
                serviceBusMessage.ApplicationProperties.Add("EvaluationRunId", message.EvaluationRunId.ToString());
                serviceBusMessage.ApplicationProperties.Add("TestCaseId", message.TestCaseId.ToString());
                serviceBusMessage.ApplicationProperties.Add("TestType", message.TestType);
                serviceBusMessage.ApplicationProperties.Add("RetryCount", message.RetryCount);
                serviceBusMessage.ApplicationProperties.Add("EnqueuedAt", DateTime.UtcNow);

                // Set message time-to-live
                serviceBusMessage.TimeToLive = TimeSpan.FromMinutes(_settings.Evaluation.DefaultTimeoutMinutes);

                // Try to add to batch
                if (messageBatch.TryAddMessage(serviceBusMessage))
                {
                    batchCount++;
                }
                else
                {
                    // Send current batch if it's full
                    if (batchCount > 0)
                    {
                        await _sender.SendMessagesAsync(messageBatch, ct);
                        totalSent += batchCount;
                        _logger.LogInformation("Sent batch of {BatchCount} messages", batchCount);
                        batchCount = 0;
                    }

                    // Create new batch and add current message
                    using var newBatch = await _sender.CreateMessageBatchAsync(ct);
                    if (newBatch.TryAddMessage(serviceBusMessage))
                    {
                        await _sender.SendMessagesAsync(newBatch, ct);
                        totalSent += 1;
                        _logger.LogInformation("Sent individual message {MessageId}", serviceBusMessage.MessageId);
                    }
                    else
                    {
                        _logger.LogError("Message too large to fit in batch for test case {TestCaseId}", message.TestCaseId);
                        throw new InvalidOperationException($"Message too large for test case {message.TestCaseId}");
                    }
                }
            }

            // Send remaining messages in batch
            if (batchCount > 0)
            {
                await _sender.SendMessagesAsync(messageBatch, ct);
                totalSent += batchCount;
                _logger.LogInformation("Sent final batch of {BatchCount} messages", batchCount);
            }

            _logger.LogInformation("Successfully sent {TotalSent} test case messages in batch", totalSent);
        }, "SendBatchTestCaseMessages",
        exception => exception is ServiceBusException,
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