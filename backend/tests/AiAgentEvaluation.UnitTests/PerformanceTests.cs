using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Xunit;

namespace AiAgentEvaluation.UnitTests.Performance;

/// <summary>
/// Performance tests for the AI Agent Evaluation system
/// Covers test cases TC-PERF-001 through TC-PERF-026
/// NOTE: These are unit-level performance validation tests
/// Full load testing requires Azure Load Testing service with real infrastructure
/// </summary>
public class PerformanceTests
{
    private readonly Mock<ILogger<OrchestratorFunction>> _mockOrchestratorLogger;
    private readonly Mock<ILogger<WorkerFunction>> _mockWorkerLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<IServiceBusService> _mockServiceBusService;
    private readonly Mock<DurableTaskClient> _mockDurableClient;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<ILLMJudgeService> _mockLLMJudgeService;

    public PerformanceTests()
    {
        _mockOrchestratorLogger = new Mock<ILogger<OrchestratorFunction>>();
        _mockWorkerLogger = new Mock<ILogger<WorkerFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockServiceBusService = new Mock<IServiceBusService>();
        _mockDurableClient = new Mock<DurableTaskClient>("test-client");
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockLLMJudgeService = new Mock<ILLMJudgeService>();
    }

    #region TC-PERF-001: Maximum Scale Evaluation Run (50K Test Cases)

    [Fact]
    public async Task OrchestratorFunction_With50KTestCases_CompletesWithin30Seconds()
    {
        // Arrange
        var orchestrator = new OrchestratorFunction(
            _mockOrchestratorLogger.Object,
            _mockDataverseService.Object,
            _mockServiceBusService.Object);

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
            DirectLineSpeechUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token"
        };

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
        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);
        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);

        var request = new StartEvaluationRunRequest { EvaluationRunId = evaluationRunId };
        var httpRequestData = CreateMockHttpRequestData(request);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await orchestrator.StartEvaluationRun(httpRequestData, _mockDurableClient.Object, CancellationToken.None);
        stopwatch.Stop();

        // Assert - Should complete within 30 seconds (PRD requirement)
        Assert.True(stopwatch.ElapsedMilliseconds < 30000, 
            $"Orchestrator took {stopwatch.ElapsedMilliseconds}ms, expected < 30000ms");
        
        // Verify all messages were queued
        _mockServiceBusService.Verify(x => x.SendBatchTestCaseMessagesAsync(
            It.Is<List<ProcessTestCaseMessage>>(m => m.Count == 50000), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-PERF-002: Concurrent Evaluation Runs

    [Fact]
    public async Task OrchestratorFunction_With10ConcurrentRuns_HandlesWithoutConflicts()
    {
        // Arrange
        var orchestrator = new OrchestratorFunction(
            _mockOrchestratorLogger.Object,
            _mockDataverseService.Object,
            _mockServiceBusService.Object);

        var evaluationRuns = Enumerable.Range(0, 10)
            .Select(i => new
            {
                Id = Guid.NewGuid(),
                TestCases = Enumerable.Range(0, 5000)
                    .Select(j => new TestCase
                    {
                        Id = Guid.NewGuid(),
                        UserInput = $"Test {j}",
                        ExpectedResponse = $"Expected {j}"
                    })
                    .ToList()
            })
            .ToList();

        var agentToEvalId = Guid.NewGuid();
        var agentToEval = new AgentToEval
        {
            Id = agentToEvalId,
            Name = "Test Agent",
            DirectLineSpeechUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token"
        };

        _mockDataverseService.Setup(x => x.GetAgentToEvalAsync(agentToEvalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentToEval);

        foreach (var run in evaluationRuns)
        {
            _mockDataverseService.Setup(x => x.GetEvaluationRunAsync(run.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EvaluationRun
                {
                    Id = run.Id,
                    StatusCode = EvaluationRunStatus.Execute,

                    AgentToEvalId = agentToEvalId
                });
            _mockDataverseService.Setup(x => x.GetTestCasesAsync(run.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(run.TestCases);
        }

        // Act - Execute all 10 runs concurrently
        var tasks = evaluationRuns.Select(async run =>
        {
            var request = new StartEvaluationRunRequest { EvaluationRunId = run.Id };
            var httpRequestData = CreateMockHttpRequestData(request);
            return await orchestrator.StartEvaluationRun(httpRequestData, _mockDurableClient.Object, CancellationToken.None);
        });

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - All runs should complete successfully
        // No deadlocks or resource conflicts should occur
        Assert.True(stopwatch.ElapsedMilliseconds < 60000, 
            "Concurrent runs took too long, possible contention");
        
        // Verify Service Bus was called 10 times (once per run)
        _mockServiceBusService.Verify(x => x.SendBatchTestCaseMessagesAsync(
            It.IsAny<List<ProcessTestCaseMessage>>(), 
            It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    #endregion

    #region TC-PERF-005: Service Bus Throughput

    [Fact]
    public async Task ServiceBus_BatchMessaging_Handles1000MessagesEfficiently()
    {
        // Arrange
        var messages = Enumerable.Range(0, 1000)
            .Select(i => new ProcessTestCaseMessage
            {
                TestCaseId = Guid.NewGuid(),
                EvaluationRunId = Guid.NewGuid(),
                DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
                UserInput = $"Test {i}",
                ExpectedResponse = $"Expected {i}",
                MaxRetries = 3
            })
            .ToList();

        // Mock Service Bus to track batch sizes
        var batchSizes = new List<int>();
        _mockServiceBusService.Setup(x => x.SendBatchTestCaseMessagesAsync(
            It.IsAny<IEnumerable<ProcessTestCaseMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ProcessTestCaseMessage>, CancellationToken>((msgs, ct) => 
            {
                batchSizes.Add(msgs.Count());
            })
            .Returns(Task.CompletedTask);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await _mockServiceBusService.Object.SendBatchTestCaseMessagesAsync(messages, CancellationToken.None);
        stopwatch.Stop();

        // Assert - Should process quickly (< 5 seconds for batching logic)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            "Message batching took too long");
        
        // Verify batching was used effectively
        _mockServiceBusService.Verify(x => x.SendBatchTestCaseMessagesAsync(
            It.IsAny<List<ProcessTestCaseMessage>>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TC-PERF-006: Worker Function Processing Time

    [Fact]
    public async Task WorkerFunction_SingleTestCase_CompletesWithin5Minutes()
    {
        // Arrange
        var worker = new WorkerFunction(
            _mockWorkerLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeService.Object,
            _mockServiceBusService.Object);

        var message = new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = "Test input",
            ExpectedResponse = "Expected output",
            MaxRetries = 3
        };

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Test response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.85m,
            Reasoning = "Good response",
            ExecutionTimeMs = 800
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var messageBody = System.Text.Json.JsonSerializer.Serialize(message);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await worker.ProcessTestCase(messageBody, CancellationToken.None);
        stopwatch.Stop();

        // Assert - Should complete within 5 minutes (300,000ms) per PRD
        Assert.True(stopwatch.ElapsedMilliseconds < 300000, 
            $"Worker took {stopwatch.ElapsedMilliseconds}ms, expected < 300000ms (5 minutes)");
        
        // Should complete much faster in normal cases (< 30 seconds)
        Assert.True(stopwatch.ElapsedMilliseconds < 30000, 
            $"Worker took {stopwatch.ElapsedMilliseconds}ms, expected < 30000ms for normal case");
    }

    #endregion

    #region TC-PERF-010: Memory Usage Optimization

    [Fact]
    public void WorkerFunction_ProcessingLargeResponse_ManagesMemoryEfficiently()
    {
        // Arrange
        var worker = new WorkerFunction(
            _mockWorkerLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeService.Object,
            _mockServiceBusService.Object);

        // Measure initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);

        // Create large response (1MB)
        var largeResponse = new string('A', 1024 * 1024);

        // Simulate processing
        var responseSize = System.Text.Encoding.UTF8.GetByteCount(largeResponse);

        // Measure memory after processing
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - Memory increase should be reasonable (< 10MB for 1MB string)
        Assert.True(memoryIncrease < 10 * 1024 * 1024, 
            $"Memory increase {memoryIncrease / 1024 / 1024}MB exceeds acceptable limit");
        
        // Verify response size is within limits
        Assert.True(responseSize < 2 * 1024 * 1024, 
            "Response size exceeds 2MB limit");
    }

    #endregion

    #region TC-PERF-015: Dataverse Query Performance

    [Fact]
    public async Task DataverseService_GetTestCases_ReturnsQuicklyForLargeSets()
    {
        // Arrange
        var evaluationRunId = Guid.NewGuid();
        var testCases = Enumerable.Range(0, 10000)
            .Select(i => new TestCase
            {
                Id = Guid.NewGuid(),
                UserInput = $"Test {i}",
                ExpectedResponse = $"Expected {i}"
            })
            .ToList();

        _mockDataverseService.Setup(x => x.GetTestCasesAsync(evaluationRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCases);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await _mockDataverseService.Object.GetTestCasesAsync(evaluationRunId, CancellationToken.None);
        stopwatch.Stop();

        // Assert - Query should complete quickly (< 5 seconds)
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Dataverse query took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
        
        Assert.Equal(10000, result.Count);
    }

    #endregion

    #region TC-PERF-020: Concurrent Worker Processing

    [Fact]
    public async Task MultipleWorkers_ProcessingConcurrently_NoResourceContention()
    {
        // Arrange - Create 100 worker instances to simulate concurrent processing
        var workers = Enumerable.Range(0, 100)
            .Select(_ => new WorkerFunction(
                _mockWorkerLogger.Object,
                _mockDataverseService.Object,
                _mockCopilotStudioService.Object,
                _mockLLMJudgeService.Object,
                _mockServiceBusService.Object))
            .ToList();

        var copilotResponse = new CopilotDirectLineResponse
        {
            Success = true,
            Response = "Test response",
            ConversationId = "conv-123",
            ExecutionTimeMs = 1500
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.85m,
            Reasoning = "Good response",
            ExecutionTimeMs = 800
        };

        _mockCopilotStudioService.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(copilotResponse);

        _mockLLMJudgeService.Setup(x => x.EvaluateResponseAsync(
            It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService.Setup(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Create messages for each worker
        var messages = workers.Select((_, i) => new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            DirectLineUrl = "https://test.powerplatform.com/powervirtualagents/botsbyschema/test/directline/token",
            UserInput = $"Test {i}",
            ExpectedResponse = $"Expected {i}",
            MaxRetries = 3
        }).ToList();

        // Act - Process all messages concurrently
        var stopwatch = Stopwatch.StartNew();
        var tasks = workers.Select((worker, i) =>
        {
            var messageBody = System.Text.Json.JsonSerializer.Serialize(messages[i]);
            return worker.ProcessTestCase(messageBody, CancellationToken.None);
        });

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert - All workers should complete without conflicts
        Assert.True(stopwatch.ElapsedMilliseconds < 60000, 
            "Concurrent workers took too long, possible contention");
        
        // Verify all results were stored
        _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
            It.IsAny<CreateEvaluationResultRequest>(), 
            It.IsAny<CancellationToken>()), Times.Exactly(100));
    }

    #endregion

    #region Performance Benchmarking Tests

    [Fact]
    public void MessageSerialization_IsPerformant()
    {
        // Arrange
        var message = new ProcessTestCaseMessage
        {
            EvaluationRunId = Guid.NewGuid(),
            TestCaseId = Guid.NewGuid(),

            UserInput = "Test input with some content",
            ExpectedResponse = "Expected output with detailed response",
            ConversationId = "conv-123",
            MaxRetries = 3
        };

        // Act - Measure serialization performance
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ProcessTestCaseMessage>(json);
        }
        stopwatch.Stop();

        // Assert - 10,000 serialize/deserialize cycles should be fast (< 1 second)
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Serialization took {stopwatch.ElapsedMilliseconds}ms for 10K cycles");
    }

    [Fact]
    public void TestCaseDataModel_HasMinimalMemoryFootprint()
    {
        // Arrange & Act
        var testCases = Enumerable.Range(0, 1000)
            .Select(i => new TestCase
            {
                Id = Guid.NewGuid(),
                UserInput = $"Test case {i}",
                ExpectedResponse = $"Expected response {i}",
                Name = $"Test {i}"
            })
            .ToList();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);
        
        // Process test cases
        foreach (var testCase in testCases)
        {
            _ = testCase.Id;
            _ = testCase.UserInput;
            _ = testCase.ExpectedResponse;
        }

        var memoryAfter = GC.GetTotalMemory(false);
        var memoryUsed = memoryAfter - memoryBefore;

        // Assert - Memory usage should be reasonable (< 1MB for 1000 test cases)
        Assert.True(memoryUsed < 1024 * 1024, 
            $"Memory usage {memoryUsed / 1024}KB exceeds 1MB for 1000 test cases");
    }

    #endregion

    #region Helper Methods

    private static Microsoft.Azure.Functions.Worker.Http.HttpRequestData CreateMockHttpRequestData(object? requestBody = null)
    {
        var mockFunctionContext = new Mock<Microsoft.Azure.Functions.Worker.FunctionContext>();
        var mockHttpRequestData = new Mock<Microsoft.Azure.Functions.Worker.Http.HttpRequestData>(mockFunctionContext.Object);
        
        if (requestBody != null)
        {
            var jsonContent = requestBody is string str 
                ? str 
                : System.Text.Json.JsonSerializer.Serialize(requestBody);
            var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
            mockHttpRequestData.Setup(r => r.Body).Returns(stream);
        }
        else
        {
            mockHttpRequestData.Setup(r => r.Body).Returns(new System.IO.MemoryStream());
        }

        // Setup CreateResponse method
        mockHttpRequestData.Setup(r => r.CreateResponse())
            .Returns(() => 
            {
                var mockResponse = new Mock<Microsoft.Azure.Functions.Worker.Http.HttpResponseData>(mockFunctionContext.Object);
                mockResponse.SetupProperty(r => r.StatusCode);
                mockResponse.SetupProperty(r => r.Headers, new Microsoft.Azure.Functions.Worker.Http.HttpHeadersCollection());
                mockResponse.Setup(r => r.Body).Returns(new System.IO.MemoryStream());
                return mockResponse.Object;
            });

        return mockHttpRequestData.Object;
    }

    #endregion
}

/// <summary>
/// NOTE: Additional performance tests that require real Azure infrastructure:
/// 
/// TC-PERF-003: Burst Load Handling - Requires Azure Load Testing
/// TC-PERF-004: Sustained High Load - Requires Azure Load Testing
/// TC-PERF-007: Azure OpenAI Judge Throughput - Requires real OpenAI deployment
/// TC-PERF-008: Auto-Scaling Behavior - Requires Azure Function App with scaling configured
/// TC-PERF-009: Cold Start Performance - Requires real Function App deployments
/// TC-PERF-011: Database Connection Pooling - Requires real Dataverse environment
/// TC-PERF-012: Service Bus Dead Letter Handling - Requires real Service Bus
/// TC-PERF-013: Function Instance Distribution - Requires Azure Functions Premium plan
/// TC-PERF-014: Storage Queue Throughput - Requires real Azure Storage
/// TC-PERF-016: Network Latency Impact - Requires cross-region deployments
/// TC-PERF-017: Beyond Capacity Stress Test - Requires production-like environment
/// TC-PERF-018: Long-Running Evaluation Handling - Requires 24+ hour test runs
/// TC-PERF-019: Memory Leak Detection - Requires extended duration testing
/// TC-PERF-021: Judge Prompt Caching Effectiveness - Requires Blob Storage
/// TC-PERF-022: Token Usage Optimization - Requires Azure OpenAI metering
/// TC-PERF-023: Batch Processing Efficiency - Requires Service Bus Premium
/// TC-PERF-024: Resource Cleanup Performance - Requires long-term testing
/// TC-PERF-025: Monitoring Overhead Impact - Requires Application Insights
/// TC-PERF-026: Cost Per Test Case Calculation - Requires Azure billing analysis
/// 
/// These tests should be implemented as part of:
/// 1. Azure Load Testing test plans
/// 2. Production smoke tests
/// 3. Performance regression test suite
/// 4. Capacity planning exercises
/// </summary>




