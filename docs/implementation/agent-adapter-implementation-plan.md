# Agent Adapter Implementation Plan

**Date**: December 9, 2025  
**Status**: Approved - Ready for Implementation  
**Branch**: `dynamic-agent-calling`

---

## Overview

Implement **Strategy Pattern with Agent Adapters** to support multiple agent types beyond Direct Line API. Users select agent type from dropdown and provide JSON configuration in a text field on the `AgentToEval` table.

### Pre-Created Adapters (Priority Order)

1. **DirectLineAdapter** - Copilot Studio Direct Line API (existing - refactor)
2. **TeamsAdapter** - Microsoft Teams Bot Framework
3. **M365CopilotAdapter** - M365 Copilot via Microsoft Graph
4. **SharePointAdapter** - SharePoint Embedded Agents
5. **M365AgentsSDKAdapter** - M365 Agents SDK
6. **SlackAdapter** - Slack Bot/App Integration

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ AgentWorkerFunction                                         │
│  [ServiceBusTrigger: agent-call-queue]                     │
│                                                              │
│  1. Receive AgentCallMessage                                │
│  2. Load AgentToEval from Dataverse                        │
│  3. Get adapter from factory based on AgentType            │
│  4. Call adapter.ExecuteAsync()                            │
│  5. Queue evaluator jobs                                   │
└──────────────────┬──────────────────────────────────────────┘
                   │
                   ▼
         ┌─────────────────────┐
         │ IAgentAdapterFactory│
         └──────────┬───────────┘
                    │ Returns adapter based on AgentType
                    ▼
         ┌─────────────────────┐
         │   IAgentAdapter     │ ◄─────── Strategy Pattern Interface
         └──────────┬───────────┘
                    │
      ┌─────────────┼──────────────┬───────────────┬────────────┐
      │             │              │               │            │
      ▼             ▼              ▼               ▼            ▼
┌──────────┐  ┌─────────┐  ┌────────────┐  ┌──────────┐  ┌──────────┐
│DirectLine│  │  Teams  │  │M365Copilot │  │SharePoint│  │M365Agents│
│ Adapter  │  │ Adapter │  │  Adapter   │  │ Adapter  │  │   SDK    │
└──────────┘  └─────────┘  └────────────┘  └──────────┘  └──────────┘
```

---

## Dataverse Schema Changes

### AgentToEval Table Updates

**New Fields:**

| Field Name | Type | Description | Required |
|------------|------|-------------|----------|
| `mcicaps_agenttype` | OptionSet | Agent type selector | Yes |
| `mcicaps_agentconfiguration` | String (Max) | JSON configuration for adapter | No |

**AgentType OptionSet Values:**

```
778760000 = DirectLine (Copilot Studio)
778760001 = Teams (Teams Bot Framework)
778760002 = M365Copilot (Microsoft Graph)
778760003 = SharePoint (SharePoint Embedded)
778760004 = M365AgentsSDK (M365 Agents SDK)
778760005 = Slack (Slack Bot/App)
```

**Authentication Modes:**

Each adapter supports one or more authentication modes via `authMode` configuration field:

| Auth Mode | Description | Use Case |
|-----------|-------------|----------|
| `ManagedIdentity` | Function App's Managed Identity | Application permissions, no user context |
| `ClientCredentials` | App ID + Secret from environment variables | Application permissions, service principal |
| `UserCredentials` | Username + Password from environment variables | Delegated permissions, user context |
| `RefreshToken` | Stored refresh token from environment variable | Delegated permissions, persistent user context |

**Configuration Pattern:**

```json
{
  "authMode": "UserCredentials",  // or "ManagedIdentity", "ClientCredentials", "RefreshToken"
  "environmentVariables": {
    "username": "TestUser_Username",
    "password": "TestUser_Password"
  }
}
```

**Example Record:**

```json
{
  "mcicaps_agenttoevalid": "guid",
  "mcicaps_name": "Customer Service Teams Bot",
  "mcicaps_agenttype": 778760001,
  "mcicaps_agentconfiguration": "{\"botId\":\"abc-123\",\"tenantId\":\"xyz-789\",\"environmentVariables\":{\"appId\":\"TeamsBot_AppId\",\"appSecret\":\"TeamsBot_AppSecret\"}}",
  "mcicaps_directlinespeechurl": null
}
```

**Credential Management via Dataverse Environment Variables:**

All sensitive credentials (API keys, secrets, tokens) are stored as **Dataverse Environment Variables**. This approach:
- ✅ Allows plain text storage (for dev/test - not recommended for production)
- ✅ Supports Azure Key Vault integration (production - recommended)
- ✅ Dataverse automatically retrieves values from Key Vault
- ✅ No custom Key Vault client code needed in adapters
- ✅ Centralized credential management in Dataverse UI

**How it works:**
1. User creates Environment Variable in Dataverse (e.g., `TeamsBot_AppSecret`)
2. User configures Environment Variable to use Key Vault secret (optional)
3. Adapter configuration references the Environment Variable name
4. Our code calls `GetEnvironmentVariableValue()` - Dataverse handles Key Vault lookup

**Reference:** [Dynamics Plugin Key Vault Integration](https://tldr-dynamics.com/blog/dynamics-plugin-key-vault-integration)

**Migration Strategy:**
- Existing agents: Set `mcicaps_agenttype` = 778760000 (DirectLine)
- Existing agents: Copy `mcicaps_directlinespeechurl` → `mcicaps_agentconfiguration` as `{"directLineUrl": "..."}`
- Mark `mcicaps_directlinespeechurl` as deprecated (keep for backward compatibility)

---

## Implementation Phases

### Phase 1: Foundation - Strategy Pattern & DirectLine Refactor (Week 1)

**Goal**: Establish adapter pattern and refactor existing Direct Line code

#### 1.1 Create Core Interfaces

**File**: `backend/AiAgentEvaluation/Services/Adapters/IAgentAdapter.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Interface for agent adapters that handle communication with different agent types
/// </summary>
public interface IAgentAdapter
{
    /// <summary>
    /// Execute agent call and return response
    /// </summary>
    Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Standardized request to any agent adapter
/// </summary>
public class AgentCallRequest
{
    public required string UserInput { get; init; }
    public ConversationContext? ConversationContext { get; init; }
    public required Guid TestCaseId { get; init; }
    public required Guid EvaluationRunId { get; init; }
    public required AgentConfiguration Configuration { get; init; }
}

/// <summary>
/// Standardized response from any agent adapter
/// </summary>
public class AgentResponse
{
    public required bool Success { get; init; }
    public string ResponseText { get; init; } = string.Empty;
    public int ExecutionTimeMs { get; init; }
    public ConversationContext? ConversationContext { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Agent configuration from Dataverse (parsed from mcicaps_agentconfiguration JSON)
/// </summary>
public class AgentConfiguration
{
    public required int AgentType { get; init; }
    public required Dictionary<string, object> Config { get; init; }
    
    public T? GetValue<T>(string key)
    {
        if (Config.TryGetValue(key, out var value))
        {
            if (value is T typedValue)
                return typedValue;
            
            // Try convert for primitives
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }
    
    /// <summary>
    /// Get all environment variable names from configuration
    /// Configuration uses nested "environmentVariables" object with field name -> env var name mappings
    /// Example: "environmentVariables": {"appId": "TeamsBot_AppId", "appSecret": "TeamsBot_AppSecret"}
    /// Returns dictionary of field name -> environment variable name
    /// </summary>
    public Dictionary<string, string> GetEnvironmentVariableNames()
    {
        var envVars = new Dictionary<string, string>();
        
        if (Config.TryGetValue("environmentVariables", out var envVarsObj))
        {
            // Handle JsonElement (from System.Text.Json deserialization)
            if (envVarsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in jsonElement.EnumerateObject())
                {
                    var envVarName = property.Value.GetString();
                    if (!string.IsNullOrEmpty(envVarName))
                    {
                        envVars[property.Name] = envVarName;
                    }
                }
            }
            // Handle Dictionary (if already parsed)
            else if (envVarsObj is Dictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    var envVarName = kvp.Value?.ToString();
                    if (!string.IsNullOrEmpty(envVarName))
                    {
                        envVars[kvp.Key] = envVarName;
                    }
                }
            }
            // Handle string (JSON string that needs parsing)
            else if (envVarsObj is string jsonString)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                    if (parsed != null)
                    {
                        envVars = parsed;
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }
        }
        
        return envVars;
    }
    
    /// <summary>
    /// Get single environment variable name from configuration
    /// </summary>
    public string? GetEnvironmentVariableName(string credentialField)
    {
        var envVars = GetEnvironmentVariableNames();
        return envVars.TryGetValue(credentialField, out var envVarName) ? envVarName : null;
    }
}
```

**File**: `backend/AiAgentEvaluation/Services/Adapters/IAgentAdapterFactory.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Factory for creating agent adapters based on agent type
/// </summary>
public interface IAgentAdapterFactory
{
    /// <summary>
    /// Get appropriate adapter for the specified agent type
    /// </summary>
    IAgentAdapter GetAdapter(int agentType);
}
```

#### 1.2 Update AgentToEval Model

**File**: `backend/AiAgentEvaluation/Models/AgentToEval.cs`

Add new properties:

```csharp
[JsonPropertyName("mcicaps_agenttype")]
public int? AgentType { get; set; }

[JsonPropertyName("mcicaps_agentconfiguration")]
public string? AgentConfiguration { get; set; }

/// <summary>
/// Parse configuration JSON into strongly-typed object
/// </summary>
public AgentConfiguration GetConfiguration()
{
    var config = new Dictionary<string, object>();
    
    if (!string.IsNullOrEmpty(AgentConfiguration))
    {
        try
        {
            config = JsonSerializer.Deserialize<Dictionary<string, object>>(AgentConfiguration) 
                ?? new Dictionary<string, object>();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse agent configuration JSON for agent {Name}", ex);
        }
    }
    
    // Backward compatibility: If no AgentType set, default to DirectLine
    var agentType = AgentType ?? 778760000;
    
    // Backward compatibility: If DirectLineSpeechUrl set but no config, migrate
    if (config.Count == 0 && !string.IsNullOrEmpty(DirectLineSpeechUrl))
    {
        config["directLineUrl"] = DirectLineSpeechUrl;
    }
    
    return new AgentConfiguration
    {
        AgentType = agentType,
        Config = config
    };
}
```

#### 1.3 Refactor CopilotStudioService → DirectLineAdapter

**File**: `backend/AiAgentEvaluation/Services/Adapters/DirectLineAdapter.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Copilot Studio agents using Direct Line API
/// </summary>
public class DirectLineAdapter : IAgentAdapter
{
    private readonly ILogger<DirectLineAdapter> _logger;
    private readonly ICopilotStudioService _copilotStudioService;
    
    public DirectLineAdapter(
        ILogger<DirectLineAdapter> logger,
        ICopilotStudioService copilotStudioService)
    {
        _logger = logger;
        _copilotStudioService = copilotStudioService;
    }
    
    public async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get Direct Line URL from configuration
            var directLineUrl = request.Configuration.GetValue<string>("directLineUrl");
            
            if (string.IsNullOrEmpty(directLineUrl))
            {
                throw new InvalidOperationException(
                    "DirectLine adapter requires 'directLineUrl' in configuration");
            }
            
            _logger.LogInformation(
                "DirectLineAdapter: Calling Direct Line API for test case {TestCaseId}",
                request.TestCaseId);
            
            // Use existing CopilotStudioService (unchanged)
            var response = await _copilotStudioService.SendMessageAsync(
                directLineUrl,
                request.UserInput,
                request.ConversationContext,
                cancellationToken);
            
            return new AgentResponse
            {
                Success = response.Success,
                ResponseText = response.ResponseText,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ConversationContext = response.ConversationContext,
                ErrorMessage = response.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "DirectLineAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
```

**Note**: Keep `ICopilotStudioService` and `CopilotStudioService` unchanged for now. DirectLineAdapter wraps it.

#### 1.4 Implement Factory

**File**: `backend/AiAgentEvaluation/Services/Adapters/AgentAdapterFactory.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

public class AgentAdapterFactory : IAgentAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentAdapterFactory> _logger;
    
    public AgentAdapterFactory(
        IServiceProvider serviceProvider,
        ILogger<AgentAdapterFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public IAgentAdapter GetAdapter(int agentType)
    {
        _logger.LogInformation("Creating adapter for agent type {AgentType}", agentType);
        
        return agentType switch
        {
            778760000 => _serviceProvider.GetRequiredService<DirectLineAdapter>(),
            778760001 => _serviceProvider.GetRequiredService<TeamsAdapter>(),
            778760002 => _serviceProvider.GetRequiredService<M365CopilotAdapter>(),
            778760003 => _serviceProvider.GetRequiredService<SharePointAdapter>(),
            778760004 => _serviceProvider.GetRequiredService<M365AgentsSDKAdapter>(),
            778760005 => _serviceProvider.GetRequiredService<SlackAdapter>(),
            _ => throw new NotSupportedException($"Agent type {agentType} is not supported")
        };
    }
}
```

#### 1.5 Update AgentWorkerFunction

**File**: `backend/AiAgentEvaluation/Functions/AgentWorkerFunction.cs`

```csharp
private readonly IAgentAdapterFactory _agentAdapterFactory;

public AgentWorkerFunction(
    ILogger<AgentWorkerFunction> logger,
    IDataverseService dataverseService,
    IAgentAdapterFactory agentAdapterFactory, // NEW
    IServiceBusService serviceBusService,
    IOptions<AppSettings> settings)
{
    _logger = logger;
    _dataverseService = dataverseService;
    _agentAdapterFactory = agentAdapterFactory; // NEW
    _serviceBusService = serviceBusService;
    _settings = settings.Value;
}

[Function("ProcessAgentCall")]
public async Task ProcessAgentCall(
    [ServiceBusTrigger("agent-call-queue", Connection = "ServiceBusNamespace")] 
    string messageBody,
    CancellationToken cancellationToken)
{
    // ... existing deserialization code ...
    
    // Get agent configuration from Dataverse
    var agentConfig = agentCallMessage.AgentToEval;
    var configuration = agentConfig.GetConfiguration();
    
    // Get appropriate adapter
    var adapter = _agentAdapterFactory.GetAdapter(configuration.AgentType);
    
    // Execute agent call
    var request = new AgentCallRequest
    {
        UserInput = agentCallMessage.UserInput,
        ConversationContext = null, // Single-turn for now
        TestCaseId = agentCallMessage.TestCaseId,
        EvaluationRunId = agentCallMessage.EvaluationRunId,
        Configuration = configuration
    };
    
    var response = await adapter.ExecuteAsync(request, cancellationToken);
    
    if (!response.Success)
    {
        _logger.LogError(
            "Agent call failed for test case {TestCaseId}: {ErrorMessage}",
            agentCallMessage.TestCaseId, response.ErrorMessage);
        // ... error handling ...
    }
    
    // ... rest of existing code (queue evaluator jobs) ...
}
```

#### 1.6 Register Services in Program.cs

**File**: `backend/AiAgentEvaluation/Program.cs`

```csharp
// Register adapter factory
builder.Services.AddScoped<IAgentAdapterFactory, AgentAdapterFactory>();

// Register adapters
builder.Services.AddScoped<DirectLineAdapter>();
builder.Services.AddScoped<TeamsAdapter>();
builder.Services.AddScoped<M365CopilotAdapter>();
builder.Services.AddScoped<SharePointAdapter>();
builder.Services.AddScoped<M365AgentsSDKAdapter>();
builder.Services.AddScoped<SlackAdapter>();
```

#### 1.7 Add Environment Variable Support to DataverseService

**File**: `backend/AiAgentEvaluation/Services/IDataverseService.cs`

Add method to retrieve environment variables:

```csharp
/// <summary>
/// Get environment variable value from Dataverse
/// Dataverse automatically retrieves from Key Vault if configured
/// </summary>
Task<string?> GetEnvironmentVariableValueAsync(
    string schemaName, 
    CancellationToken cancellationToken = default);
```

**File**: `backend/AiAgentEvaluation/Services/DataverseService.cs`

Implement environment variable retrieval:

```csharp
public async Task<string?> GetEnvironmentVariableValueAsync(
    string schemaName,
    CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Getting environment variable {SchemaName}", schemaName);
    EnsureClientReady();

    return await _resilientService.ExecuteAsync(async ct =>
    {
        // Query environmentvariabledefinition by schemaname
        var query = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid", "defaultvalue", "valueschema"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName)
                }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "environmentvariabledefinition",
                    LinkToEntityName = "environmentvariablevalue",
                    LinkFromAttributeName = "environmentvariabledefinitionid",
                    LinkToAttributeName = "environmentvariabledefinitionid",
                    JoinOperator = JoinOperator.LeftOuter,
                    Columns = new ColumnSet("value"),
                    EntityAlias = "envvalue"
                }
            }
        };

        var results = await Task.Run(() => _serviceClient!.RetrieveMultiple(query), ct);
        
        if (results.Entities.Count == 0)
        {
            _logger.LogWarning("Environment variable {SchemaName} not found", schemaName);
            return null;
        }

        var entity = results.Entities[0];
        
        // Check for current value (from environmentvariablevalue - higher priority)
        if (entity.Contains("envvalue.value"))
        {
            var currentValue = entity.GetAttributeValue<AliasedValue>("envvalue.value")?.Value as string;
            if (!string.IsNullOrEmpty(currentValue))
            {
                _logger.LogInformation("Retrieved environment variable {SchemaName} from current value", schemaName);
                return currentValue;
            }
        }
        
        // Fall back to default value (from environmentvariabledefinition)
        var defaultValue = entity.GetAttributeValue<string>("defaultvalue");
        if (!string.IsNullOrEmpty(defaultValue))
        {
            _logger.LogInformation("Retrieved environment variable {SchemaName} from default value", schemaName);
            return defaultValue;
        }
        
        // Check valueschema for Azure Key Vault reference
        var valueSchema = entity.GetAttributeValue<string>("valueschema");
        if (!string.IsNullOrEmpty(valueSchema))
        {
            // If valueschema is set, Dataverse will retrieve from Key Vault automatically
            // and populate it in the value field above. If we get here, Key Vault lookup failed.
            _logger.LogWarning(
                "Environment variable {SchemaName} has Key Vault schema but no value retrieved", 
                schemaName);
        }
        
        _logger.LogWarning("Environment variable {SchemaName} has no value", schemaName);
        return null;
        
    }, "GetEnvironmentVariableValue",
    exception => exception is FaultException<OrganizationServiceFault>,
    cancellationToken);
}
```

#### 1.8 Update DataverseService GetAgentToEval

**File**: `backend/AiAgentEvaluation/Services/DataverseService.cs`

Update `GetAgentToEvalAsync` to include new fields:

```csharp
var columnSet = new ColumnSet(
    Prefixed("agenttoevalid"),
    Prefixed("name"),
    Prefixed("directlinespeechurl"),
    Prefixed("agenttype"),              // NEW
    Prefixed("agentconfiguration"),     // NEW
    "statecode",
    "statuscode",
    "createdon",
    "modifiedon"
);

// ... in mapping ...
return new AgentToEval
{
    Id = entity.Id,
    Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
    DirectLineSpeechUrl = entity.GetAttributeValue<string>(Prefixed("directlinespeechurl")) ?? string.Empty,
    AgentType = entity.GetAttributeValue<OptionSetValue>(Prefixed("agenttype"))?.Value,  // NEW
    AgentConfiguration = entity.GetAttributeValue<string>(Prefixed("agentconfiguration")), // NEW
    StateCode = GetIntValue(entity, "statecode"),
    StatusCode = GetIntValue(entity, "statuscode"),
    CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
    ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
};
```

#### 1.9 Create Base Adapter with Credential Retrieval

**File**: `backend/AiAgentEvaluation/Services/Adapters/BaseAgentAdapter.cs`

Create base class with common credential retrieval logic:

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Base class for agent adapters with common functionality
/// </summary>
public abstract class BaseAgentAdapter : IAgentAdapter
{
    protected readonly ILogger Logger;
    protected readonly IDataverseService DataverseService;
    
    protected BaseAgentAdapter(
        ILogger logger,
        IDataverseService dataverseService)
    {
        Logger = logger;
        DataverseService = dataverseService;
    }
    
    public abstract Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get credential value from Dataverse environment variable
    /// Configuration should contain "environmentVariables" object with field name -> env var name mapping
    /// Example: "environmentVariables": {"appSecret": "TeamsBot_AppSecret"}
    /// </summary>
    protected async Task<string> GetCredentialFromEnvironmentVariableAsync(
        AgentConfiguration configuration,
        string credentialField,
        CancellationToken cancellationToken = default)
    {
        var envVarName = configuration.GetEnvironmentVariableName(credentialField);
        
        if (string.IsNullOrEmpty(envVarName))
        {
            throw new InvalidOperationException(
                $"Configuration must contain 'environmentVariables.{credentialField}' with environment variable name");
        }
        
        Logger.LogInformation(
            "Retrieving credential '{CredentialField}' from environment variable '{EnvVarName}'",
            credentialField,
            envVarName);
        
        var value = await DataverseService.GetEnvironmentVariableValueAsync(
            envVarName,
            cancellationToken);
        
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Environment variable '{envVarName}' (for field '{credentialField}') not found or has no value. " +
                $"Please create it in Dataverse with schema name '{envVarName}'.");
        }
        
        return value;
    }
    
    /// <summary>
    /// Get all credentials from Dataverse environment variables
    /// Reads "environmentVariables" object from configuration and retrieves all values
    /// Returns dictionary of credential field name -> credential value
    /// Example: {"appId": "client-id-value", "appSecret": "secret-value"}
    /// </summary>
    protected async Task<Dictionary<string, string>> GetAllCredentialsFromEnvironmentVariablesAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var envVarNames = configuration.GetEnvironmentVariableNames();
        
        if (envVarNames.Count == 0)
        {
            Logger.LogInformation("No environment variables to retrieve");
            return new Dictionary<string, string>();
        }
        
        Logger.LogInformation(
            "Retrieving {Count} credentials from environment variables: {EnvVars}",
            envVarNames.Count,
            string.Join(", ", envVarNames.Values));
        
        var credentials = new Dictionary<string, string>();
        
        // Retrieve all environment variables in parallel
        var tasks = envVarNames.Select(async kvp =>
        {
            var fieldName = kvp.Key;
            var envVarName = kvp.Value;
            
            var value = await DataverseService.GetEnvironmentVariableValueAsync(
                envVarName,
                cancellationToken);
            
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{envVarName}' (for field '{fieldName}') not found or has no value. " +
                    $"Please create it in Dataverse with schema name '{envVarName}'.");
            }
            
            return new KeyValuePair<string, string>(fieldName, value);
        });
        
        var results = await Task.WhenAll(tasks);
        
        foreach (var result in results)
        {
            credentials[result.Key] = result.Value;
        }
        
        return credentials;
    }
    
    /// <summary>
    /// Get Azure credential based on authentication mode
    /// Supports: ManagedIdentity, ClientCredentials, UserCredentials, RefreshToken
    /// </summary>
    protected async Task<TokenCredential> GetAzureCredentialAsync(
        AgentConfiguration configuration,
        string[] scopes,
        CancellationToken cancellationToken = default)
    {
        var authMode = configuration.GetValue<string>("authMode") ?? "ManagedIdentity";
        
        Logger.LogInformation("Using authentication mode: {AuthMode}", authMode);
        
        return authMode.ToLowerInvariant() switch
        {
            "managedidentity" => new DefaultAzureCredential(),
            
            "clientcredentials" => await GetClientCredentialAsync(configuration, cancellationToken),
            
            "usercredentials" => await GetUserPasswordCredentialAsync(configuration, scopes, cancellationToken),
            
            "refreshtoken" => await GetRefreshTokenCredentialAsync(configuration, scopes, cancellationToken),
            
            _ => throw new NotSupportedException($"Authentication mode '{authMode}' is not supported")
        };
    }
    
    private async Task<TokenCredential> GetClientCredentialAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(configuration, cancellationToken);
        
        var tenantId = configuration.GetValue<string>("tenantId") ?? 
            throw new InvalidOperationException("ClientCredentials mode requires 'tenantId'");
        var clientId = credentials.GetValueOrDefault("clientId") ?? 
            throw new InvalidOperationException("ClientCredentials mode requires 'clientId' in environmentVariables");
        var clientSecret = credentials.GetValueOrDefault("clientSecret") ?? 
            throw new InvalidOperationException("ClientCredentials mode requires 'clientSecret' in environmentVariables");
        
        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }
    
    private async Task<TokenCredential> GetUserPasswordCredentialAsync(
        AgentConfiguration configuration,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(configuration, cancellationToken);
        
        var tenantId = configuration.GetValue<string>("tenantId") ?? 
            throw new InvalidOperationException("UserCredentials mode requires 'tenantId'");
        var clientId = configuration.GetValue<string>("clientId") ?? 
            throw new InvalidOperationException("UserCredentials mode requires 'clientId'");
        var username = credentials.GetValueOrDefault("username") ?? 
            throw new InvalidOperationException("UserCredentials mode requires 'username' in environmentVariables");
        var password = credentials.GetValueOrDefault("password") ?? 
            throw new InvalidOperationException("UserCredentials mode requires 'password' in environmentVariables");
        
        return new UsernamePasswordCredential(username, password, tenantId, clientId);
    }
    
    private async Task<TokenCredential> GetRefreshTokenCredentialAsync(
        AgentConfiguration configuration,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        // RefreshToken mode: Store long-lived refresh token obtained via interactive OAuth flow
        // This allows evaluations to run with user context without requiring password
        var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(configuration, cancellationToken);
        
        var tenantId = configuration.GetValue<string>("tenantId") ?? 
            throw new InvalidOperationException("RefreshToken mode requires 'tenantId'");
        var clientId = configuration.GetValue<string>("clientId") ?? 
            throw new InvalidOperationException("RefreshToken mode requires 'clientId'");
        var refreshToken = credentials.GetValueOrDefault("refreshToken") ?? 
            throw new InvalidOperationException("RefreshToken mode requires 'refreshToken' in environmentVariables");
        
        // Use OnBehalfOfCredential or custom implementation
        // Note: Azure.Identity doesn't have direct RefreshTokenCredential
        // This would require custom implementation using MSAL.NET
        throw new NotImplementedException(
            "RefreshToken authentication mode requires custom MSAL.NET implementation. " +
            "Consider using UserCredentials mode instead.");
    }
}
```

#### 1.10 Unit Tests

**File**: `backend/tests/AiAgentEvaluation.UnitTests/Services/Adapters/DirectLineAdapterTests.cs`

```csharp
public class DirectLineAdapterTests
{
    [Fact]
    public async Task ExecuteAsync_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectLineAdapter>>();
        var mockCopilotService = new Mock<ICopilotStudioService>();
        
        mockCopilotService
            .Setup(x => x.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ConversationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CopilotDirectLineResponse
            {
                Success = true,
                ResponseText = "Test response",
                ExecutionTimeMs = 123
            });
        
        var adapter = new DirectLineAdapter(mockLogger.Object, mockCopilotService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test input",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>
                {
                    ["directLineUrl"] = "https://example.com/directline"
                }
            }
        };
        
        // Act
        var result = await adapter.ExecuteAsync(request);
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test response", result.ResponseText);
        mockCopilotService.Verify(x => x.SendMessageAsync(
            "https://example.com/directline",
            "Test input",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }
    
    [Fact]
    public async Task ExecuteAsync_MissingDirectLineUrl_ThrowsException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectLineAdapter>>();
        var mockCopilotService = new Mock<ICopilotStudioService>();
        var adapter = new DirectLineAdapter(mockLogger.Object, mockCopilotService.Object);
        
        var request = new AgentCallRequest
        {
            UserInput = "Test input",
            TestCaseId = Guid.NewGuid(),
            EvaluationRunId = Guid.NewGuid(),
            Configuration = new AgentConfiguration
            {
                AgentType = 778760000,
                Config = new Dictionary<string, object>() // Empty config
            }
        };
        
        // Act
        var result = await adapter.ExecuteAsync(request);
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("directLineUrl", result.ErrorMessage);
    }
}
```

---

### Phase 2: Teams Adapter (Week 2)

**Goal**: Implement Teams Bot Framework adapter

#### 2.1 Research & Design

- Research Bot Framework Direct Line API for Teams
- Determine authentication requirements (Bot credentials)
- Define configuration schema

**Configuration Schema:**

```json
{
  "botId": "abc-123-bot-id",
  "tenantId": "xyz-789-tenant",
  "environmentVariables": {
    "appId": "TeamsBot_AppId",
    "appSecret": "TeamsBot_AppSecret"
  }
}
```

**Dataverse Environment Variables Setup:**

Users create environment variables in Dataverse:

1. **App ID:**
   - **Schema Name**: `TeamsBot_AppId`
   - **Display Name**: Teams Bot App ID
   - **Type**: Text
   - **Current Value**: Bot application (client) ID

2. **App Secret:**
   - **Schema Name**: `TeamsBot_AppSecret`
   - **Display Name**: Teams Bot App Secret
   - **Type**: Secret (recommended)
   - **Current Value**: 
     - Option 1 (Dev/Test): Plain text secret
     - Option 2 (Production): Azure Key Vault reference

**Key Vault Integration (Recommended for Production):**

Follow [this guide](https://tldr-dynamics.com/blog/dynamics-plugin-key-vault-integration) to configure Key Vault:
1. Create secret in Azure Key Vault (e.g., `TeamsBot-AppSecret`)
2. Grant Dataverse managed identity access to Key Vault
3. In environment variable, set **Value Schema**:
   ```json
   {
     "SecretUrl": "https://your-keyvault.vault.azure.net/secrets/TeamsBot-AppSecret"
   }
   ```
4. Dataverse automatically retrieves value from Key Vault

#### 2.2 Implementation

**File**: `backend/AiAgentEvaluation/Services/Adapters/TeamsAdapter.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Microsoft Teams bots using Bot Framework
/// </summary>
public class TeamsAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public TeamsAdapter(
        ILogger<TeamsAdapter> logger,
        HttpClient httpClient,
        IDataverseService dataverseService)
        : base(logger, dataverseService)
    {
        _httpClient = httpClient;
    }
    
    public override async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get configuration
            var botId = request.Configuration.GetValue<string>("botId");
            var tenantId = request.Configuration.GetValue<string>("tenantId");
            
            // Validate required fields
            if (string.IsNullOrEmpty(botId))
            {
                throw new InvalidOperationException(
                    "Teams adapter requires 'botId' in configuration");
            }
            
            Logger.LogInformation(
                "TeamsAdapter: Calling Teams bot {BotId} for test case {TestCaseId}",
                botId, request.TestCaseId);
            
            // Get all credentials from Dataverse environment variables
            var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(
                request.Configuration,
                cancellationToken);
            
            var appId = credentials["appId"];
            var appSecret = credentials["appSecret"];
            
            // Get Bot Framework token
            var token = await GetBotFrameworkTokenAsync(appId, appSecret, cancellationToken);
            
            // Start conversation with bot
            var conversationId = await StartConversationAsync(botId, token, cancellationToken);
            
            // Send message to bot
            var botResponse = await SendMessageToBotAsync(
                botId, 
                conversationId, 
                request.UserInput, 
                token, 
                cancellationToken);
            
            return new AgentResponse
            {
                Success = true,
                ResponseText = botResponse,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "TeamsAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
    
    private async Task<string> GetBotFrameworkTokenAsync(string botId, string appSecret, CancellationToken ct)
    {
        // Implement Bot Framework OAuth flow
        // POST https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token
        throw new NotImplementedException("Bot Framework token acquisition");
    }
    
    private async Task<string> StartConversationAsync(string botId, string token, CancellationToken ct)
    {
        // POST https://directline.botframework.com/v3/directline/conversations
        throw new NotImplementedException("Start conversation with bot");
    }
    
    private async Task<string> SendMessageToBotAsync(
        string botId, 
        string conversationId, 
        string message, 
        string token, 
        CancellationToken ct)
    {
        // POST https://directline.botframework.com/v3/directline/conversations/{conversationId}/activities
        // GET activities and return bot response
        throw new NotImplementedException("Send message and get response");
    }
}
```

#### 2.3 Unit Tests

**File**: `backend/tests/AiAgentEvaluation.UnitTests/Services/Adapters/TeamsAdapterTests.cs`

---

### Phase 3: M365 Copilot Adapter (Week 3)

**Goal**: Implement M365 Copilot adapter using Microsoft Graph API

#### 3.1 Research & Design

- Research Microsoft Graph API for M365 Copilot (if available)
- Determine authentication requirements (Delegated vs Application permissions)
- Define configuration schema

**Configuration Schema:**

**Option 1: Managed Identity (Application Permissions)**
```json
{
  "authMode": "ManagedIdentity",
  "tenantId": "xyz-789",
  "scopes": ["https://graph.microsoft.com/.default"]
}
```

**Option 2: User Credentials (Delegated Permissions)**
```json
{
  "authMode": "UserCredentials",
  "tenantId": "xyz-789",
  "clientId": "app-client-id",
  "scopes": ["User.Read", "Chat.Read"],
  "environmentVariables": {
    "username": "TestUser_M365_Username",
    "password": "TestUser_M365_Password"
  }
}
```

**Option 3: Client Credentials (Application Permissions with Service Principal)**
```json
{
  "authMode": "ClientCredentials",
  "tenantId": "xyz-789",
  "environmentVariables": {
    "clientId": "M365App_ClientId",
    "clientSecret": "M365App_ClientSecret"
  }
}
```

**Note**: 
- **ManagedIdentity**: Function App's identity must have Graph API application permissions
- **UserCredentials**: Runs as specific user (delegated permissions) - best for user-specific context like M365 Copilot
- **ClientCredentials**: Runs as service principal (application permissions)

#### 3.2 Implementation

**File**: `backend/AiAgentEvaluation/Services/Adapters/M365CopilotAdapter.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for M365 Copilot using Microsoft Graph API
/// Supports ManagedIdentity, UserCredentials, and ClientCredentials authentication modes
/// </summary>
public class M365CopilotAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public M365CopilotAdapter(
        ILogger<M365CopilotAdapter> logger,
        HttpClient httpClient,
        IDataverseService dataverseService)
        : base(logger, dataverseService)
    {
        _httpClient = httpClient;
    }
    
    public override async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var tenantId = request.Configuration.GetValue<string>("tenantId");
            var scopes = request.Configuration.GetValue<string[]>("scopes") ?? 
                new[] { "https://graph.microsoft.com/.default" };
            
            Logger.LogInformation(
                "M365CopilotAdapter: Calling M365 Copilot for test case {TestCaseId} with auth mode {AuthMode}",
                request.TestCaseId,
                request.Configuration.GetValue<string>("authMode") ?? "ManagedIdentity");
            
            // Get appropriate credential based on auth mode
            var credential = await GetAzureCredentialAsync(
                request.Configuration,
                scopes,
                cancellationToken);
            
            // Create Graph client with chosen credential
            var graphClient = new GraphServiceClient(credential, scopes);
            
            // TODO: Replace with actual M365 Copilot API when available
            // For now, placeholder implementation
            var responseText = $"M365 Copilot response to: {request.UserInput}";
            
            return new AgentResponse
            {
                Success = true,
                ResponseText = responseText,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "M365CopilotAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
```

---

### Phase 4: SharePoint Adapter (Week 4)

**Configuration Schema:**

**Option 1: Managed Identity (Application Permissions)**
```json
{
  "authMode": "ManagedIdentity",
  "siteUrl": "https://contoso.sharepoint.com/sites/site1",
  "agentId": "agent-guid"
}
```

**Option 2: User Credentials (Delegated Permissions)**
```json
{
  "authMode": "UserCredentials",
  "tenantId": "xyz-789",
  "clientId": "app-client-id",
  "siteUrl": "https://contoso.sharepoint.com/sites/site1",
  "agentId": "agent-guid",
  "environmentVariables": {
    "username": "TestUser_SharePoint_Username",
    "password": "TestUser_SharePoint_Password"
  }
}
```

**Note**: 
- **ManagedIdentity**: Function App's identity must have access to SharePoint site
- **UserCredentials**: Runs as specific user (useful for testing user-specific permissions)

**File**: `backend/AiAgentEvaluation/Services/Adapters/SharePointAdapter.cs`

---

### Phase 5: M365 Agents SDK Adapter (Week 5)

**Configuration Schema:**

```json
{
  "agentId": "agent-guid",
  "endpoint": "https://agents.microsoft.com/api"
}
```

**Note**: M365 Agents SDK adapter uses Managed Identity for authentication.

**File**: `backend/AiAgentEvaluation/Services/Adapters/M365AgentsSDKAdapter.cs`

---

### Phase 6: Slack Adapter (Week 6)

**Goal**: Implement Slack bot/app adapter

#### 6.1 Research & Design

- Research Slack API for bot messaging (Web API / Socket Mode)
- Determine authentication requirements (Bot User OAuth Token)
- Define configuration schema

**Configuration Schema:**

```json
{
  "channelId": "C1234567890",
  "environmentVariables": {
    "botToken": "Slack_BotToken",
    "appToken": "Slack_AppToken"
  }
}
```

**Dataverse Environment Variables Setup:**

Users create environment variables in Dataverse:

1. **Bot Token:**
   - **Schema Name**: `Slack_BotToken`
   - **Display Name**: Slack Bot Token
   - **Type**: Secret (recommended)
   - **Current Value**: `xoxb-...` (plain text or Key Vault reference)

2. **App Token (for Socket Mode - optional):**
   - **Schema Name**: `Slack_AppToken`
   - **Display Name**: Slack App Token
   - **Type**: Secret (recommended)
   - **Current Value**: `xapp-...` (plain text or Key Vault reference)

#### 6.2 Implementation

**File**: `backend/AiAgentEvaluation/Services/Adapters/SlackAdapter.cs`

```csharp
namespace AiAgentEvaluation.Services.Adapters;

/// <summary>
/// Adapter for Slack bots using Slack Web API
/// </summary>
public class SlackAdapter : BaseAgentAdapter
{
    private readonly HttpClient _httpClient;
    
    public SlackAdapter(
        ILogger<SlackAdapter> logger,
        HttpClient httpClient,
        IDataverseService dataverseService)
        : base(logger, dataverseService)
    {
        _httpClient = httpClient;
    }
    
    public override async Task<AgentResponse> ExecuteAsync(
        AgentCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get configuration
            var channelId = request.Configuration.GetValue<string>("channelId");
            
            // Validate required fields
            if (string.IsNullOrEmpty(channelId))
            {
                throw new InvalidOperationException(
                    "Slack adapter requires 'channelId' in configuration");
            }
            
            Logger.LogInformation(
                "SlackAdapter: Sending message to Slack for test case {TestCaseId}",
                request.TestCaseId);
            
            // Get all credentials from Dataverse environment variables
            var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(
                request.Configuration,
                cancellationToken);
            
            var botToken = credentials["botToken"];
            
            // Optional: Get app token if using Socket Mode
            credentials.TryGetValue("appToken", out var appToken);
            
            // Send message to Slack bot
            // POST https://slack.com/api/chat.postMessage
            var slackResponse = await SendMessageToSlackAsync(
                botToken,
                channelId,
                request.UserInput,
                cancellationToken);
            
            // Poll for bot response
            // GET https://slack.com/api/conversations.history
            var responseText = await WaitForBotResponseAsync(
                botToken,
                channelId,
                slackResponse.MessageTimestamp,
                cancellationToken);
            
            return new AgentResponse
            {
                Success = true,
                ResponseText = responseText,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "SlackAdapter: Failed for test case {TestCaseId}", 
                request.TestCaseId);
            
            return new AgentResponse
            {
                Success = false,
                ResponseText = string.Empty,
                ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
    
    private async Task<SlackMessageResponse> SendMessageToSlackAsync(
        string botToken,
        string channelId,
        string message,
        CancellationToken ct)
    {
        // Implement Slack Web API call
        // POST https://slack.com/api/chat.postMessage
        throw new NotImplementedException("Slack message sending");
    }
    
    private async Task<string> WaitForBotResponseAsync(
        string botToken,
        string channelId,
        string afterTimestamp,
        CancellationToken ct)
    {
        // Poll conversations.history for bot response
        // Retry with exponential backoff (max 30 seconds)
        throw new NotImplementedException("Wait for Slack bot response");
    }
    
    private class SlackMessageResponse
    {
        public string MessageTimestamp { get; set; } = string.Empty;
    }
}
```

#### 6.3 Unit Tests

**File**: `backend/tests/AiAgentEvaluation.UnitTests/Services/Adapters/SlackAdapterTests.cs`

---

## Testing Strategy

### Unit Tests (Per Adapter)

- ✅ Valid configuration → Success
- ✅ Missing required config → Error
- ✅ Authentication failure → Error
- ✅ Network timeout → Error
- ✅ Invalid response → Error

### Integration Tests

- ✅ End-to-end agent call via adapter
- ✅ Factory returns correct adapter type
- ✅ Configuration parsing from Dataverse
- ✅ AgentWorkerFunction uses adapter correctly

### Manual Testing Checklist

1. Create test agent in Dataverse with each adapter type
2. Run evaluation and verify agent response
3. Check Application Insights logs
4. Verify error handling
5. Test multi-turn (future)

---

## Deployment Strategy

### Phase 1 Deployment (Week 1 - Foundation)

1. **Schema Update**: Deploy new fields to Dataverse (staging)
2. **Data Migration**: Run script to set AgentType=DirectLine for existing agents
3. **Code Deployment**: Deploy Phase 1 code to staging
4. **Smoke Test**: Run evaluation with existing Direct Line agent
5. **Production**: Deploy to production after validation

### Subsequent Phases (Weeks 2-5)

- Deploy one adapter at a time
- Each deployment includes adapter code + unit tests
- Validate in staging before production
- Document configuration examples

---

## Configuration Examples

### DirectLine (Copilot Studio)

```json
{
  "directLineUrl": "https://directline.botframework.com/v3/directline/conversations"
}
```

**Note**: No credentials needed - Direct Line URL includes embedded token.

---

### Teams Bot

```json
{
  "botId": "abc-123-bot-id",
  "tenantId": "xyz-789-tenant",
  "environmentVariables": {
    "appId": "TeamsBot_AppId",
    "appSecret": "TeamsBot_AppSecret"
  }
}
```

**Environment Variables**: 
- Create `TeamsBot_AppId` in Dataverse with bot application ID
- Create `TeamsBot_AppSecret` in Dataverse with bot app secret value or Key Vault reference

---

### M365 Copilot (Managed Identity)

```json
{
  "authMode": "ManagedIdentity",
  "tenantId": "xyz-789",
  "scopes": ["https://graph.microsoft.com/.default"]
}
```

**Note**: Function App's Managed Identity must have Graph API application permissions.

### M365 Copilot (User Credentials)

```json
{
  "authMode": "UserCredentials",
  "tenantId": "xyz-789",
  "clientId": "app-client-id",
  "scopes": ["User.Read", "Chat.Read"],
  "environmentVariables": {
    "username": "TestUser_M365_Username",
    "password": "TestUser_M365_Password"
  }
}
```

**Environment Variables**:
- Create `TestUser_M365_Username` in Dataverse (e.g., `test@contoso.com`)
- Create `TestUser_M365_Password` in Dataverse with user password or Key Vault reference

**Note**: UserCredentials mode provides user-specific context for M365 Copilot, which may return different results based on user permissions and data.

---

### SharePoint Embedded Agent (Managed Identity)

```json
{
  "authMode": "ManagedIdentity",
  "siteUrl": "https://contoso.sharepoint.com/sites/site1",
  "agentId": "agent-guid"
}
```

**Note**: Function App's Managed Identity must have access to SharePoint site.

### SharePoint Embedded Agent (User Credentials)

```json
{
  "authMode": "UserCredentials",
  "tenantId": "xyz-789",
  "clientId": "app-client-id",
  "siteUrl": "https://contoso.sharepoint.com/sites/site1",
  "agentId": "agent-guid",
  "environmentVariables": {
    "username": "TestUser_SharePoint_Username",
    "password": "TestUser_SharePoint_Password"
  }
}
```

**Environment Variables**:
- Create `TestUser_SharePoint_Username` in Dataverse
- Create `TestUser_SharePoint_Password` in Dataverse with user password or Key Vault reference

---

### M365 Agents SDK

```json
{
  "agentId": "agent-guid",
  "endpoint": "https://agents.microsoft.com/api"
}
```

**Note**: Uses Function App's Managed Identity - no explicit credentials needed.

---

### Slack Bot

```json
{
  "channelId": "C1234567890",
  "environmentVariables": {
    "botToken": "Slack_BotToken",
    "appToken": "Slack_AppToken"
  }
}
```

**Environment Variables**: 
- Create `Slack_BotToken` in Dataverse with Slack bot token (`xoxb-...`) or Key Vault reference
- Create `Slack_AppToken` in Dataverse with Slack app token (`xapp-...`) for Socket Mode (optional)

---

## Success Criteria

### Phase 1 (Foundation)
- [ ] IAgentAdapter interface defined
- [ ] DirectLineAdapter implemented and tested
- [ ] AgentAdapterFactory working
- [ ] AgentWorkerFunction uses adapters
- [ ] Existing Direct Line agents still work
- [ ] Unit tests pass (>80% coverage)
- [ ] Deployed to staging and validated

### Phases 2-6 (New Adapters)
- [ ] Each adapter implemented per specification
- [ ] Configuration schema documented
- [ ] Unit tests pass
- [ ] Integration test validates end-to-end
- [ ] Deployed to staging
- [ ] Example configuration documented

---

## Timeline

| Phase | Duration | Deliverable |
|-------|----------|-------------|
| Phase 1: Foundation | 1 week | Strategy Pattern + DirectLine refactor |
| Phase 2: Teams | 1 week | TeamsAdapter complete |
| Phase 3: M365 Copilot | 1 week | M365CopilotAdapter complete |
| Phase 4: SharePoint | 1 week | SharePointAdapter complete |
| Phase 5: M365 Agents SDK | 1 week | M365AgentsSDKAdapter complete |
| Phase 6: Slack | 1 week | SlackAdapter complete |
| **Total** | **6 weeks** | All adapters deployed |

---

## Authentication & Credential Management

### Authentication Modes

The system supports **four authentication modes** to handle different scenarios:

| Mode | Use Case | Credentials Required | Permissions Type |
|------|----------|---------------------|------------------|
| **ManagedIdentity** | Function App runs as itself | None | Application permissions |
| **ClientCredentials** | Service principal with app-only access | clientId + clientSecret | Application permissions |
| **UserCredentials** | Test as specific user with password | username + password | Delegated permissions |
| **RefreshToken** | Test as user without password | refreshToken | Delegated permissions |

### When to Use Each Mode

**ManagedIdentity** (Default):
- ✅ Most secure (no stored credentials)
- ✅ Best for application-level operations
- ✅ Works with Azure services that support Managed Identity
- ❌ Cannot access user-specific data or context

**ClientCredentials**:
- ✅ Works with any Azure AD application
- ✅ Application-level permissions
- ⚠️ Requires storing client secret
- ❌ Cannot access user-specific data or context

**UserCredentials**:
- ✅ Test with specific user context (M365 Copilot, SharePoint)
- ✅ Delegated permissions (user's data only)
- ✅ Useful for user-specific agent behavior testing
- ⚠️ Requires storing username + password
- ⚠️ Requires modern auth to be enabled

**RefreshToken** (Advanced):
- ✅ User context without storing password
- ✅ Long-lived tokens (up to 90 days)
- ✅ More secure than UserCredentials
- ⚠️ Requires initial interactive OAuth flow
- ⚠️ Requires custom MSAL.NET implementation

### Credential Storage: Dataverse Environment Variables

All sensitive credentials are stored as **Dataverse Environment Variables**. Benefits:

✅ **Unified credential management** - All credentials in one place (Dataverse UI)  
✅ **Flexible storage** - Plain text (dev/test) or Azure Key Vault (production)  
✅ **No custom Key Vault code** - Dataverse handles Key Vault retrieval automatically  
✅ **Audit trail** - Dataverse tracks who creates/modifies environment variables  
✅ **Solution-aware** - Environment variables can be packaged in solutions  

### How It Works

1. **User creates environment variables in Dataverse:**
   - Schema Name: `TestUser_M365_Username` (referenced in configuration)
   - Type: Secret (recommended) or Text
   
2. **For production, user configures Key Vault integration:**
   - Set Value Schema to Key Vault secret URL
   - Grant Dataverse managed identity access to Key Vault
   - Dataverse retrieves value from Key Vault automatically

3. **Adapter retrieves credentials:**
   ```csharp
   var credentials = await GetAllCredentialsFromEnvironmentVariablesAsync(
       request.Configuration,
       cancellationToken);
   
   var username = credentials["username"];
   var password = credentials["password"];
   ```

4. **Adapter creates appropriate credential:**
   ```csharp
   var credential = await GetAzureCredentialAsync(
       request.Configuration,
       scopes,
       cancellationToken);
   // Returns: DefaultAzureCredential, ClientSecretCredential, 
   //          UsernamePasswordCredential, etc.
   ```

### Configuration Examples by Auth Mode

**ManagedIdentity:**
```json
{
  "authMode": "ManagedIdentity"
}
```

**ClientCredentials:**
```json
{
  "authMode": "ClientCredentials",
  "tenantId": "xyz-789",
  "environmentVariables": {
    "clientId": "App_ClientId",
    "clientSecret": "App_ClientSecret"
  }
}
```

**UserCredentials:**
```json
{
  "authMode": "UserCredentials",
  "tenantId": "xyz-789",
  "clientId": "app-id",
  "environmentVariables": {
    "username": "TestUser_Username",
    "password": "TestUser_Password"
  }
}
```

### Security Best Practices

1. **Use Key Vault for production**:
   - Store passwords, secrets, tokens in Azure Key Vault
   - Configure environment variables to reference Key Vault
   - Never store plain text credentials in production

2. **Use Managed Identity when possible**:
   - Most secure option (no credentials to manage)
   - Works for application-level operations

3. **UserCredentials only for testing**:
   - Use for user-specific context testing (M365 Copilot)
   - Consider service accounts (not personal accounts)
   - Rotate passwords regularly

4. **Monitor credential usage**:
   - Dataverse tracks environment variable access
   - Review logs for suspicious activity

### Reference

See [Dynamics Plugin Key Vault Integration](https://tldr-dynamics.com/blog/dynamics-plugin-key-vault-integration) for detailed setup instructions.

---

## Open Questions

1. **M365 Copilot API**: What is the official Microsoft Graph API endpoint for M365 Copilot? (As of Dec 2025, may need to confirm latest API)
2. **SharePoint Embedded Agents**: What is the authentication and API pattern for SharePoint embedded agents?
3. **M365 Agents SDK**: What SDK should we use? Is there an official NuGet package?
4. **Rate Limiting**: Do we need per-adapter rate limiting strategies?

---

## Next Steps

1. **Review this plan** with team
2. **Create Dataverse schema changes** (OptionSet + field definitions)
3. **Create feature branch** from `dynamic-agent-calling`
4. **Begin Phase 1 implementation**
5. **Schedule Phase 1 code review**

---

**Approval Needed:**
- [ ] Architecture approved
- [ ] OptionSet values approved
- [ ] Configuration schema approved
- [ ] Timeline approved
- [ ] Ready to start Phase 1 implementation
