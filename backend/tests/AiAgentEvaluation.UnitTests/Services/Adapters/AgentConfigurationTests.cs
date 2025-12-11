using AiAgentEvaluation.Services.Adapters;
using System.Text.Json;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Services.Adapters;

/// <summary>
/// Unit tests for AgentConfiguration parsing
/// </summary>
public class AgentConfigurationTests
{
    [Fact]
    public void GetValue_StringValue_ReturnsCorrectValue()
    {
        // Arrange
        var config = new AgentConfiguration
        {
            AgentType = 778760000,
            Config = new Dictionary<string, object>
            {
                ["directLineUrl"] = "https://example.com"
            }
        };

        // Act
        var result = config.GetValue<string>("directLineUrl");

        // Assert
        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void GetValue_IntValue_ReturnsCorrectValue()
    {
        // Arrange
        var config = new AgentConfiguration
        {
            AgentType = 778760000,
            Config = new Dictionary<string, object>
            {
                ["timeout"] = 5000
            }
        };

        // Act
        var result = config.GetValue<int>("timeout");

        // Assert
        Assert.Equal(5000, result);
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        // Arrange
        var config = new AgentConfiguration
        {
            AgentType = 778760000,
            Config = new Dictionary<string, object>()
        };

        // Act
        var result = config.GetValue<string>("missingKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_JsonElement_DeserializesCorrectly()
    {
        // Arrange - Simulate JSON deserialization
        var json = "{\"scopes\": [\"User.Read\", \"Chat.Read\"]}";
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        
        var config = new AgentConfiguration
        {
            AgentType = 778760002,
            Config = dict!
        };

        // Act
        var result = config.GetValue<string[]>("scopes");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Contains("User.Read", result);
        Assert.Contains("Chat.Read", result);
    }

    [Fact]
    public void GetEnvironmentVariableNames_ValidConfig_ReturnsCorrectMapping()
    {
        // Arrange - Simulate JSON deserialization
        var json = "{\"environmentVariables\": {\"appId\": \"TeamsBot_AppId\", \"appSecret\": \"TeamsBot_AppSecret\"}}";
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        
        var config = new AgentConfiguration
        {
            AgentType = 778760001,
            Config = dict!
        };

        // Act
        var result = config.GetEnvironmentVariableNames();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("TeamsBot_AppId", result["appId"]);
        Assert.Equal("TeamsBot_AppSecret", result["appSecret"]);
    }

    [Fact]
    public void GetEnvironmentVariableNames_NoEnvironmentVariables_ReturnsEmpty()
    {
        // Arrange
        var config = new AgentConfiguration
        {
            AgentType = 778760000,
            Config = new Dictionary<string, object>
            {
                ["directLineUrl"] = "https://example.com"
            }
        };

        // Act
        var result = config.GetEnvironmentVariableNames();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetEnvironmentVariableName_ValidField_ReturnsEnvVarName()
    {
        // Arrange - Simulate JSON deserialization
        var json = "{\"environmentVariables\": {\"appSecret\": \"TeamsBot_AppSecret\"}}";
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        
        var config = new AgentConfiguration
        {
            AgentType = 778760001,
            Config = dict!
        };

        // Act
        var result = config.GetEnvironmentVariableName("appSecret");

        // Assert
        Assert.Equal("TeamsBot_AppSecret", result);
    }

    [Fact]
    public void GetEnvironmentVariableName_MissingField_ReturnsNull()
    {
        // Arrange - Simulate JSON deserialization
        var json = "{\"environmentVariables\": {\"appId\": \"TeamsBot_AppId\"}}";
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        
        var config = new AgentConfiguration
        {
            AgentType = 778760001,
            Config = dict!
        };

        // Act
        var result = config.GetEnvironmentVariableName("appSecret");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetEnvironmentVariableNames_DictionaryFormat_ParsesCorrectly()
    {
        // Arrange - Direct dictionary (not from JSON)
        var envVars = new Dictionary<string, object>
        {
            ["username"] = "TestUser_Username",
            ["password"] = "TestUser_Password"
        };
        
        var config = new AgentConfiguration
        {
            AgentType = 778760002,
            Config = new Dictionary<string, object>
            {
                ["environmentVariables"] = envVars
            }
        };

        // Act
        var result = config.GetEnvironmentVariableNames();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("TestUser_Username", result["username"]);
        Assert.Equal("TestUser_Password", result["password"]);
    }
}
