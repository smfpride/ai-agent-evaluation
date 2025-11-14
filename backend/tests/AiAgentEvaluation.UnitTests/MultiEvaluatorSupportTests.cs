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
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for Multi-Evaluator Support (Story 09)
/// Tests CalculateTotalEvaluationUnits, NA handling, and multi-evaluator orchestration
/// </summary>
public class MultiEvaluatorSupportTests
{
    private readonly Mock<ILogger<OrchestratorFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly Mock<DurableTaskClient> _mockDurableClient;
    private readonly OrchestratorFunction _orchestratorFunction;

    public MultiEvaluatorSupportTests()
    {
        _mockLogger = new Mock<ILogger<OrchestratorFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockServiceBusService = new Mock<IServiceBusService>();
        _mockDurableClient = new Mock<DurableTaskClient>("test-client");

        _orchestratorFunction = new OrchestratorFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockServiceBusService.Object);
    }

    #region Helper Methods

    private static MethodInfo GetCalculateTotalEvaluationUnitsMethod()
    {
        var method = typeof(OrchestratorFunction).GetMethod(
            "CalculateTotalEvaluationUnits",
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        return method;
    }

    private static int CallCalculateTotalEvaluationUnits(List<TestCase> testCases, int evaluatorCount)
    {
        var method = GetCalculateTotalEvaluationUnitsMethod();
        var result = method.Invoke(null, new object[] { testCases, evaluatorCount });
        return (int)result!;
    }

    #endregion

    #region CalculateTotalEvaluationUnits Tests

    [Fact]
    public void CalculateTotalEvaluationUnits_SingleTurnOnly_ReturnsTestCountTimesEvaluators()
    {
        // Arrange: 5 single-turn tests × 3 evaluators = 15 units
        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false }
        };

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 3);

        // Assert
        Assert.Equal(15, result);
    }

    [Fact]
    public void CalculateTotalEvaluationUnits_MultiTurnOnly_IncludesAggregates()
    {
        // Arrange: 2 multi-turn tests (5 turns each) × 3 evaluators
        // = 2 × [(5 × 3) + 3] = 2 × 18 = 36 units
        var testCases = new List<TestCase>
        {
            new() 
            { 
                Id = Guid.NewGuid(), 
                IsMultiTurn = true,
                Turns = new List<TestCaseTurn>
                {
                    new() { Id = Guid.NewGuid(), Sequence = 1 },
                    new() { Id = Guid.NewGuid(), Sequence = 2 },
                    new() { Id = Guid.NewGuid(), Sequence = 3 },
                    new() { Id = Guid.NewGuid(), Sequence = 4 },
                    new() { Id = Guid.NewGuid(), Sequence = 5 }
                }
            },
            new() 
            { 
                Id = Guid.NewGuid(), 
                IsMultiTurn = true,
                Turns = new List<TestCaseTurn>
                {
                    new() { Id = Guid.NewGuid(), Sequence = 1 },
                    new() { Id = Guid.NewGuid(), Sequence = 2 },
                    new() { Id = Guid.NewGuid(), Sequence = 3 },
                    new() { Id = Guid.NewGuid(), Sequence = 4 },
                    new() { Id = Guid.NewGuid(), Sequence = 5 }
                }
            }
        };

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 3);

        // Assert
        Assert.Equal(36, result);
    }

    [Fact]
    public void CalculateTotalEvaluationUnits_MixedTestTypes_CalculatesCorrectly()
    {
        // Arrange: 3 single-turn + 1 multi-turn (4 turns) × 2 evaluators
        // = (3 × 2) + [(4 × 2) + 2] = 6 + 10 = 16 units
        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() 
            { 
                Id = Guid.NewGuid(), 
                IsMultiTurn = true,
                Turns = new List<TestCaseTurn>
                {
                    new() { Id = Guid.NewGuid(), Sequence = 1 },
                    new() { Id = Guid.NewGuid(), Sequence = 2 },
                    new() { Id = Guid.NewGuid(), Sequence = 3 },
                    new() { Id = Guid.NewGuid(), Sequence = 4 }
                }
            }
        };

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 2);

        // Assert
        Assert.Equal(16, result);
    }

    [Fact]
    public void CalculateTotalEvaluationUnits_WithSingleEvaluator_MatchesTestCaseCount()
    {
        // Arrange: With 1 evaluator, should match traditional count for single-turn
        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false },
            new() { Id = Guid.NewGuid(), IsMultiTurn = false }
        };

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 1);

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public void CalculateTotalEvaluationUnits_EmptyTestCases_ReturnsZero()
    {
        // Arrange
        var testCases = new List<TestCase>();

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 5);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CalculateTotalEvaluationUnits_MultiTurnWithNoTurns_CountsOnlyAggregates()
    {
        // Arrange: Multi-turn test with null/empty turns should count just the aggregate
        var testCases = new List<TestCase>
        {
            new() { Id = Guid.NewGuid(), IsMultiTurn = true, Turns = null },
            new() { Id = Guid.NewGuid(), IsMultiTurn = true, Turns = new List<TestCaseTurn>() }
        };

        // Act
        var result = CallCalculateTotalEvaluationUnits(testCases, evaluatorCount: 2);

        // Assert: 2 tests × 2 evaluators = 4 aggregates (0 turns + 2 aggregates per test)
        Assert.Equal(4, result);
    }

    #endregion

    #region NA Short-Circuit Tests

    [Fact]
    public async Task StartEvaluationRun_NAScenario_CreatesResultAndSkipsExecution()
    {
        // Arrange: Accuracy evaluator (requires expected) + test without expected
        var evaluationRunId = Guid.NewGuid();
        var agentToEvalId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluatorTypeId = Guid.NewGuid();

        var evaluatorTypes = new List<EvaluatorType> 
        { 
            new() 
            { 
                Id = evaluatorTypeId, 
                Name = "Accuracy", 
                RequiresExpectedResponse = true 
            }
        };

        var testCases = new List<TestCase>
        {
            new() 
            { 
                Id = testCaseId, 
                UserInput = "Test prompt", 
                ExpectedResponse = string.Empty, // Empty expected response
                IsMultiTurn = false
            }
        };

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
            DirectLineSpeechUrl = "https://test.com/directline"
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorTypes);
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);
        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var request = CreateMockHttpRequest(new StartEvaluationRunRequest { EvaluationRunId = evaluationRunId });

        // Act
        var response = await _orchestratorFunction.StartEvaluationRun(request.Object, _mockDurableClient.Object, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify NA result was created
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => 
                r.StatusCode == EvaluationResultStatus.NotApplicable && 
                r.Score == 0.0m &&
                r.EvaluatorTypeId == evaluatorTypeId &&
                r.TestCaseId == testCaseId &&
                r.Reasoning!.Contains("requires expected response")),
            It.IsAny<CancellationToken>()), 
            Times.Once);

        // Verify progress was incremented
        _mockDataverseService.Verify(x => x.IncrementCompletedTestCasesAsync(
            evaluationRunId, 
            false, 
            It.IsAny<CancellationToken>()), 
            Times.Once);

        // Should NOT queue message to Service Bus
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.IsAny<ProcessTestCaseMessage>(), 
            It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task StartEvaluationRun_MultipleEvaluators_SomeNA_OnlyNACreatesResults()
    {
        // Arrange: 3 evaluators (1 requires expected, 2 don't) + test without expected
        var evaluationRunId = Guid.NewGuid();
        var agentToEvalId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var accuracyEvaluatorId = Guid.NewGuid();
        var similarityEvaluatorId = Guid.NewGuid();
        var relevancyEvaluatorId = Guid.NewGuid();

        var evaluatorTypes = new List<EvaluatorType> 
        { 
            new() { Id = accuracyEvaluatorId, Name = "Accuracy", RequiresExpectedResponse = true },
            new() { Id = similarityEvaluatorId, Name = "Similarity", RequiresExpectedResponse = false },
            new() { Id = relevancyEvaluatorId, Name = "Relevancy", RequiresExpectedResponse = false }
        };

        var testCases = new List<TestCase>
        {
            new() 
            { 
                Id = testCaseId, 
                UserInput = "Test prompt", 
                ExpectedResponse = "", // Empty expected response
                IsMultiTurn = false
            }
        };

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
            DirectLineSpeechUrl = "https://test.com/directline"
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorTypes);
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);
        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var request = CreateMockHttpRequest(new StartEvaluationRunRequest { EvaluationRunId = evaluationRunId });

        // Act
        var response = await _orchestratorFunction.StartEvaluationRun(request.Object, _mockDurableClient.Object, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify NA result was created once (for Accuracy only)
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.Is<CreateEvaluationResultRequest>(r => 
                r.StatusCode == EvaluationResultStatus.NotApplicable &&
                r.EvaluatorTypeId == accuracyEvaluatorId),
            It.IsAny<CancellationToken>()), 
            Times.Once);

        // Verify 1 message queued with 2 evaluator IDs (for Similarity and Relevancy)
        _mockServiceBusService.Verify(x => x.SendTestCaseMessageAsync(
            It.Is<ProcessTestCaseMessage>(m => 
                m.EvaluatorTypeIds.Contains(similarityEvaluatorId) && 
                m.EvaluatorTypeIds.Contains(relevancyEvaluatorId) &&
                m.EvaluatorTypeIds.Count == 2), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task StartEvaluationRun_NoEvaluatorTypes_ReturnsBadRequest()
    {
        // Arrange: Evaluation run with no evaluator types configured
        var evaluationRunId = Guid.NewGuid();
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
            DirectLineSpeechUrl = "https://test.com/directline"
        };

        _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationRun);
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);
        _mockDataverseService.Setup(x => x.GetEvaluatorTypesForRunAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EvaluatorType>()); // Empty list
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCase> { new() { Id = Guid.NewGuid() } });

        var request = CreateMockHttpRequest(new StartEvaluationRunRequest { EvaluationRunId = evaluationRunId });

        // Act
        var response = await _orchestratorFunction.StartEvaluationRun(request.Object, _mockDurableClient.Object, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Verify status was set to Errored
        _mockDataverseService.Verify(x => x.UpdateEvaluationRunStatusAsync(
            evaluationRunId, 
            EvaluationRunStatus.Errored, 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private Mock<HttpRequestData> CreateMockHttpRequest(StartEvaluationRunRequest request)
    {
        var mockRequest = new Mock<HttpRequestData>(Mock.Of<FunctionContext>());
        var json = JsonSerializer.Serialize(request);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        
        mockRequest.Setup(r => r.Body).Returns(stream);
        mockRequest.Setup(r => r.Url).Returns(new Uri("https://test.com/api/evaluations/start"));
        mockRequest.Setup(r => r.CreateResponse()).Returns(() =>
        {
            var mockResponse = new Mock<HttpResponseData>(Mock.Of<FunctionContext>());
            mockResponse.SetupProperty(r => r.StatusCode);
            mockResponse.SetupProperty(r => r.Headers, new HttpHeadersCollection());
            mockResponse.Setup(r => r.Body).Returns(new MemoryStream());
            return mockResponse.Object;
        });
        
        return mockRequest;
    }

    #endregion
}
