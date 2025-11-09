using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Reflection;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for LLMJudgeService
/// </summary>
public class LLMJudgeServiceTests
{
    private readonly Mock<ILogger<LLMJudgeService>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly IOptions<AppSettings> _settings;

    public LLMJudgeServiceTests()
    {
        _mockLogger = new Mock<ILogger<LLMJudgeService>>();
        _mockDataverseService = new Mock<IDataverseService>();
        
        _settings = Options.Create(new AppSettings
        {
            AzureOpenAI = new AzureOpenAISettings
            {
                Endpoint = "https://test.openai.azure.com/",
                DeploymentName = "gpt-4",
                MaxTokens = 1000,
                Temperature = 0.3
            }
        });
    }

    [Fact]
    public void Constructor_WithValidSettings_InitializesSuccessfully()
    {
        // Act & Assert - Should not throw
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullEndpoint_ThrowsException()
    {
        // Arrange
        var invalidSettings = Options.Create(new AppSettings
        {
            AzureOpenAI = new AzureOpenAISettings
            {
                Endpoint = null!,
                DeploymentName = "gpt-4"
            }
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new LLMJudgeService(_mockLogger.Object, invalidSettings, _mockDataverseService.Object));
    }

    [Fact]
    public async Task GetPromptTemplateAsync_WithEvaluatorType_ReturnsCorrectTemplate()
    {
        // Arrange
        var templateContent = "Test template content";
        var evaluatorTypeId = Guid.NewGuid();
        var evaluatorType = new EvaluatorType
        {
            Id = evaluatorTypeId,
            Name = "Test Evaluator",
            EvaluatorPrompt = templateContent
        };
        
        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorType);

        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);

        // Act
        var result = await service.GetPromptTemplateAsync(evaluatorTypeId);

        // Assert
        Assert.Equal(templateContent, result);
        _mockDataverseService.Verify(
            x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPromptTemplateAsync_WithoutEvaluatorType_ReturnsDefaultTemplate()
    {
        // Arrange
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);

        // Act
        var result = await service.GetPromptTemplateAsync(null);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Evaluate", result);
    }

    [Fact]
    public async Task GetPromptTemplateAsync_WhenDataverseFails_ReturnsDefaultTemplate()
    {
        // Arrange
        var evaluatorTypeId = Guid.NewGuid();
        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dataverse error"));

        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);

        // Act
        var result = await service.GetPromptTemplateAsync(evaluatorTypeId);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("evaluate", result.ToLowerInvariant());
    }

    [Fact]
    public void ParseLLMResponse_WithValidJsonScore_ReturnsCorrectScore()
    {
        // Arrange
        var jsonResponse = @"{
            ""score"": 0.85,
            ""reasoning"": ""The response is highly similar to the expected output.""
        }";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { jsonResponse })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.85m, result.Score);
        Assert.Equal("The response is highly similar to the expected output.", result.Reasoning);
    }

    [Fact]
    public void ParseLLMResponse_WithStringScore_ParsesCorrectly()
    {
        // Arrange
        var jsonResponse = @"{
            ""score"": ""0.7"",
            ""reasoning"": ""Good match with minor differences.""
        }";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { jsonResponse })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.7m, result.Score);
    }

    [Fact]
    public void ParseLLMResponse_WithScoreOutOfRange_NormalizesScore()
    {
        // Arrange
        var jsonResponse = @"{
            ""score"": 1.5,
            ""reasoning"": ""Score too high""
        }";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { jsonResponse })!;

        // Assert - Score should be capped at 1
        Assert.NotNull(result);
        Assert.Equal(1m, result.Score);
    }

    [Fact]
    public void ParseLLMResponse_WithNegativeScore_NormalizesToZero()
    {
        // Arrange
        var jsonResponse = @"{
            ""score"": -3,
            ""reasoning"": ""Negative score""
        }";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { jsonResponse })!;

        // Assert - Score should be normalized to 0
        Assert.NotNull(result);
        Assert.Equal(0m, result.Score);
    }

    [Theory]
    [InlineData("The response score: 0.8", 0.8)]
    [InlineData("Rating: 0.75", 0.75)]
    [InlineData("Score: 0.6", 0.6)]
    [InlineData("This gets 0.9 points", 0.9)]
    public void ParseLLMResponseWithRegex_ExtractsScoreCorrectly(string responseText, decimal expectedScore)
    {
        // Arrange
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponseWithRegex");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { responseText })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedScore, result.Score);
    }

    [Theory]
    [InlineData("This is an excellent response", 0.9)]
    [InlineData("The result is good overall", 0.7)]
    [InlineData("This is an average response", 0.5)]
    [InlineData("The quality is poor", 0.3)]
    [InlineData("This is a terrible response", 0.1)]
    public void ParseLLMResponseWithRegex_InfersScoreFromKeywords(string responseText, decimal expectedScore)
    {
        // Arrange
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponseWithRegex");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { responseText })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedScore, result.Score);
    }

    [Fact]
    public void BuildEvaluationPrompt_ReplacesAllPlaceholders()
    {
        // Arrange
        var template = "Input: {UserInput}, Expected: {ExpectedResponse}, Actual: {ActualResponse}, Conv: {ConversationId}";
        var request = new LLMJudgeRequest
        {
            UserInput = "Test input",
            ExpectedResponse = "Expected output",
            ActualResponse = "Actual output",
            ConversationId = "conv123"
        };

        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var buildMethod = GetPrivateMethod(service, "BuildEvaluationPrompt");

        // Act
        var result = (string)buildMethod.Invoke(service, new object[] { template, request })!;

        // Assert
        Assert.Contains("Test input", result);
        Assert.Contains("Expected output", result);
        Assert.Contains("Actual output", result);
        Assert.Contains("conv123", result);
        Assert.DoesNotContain("{", result);
    }

    [Fact]
    public void BuildEvaluationPrompt_WithNullConversationId_UsesNA()
    {
        // Arrange
        var template = "Conv: {ConversationId}";
        var request = new LLMJudgeRequest
        {
            UserInput = "Test",
            ExpectedResponse = "Expected",
            ActualResponse = "Actual",
            ConversationId = null
        };

        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var buildMethod = GetPrivateMethod(service, "BuildEvaluationPrompt");

        // Act
        var result = (string)buildMethod.Invoke(service, new object[] { template, request })!;

        // Assert
        Assert.Contains("N/A", result);
    }

    [Theory]
    [InlineData(TestType.Similarity, "Similarity")]
    [InlineData(TestType.Accuracy, "Accuracy")]
    [InlineData(TestType.Relevancy, "Relevancy")]
    [InlineData(999, "Unknown")]
    public void TestType_GetName_ReturnsCorrectName(int testType, string expectedName)
    {
        // Act
        var result = TestType.GetName(testType);

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void ParseLLMResponse_WithMalformedJson_FallsBackToRegex()
    {
        // Arrange
        var malformedResponse = "Not valid JSON but score: 0.7";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { malformedResponse })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.7m, result.Score);
    }

    [Fact]
    public void ParseLLMResponse_WithNoScore_ReturnsDefaultScore()
    {
        // Arrange
        var noScoreResponse = "This is a response without any score indicators";
        
        using var service = new LLMJudgeService(_mockLogger.Object, _settings, _mockDataverseService.Object);
        var parseMethod = GetPrivateMethod(service, "ParseLLMResponse");

        // Act
        var result = (LLMJudgeResponse)parseMethod.Invoke(service, new object[] { noScoreResponse })!;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0.5m, result.Score); // Default neutral score
    }

    // Helper method to access private methods via reflection
    private MethodInfo GetPrivateMethod(object obj, string methodName)
    {
        var method = obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return method;
    }
}
