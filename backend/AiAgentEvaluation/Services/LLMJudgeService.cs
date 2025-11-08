using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure;
using OpenAI.Chat;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of LLM Judge service using Azure OpenAI
/// </summary>
public class LLMJudgeService : ILLMJudgeService, IDisposable
{
    private readonly ILogger<LLMJudgeService> _logger;
    private readonly AppSettings _settings;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ChatClient _chatClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public LLMJudgeService(ILogger<LLMJudgeService> logger, IOptions<AppSettings> settings, IBlobStorageService blobStorageService)
    {
        _logger = logger;
        _settings = settings.Value;
        _blobStorageService = blobStorageService;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // Configure Azure OpenAI client with managed identity
        if (!string.IsNullOrEmpty(_settings.AzureOpenAI.Endpoint))
        {
            var credential = new DefaultAzureCredential();
            var openAIClient = new AzureOpenAIClient(new Uri(_settings.AzureOpenAI.Endpoint), credential);
            _chatClient = openAIClient.GetChatClient(_settings.AzureOpenAI.DeploymentName);
            
            _logger.LogInformation("Initialized Azure OpenAI client with managed identity for endpoint {Endpoint}", 
                _settings.AzureOpenAI.Endpoint);
        }
        else if (!string.IsNullOrEmpty(_settings.AzureOpenAI.ApiKey))
        {
            // Fallback to API key for local development
            var openAIClient = new AzureOpenAIClient(new Uri(_settings.AzureOpenAI.Endpoint), new AzureKeyCredential(_settings.AzureOpenAI.ApiKey));
            _chatClient = openAIClient.GetChatClient(_settings.AzureOpenAI.DeploymentName);
            
            _logger.LogWarning("Using API key for Azure OpenAI - this should only be used for local development");
        }
        else
        {
            throw new InvalidOperationException("Azure OpenAI endpoint must be configured for managed identity authentication");
        }
    }

    public async Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Evaluating response for test type {TestType}", request.TestType);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Get the appropriate prompt template
            var promptTemplate = await GetPromptTemplateAsync(request.TestType, ct);
            
            // Build the evaluation prompt
            var evaluationPrompt = BuildEvaluationPrompt(promptTemplate, request);
            
            // Create the chat messages
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an expert evaluator. Provide a JSON response with 'score' (0-1) and 'reasoning' fields."),
                new UserChatMessage(evaluationPrompt)
            };

            var chatCompletionOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = _settings.AzureOpenAI.MaxTokens,
                Temperature = (float)_settings.AzureOpenAI.Temperature,
                TopP = 1.0f,
                FrequencyPenalty = 0,
                PresencePenalty = 0
            };

            // Make the API call to Azure OpenAI
            var response = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions, ct);
            stopwatch.Stop();
            
            // Get the response content
            var messageContent = response.Value.Content[0].Text;
            
            // Parse the LLM evaluation response
            var result = ParseLLMResponse(messageContent);
            result.ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds;
            result.Success = true;
            
            _logger.LogInformation("LLM evaluation completed with score {Score} in {ElapsedMs}ms", 
                result.Score, result.ExecutionTimeMs);
            
            return result;
        }, "EvaluateResponse",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    private string BuildEvaluationPrompt(string template, LLMJudgeRequest request)
    {
        var testTypeName = TestType.GetName(request.TestType);
        
        return template
            .Replace("{TestType}", testTypeName)
            .Replace("{UserInput}", request.UserInput)
            .Replace("{ExpectedResponse}", request.ExpectedResponse)
            .Replace("{ActualResponse}", request.ActualResponse)
            .Replace("{ConversationId}", request.ConversationId ?? "N/A");
    }

    private LLMJudgeResponse ParseLLMResponse(string responseText)
    {
        try
        {
            // Try to parse as JSON first
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseText, jsonOptions);
            
            var score = 0m;
            var reasoning = string.Empty;
            
            if (jsonResponse.TryGetProperty("score", out var scoreElement))
            {
                if (scoreElement.ValueKind == JsonValueKind.Number)
                {
                    score = scoreElement.GetDecimal();
                }
                else if (scoreElement.ValueKind == JsonValueKind.String && 
                         decimal.TryParse(scoreElement.GetString(), out var parsedScore))
                {
                    score = parsedScore;
                }
            }
            
            if (jsonResponse.TryGetProperty("reasoning", out var reasoningElement))
            {
                reasoning = reasoningElement.GetString() ?? string.Empty;
            }
            
            // Ensure score is within valid range (0-1 scale)
            score = Math.Max(0, Math.Min(1, score));
            
            return new LLMJudgeResponse
            {
                Score = score,
                Reasoning = reasoning
            };
        }
        catch (JsonException)
        {
            // Fallback to regex parsing if JSON parsing fails
            return ParseLLMResponseWithRegex(responseText);
        }
    }

    private LLMJudgeResponse ParseLLMResponseWithRegex(string responseText)
    {
        var score = 0m;
        var reasoning = responseText;
        
        // Try to extract score using various patterns
        var scorePatterns = new[]
        {
            @"score[:\s]*(\d+(?:\.\d+)?)",
            @"(\d+(?:\.\d+)?)\s*(?:/\s*10|out of 10)",
            @"rating[:\s]*(\d+(?:\.\d+)?)",
            @"(\d+(?:\.\d+)?)\s*(?:points|pts)"
        };
        
        foreach (var pattern in scorePatterns)
        {
            var match = Regex.Match(responseText, pattern, RegexOptions.IgnoreCase);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var extractedScore))
            {
                score = Math.Max(0, Math.Min(1, extractedScore));
                break;
            }
        }
        
        // If no score found, try to infer from keywords
        if (score == 0)
        {
            var lowerText = responseText.ToLowerInvariant();
            if (lowerText.Contains("excellent") || lowerText.Contains("perfect"))
                score = 0.9m;
            else if (lowerText.Contains("good") || lowerText.Contains("satisfactory"))
                score = 0.7m;
            else if (lowerText.Contains("average") || lowerText.Contains("moderate"))
                score = 0.5m;
            else if (lowerText.Contains("poor") || lowerText.Contains("bad"))
                score = 0.3m;
            else if (lowerText.Contains("terrible") || lowerText.Contains("awful"))
                score = 0.1m;
            else
                score = 0.5m; // Default neutral score
        }
        
        return new LLMJudgeResponse
        {
            Score = score,
            Reasoning = reasoning.Trim()
        };
    }

    public async Task<string> GetPromptTemplateAsync(int testType, CancellationToken cancellationToken = default)
    {
        var templateName = GetTemplateNameForTestType(testType);
        
        try
        {
            return await _blobStorageService.GetPromptTemplateAsync(templateName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get prompt template {TemplateName}, using default", templateName);
            return GetDefaultPromptTemplate(testType);
        }
    }

    private static string GetTemplateNameForTestType(int testType) => testType switch
    {
        TestType.Similarity => "similarity-evaluation-prompt.txt",
        TestType.Accuracy => "accuracy-evaluation-prompt.txt", 
        TestType.Relevancy => "relevancy-evaluation-prompt.txt",
        _ => "default-evaluation-prompt.txt"
    };

    private static string GetDefaultPromptTemplate(int testType) => testType switch
    {
        TestType.Similarity => "Evaluate the similarity between the expected and actual responses. Score from 0-1.",
        TestType.Accuracy => "Evaluate the accuracy of the actual response compared to the expected response. Score from 0-1.",
        TestType.Relevancy => "Evaluate how relevant the actual response is to the user input. Score from 0-1.",
        _ => "Evaluate the response quality. Score from 0-1."
    };

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // HttpClient is typically managed by DI container, so we don't dispose it here
            _disposed = true;
        }
    }
}