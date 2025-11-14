namespace AiAgentEvaluation.Models;

/// <summary>
/// Application configuration settings
/// </summary>
public class AppSettings
{
    public DataverseSettings Dataverse { get; set; } = new();
    public CopilotStudioSettings CopilotStudio { get; set; } = new();
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();
    public ServiceBusSettings ServiceBus { get; set; } = new();
    public KeyVaultSettings KeyVault { get; set; } = new();
    public EvaluationSettings Evaluation { get; set; } = new();
}

public class DataverseSettings
{
    /// <summary>
    /// OAuth connection string for Dataverse authentication
    /// Example: "AuthType=OAuth;Url=https://demo-staging.crm.dynamics.com/;ClientId={your-client-id};ClientSecret={your-client-secret}"
    /// Supported auth types: OAuth, Certificate, ClientSecret, Office365 (Managed Identity NOT supported)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    
    private string _schemaPrefix = "mcicaps";
    
    /// <summary>
    /// Schema prefix for Dataverse tables and fields (default: "mcicaps")
    /// The underscore will be automatically appended if not present.
    /// Example: Setting "mcicaps" will result in "mcicaps_" being used for all table and field names.
    /// </summary>
    public string SchemaPrefix 
    { 
        get => _schemaPrefix;
        set => _schemaPrefix = value ?? "mcicaps";
    }
    
    /// <summary>
    /// Gets the schema prefix with underscore appended if not already present
    /// </summary>
    public string SchemaPrefixWithUnderscore
    {
        get
        {
            if (string.IsNullOrEmpty(SchemaPrefix))
            {
                return "mcicaps_";
            }
            return SchemaPrefix.EndsWith("_") ? SchemaPrefix : SchemaPrefix + "_";
        }
    }
    
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}

public class CopilotStudioSettings
{
    /// <summary>
    /// Default bot/agent ID for conversations
    /// </summary>
    public string DefaultBotId { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP timeout in seconds for API calls
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
    
    /// <summary>
    /// Maximum retry attempts for failed requests
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// Maximum time to wait for bot response in seconds (polling timeout)
    /// </summary>
    public int BotResponseTimeoutSeconds { get; set; } = 60;
}

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
    
    public string ApiVersion { get; set; } = "2024-02-01";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.0;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 5; // Increased from 3 to 5 for better rate limit handling
}

public class ServiceBusSettings
{
    public string ConnectionString { get; set; } = string.Empty; // Legacy - will be removed
    public string Namespace { get; set; } = string.Empty; // For managed identity: "yournamespace.servicebus.windows.net"
    public string QueueName { get; set; } = "test-case-processing";
    public int AutoCompleteTimeoutMinutes { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    
    /// <summary>
    /// Message lock duration for multi-turn test cases in minutes
    /// Multi-turn conversations can take longer (10 turns × ~60s = 10 minutes)
    /// </summary>
    public int MultiTurnAutoCompleteTimeoutMinutes { get; set; } = 15;
}

public class KeyVaultSettings
{
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class EvaluationSettings
{
    public int DefaultTimeoutMinutes { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public bool EnableDetailedLogging { get; set; } = true;
    public double MinScore { get; set; } = 0.0;
    public double MaxScore { get; set; } = 10.0;
}