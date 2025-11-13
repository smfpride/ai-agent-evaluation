# Dynamic Azure OpenAI Deployment Names - Implementation Guide

**Date**: November 13, 2025  
**Feature**: Per-Evaluator Azure OpenAI Deployment Name Configuration  
**Status**: Implementation Ready  

## Overview

This guide provides exact code changes needed to implement dynamic Azure OpenAI deployment name configuration per evaluator type. This allows different evaluators to use different models (e.g., GPT-4o, GPT-4-turbo, GPT-3.5-turbo) based on their evaluation requirements.

## Business Value

- **Cost Optimization**: Use cheaper models for simple evaluations, advanced models for complex reasoning
- **Performance Tuning**: Match model capabilities to evaluator requirements
- **Flexibility**: Different evaluators can leverage different Azure OpenAI deployments
- **Backward Compatibility**: Existing evaluators without deployment names use system default

## Architecture Changes

### Before
- Single `ChatClient` initialized at service startup
- All evaluators use same Azure OpenAI deployment from `AppSettings.AzureOpenAI.DeploymentName`
- No per-evaluator model selection

### After
- `AzureOpenAIClient` cached at service level (reusable)
- `ChatClient` created on-demand per evaluation using deployment name from evaluator type
- Falls back to `AppSettings.AzureOpenAI.DeploymentName` if evaluator doesn't specify
- Deployment name logged for observability

## Implementation Steps

---

## Step 1: Update EvaluatorType Model

**File**: `backend/AiAgentEvaluation/Models/EvaluatorType.cs`

**Change**: Add `DeploymentName` property to store the Azure OpenAI deployment name

### Exact Code Change

**Find this block** (lines 17-20):
```csharp
    [JsonPropertyName("mcicaps_evaluatorprompt")]
    [Required]
    public string EvaluatorPrompt { get; set; } = string.Empty;

    [JsonPropertyName("statecode")]
```

**Replace with**:
```csharp
    [JsonPropertyName("mcicaps_evaluatorprompt")]
    [Required]
    public string EvaluatorPrompt { get; set; } = string.Empty;

    [JsonPropertyName("mcicaps_deploymentname")]
    public string? DeploymentName { get; set; }

    [JsonPropertyName("statecode")]
```

**Explanation**: Adds optional `DeploymentName` field mapped to Dataverse column `mcicaps_deploymentname`. Nullable to maintain backward compatibility.

---

## Step 2: Refactor LLMJudgeService for Dynamic Deployment Names

**File**: `backend/AiAgentEvaluation/Services/LLMJudgeService.cs`

### Change 2a: Update Private Fields

**Find this block** (lines 18-26):
```csharp
    private readonly ILogger<LLMJudgeService> _logger;
    private readonly AppSettings _settings;
    private readonly IDataverseService _dataverseService;
    private readonly ChatClient _chatClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;
```

**Replace with**:
```csharp
    private readonly ILogger<LLMJudgeService> _logger;
    private readonly AppSettings _settings;
    private readonly IDataverseService _dataverseService;
    private readonly AzureOpenAIClient _openAIClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResilientService _resilientService;
    private bool _disposed;
```

**Explanation**: Replace `_chatClient` with `_openAIClient` (base client) which can create multiple `ChatClient` instances.

### Change 2b: Update Constructor

**Find this block** (lines 28-66):
```csharp
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
```

**Replace with**:
```csharp
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
            _openAIClient = new AzureOpenAIClient(new Uri(_settings.AzureOpenAI.Endpoint), credential);
            
            _logger.LogInformation("Initialized Azure OpenAI client with managed identity for endpoint {Endpoint}", 
                _settings.AzureOpenAI.Endpoint);
        }
        else if (!string.IsNullOrEmpty(_settings.AzureOpenAI.ApiKey))
        {
            // Fallback to API key for local development
            _openAIClient = new AzureOpenAIClient(new Uri(_settings.AzureOpenAI.Endpoint), new AzureKeyCredential(_settings.AzureOpenAI.ApiKey));
            
            _logger.LogWarning("Using API key for Azure OpenAI - this should only be used for local development");
        }
        else
        {
            throw new InvalidOperationException("Azure OpenAI endpoint must be configured for managed identity authentication");
        }
    }
```

**Explanation**: Store `AzureOpenAIClient` instead of `ChatClient`. Remove premature `GetChatClient()` call - we'll create clients on-demand.

### Change 2c: Update EvaluateResponseAsync Method

**Find this block** (lines 67-107):
```csharp
    public async Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Evaluating response with evaluator type {EvaluatorTypeId}", request.EvaluatorTypeId);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Get the appropriate prompt template
            var promptTemplate = await GetPromptTemplateAsync(request.EvaluatorTypeId, ct);
            
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
```

**Replace with**:
```csharp
    public async Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Evaluating response with evaluator type {EvaluatorTypeId}", request.EvaluatorTypeId);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Get the appropriate prompt template and deployment name
            var (promptTemplate, deploymentName) = await GetPromptTemplateAndDeploymentAsync(request.EvaluatorTypeId, ct);
            
            // Create ChatClient for the specific deployment
            var chatClient = _openAIClient.GetChatClient(deploymentName);
            
            _logger.LogInformation("Using Azure OpenAI deployment '{DeploymentName}' for evaluator type {EvaluatorTypeId}", 
                deploymentName, request.EvaluatorTypeId);
            
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
            var response = await chatClient.CompleteChatAsync(messages, chatCompletionOptions, ct);
            stopwatch.Stop();
```

**Explanation**: 
- Retrieve both prompt template AND deployment name from evaluator type
- Create `ChatClient` on-demand for specific deployment
- Log which deployment is being used for observability

### Change 2d: Replace GetPromptTemplateAsync with GetPromptTemplateAndDeploymentAsync

**Find this method** (lines 262-287):
```csharp
    public async Task<string> GetPromptTemplateAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default)
    {
        try
        {
            // If evaluator type is specified, use it to get the prompt
            if (evaluatorTypeId.HasValue)
            {
                var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId.Value, cancellationToken);
                
                if (evaluatorType != null && !string.IsNullOrWhiteSpace(evaluatorType.EvaluatorPrompt))
                {
                    _logger.LogInformation("Retrieved evaluator prompt '{EvaluatorName}' for evaluator type {EvaluatorTypeId}", 
                        evaluatorType.Name, evaluatorTypeId);
                    return evaluatorType.EvaluatorPrompt;
                }
                
                _logger.LogWarning("Evaluator type {EvaluatorTypeId} not found or has no prompt, using default", evaluatorTypeId);
            }
            else
            {
                _logger.LogWarning("No evaluator type specified, using default");
            }
            
            return GetDefaultPromptTemplate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get prompt for evaluator type {EvaluatorTypeId}, using default", evaluatorTypeId);
            return GetDefaultPromptTemplate();
        }
    }
```

**Replace with**:
```csharp
    public async Task<(string promptTemplate, string deploymentName)> GetPromptTemplateAndDeploymentAsync(
        Guid? evaluatorTypeId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // If evaluator type is specified, use it to get the prompt and deployment name
            if (evaluatorTypeId.HasValue)
            {
                var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId.Value, cancellationToken);
                
                if (evaluatorType != null && !string.IsNullOrWhiteSpace(evaluatorType.EvaluatorPrompt))
                {
                    var deploymentName = !string.IsNullOrWhiteSpace(evaluatorType.DeploymentName)
                        ? evaluatorType.DeploymentName
                        : _settings.AzureOpenAI.DeploymentName;
                    
                    _logger.LogInformation(
                        "Retrieved evaluator '{EvaluatorName}' (ID: {EvaluatorTypeId}) - Prompt: custom, Deployment: {DeploymentName} {DeploymentSource}", 
                        evaluatorType.Name, 
                        evaluatorTypeId, 
                        deploymentName,
                        !string.IsNullOrWhiteSpace(evaluatorType.DeploymentName) ? "(from evaluator)" : "(system default)");
                    
                    return (evaluatorType.EvaluatorPrompt, deploymentName);
                }
                
                _logger.LogWarning("Evaluator type {EvaluatorTypeId} not found or has no prompt, using defaults", evaluatorTypeId);
            }
            else
            {
                _logger.LogWarning("No evaluator type specified, using defaults");
            }
            
            return (GetDefaultPromptTemplate(), _settings.AzureOpenAI.DeploymentName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get prompt for evaluator type {EvaluatorTypeId}, using defaults", evaluatorTypeId);
            return (GetDefaultPromptTemplate(), _settings.AzureOpenAI.DeploymentName);
        }
    }
    
    public async Task<string> GetPromptTemplateAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default)
    {
        var (promptTemplate, _) = await GetPromptTemplateAndDeploymentAsync(evaluatorTypeId, cancellationToken);
        return promptTemplate;
    }
```

**Explanation**: 
- New method returns both prompt template and deployment name as tuple
- Falls back to system default deployment if evaluator doesn't specify
- Keeps old `GetPromptTemplateAsync` for interface compatibility
- Enhanced logging shows deployment source (evaluator vs system default)

### Change 2e: Update CheckHealthAsync

**Find this block** (lines 305-318):
```csharp
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
```

**Replace with**:
```csharp
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_openAIClient == null)
            {
                _logger.LogWarning("Azure OpenAI client is not initialized");
                return false;
            }

            // Create a test ChatClient with default deployment
            var chatClient = _openAIClient.GetChatClient(_settings.AzureOpenAI.DeploymentName);

            // Send a minimal test request to verify connectivity
            var testMessages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage("Hello")
            };
```

**Explanation**: Update health check to use `_openAIClient` and create test `ChatClient` with default deployment.

---

## Step 3: Update ILLMJudgeService Interface

**File**: `backend/AiAgentEvaluation/Services/ILLMJudgeService.cs`

**Change**: Add new method signature for retrieving both template and deployment name

### Exact Code Change

**Find this interface** (entire file):
```csharp
using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for evaluating agent responses using LLM Judge (Azure OpenAI)
/// </summary>
public interface ILLMJudgeService
{
    Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default);
    Task<string> GetPromptTemplateAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
```

**Replace with**:
```csharp
using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for evaluating agent responses using LLM Judge (Azure OpenAI)
/// </summary>
public interface ILLMJudgeService
{
    Task<LLMJudgeResponse> EvaluateResponseAsync(LLMJudgeRequest request, CancellationToken cancellationToken = default);
    Task<string> GetPromptTemplateAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<(string promptTemplate, string deploymentName)> GetPromptTemplateAndDeploymentAsync(Guid? evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
```

**Explanation**: Adds `GetPromptTemplateAndDeploymentAsync` method to interface. Existing `GetPromptTemplateAsync` maintained for backward compatibility.

---

## Step 4: Update WorkerFunction (No Changes Required)

**File**: `backend/AiAgentEvaluation/Functions/WorkerFunction.cs`

**Analysis**: WorkerFunction calls `_llmJudgeService.EvaluateResponseAsync(judgeRequest, cancellationToken)` which already passes `EvaluatorTypeId` in the request. The refactored service will automatically use the deployment name from the evaluator type.

**Action**: ✅ **No code changes needed** - existing implementation already passes evaluator type ID correctly.

---

## Step 5: Update MultiTurnExecutionService (No Changes Required)

**File**: `backend/AiAgentEvaluation/Services/MultiTurnExecutionService.cs`

**Analysis**: Line 147 calls `_llmJudgeService.EvaluateResponseAsync()` with a `LLMJudgeRequest` that includes `EvaluatorTypeId`. The refactored service handles deployment name resolution internally.

**Action**: ✅ **No code changes needed** - existing implementation already passes evaluator type ID correctly.

---

## Step 6: Update MultiTurnDurableActivities (No Changes Required)

**File**: `backend/AiAgentEvaluation/Functions/MultiTurnDurableActivities.cs`

**Analysis**: Line 106 calls `_llmJudgeService.EvaluateResponseAsync()` with evaluator type ID included. Internal service logic handles deployment resolution.

**Action**: ✅ **No code changes needed** - existing implementation already passes evaluator type ID correctly.

---

## Step 7: Update Configuration and Settings

### Change 7a: AppSettings Documentation Comment

**File**: `backend/AiAgentEvaluation/Models/AppSettings.cs`

**Find this block** (lines 51-53):
```csharp
public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty; // Legacy - will be removed for managed identity
    public string DeploymentName { get; set; } = "gpt-4o";
```

**Replace with**:
```csharp
public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty; // Legacy - will be removed for managed identity
    
    /// <summary>
    /// Default Azure OpenAI deployment name (e.g., gpt-4o, gpt-4-turbo, gpt-35-turbo)
    /// Used when evaluator type does not specify a deployment name
    /// Individual evaluators can override by setting mcicaps_deploymentname in Dataverse
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4o";
```

**Explanation**: Documents the fallback behavior and per-evaluator override capability.

### Change 7b: Update local.settings.example.json

**File**: `backend/AiAgentEvaluation/local.settings.example.json`

**Find this block**:
```json
    "_README5": "=== APPLICATION CONFIGURATION ===",
    "DefaultEvaluationTimeoutMinutes": "5",
    "PromptTemplateContainerName": "evaluation-prompts",
    "_AzureOpenAI__MaxRetryAttempts": "5",
    "_AzureOpenAI__TimeoutSeconds": "60",
```

**Replace with**:
```json
    "_README5": "=== APPLICATION CONFIGURATION ===",
    "DefaultEvaluationTimeoutMinutes": "5",
    "PromptTemplateContainerName": "evaluation-prompts",
    "_AzureOpenAI__MaxRetryAttempts": "5",
    "_AzureOpenAI__TimeoutSeconds": "60",
    "_AzureOpenAI__DeploymentName": "gpt-4o",
    
    "_README5A": "=== AZURE OPENAI DEPLOYMENT CONFIGURATION ===",
    "_README5B": "The default deployment name is used when evaluator types don't specify their own",
    "_README5C": "Individual evaluators can override by setting mcicaps_deploymentname in Dataverse",
    "_README5D": "Example deployment names: gpt-4o, gpt-4-turbo, gpt-35-turbo",
```

**Explanation**: Adds guidance on deployment name configuration and per-evaluator override behavior.

---

## Step 8: Unit Tests

### Test File: `backend/tests/AiAgentEvaluation.UnitTests/Services/LLMJudgeServiceTests.cs`

**New Test Cases to Add**:

```csharp
[Fact]
public async Task EvaluateResponseAsync_UsesEvaluatorDeploymentName_WhenSpecified()
{
    // Arrange
    var evaluatorTypeId = Guid.NewGuid();
    var customDeploymentName = "gpt-4-turbo";
    
    var evaluatorType = new EvaluatorType
    {
        Id = evaluatorTypeId,
        Name = "Custom Evaluator",
        EvaluatorPrompt = "Test prompt with placeholders",
        DeploymentName = customDeploymentName
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(evaluatorType);
    
    var request = new LLMJudgeRequest
    {
        EvaluatorTypeId = evaluatorTypeId,
        UserInput = "test input",
        ExpectedResponse = "expected",
        ActualResponse = "actual"
    };
    
    // Act
    var result = await _service.EvaluateResponseAsync(request);
    
    // Assert
    Assert.True(result.Success);
    // Verify logs contain the custom deployment name
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(customDeploymentName)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}

[Fact]
public async Task EvaluateResponseAsync_UsesDefaultDeploymentName_WhenEvaluatorDoesNotSpecify()
{
    // Arrange
    var evaluatorTypeId = Guid.NewGuid();
    
    var evaluatorType = new EvaluatorType
    {
        Id = evaluatorTypeId,
        Name = "Default Evaluator",
        EvaluatorPrompt = "Test prompt",
        DeploymentName = null // No deployment specified
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(evaluatorType);
    
    var request = new LLMJudgeRequest
    {
        EvaluatorTypeId = evaluatorTypeId,
        UserInput = "test",
        ExpectedResponse = "expected",
        ActualResponse = "actual"
    };
    
    // Act
    var result = await _service.EvaluateResponseAsync(request);
    
    // Assert
    Assert.True(result.Success);
    // Verify logs indicate using system default
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("system default")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}

[Fact]
public async Task GetPromptTemplateAndDeploymentAsync_ReturnsCustomDeployment_WhenSpecified()
{
    // Arrange
    var evaluatorTypeId = Guid.NewGuid();
    var customDeployment = "gpt-35-turbo";
    
    var evaluatorType = new EvaluatorType
    {
        Id = evaluatorTypeId,
        Name = "Budget Evaluator",
        EvaluatorPrompt = "Simple evaluation",
        DeploymentName = customDeployment
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(evaluatorType);
    
    // Act
    var (prompt, deploymentName) = await _service.GetPromptTemplateAndDeploymentAsync(evaluatorTypeId);
    
    // Assert
    Assert.Equal("Simple evaluation", prompt);
    Assert.Equal(customDeployment, deploymentName);
}

[Fact]
public async Task GetPromptTemplateAndDeploymentAsync_ReturnsDefaultDeployment_WhenNotSpecified()
{
    // Arrange
    var evaluatorTypeId = Guid.NewGuid();
    var defaultDeployment = "gpt-4o"; // From AppSettings
    
    var evaluatorType = new EvaluatorType
    {
        Id = evaluatorTypeId,
        Name = "Standard Evaluator",
        EvaluatorPrompt = "Standard evaluation",
        DeploymentName = null
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(evaluatorType);
    
    // Act
    var (prompt, deploymentName) = await _service.GetPromptTemplateAndDeploymentAsync(evaluatorTypeId);
    
    // Assert
    Assert.Equal("Standard evaluation", prompt);
    Assert.Equal(defaultDeployment, deploymentName);
}
```

**Test Execution Command**:
```powershell
cd backend/AiAgentEvaluation
dotnet test ../tests/AiAgentEvaluation.UnitTests/AiAgentEvaluation.UnitTests.csproj --filter "FullyQualifiedName~LLMJudgeService"
```

---

## Step 9: Dataverse Schema Changes

**Table**: `mcicaps_evaluatortype`

**New Column**:
- **Name**: `mcicaps_deploymentname`
- **Display Name**: "Deployment Name"
- **Type**: Single Line of Text
- **Max Length**: 100
- **Required**: No (Optional)
- **Description**: "Azure OpenAI deployment name to use for this evaluator (e.g., gpt-4o, gpt-4-turbo, gpt-35-turbo). Leave blank to use system default."

**Migration Notes**:
- Existing evaluator types will have `NULL` for this field → system uses default deployment
- No data migration required
- Fully backward compatible

**Example Values**:
- Similarity evaluator: `gpt-4o` (high accuracy for semantic comparison)
- Simple classification: `gpt-35-turbo` (cost-effective)
- Complex reasoning: `gpt-4-turbo` (balanced performance)

---

## Validation & Testing Strategy

### Unit Tests
1. ✅ Test deployment name from evaluator type is used
2. ✅ Test fallback to default deployment when not specified
3. ✅ Test `GetPromptTemplateAndDeploymentAsync` returns correct values
4. ✅ Test backward compatibility with existing evaluators

### Integration Tests
1. Create test evaluator with custom deployment name in Dataverse
2. Execute evaluation and verify correct deployment is called
3. Verify logging shows deployment source (evaluator vs default)
4. Test with non-existent deployment name (should fail gracefully)

### Deployment Validation
1. Deploy to staging environment
2. Create test evaluators with different deployment names
3. Run evaluation runs and verify Application Insights logs show correct deployments
4. Monitor costs to validate cheaper models are being used

### Acceptance Criteria
- [ ] Evaluator types can specify custom deployment names in Dataverse
- [ ] Evaluations use evaluator-specified deployment when available
- [ ] System falls back to default deployment when not specified
- [ ] All unit tests pass (>80% coverage on new code)
- [ ] Logs clearly indicate which deployment is being used
- [ ] No breaking changes to existing evaluations
- [ ] Documentation updated (Architecture, PRD, stories)

---

## Rollback Plan

If issues arise:

1. **Code Rollback**: Revert to previous commit (single `ChatClient` approach)
2. **Dataverse Rollback**: Not needed - new column is optional and unused if code is reverted
3. **Configuration Rollback**: Remove deployment name overrides from evaluator types

**Risk Level**: LOW
- New column is optional (nullable)
- Falls back to existing default behavior
- No breaking changes to API contracts
- All existing evaluators continue working unchanged

---

## Post-Implementation

### Monitoring
- Watch Application Insights for deployment name logs
- Monitor Azure OpenAI usage by deployment name
- Track cost changes per evaluation type

### Optimization Opportunities
1. Map simple evaluations to `gpt-35-turbo` for cost savings
2. Use `gpt-4o` for complex semantic similarity
3. Use `gpt-4-turbo` for balanced performance/cost

### Documentation Updates Required
- ✅ Architecture diagram showing deployment resolution flow
- ✅ PRD section on per-evaluator model selection
- ✅ Story documenting feature implementation
- ✅ Update Dataverse schema documentation

---

## Summary

This implementation enables flexible, per-evaluator Azure OpenAI deployment configuration while maintaining full backward compatibility. The changes are minimal, focused, and follow the existing architecture patterns. All existing evaluators continue working without modification, and new evaluators can leverage model-specific optimizations.

**Estimated Implementation Time**: 2-3 hours  
**Testing Time**: 1-2 hours  
**Documentation Time**: 1 hour  
**Total**: 4-6 hours
