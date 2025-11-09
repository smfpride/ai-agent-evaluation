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
    private readonly IDataverseService _dataverseService;
    private readonly ChatClient _chatClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public LLMJudgeService(ILogger<LLMJudgeService> logger, IOptions<AppSettings> settings, IDataverseService dataverseService)
    {
        _logger = logger;
        _settings = settings.Value;
        _dataverseService = dataverseService;
        
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
            
            _logger.LogInformation("Raw LLM response: {RawResponse}", messageContent);
            
            // Parse the LLM evaluation response
            var result = ParseLLMResponse(messageContent);
            result.ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds;
            result.Success = true;
            
            _logger.LogInformation("LLM evaluation completed with score {Score} (type: {ScoreType}) in {ElapsedMs}ms", 
                result.Score, result.Score.GetType().Name, result.ExecutionTimeMs);
            
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
            // Clean the response text - remove markdown code fences if present
            var cleanedText = responseText.Trim();
            if (cleanedText.StartsWith("```json"))
            {
                cleanedText = cleanedText.Substring(7); // Remove ```json
                if (cleanedText.EndsWith("```"))
                {
                    cleanedText = cleanedText.Substring(0, cleanedText.Length - 3); // Remove trailing ```
                }
                cleanedText = cleanedText.Trim();
                _logger.LogInformation("Removed markdown code fences from response");
            }
            else if (cleanedText.StartsWith("```"))
            {
                cleanedText = cleanedText.Substring(3); // Remove ```
                if (cleanedText.EndsWith("```"))
                {
                    cleanedText = cleanedText.Substring(0, cleanedText.Length - 3); // Remove trailing ```
                }
                cleanedText = cleanedText.Trim();
                _logger.LogInformation("Removed markdown code fences from response");
            }
            
            // Try to parse as JSON first
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(cleanedText, jsonOptions);
            
            var score = 0m;
            var reasoning = string.Empty;
            
            if (jsonResponse.TryGetProperty("score", out var scoreElement))
            {
                _logger.LogInformation("Score element found - ValueKind: {ValueKind}, Raw: {RawValue}", 
                    scoreElement.ValueKind, scoreElement.GetRawText());
                
                if (scoreElement.ValueKind == JsonValueKind.Number)
                {
                    score = scoreElement.GetDecimal();
                    _logger.LogInformation("Parsed score as decimal from number: {Score}", score);
                }
                else if (scoreElement.ValueKind == JsonValueKind.String && 
                         decimal.TryParse(scoreElement.GetString(), out var parsedScore))
                {
                    score = parsedScore;
                    _logger.LogInformation("Parsed score as decimal from string: {Score}", score);
                }
            }
            else
            {
                _logger.LogWarning("No 'score' property found in JSON response");
            }
            
            if (jsonResponse.TryGetProperty("reasoning", out var reasoningElement))
            {
                reasoning = reasoningElement.GetString() ?? string.Empty;
            }
            
            // Ensure score is within valid range (0-1 scale)
            var originalScore = score;
            score = Math.Max(0, Math.Min(1, score));
            
            if (originalScore != score)
            {
                _logger.LogWarning("Score was clamped from {OriginalScore} to {ClampedScore}", originalScore, score);
            }
            
            _logger.LogInformation("Final parsed score: {Score}", score);
            
            return new LLMJudgeResponse
            {
                Score = score,
                Reasoning = reasoning
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response, falling back to regex parsing");
            // Fallback to regex parsing if JSON parsing fails
            return ParseLLMResponseWithRegex(responseText);
        }
    }

    private LLMJudgeResponse ParseLLMResponseWithRegex(string responseText)
    {
        _logger.LogWarning("Using regex fallback parser - JSON parsing failed");
        
        var score = 0m;
        var reasoning = responseText;
        var scoreFound = false;
        
        // First, try to find a JSON-like score pattern (more specific)
        var jsonScorePattern = @"""score""\s*:\s*(\d+(?:\.\d+)?)";
        var jsonMatch = Regex.Match(responseText, jsonScorePattern, RegexOptions.IgnoreCase);
        if (jsonMatch.Success && decimal.TryParse(jsonMatch.Groups[1].Value, out var jsonScore))
        {
            score = Math.Max(0, Math.Min(1, jsonScore));
            scoreFound = true;
            _logger.LogInformation("Regex parser found JSON-style score: {Score}", score);
        }
        
        // If no JSON-style score, try other patterns
        if (!scoreFound)
        {
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
                    scoreFound = true;
                    _logger.LogInformation("Regex parser found score via pattern '{Pattern}': {Score}", pattern, score);
                    break;
                }
            }
        }
        
        // ONLY infer from keywords if we found absolutely no numeric score
        if (!scoreFound)
        {
            _logger.LogWarning("No numeric score found in response, attempting keyword inference");
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
            
            _logger.LogWarning("Inferred score from keywords: {Score}", score);
        }
        
        return new LLMJudgeResponse
        {
            Score = score,
            Reasoning = reasoning.Trim()
        };
    }

    public async Task<string> GetPromptTemplateAsync(int testType, CancellationToken cancellationToken = default)
    {
        try
        {
            var judgePrompt = await _dataverseService.GetJudgePromptByTestTypeAsync(testType, cancellationToken);
            
            if (judgePrompt != null && !string.IsNullOrWhiteSpace(judgePrompt.PromptTemplate))
            {
                _logger.LogInformation("Retrieved judge prompt '{PromptName}' for test type {TestType}", 
                    judgePrompt.Name, testType);
                return judgePrompt.PromptTemplate;
            }
            
            _logger.LogWarning("No judge prompt found for test type {TestType}, using default", testType);
            return GetDefaultPromptTemplate(testType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get judge prompt for test type {TestType}, using default", testType);
            return GetDefaultPromptTemplate(testType);
        }
    }

    private static string GetDefaultPromptTemplate(int testType) => testType switch
    {
        TestType.Similarity => "Evaluate the similarity between the expected and actual responses. Score from 0-1.",
        TestType.Accuracy => "Evaluate the accuracy of the actual response compared to the expected response. Score from 0-1.",
        TestType.Relevancy => "Evaluate how relevant the actual response is to the user input. Score from 0-1.",
        _ => "Evaluate the response quality. Score from 0-1."
    };

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_chatClient == null)
            {
                _logger.LogWarning("Azure OpenAI chat client is not initialized");
                return false;
            }

            // Send a minimal test request to verify connectivity
            var testMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage("Hello")
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 10,
                Temperature = 0
            };

            await _chatClient.CompleteChatAsync(testMessages, options, cancellationToken);
            
            _logger.LogDebug("Azure OpenAI health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI health check failed");
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
            // HttpClient is typically managed by DI container, so we don't dispose it here
            _disposed = true;
        }
    }
}