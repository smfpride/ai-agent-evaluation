using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of LLM Judge service using Copilot Studio via Direct Line API
/// </summary>
public class CopilotStudioJudgeService : ILLMJudgeService, IDisposable
{
    private readonly ILogger<CopilotStudioJudgeService> _logger;
    private readonly ICopilotStudioService _copilotStudioService;
    private readonly IDataverseService _dataverseService;
    private readonly JudgeAgent _judgeAgent;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public CopilotStudioJudgeService(
        ILogger<CopilotStudioJudgeService> logger,
        IOptions<AppSettings> settings,
        ICopilotStudioService copilotStudioService,
        IDataverseService dataverseService,
        JudgeAgent judgeAgent)
    {
        _logger = logger;
        _copilotStudioService = copilotStudioService;
        _dataverseService = dataverseService;
        _judgeAgent = judgeAgent;

        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);

        // Validate configuration
        if (string.IsNullOrEmpty(_judgeAgent.DirectLineUrl))
        {
            throw new InvalidOperationException(
                $"Judge Agent '{_judgeAgent.Name}' is configured for Copilot Studio " +
                $"but has no Direct Line URL");
        }

        // Model name is optional - if not specified, agent routes to default topic
        if (string.IsNullOrEmpty(_judgeAgent.ModelName))
        {
            _logger.LogInformation(
                "Judge Agent '{JudgeName}' has no model name - agent will route to default topic",
                _judgeAgent.Name);
        }
        else
        {
            _logger.LogInformation(
                "Judge Agent '{JudgeName}' configured with model '{ModelName}' for topic routing",
                _judgeAgent.Name, _judgeAgent.ModelName);
        }
    }

    public async Task<LLMJudgeResponse> EvaluateResponseAsync(
        LLMJudgeRequest request, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Evaluating response with Copilot Studio judge '{JudgeName}' (evaluator type {EvaluatorTypeId})", 
            _judgeAgent.Name, request.EvaluatorTypeId);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            try
            {
                // 1. Get prompt template from Dataverse
                var promptTemplate = await GetPromptTemplateAsync(
                    request.EvaluatorTypeId, ct);
                
                // 2. Build evaluation prompt with placeholders replaced
                var evaluationPrompt = BuildEvaluationPrompt(promptTemplate, request);
                
                _logger.LogDebug("Copilot Studio judge prompt length: {Length} characters", 
                    evaluationPrompt.Length);
                
                // 3. Send to Copilot Studio judge agent (new conversation each time)
                var copilotResponse = await _copilotStudioService.SendMessageAsync(
                    _judgeAgent.DirectLineUrl!,  // Already validated in constructor
                    evaluationPrompt,
                    conversationId: null, // Start fresh conversation
                    directLineToken: null,
                    ct);
                
                stopwatch.Stop();
                
                if (!copilotResponse.Success)
                {
                    _logger.LogError(
                        "Copilot Studio judge failed: {ErrorMessage}", 
                        copilotResponse.ErrorMessage);
                    
                    return new LLMJudgeResponse
                    {
                        Success = false,
                        ErrorMessage = $"Copilot Studio judge call failed: {copilotResponse.ErrorMessage}",
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                
                // 4. Parse response (JSON or regex fallback)
                var result = ParseLLMResponse(copilotResponse.Response ?? string.Empty);
                result.ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds;
                result.Success = true;
                
                _logger.LogInformation(
                    "Copilot Studio judge completed with score {Score} in {ElapsedMs}ms", 
                    result.Score, result.ExecutionTimeMs);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error during Copilot Studio judge evaluation for evaluator type {EvaluatorTypeId}", 
                    request.EvaluatorTypeId);
                
                stopwatch.Stop();
                
                return new LLMJudgeResponse
                {
                    Success = false,
                    ErrorMessage = $"Exception during Copilot Studio evaluation: {ex.Message}",
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }
        }, "EvaluateCopilotStudioResponse",
        exception => exception is HttpRequestException || exception is TaskCanceledException,
        cancellationToken);
    }

    public async Task<string> GetPromptTemplateAsync(
        Guid? evaluatorTypeId, 
        CancellationToken cancellationToken = default)
    {
        var (promptTemplate, _) = await GetPromptTemplateAndDeploymentAsync(
            evaluatorTypeId, cancellationToken);
        return promptTemplate;
    }

    public async Task<(string promptTemplate, string deploymentName)> GetPromptTemplateAndDeploymentAsync(
        Guid? evaluatorTypeId, 
        CancellationToken cancellationToken = default)
    {
        // Copilot Studio doesn't use deployment name, return judge agent name instead
        var deploymentName = _judgeAgent.Name;
        
        try
        {
            if (evaluatorTypeId.HasValue)
            {
                var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(
                    evaluatorTypeId.Value, cancellationToken);
                
                if (evaluatorType != null && !string.IsNullOrWhiteSpace(evaluatorType.EvaluatorPrompt))
                {
                    _logger.LogInformation(
                        "Retrieved evaluator '{EvaluatorName}' prompt for Copilot Studio judge '{JudgeName}'", 
                        evaluatorType.Name, _judgeAgent.Name);
                    
                    // Wrap prompt with JSON instruction
                    var wrappedPrompt = WrapPromptForJsonResponse(evaluatorType.EvaluatorPrompt);
                    return (wrappedPrompt, deploymentName);
                }
            }
            
            _logger.LogWarning(
                "No evaluator type specified or prompt not found, using default");
            return (WrapPromptForJsonResponse(GetDefaultPromptTemplate()), deploymentName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to get prompt for evaluator type {EvaluatorTypeId}, using default", 
                evaluatorTypeId);
            return (WrapPromptForJsonResponse(GetDefaultPromptTemplate()), deploymentName);
        }
    }

    private string WrapPromptForJsonResponse(string originalPrompt)
    {
        // Inject model name at the very start for Copilot Studio topic routing
        // If no model name specified, don't add any prefix - agent routes to default topic
        var modelInstruction = !string.IsNullOrWhiteSpace(_judgeAgent.ModelName)
            ? $"MODEL: {_judgeAgent.ModelName}\n\n"
            : string.Empty;

        // Add instruction to ensure JSON response format
        return $@"{modelInstruction}IMPORTANT: You must respond with ONLY valid JSON in this format:
{{
  ""score"": <number between 0 and 1>,
  ""reasoning"": ""<your explanation>""
}}

{originalPrompt}

Remember: Respond with ONLY valid JSON, no additional text.";
    }

    private string BuildEvaluationPrompt(string template, LLMJudgeRequest request)
    {
        return template
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

    private static string GetDefaultPromptTemplate()
    {
        return "Evaluate the quality of the actual response compared to the expected response and user input. " +
               "Provide a score from 0 to 1, where 0 is completely incorrect and 1 is perfect. " +
               "Consider accuracy, relevance, and completeness in your evaluation.";
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Test connectivity by sending a simple evaluation request
            var testRequest = new LLMJudgeRequest
            {
                UserInput = "Hello",
                ExpectedResponse = "Hi there!",
                ActualResponse = "Hello!"
            };
            
            var result = await EvaluateResponseAsync(testRequest, cancellationToken);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Copilot Studio judge health check failed for judge '{JudgeName}'", 
                _judgeAgent.Name);
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
            // No resources to dispose (ICopilotStudioService managed by DI)
            _disposed = true;
        }
    }
}
