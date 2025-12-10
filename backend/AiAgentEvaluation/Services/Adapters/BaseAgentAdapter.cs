using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

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
        
        Logger.LogDebug(
            "Retrieving credential from environment variable");
        
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
        
        Logger.LogDebug(
            "Retrieving {Count} credentials from environment variables",
            envVarNames.Count);
        
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
    /// Supports: ManagedIdentity, ClientCredentials, UserCredentials
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
        string[] scopes, // Note: scopes parameter kept for interface consistency but not used in credential construction
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
        
        // UsernamePasswordCredential doesn't take scopes at construction - scopes are provided when requesting tokens
        return new UsernamePasswordCredential(username, password, tenantId, clientId);
    }
}
