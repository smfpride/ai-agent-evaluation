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
    public StorageSettings Storage { get; set; } = new();
    public KeyVaultSettings KeyVault { get; set; } = new();
    public EvaluationSettings Evaluation { get; set; } = new();
}

public class DataverseSettings
{
    /// <summary>
    /// OAuth connection string for Dataverse authentication
    /// Example: "AuthType=OAuth;Url=https://demo-staging.crm.dynamics.com/;ClientId=2e8f2905-8f14-42a2-9f6d-3080b5e85146;ClientSecret=SEl8Q~~xLt3PfT2F5qOYNlgBgn8zLbICdnl2gdqr"
    /// Supported auth types: OAuth, Certificate, ClientSecret, Office365 (Managed Identity NOT supported)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}

public class CopilotStudioSettings
{
    /// <summary>
    /// Copilot Studio token endpoint URL
    /// Example: "https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview"
    /// </summary>
    public string TokenEndpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// Direct Line API base URL (typically https://directline.botframework.com)
    /// </summary>
    public string DirectLineEndpoint { get; set; } = "https://directline.botframework.com";
    
    /// <summary>
    /// Default bot/agent ID for conversations
    /// </summary>
    public string DefaultBotId { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum retry attempts for failed requests
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
}

public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty; // Legacy - will be removed for managed identity
    public string DeploymentName { get; set; } = "gpt-4o";
    public string ApiVersion { get; set; } = "2024-02-01";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.0;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 3;
}

public class ServiceBusSettings
{
    public string ConnectionString { get; set; } = string.Empty; // Legacy - will be removed
    public string Namespace { get; set; } = string.Empty; // For managed identity: "yournamespace.servicebus.windows.net"
    public string QueueName { get; set; } = "test-case-processing";
    public int MaxConcurrentCalls { get; set; } = 100;
    public int AutoCompleteTimeoutMinutes { get; set; } = 5;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
}

public class StorageSettings
{
    public string ConnectionString { get; set; } = string.Empty; // Legacy - will be removed
    public string AccountName { get; set; } = string.Empty; // For managed identity
    public string PromptTemplateContainerName { get; set; } = "evaluation-prompts";
    public int TimeoutSeconds { get; set; } = 30;
}

public class KeyVaultSettings
{
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class EvaluationSettings
{
    public int DefaultTimeoutMinutes { get; set; } = 5;
    public int MaxConcurrentTestCases { get; set; } = 100;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;
    public bool EnableDetailedLogging { get; set; } = true;
    public double MinScore { get; set; } = 0.0;
    public double MaxScore { get; set; } = 10.0;
}