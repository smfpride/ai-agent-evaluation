using System.Text.Json;

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
    public string? ConversationId { get; init; }
    public string? DirectLineToken { get; init; }
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
    public string? ConversationId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawActivityJson { get; init; }
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
            
            // Handle JsonElement (from System.Text.Json deserialization)
            if (value is JsonElement jsonElement)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                }
                catch
                {
                    // Fall through to Convert.ChangeType
                }
            }
            
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
