using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using AiAgentEvaluation.Extensions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Create a temporary logger for configuration setup
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("ConfigurationSetup");

// Configuration
builder.Configuration
    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Add Key Vault if configured
var keyVaultUrl = builder.Configuration["KeyVaultUrl"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVaultWithManagedIdentity(keyVaultUrl, logger);
}

// Validate configuration
if (!builder.Configuration.ValidateConfiguration(logger))
{
    logger.LogWarning("Some configuration validation errors were found - proceeding with available configuration");
}

// Bind configuration - use try-catch to prevent startup failures
try
{
    builder.Services.Configure<AppSettings>(builder.Configuration);
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to configure AppSettings: {ex.Message}");
    // Register empty settings as fallback
    builder.Services.Configure<AppSettings>(_ => { });
}

// Map flat environment variables to nested configuration structure - CRITICAL FIX
builder.Services.Configure<AppSettings>(options =>
{
    // Dataverse settings (OAuth connection string only - Managed Identity not supported)
    options.Dataverse.ConnectionString = builder.Configuration["DataverseConnectionString"] ?? options.Dataverse.ConnectionString;
    
    // CopilotStudio settings  
    options.CopilotStudio.TokenEndpoint = builder.Configuration["CopilotStudioTokenEndpoint"] ?? options.CopilotStudio.TokenEndpoint;
    
    // Azure OpenAI settings
    options.AzureOpenAI.Endpoint = builder.Configuration["AzureOpenAIEndpoint"] ?? options.AzureOpenAI.Endpoint;
    options.AzureOpenAI.ApiKey = builder.Configuration["AzureOpenAIApiKey"] ?? options.AzureOpenAI.ApiKey;
    
    // Service Bus settings
    options.ServiceBus.Namespace = builder.Configuration["ServiceBusNamespace"] ?? options.ServiceBus.Namespace;
    options.ServiceBus.ConnectionString = builder.Configuration["ServiceBusConnectionString"] ?? options.ServiceBus.ConnectionString;
    options.ServiceBus.QueueName = builder.Configuration["QueueName"] ?? options.ServiceBus.QueueName;
    
    // Storage settings
    options.Storage.AccountName = builder.Configuration["StorageAccountName"] ?? options.Storage.AccountName;
    options.Storage.ConnectionString = builder.Configuration["StorageAccountConnectionString"] ?? options.Storage.ConnectionString;
    options.Storage.PromptTemplateContainerName = builder.Configuration["PromptTemplateContainerName"] ?? options.Storage.PromptTemplateContainerName;
    
    // Key Vault settings
    options.KeyVault.Url = builder.Configuration["KeyVaultUrl"] ?? options.KeyVault.Url;
    
    // Evaluation settings
    if (int.TryParse(builder.Configuration["DefaultEvaluationTimeoutMinutes"], out int timeout))
        options.Evaluation.DefaultTimeoutMinutes = timeout;
    if (int.TryParse(builder.Configuration["MaxConcurrentTestCases"], out int maxCases))
        options.Evaluation.MaxConcurrentTestCases = maxCases;
});

// Azure Identity
builder.Services.AddSingleton<DefaultAzureCredential>();

// Application Insights
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// HTTP Client
builder.Services.AddHttpClient();

// Add custom services with error handling
try
{
    builder.Services.AddScoped<IDataverseService, DataverseService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register DataverseService: {ex.Message}");
}

try
{
    builder.Services.AddScoped<ICopilotStudioService>(serviceProvider =>
    {
        var logger = serviceProvider.GetRequiredService<ILogger<CopilotStudioService>>();
        var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>();
        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        return new CopilotStudioService(logger, settings, httpClient, httpClientFactory);
    });
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register CopilotStudioService: {ex.Message}");
}

try
{
    builder.Services.AddScoped<ILLMJudgeService, LLMJudgeService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register LLMJudgeService: {ex.Message}");
}

try
{
    builder.Services.AddScoped<IServiceBusService, ServiceBusService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register ServiceBusService: {ex.Message}");
}

try
{
    builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register BlobStorageService: {ex.Message}");
}

// Add logging
builder.Services.AddLogging();

builder.Build().Run();
