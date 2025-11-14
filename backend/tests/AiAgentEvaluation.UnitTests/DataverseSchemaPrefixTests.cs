using AiAgentEvaluation.Models;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Tests for Dataverse schema prefix configuration functionality
/// </summary>
public class DataverseSchemaPrefixTests
{
    [Fact]
    public void SchemaPrefix_DefaultValue_ShouldBeMcicaps()
    {
        // Arrange
        var settings = new DataverseSettings();

        // Act & Assert
        Assert.Equal("mcicaps", settings.SchemaPrefix);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenPrefixWithoutUnderscore_ShouldAppendUnderscore()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "mcicaps"
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("mcicaps_", result);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenPrefixWithUnderscore_ShouldNotDuplicate()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "mcicaps_"
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("mcicaps_", result);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenCustomPrefix_ShouldAppendUnderscore()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "custom"
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("custom_", result);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenCustomPrefixWithUnderscore_ShouldNotDuplicate()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "custom_"
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("custom_", result);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenEmptyString_ShouldReturnDefaultWithUnderscore()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = ""
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("mcicaps_", result);
    }

    [Fact]
    public void SchemaPrefixWithUnderscore_WhenNull_ShouldReturnDefaultWithUnderscore()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = null!
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("mcicaps_", result);
    }

    [Fact]
    public void SchemaPrefix_WhenSetToNull_ShouldFallbackToDefault()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = null!
        };

        // Act & Assert
        Assert.Equal("mcicaps", settings.SchemaPrefix);
    }

    [Theory]
    [InlineData("prefix1", "prefix1_")]
    [InlineData("prefix1_", "prefix1_")]
    [InlineData("my_custom_prefix", "my_custom_prefix_")]
    [InlineData("my_custom_prefix_", "my_custom_prefix_")]
    [InlineData("a", "a_")]
    [InlineData("a_", "a_")]
    public void SchemaPrefixWithUnderscore_VariousPrefixes_ShouldHandleCorrectly(string input, string expected)
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = input
        };

        // Act
        var result = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AppSettings_DataverseSettings_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var appSettings = new AppSettings();

        // Assert
        Assert.NotNull(appSettings.Dataverse);
        Assert.Equal("mcicaps", appSettings.Dataverse.SchemaPrefix);
        Assert.Equal("mcicaps_", appSettings.Dataverse.SchemaPrefixWithUnderscore);
    }

    [Fact]
    public void DataverseSettings_MultipleAccess_ShouldReturnConsistentResults()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "test"
        };

        // Act
        var result1 = settings.SchemaPrefixWithUnderscore;
        var result2 = settings.SchemaPrefixWithUnderscore;
        var result3 = settings.SchemaPrefixWithUnderscore;

        // Assert
        Assert.Equal("test_", result1);
        Assert.Equal("test_", result2);
        Assert.Equal("test_", result3);
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void DataverseSettings_ChangingPrefix_ShouldReflectImmediately()
    {
        // Arrange
        var settings = new DataverseSettings
        {
            SchemaPrefix = "prefix1"
        };

        // Act & Assert - First value
        Assert.Equal("prefix1_", settings.SchemaPrefixWithUnderscore);

        // Change prefix
        settings.SchemaPrefix = "prefix2";

        // Act & Assert - New value
        Assert.Equal("prefix2_", settings.SchemaPrefixWithUnderscore);

        // Change to prefix with underscore
        settings.SchemaPrefix = "prefix3_";

        // Act & Assert - Should not duplicate underscore
        Assert.Equal("prefix3_", settings.SchemaPrefixWithUnderscore);
    }
}
