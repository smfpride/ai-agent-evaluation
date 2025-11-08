using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using Azure;
using System.Text;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of Blob Storage service using Azure Blob Storage SDK
/// </summary>
public class BlobStorageService : IBlobStorageService, IDisposable
{
    private readonly ILogger<BlobStorageService> _logger;
    private readonly AppSettings _settings;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public BlobStorageService(ILogger<BlobStorageService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);

        try
        {
            // Use managed identity for authentication - no connection strings needed
            var credential = new DefaultAzureCredential();
            
            if (!string.IsNullOrEmpty(_settings.Storage.AccountName))
            {
                // Use managed identity with storage account name
                var storageUri = new Uri($"https://{_settings.Storage.AccountName}.blob.core.windows.net");
                _blobServiceClient = new BlobServiceClient(storageUri, credential);
                _containerClient = _blobServiceClient.GetBlobContainerClient(_settings.Storage.PromptTemplateContainerName);
                
                _logger.LogInformation("Initialized Blob Storage client with managed identity for account {AccountName}, container {ContainerName}", 
                    _settings.Storage.AccountName, _settings.Storage.PromptTemplateContainerName);
            }
            else if (!string.IsNullOrEmpty(_settings.Storage.ConnectionString))
            {
                // Fallback to connection string for local development
                _blobServiceClient = new BlobServiceClient(_settings.Storage.ConnectionString);
                _containerClient = _blobServiceClient.GetBlobContainerClient(_settings.Storage.PromptTemplateContainerName);
                
                _logger.LogWarning("Using connection string for Blob Storage - this should only be used for local development");
            }
            else
            {
                throw new InvalidOperationException("Storage account name is not configured. Please set Storage:AccountName for managed identity authentication.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Blob Storage service");
            throw;
        }
    }

    public async Task<string> GetPromptTemplateAsync(string templateName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting prompt template {TemplateName}", templateName);
        
        return await _resilientService.ExecuteAsync(async ct =>
        {
            var blobClient = _containerClient.GetBlobClient(templateName);
            
            if (await blobClient.ExistsAsync(ct))
            {
                var response = await blobClient.DownloadContentAsync(ct);
                var content = response.Value.Content.ToString();
                
                _logger.LogInformation("Retrieved prompt template {TemplateName} from blob storage", templateName);
                return content;
            }
            else
            {
                _logger.LogWarning("Prompt template {TemplateName} not found in blob storage, using default", templateName);
                return GetDefaultTemplate(templateName);
            }
        }, "GetPromptTemplate",
        exception => exception is RequestFailedException || exception is HttpRequestException,
        cancellationToken);
    }

    public async Task<bool> PromptTemplateExistsAsync(string templateName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking if prompt template {TemplateName} exists", templateName);
        
        try
        {
            var blobClient = _containerClient.GetBlobClient(templateName);
            return await blobClient.ExistsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking prompt template {TemplateName} existence", templateName);
            return false;
        }
    }

    public async Task<List<string>> ListPromptTemplatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Listing prompt templates");
        
        try
        {
            var templates = new List<string>();
            
            await foreach (var blobItem in _containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                templates.Add(blobItem.Name);
            }
            
            if (templates.Count == 0)
            {
                _logger.LogInformation("No prompt templates found in blob storage, returning default list");
                return new List<string>
                {
                    "similarity-evaluation-prompt.txt",
                    "accuracy-evaluation-prompt.txt",
                    "relevancy-evaluation-prompt.txt"
                };
            }
            
            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing prompt templates, returning default list");
            return new List<string>
            {
                "similarity-evaluation-prompt.txt",
                "accuracy-evaluation-prompt.txt", 
                "relevancy-evaluation-prompt.txt"
            };
        }
    }

    public async Task UploadPromptTemplateAsync(string templateName, string content, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading prompt template {TemplateName} to blob storage", templateName);
        
        await _resilientService.ExecuteAsync(async ct =>
        {
            var blobClient = _containerClient.GetBlobClient(templateName);
            
            // Convert content to BinaryData
            var binaryData = BinaryData.FromString(content);
            
            // Upload the blob, overwriting if it exists
            await blobClient.UploadAsync(binaryData, overwrite: true, cancellationToken: ct);
            
            _logger.LogInformation("Successfully uploaded prompt template {TemplateName}", templateName);
        }, "UploadPromptTemplate",
        exception => exception is RequestFailedException || exception is HttpRequestException,
        cancellationToken);
    }

    private static string GetDefaultTemplate(string templateName) => templateName switch
    {
        "similarity-evaluation-prompt.txt" => @"
You are an expert evaluator tasked with assessing the similarity between an expected response and an actual response from an AI agent.

Instructions:
1. Compare the expected response with the actual response
2. Consider semantic meaning, not just exact word matches
3. Evaluate how well the actual response captures the intent and information of the expected response
4. Provide a score from 0 to 1, where:
   - 0-0.2: Completely different, no similarity
   - 0.3-0.4: Some similarity but major differences
   - 0.5-0.6: Moderate similarity with notable differences
   - 0.7-0.8: Good similarity with minor differences
   - 0.9-1.0: Very similar or nearly identical meaning

User Input: {user_input}
Expected Response: {expected_response}
Actual Response: {actual_response}

Please provide your evaluation in the following JSON format:
{
  ""score"": [0-1],
  ""reasoning"": ""Your detailed reasoning for the score""
}",
        
        "accuracy-evaluation-prompt.txt" => @"
You are an expert evaluator tasked with assessing the factual accuracy of an AI agent's response.

Instructions:
1. Evaluate if the actual response contains factually correct information
2. Compare against the expected response to identify any inaccuracies
3. Consider if the actual response provides correct, helpful information even if differently worded
4. Provide a score from 0 to 1, where:
   - 0-0.2: Contains significant factual errors
   - 0.3-0.4: Some accurate information but notable errors
   - 0.5-0.6: Mostly accurate with minor errors
   - 0.7-0.8: Accurate information with very minor issues
   - 0.9-1.0: Completely accurate information

User Input: {user_input}
Expected Response: {expected_response}
Actual Response: {actual_response}

Please provide your evaluation in the following JSON format:
{
  ""score"": [0-1],
  ""reasoning"": ""Your detailed reasoning for the score""
}",
        
        "relevancy-evaluation-prompt.txt" => @"
You are an expert evaluator tasked with assessing how relevant an AI agent's response is to the user's input.

Instructions:
1. Evaluate how well the actual response addresses the user's question or request
2. Consider if the response is on-topic and provides useful information
3. Compare with the expected response to understand the intended relevance level
4. Provide a score from 0 to 1, where:
   - 0-0.2: Completely irrelevant to the user input
   - 0.3-0.4: Somewhat related but misses the main point
   - 0.5-0.6: Moderately relevant with some off-topic content
   - 0.7-0.8: Relevant and addresses the user input well
   - 0.9-1.0: Highly relevant and directly addresses the user's needs

User Input: {user_input}
Expected Response: {expected_response}
Actual Response: {actual_response}

Please provide your evaluation in the following JSON format:
{
  ""score"": [0-1],
  ""reasoning"": ""Your detailed reasoning for the score""
}",
        
        _ => "Default evaluation prompt template"
    };

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_blobServiceClient == null || _containerClient == null)
            {
                _logger.LogWarning("Blob Storage client is not initialized");
                return false;
            }

            // Check if the container exists to verify connectivity
            var exists = await _containerClient.ExistsAsync(cancellationToken);
            
            if (!exists.Value)
            {
                _logger.LogWarning("Blob Storage container {ContainerName} does not exist", _containerClient.Name);
                return false;
            }
            
            _logger.LogDebug("Blob Storage health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blob Storage health check failed");
            return false;
        }
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
            // BlobServiceClient doesn't need explicit disposal
            _disposed = true;
        }
    }
}