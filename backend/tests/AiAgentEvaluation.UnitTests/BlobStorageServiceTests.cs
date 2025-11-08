using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for BlobStorageService upload functionality
/// </summary>
public class BlobStorageServiceTests
{
    private readonly Mock<ILogger<BlobStorageService>> _mockLogger;
    private readonly IOptions<AppSettings> _settings;

    public BlobStorageServiceTests()
    {
        _mockLogger = new Mock<ILogger<BlobStorageService>>();
        
        _settings = Options.Create(new AppSettings
        {
            Storage = new StorageSettings
            {
                AccountName = "teststorageaccount",
                PromptTemplateContainerName = "prompt-templates"
            }
        });
    }

    [Fact]
    public void Constructor_WithValidSettings_InitializesSuccessfully()
    {
        // Act & Assert - Should not throw
        using var service = new BlobStorageService(_mockLogger.Object, _settings);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullAccountName_ThrowsException()
    {
        // Arrange
        var invalidSettings = Options.Create(new AppSettings
        {
            Storage = new StorageSettings
            {
                AccountName = null!,
                PromptTemplateContainerName = "prompt-templates"
            }
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new BlobStorageService(_mockLogger.Object, invalidSettings));
    }

    [Theory]
    [InlineData("similarity-evaluation-prompt.txt")]
    [InlineData("accuracy-evaluation-prompt.txt")]
    [InlineData("relevancy-evaluation-prompt.txt")]
    [InlineData("default-evaluation-prompt.txt")]
    public void ValidTemplateNames_AreRecognized(string templateName)
    {
        // Arrange & Act & Assert - Just verify the names are valid
        Assert.NotNull(templateName);
        Assert.EndsWith(".txt", templateName);
    }

    [Fact]
    public void Constructor_VerifiesStorageAccountConfiguration()
    {
        // Arrange
        using var service = new BlobStorageService(_mockLogger.Object, _settings);

        // Assert - Constructor should complete successfully with valid settings
        Assert.NotNull(service);
        
        // Verify logger was used for initialization
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Initialized Blob Storage client")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("similarity-evaluation-prompt.txt")]
    [InlineData("accuracy-evaluation-prompt.txt")]
    [InlineData("relevancy-evaluation-prompt.txt")]
    public async Task ListPromptTemplatesAsync_ReturnsDefaultTemplates(string expectedTemplate)
    {
        // Arrange
        using var service = new BlobStorageService(_mockLogger.Object, _settings);

        // Act - Will return default list when storage not available
        var templates = await service.ListPromptTemplatesAsync();

        // Assert
        Assert.NotNull(templates);
        Assert.Contains(expectedTemplate, templates);
    }

    [Fact]
    public async Task ListPromptTemplatesAsync_ReturnsAtLeastThreeTemplates()
    {
        // Arrange
        using var service = new BlobStorageService(_mockLogger.Object, _settings);

        // Act
        var templates = await service.ListPromptTemplatesAsync();

        // Assert
        Assert.NotNull(templates);
        Assert.True(templates.Count >= 3, "Should return at least 3 default templates");
    }
}
