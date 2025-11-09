using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Services;
using Xunit;

namespace AiAgentEvaluation.IntegrationTests;

/// <summary>
/// Integration tests for Azure service authentication and basic connectivity
/// </summary>
public class ServiceAuthenticationTests : IntegrationTestBase
{
    [Fact]
    public async Task ServiceBusService_ShouldInitializeWithManagedIdentity()
    {
        // Arrange
        var service = GetService<IServiceBusService>();
        
        // Act & Assert
        Assert.NotNull(service);
        Logger.LogInformation("ServiceBusService initialized successfully");
        
        // Test basic connectivity (this will fail gracefully if not properly configured)
        try
        {
            await service.SendTestCaseMessageAsync(new Models.ProcessTestCaseMessage 
            { 
                TestCaseId = Guid.NewGuid(),
                EvaluationRunId = Guid.NewGuid(),
                UserInput = "Test",
                ExpectedResponse = "Test"
            }, CancellationToken.None);
            Logger.LogInformation("ServiceBusService connectivity test completed");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("ServiceBusService connectivity test failed (expected if not configured): {Message}", ex.Message);
        }
    }
    
    [Fact]
    public void LLMJudgeService_ShouldInitializeWithManagedIdentity()
    {
        // Arrange & Act
        var service = GetService<ILLMJudgeService>();
        
        // Assert
        Assert.NotNull(service);
        Logger.LogInformation("LLMJudgeService initialized successfully");
        
        // Note: We can't test Azure OpenAI connectivity without making actual API calls
        // which would require quota and might incur costs, so we just verify initialization
    }
    
    [Fact]
    public void DataverseService_ShouldInitializeWithManagedIdentity()
    {
        // Arrange & Act
        var service = GetService<IDataverseService>();
        
        // Assert
        Assert.NotNull(service);
        Logger.LogInformation("DataverseService initialized successfully");
        
        // Note: We can't test Dataverse connectivity without actual environment
        // but we can verify the service initializes properly
    }
    
    [Fact]
    public void CopilotStudioService_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var service = GetService<ICopilotStudioService>();
        
        // Assert
        Assert.NotNull(service);
        Logger.LogInformation("CopilotStudioService initialized successfully");
        
        // Note: We can't test Copilot Studio connectivity without actual bot configuration
        // but we can verify the service initializes properly
    }
    
    [Fact]
    public void Configuration_ShouldLoadCorrectly()
    {
        // Arrange & Act
        var settings = GetAppSettings().Value;
        
        // Assert
        Assert.NotNull(settings);
        Assert.NotNull(settings.Dataverse);
        Assert.NotNull(settings.ServiceBus);
        Assert.NotNull(settings.AzureOpenAI);
        Assert.NotNull(settings.KeyVault);
        Assert.NotNull(settings.Evaluation);
        
        Logger.LogInformation("Configuration loaded successfully");
        Logger.LogInformation("Dataverse ConnectionString: {DataverseUrl}", string.IsNullOrEmpty(settings.Dataverse.ConnectionString) ? "Not configured" : "Configured");
        Logger.LogInformation("Service Bus Namespace: {ServiceBusNamespace}", string.IsNullOrEmpty(settings.ServiceBus.Namespace) ? "Not configured" : "Configured");
        Logger.LogInformation("Azure OpenAI Endpoint: {OpenAIEndpoint}", string.IsNullOrEmpty(settings.AzureOpenAI.Endpoint) ? "Not configured" : "Configured");
        Logger.LogInformation("Key Vault URL: {KeyVaultUrl}", string.IsNullOrEmpty(settings.KeyVault.Url) ? "Not configured" : "Configured");
    }
}