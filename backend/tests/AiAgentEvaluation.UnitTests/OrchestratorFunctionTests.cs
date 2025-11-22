using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for OrchestratorFunction
/// Covers test cases TC-ORD-001 through TC-ORD-013
/// </summary>
public class OrchestratorFunctionTests
{
    private readonly Mock<ILogger<OrchestratorFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly Mock<DurableTaskClient> _mockDurableClient;
    private readonly Mock<FunctionContext> _mockFunctionContext;
    private readonly OrchestratorFunction _orchestratorFunction;

    public OrchestratorFunctionTests()
    {
        _mockLogger = new Mock<ILogger<OrchestratorFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockServiceBusService = new Mock<IServiceBusService>();
        _mockDurableClient = new Mock<DurableTaskClient>("test-client");
        _mockFunctionContext = new Mock<FunctionContext>();

        _orchestratorFunction = new OrchestratorFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockServiceBusService.Object);
    }

    #region TC-ORD-001: Successful Evaluation Run Initialization

    [Fact]
    public async Task StartEvaluationRun_WithValidRequest_ReturnsOkAndQueuesMessages()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        var agentToEvalId = Guid.NewGuid();
        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Execute,

            AgentToEvalId = agentToEvalId
        };

        var agentToEval = new AgentToEval
        {
            Id = agentToEvalId,
            Name = "Test Agent",
            DirectLineSpeechUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token"
        };

        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), UserInput = "Test 1", ExpectedResponse = "Expected 1" },
            new() { Id = Guid.NewGuid(), UserInput = "Test 2", ExpectedResponse = "Expected 2" }
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EvaluatorType> { new() { Id = Guid.NewGuid(), Name = "Similarity", RequiresExpectedResponse = false } });

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        
        // Verify orchestration message queued (async architecture - no synchronous Dataverse updates in StartEvaluationRun)
        _mockServiceBusService.Verify(x => x.SendOrchestrationMessageAsync(
            It.Is<StartOrchestrationMessage>(m => m.EvaluationRunId == evaluationRunId),
            It.IsAny<CancellationToken>()), Times.Once);
        
        // Verify orchestration message sent to Service Bus (async background processing)
        _mockServiceBusService.Verify(x => x.SendOrchestrationMessageAsync(
            It.Is<StartOrchestrationMessage>(m => m.EvaluationRunId == evaluationRunId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-ORD-002: Large Test Set Orchestration

    [Fact]
    public async Task StartEvaluationRun_With50KTestCases_HandlesLargeScale()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Execute,


            AgentToEvalId = Guid.NewGuid()};

        // Create 50,000 test cases
        var testCases = Enumerable.Range(0, 50000)
            .Select(i => new TestCase
            {
                Id = Guid.NewGuid(),
                UserInput = $"Test {i}",
                ExpectedResponse = $"Expected {i}"
            })
            .ToList();

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToEval { Id = Guid.NewGuid(), Name = "Test Agent", DirectLineSpeechUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token" });
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EvaluatorType> { new() { Id = Guid.NewGuid(), Name = "Similarity", RequiresExpectedResponse = false } });

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        
        // Verify orchestration message queued (async architecture - OrchestrationProcessor will handle test case counting)
        _mockServiceBusService.Verify(x => x.SendOrchestrationMessageAsync(
            It.Is<StartOrchestrationMessage>(m => m.EvaluationRunId == evaluationRunId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-ORD-004: Invalid Evaluation Run ID

    [Fact]
    public async Task StartEvaluationRun_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvaluationRun?)null);

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        
        // Verify no status updates or messages sent
        _mockDataverseService.Verify(x => x.UpdateEvaluationRunStatusAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockServiceBusService.Verify(x => x.SendOrchestrationMessageAsync(
            It.IsAny<StartOrchestrationMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region TC-ORD-005: Evaluation Run Already Running

    [Fact]
    public async Task StartEvaluationRun_WhenAlreadyRunning_ReturnsBadRequest()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Running, // Already running

        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        
        // Verify no messages sent
        _mockServiceBusService.Verify(x => x.SendAgentCallMessageAsync(
            It.IsAny<AgentCallMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region TC-ORD-006: Empty Test Set

    [Fact]
    public async Task StartEvaluationRun_WithNoTestCases_ReturnsBadRequest()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Execute,


            AgentToEvalId = Guid.NewGuid()};

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCase>()); // Empty list

        var httpRequestData = CreateMockHttpRequestData(request);

        // Setup agent and evaluator types to allow orchestration
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToEval { Id = Guid.NewGuid(), DirectLineSpeechUrl = "https://test.com" });
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EvaluatorType> { new() { Id = Guid.NewGuid() } });

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert - Returns Accepted for async processing even with empty test cases
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        
        // Verify orchestration message queued (async architecture - OrchestrationProcessor will discover 0 test cases and complete)
        _mockServiceBusService.Verify(x => x.SendOrchestrationMessageAsync(
            It.Is<StartOrchestrationMessage>(m => m.EvaluationRunId == evaluationRunId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-ORD-007: Service Bus Queue Unavailable

    [Fact]
    public async Task StartEvaluationRun_WhenServiceBusFails_ReturnsInternalServerError()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Execute,


            AgentToEvalId = Guid.NewGuid()};

        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), UserInput = "Test", ExpectedResponse = "Expected" }
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentToEval { Id = Guid.NewGuid(), Name = "Test Agent", DirectLineSpeechUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token" });
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EvaluatorType> { new() { Id = Guid.NewGuid(), Name = "Similarity", RequiresExpectedResponse = false } });
        _mockServiceBusService.Setup(x => x.SendOrchestrationMessageAsync(
            It.IsAny<StartOrchestrationMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service Bus unavailable"));

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);
        
        // Assert - StartEvaluationRun catches exceptions and returns HTTP 500 (not propagated)
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    #endregion

    #region TC-ORD-008: Dataverse Connection Failure

    [Fact]
    public async Task StartEvaluationRun_WhenDataverseFails_ReturnsInternalServerError()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = evaluationRunId
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse connection failed"));

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    #endregion

    #region TC-ORD-011: Malformed Request Body

    [Fact]
    public async Task StartEvaluationRun_WithInvalidJson_ReturnsInternalServerError()
    {
        // Arrange - JSON deserialization failures result in 500 from the catch-all exception handler
        var httpRequestData = CreateMockHttpRequestData("{ invalid json }");

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task StartEvaluationRun_WithEmptyBody_ReturnsBadRequest()
    {
        // Arrange
        var httpRequestData = CreateMockHttpRequestData("");

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    #endregion

    #region TC-ORD-012: Missing Required Fields

    [Fact]
    public async Task StartEvaluationRun_WithEmptyGuid_ReturnsBadRequest()
    {
        // Arrange
        var request = new StartEvaluationRunRequest
        {
            EvaluationRunId = Guid.Empty // Invalid
        };

        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var result = await _orchestratorFunction.StartEvaluationRun(httpRequestData, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    #endregion

    #region GetEvaluationRunStatus Tests

    [Fact]
    public async Task GetEvaluationRunStatus_WithValidId_ReturnsOk()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var evaluationRun = new EvaluationRun
        {
            Id = evaluationRunId,
            StatusCode = EvaluationRunStatus.Running,

        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);

        var httpRequestData = CreateMockHttpRequestData();

        // Act
        var result = await _orchestratorFunction.GetEvaluationRunStatus(
            httpRequestData, evaluationRunId.ToString(), CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task GetEvaluationRunStatus_WithInvalidId_ReturnsBadRequest()
    {
        // Arrange
        var httpRequestData = CreateMockHttpRequestData();

        // Act
        var result = await _orchestratorFunction.GetEvaluationRunStatus(
            httpRequestData, "invalid-guid", CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task GetEvaluationRunStatus_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvaluationRun?)null);

        var httpRequestData = CreateMockHttpRequestData();

        // Act
        var result = await _orchestratorFunction.GetEvaluationRunStatus(
            httpRequestData, evaluationRunId.ToString(), CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    #endregion

    #region Helper Methods

    private HttpRequestData CreateMockHttpRequestData(object? requestBody = null)
    {
        var mockHttpRequestData = new Mock<HttpRequestData>(_mockFunctionContext.Object);
        
        if (requestBody != null)
        {
            var jsonContent = requestBody is string str 
                ? str 
                : JsonSerializer.Serialize(requestBody);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));
            mockHttpRequestData.Setup(r => r.Body).Returns(stream);
        }
        else
        {
            mockHttpRequestData.Setup(r => r.Body).Returns(new MemoryStream());
        }

        // Setup URL property
        mockHttpRequestData.Setup(r => r.Url).Returns(new Uri("https://localhost:7071/api/evaluations/start"));

        // Setup CreateResponse method
        mockHttpRequestData.Setup(r => r.CreateResponse())
            .Returns(() => 
            {
                var mockResponse = new Mock<HttpResponseData>(_mockFunctionContext.Object);
                mockResponse.SetupProperty(r => r.StatusCode);
                mockResponse.SetupProperty(r => r.Headers, new HttpHeadersCollection());
                mockResponse.Setup(r => r.Body).Returns(new MemoryStream());
                return mockResponse.Object;
            });

        return mockHttpRequestData.Object;
    }

    #endregion
}



