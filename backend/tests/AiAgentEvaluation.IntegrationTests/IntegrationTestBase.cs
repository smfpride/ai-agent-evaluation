using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Identity;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using AiAgentEvaluation.Extensions;

namespace AiAgentEvaluation.IntegrationTests;

/// <summary>
/// Base class for integration tests with proper service setup
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly IConfiguration Configuration;
    protected readonly ILogger Logger;
    private bool _disposed;

    protected IntegrationTestBase()
    {
        // Build configuration
        Configuration = BuildConfiguration();
        
        // Build service provider
        ServiceProvider = BuildServiceProvider();
        
        // Get logger
        Logger = ServiceProvider.GetRequiredService<ILogger<IntegrationTestBase>>();
        
        // Validate configuration
        if (!Configuration.ValidateConfiguration(Logger))
        {
            Logger.LogWarning("Some configuration validation errors found - tests may fail");
        }
    }

    private IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddUserSecrets<IntegrationTestBase>();

        // Add Key Vault if configured
        var keyVaultUrl = builder.Build()["KeyVault:Url"];
        if (!string.IsNullOrEmpty(keyVaultUrl))
        {
            using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
            var logger = loggerFactory.CreateLogger("TestConfiguration");
            
            builder.AddAzureKeyVaultWithManagedIdentity(keyVaultUrl, logger);
        }

        return builder.Build();
    }

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Configuration
        services.AddSingleton(Configuration);
        services.Configure<AppSettings>(Configuration);
        
        // Logging
        services.AddLogging(builder => builder.AddConsole());
        
        // Azure Identity
        services.AddSingleton<DefaultAzureCredential>();
        
        // HTTP Client
        services.AddHttpClient();
        
        // Add services
        services.AddScoped<IDataverseService, DataverseService>();
        services.AddScoped<ICopilotStudioService, CopilotStudioService>();
        services.AddScoped<ILLMJudgeService, LLMJudgeService>();
        services.AddScoped<IServiceBusService, ServiceBusService>();
        services.AddScoped<IBlobStorageService, BlobStorageService>();
        
        return services.BuildServiceProvider();
    }

    protected T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    protected IOptions<AppSettings> GetAppSettings()
    {
        return ServiceProvider.GetRequiredService<IOptions<AppSettings>>();
    }

    public virtual void Dispose()
    {
        if (!_disposed)
        {
            if (ServiceProvider is IDisposable disposableServiceProvider)
            {
                disposableServiceProvider.Dispose();
            }
            _disposed = true;
        }
    }
}