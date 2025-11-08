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
    public string ConnectionString { get; set; } = string.Empty; // Legacy - will be removed
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty; // For managed identity
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}

public class CopilotStudioSettings
{
    public string DirectLineSecret { get; set; } = string.Empty;
    public string DirectLineEndpoint { get; set; } = "https://directline.botframework.com";
    public string DefaultBotId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
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