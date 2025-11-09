using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.Extensions.Logging;

namespace AiAgentEvaluation.Extensions;

/// <summary>
/// Extension methods for configuration
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Add Azure Key Vault configuration provider with proper managed identity support
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="keyVaultUrl">Key Vault URL</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>Configuration builder</returns>
    public static IConfigurationBuilder AddAzureKeyVaultWithManagedIdentity(
        this IConfigurationBuilder builder, 
        string keyVaultUrl,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(keyVaultUrl))
        {
            logger?.LogWarning("Key Vault URL not provided - skipping Key Vault configuration");
            return builder;
        }

        try
        {
            // Use DefaultAzureCredential for managed identity authentication
            var credential = new DefaultAzureCredential();
            
            // Add Key Vault configuration provider with the correct API
            builder.AddAzureKeyVault(
                new Uri(keyVaultUrl), 
                credential);
            
            logger?.LogInformation("Successfully configured Key Vault provider with managed identity for {KeyVaultUrl}", keyVaultUrl);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to configure Key Vault provider for {KeyVaultUrl}", keyVaultUrl);
            // Don't throw - allow fallback authentication
        }

        return builder;
    }

    /// <summary>
    /// Validate that all required configuration values are present
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="logger">Logger for validation messages</param>
    /// <returns>True if configuration is valid</returns>
    public static bool ValidateConfiguration(this IConfiguration configuration, ILogger? logger = null)
    {
        var isValid = true;
        var requiredSettings = new Dictionary<string, string[]>
        {
            ["ServiceBus"] = new[] { "Namespace", "QueueName" },
            ["AzureOpenAI"] = new[] { "Endpoint", "DeploymentName" },
            ["Storage"] = new[] { "AccountName", "PromptTemplateContainerName" },
            ["KeyVault"] = new[] { "Url" }
        };

        foreach (var (section, settings) in requiredSettings)
        {
            foreach (var setting in settings)
            {
                var value = configuration[$"{section}:{setting}"];
                if (string.IsNullOrEmpty(value))
                {
                    logger?.LogError("Required configuration setting missing: {Section}:{Setting}", section, setting);
                    isValid = false;
                }
            }
        }

        // Check for authentication configuration
        var hasDataverseFallback = !string.IsNullOrEmpty(configuration["Dataverse:ConnectionString"]) ||
                                  !string.IsNullOrEmpty(configuration["DataverseConnectionString"]);

        var hasStorageFallback = !string.IsNullOrEmpty(configuration["Storage:ConnectionString"]) ||
                                !string.IsNullOrEmpty(configuration["AzureWebJobsStorage"]);

        if (!hasDataverseFallback)
        {
            logger?.LogWarning("Dataverse connection string not found - Dataverse operations may fail");
        }

        logger?.LogInformation("Azure services (OpenAI, Service Bus, Storage) configured for managed identity authentication");
        
        if (hasStorageFallback)
        {
            logger?.LogInformation("Storage connection string found (required for Consumption plan)");
        }

        return isValid;
    }
}