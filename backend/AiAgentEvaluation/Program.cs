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
    // Dataverse settings (ClientSecret connection string only - Managed Identity not supported)
    options.Dataverse.ConnectionString = builder.Configuration["DataverseConnectionString"] ?? options.Dataverse.ConnectionString;
    options.Dataverse.SchemaPrefix = builder.Configuration["DataverseSchemaPrefix"] ?? options.Dataverse.SchemaPrefix;
    
    // Azure OpenAI settings
    options.AzureOpenAI.Endpoint = builder.Configuration["AzureOpenAIEndpoint"] ?? options.AzureOpenAI.Endpoint;
    options.AzureOpenAI.ApiKey = builder.Configuration["AzureOpenAIApiKey"] ?? options.AzureOpenAI.ApiKey;
    
    // Service Bus settings (Managed Identity only)
    // Note: Settings use ServiceBus__ prefix which gets mapped to ServiceBus: in configuration
    options.ServiceBus.Namespace = builder.Configuration["ServiceBus:Namespace"] ?? options.ServiceBus.Namespace;
    options.ServiceBus.QueueName = builder.Configuration["ServiceBus:QueueName"] ?? options.ServiceBus.QueueName;
    
    // Key Vault settings
    options.KeyVault.Url = builder.Configuration["KeyVaultUrl"] ?? options.KeyVault.Url;
    
    // Evaluation settings
    if (int.TryParse(builder.Configuration["DefaultEvaluationTimeoutMinutes"], out int timeout))
        options.Evaluation.DefaultTimeoutMinutes = timeout;
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
    // Register factory for configurable judge selection (replaces direct LLMJudgeService registration)
    builder.Services.AddScoped<ILLMJudgeServiceFactory, LLMJudgeServiceFactory>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register LLMJudgeServiceFactory: {ex.Message}");
}

try
{
    builder.Services.AddScoped<IServiceBusService, ServiceBusService>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register ServiceBusService: {ex.Message}");
}

// Register adapter factory and adapters
try
{
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.IAgentAdapterFactory, AiAgentEvaluation.Services.Adapters.AgentAdapterFactory>();
    
    // Register adapters
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.DirectLineAdapter>();
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.TeamsAdapter>();
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.M365CopilotAdapter>();
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.SharePointAdapter>();
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.M365AgentsSDKAdapter>();
    builder.Services.AddScoped<AiAgentEvaluation.Services.Adapters.SlackAdapter>();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to register agent adapters: {ex.Message}");
}

// Add logging
builder.Services.AddLogging();

builder.Build().Run();
