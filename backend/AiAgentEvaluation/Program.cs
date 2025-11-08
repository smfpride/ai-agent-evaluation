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

// Bind configuration
builder.Services.Configure<AppSettings>(builder.Configuration);

// Azure Identity
builder.Services.AddSingleton<DefaultAzureCredential>();

// Application Insights
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// HTTP Client
builder.Services.AddHttpClient();

// Add custom services
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddScoped<ICopilotStudioService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<CopilotStudioService>>();
    var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>();
    var httpClient = serviceProvider.GetRequiredService<HttpClient>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    return new CopilotStudioService(logger, settings, httpClient, httpClientFactory);
});
builder.Services.AddScoped<ILLMJudgeService, LLMJudgeService>();
builder.Services.AddScoped<IServiceBusService, ServiceBusService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

// Add logging
builder.Services.AddLogging();

builder.Build().Run();
