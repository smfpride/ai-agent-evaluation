using AiAgentEvaluation.Models;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for AgentToEval model configuration
/// </summary>
public class AgentToEvalConfigurationTests
{
    [Fact]
    public void GetConfiguration_WithValidJsonConfiguration_ParsesCorrectly()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Test Agent",
            AgentType = 778760001,
            AgentConfiguration = "{\"botId\":\"abc-123\",\"tenantId\":\"xyz-789\"}"
        };

        // Act
        var config = agent.GetConfiguration();

        // Assert
        Assert.Equal(778760001, config.AgentType);
        Assert.Equal("abc-123", config.GetValue<string>("botId"));
        Assert.Equal("xyz-789", config.GetValue<string>("tenantId"));
    }

    [Fact]
    public void GetConfiguration_NoAgentTypeSet_DefaultsToDirectLine()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Legacy Agent",
            AgentType = null,
            DirectLineSpeechUrl = "https://directline.example.com"
        };

        // Act
        var config = agent.GetConfiguration();

        // Assert
        Assert.Equal(778760000, config.AgentType); // Default to DirectLine
    }

    [Fact]
    public void GetConfiguration_LegacyDirectLineUrlOnly_MigratesConfiguration()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Legacy Agent",
            AgentType = null,
            DirectLineSpeechUrl = "https://directline.example.com",
            AgentConfiguration = null
        };

        // Act
        var config = agent.GetConfiguration();

        // Assert
        Assert.Equal(778760000, config.AgentType);
        Assert.Equal("https://directline.example.com", config.GetValue<string>("directLineUrl"));
    }

    [Fact]
    public void GetConfiguration_EmptyConfiguration_ThrowsInvalidOperationException()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Test Agent",
            AgentType = 778760000,
            AgentConfiguration = null,
            DirectLineSpeechUrl = string.Empty
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => agent.GetConfiguration());
        Assert.Contains("must have either DirectLineSpeechUrl (legacy) or AgentConfiguration set", exception.Message);
    }

    [Fact]
    public void GetConfiguration_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Test Agent",
            AgentType = 778760001,
            AgentConfiguration = "{ invalid json "
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => agent.GetConfiguration());
        Assert.Contains("Failed to parse agent configuration JSON", exception.Message);
        Assert.Contains("Test Agent", exception.Message);
    }

    [Fact]
    public void GetConfiguration_ConfigurationOverridesLegacyUrl()
    {
        // Arrange - Configuration takes precedence over legacy URL
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "Test Agent",
            AgentType = 778760000,
            DirectLineSpeechUrl = "https://legacy.example.com",
            AgentConfiguration = "{\"directLineUrl\":\"https://new.example.com\"}"
        };

        // Act
        var config = agent.GetConfiguration();

        // Assert
        Assert.Equal("https://new.example.com", config.GetValue<string>("directLineUrl"));
    }

    [Fact]
    public void GetConfiguration_ComplexConfiguration_ParsesAllFields()
    {
        // Arrange
        var agent = new AgentToEval
        {
            Id = Guid.NewGuid(),
            Name = "M365 Copilot Agent",
            AgentType = 778760002,
            AgentConfiguration = "{\"authMode\":\"UserCredentials\",\"tenantId\":\"tenant-123\",\"clientId\":\"client-456\",\"scopes\":[\"User.Read\",\"Chat.Read\"],\"environmentVariables\":{\"username\":\"TestUser_Username\",\"password\":\"TestUser_Password\"}}"
        };

        // Act
        var config = agent.GetConfiguration();

        // Assert
        Assert.Equal(778760002, config.AgentType);
        Assert.Equal("UserCredentials", config.GetValue<string>("authMode"));
        Assert.Equal("tenant-123", config.GetValue<string>("tenantId"));
        Assert.Equal("client-456", config.GetValue<string>("clientId"));
        
        var envVars = config.GetEnvironmentVariableNames();
        Assert.Equal(2, envVars.Count);
        Assert.Equal("TestUser_Username", envVars["username"]);
        Assert.Equal("TestUser_Password", envVars["password"]);
    }
}
