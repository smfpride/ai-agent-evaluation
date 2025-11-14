using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for Multi-Turn Aggregate NA handling (Story 09)
/// Tests CreateAggregateResult with various NA scenarios
/// </summary>
public class MultiTurnAggregateNATests
{
    private readonly Mock<ILogger<MultiTurnDurableActivities>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ICopilotStudioService> _mockCopilotStudioService;
    private readonly Mock<ILLMJudgeServiceFactory> _mockLLMJudgeServiceFactory;
    private readonly MultiTurnDurableActivities _activities;

    public MultiTurnAggregateNATests()
    {
        _mockLogger = new Mock<ILogger<MultiTurnDurableActivities>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockCopilotStudioService = new Mock<ICopilotStudioService>();
        _mockLLMJudgeServiceFactory = new Mock<ILLMJudgeServiceFactory>();

        _activities = new MultiTurnDurableActivities(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockCopilotStudioService.Object,
            _mockLLMJudgeServiceFactory.Object);
    }

    [Fact]
    public async Task CreateAggregateResult_AllTurnsNA_AggregateAlsoNA()
    {
        // Arrange: 3 turns, all NA
        var aggregateResultId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestCase = new TestCase { Id = testCaseId },
            TestRunId = testRunId,
            AggregateResultId = aggregateResultId,
            PassingThreshold = 0.7m,
            Turns = new List<TestCaseTurn>
            {
                new() { Id = Guid.NewGuid(), Sequence = 1 },
                new() { Id = Guid.NewGuid(), Sequence = 2 },
                new() { Id = Guid.NewGuid(), Sequence = 3 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } },
                { 2, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } },
                { 3, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert
        _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
            aggregateResultId,
            0.0m, // score
            false, // passed
            EvaluationResultStatus.NotApplicable, // statuscode
            It.Is<string>(s => s.Contains("All turns Not Applicable")),
            0, // executionTimeMs
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify progress incremented as completed (not failed)
        _mockDataverseService.Verify(x => x.IncrementCompletedTestCasesAsync(
            testRunId,
            false, // not failed
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_MixedNAAndScored_ExcludesNAFromAverage()
    {
        // Arrange: 5 turns, 2 NA, 3 scored (0.8, 0.9, 1.0)
        var aggregateResultId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestCase = new TestCase { Id = testCaseId },
            TestRunId = testRunId,
            AggregateResultId = aggregateResultId,
            PassingThreshold = 0.7m,
            Turns = new List<TestCaseTurn>
            {
                new() { Id = Guid.NewGuid(), Sequence = 1 },
                new() { Id = Guid.NewGuid(), Sequence = 2 },
                new() { Id = Guid.NewGuid(), Sequence = 3 },
                new() { Id = Guid.NewGuid(), Sequence = 4 },
                new() { Id = Guid.NewGuid(), Sequence = 5 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } },
                { 2, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.8m, Passed = true, ExecutionTimeMs = 100 } },
                { 3, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.9m, Passed = true, ExecutionTimeMs = 150 } },
                { 4, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } },
                { 5, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 1.0m, Passed = true, ExecutionTimeMs = 200 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert: (0.8 + 0.9 + 1.0) / 3 = 0.9
        _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
            aggregateResultId,
            0.9m, // average of 3 scored turns
            true, // passed (>= 0.7 threshold, 3/3 valid turns passed)
            EvaluationResultStatus.Completed,
            It.Is<string>(s => s.Contains("3/3") && s.Contains("excluded as NA")),
            450, // 100 + 150 + 200 (only valid results)
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_MixedNAAndFailed_CalculatesPassRateFromValidOnly()
    {
        // Arrange: 4 turns, 1 NA, 2 passed, 1 failed
        // Pass rate should be 2/3 (66.7%) excluding NA
        var aggregateResultId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestCase = new TestCase { Id = testCaseId },
            TestRunId = testRunId,
            AggregateResultId = aggregateResultId,
            PassingThreshold = 0.6m, // 60% threshold
            Turns = new List<TestCaseTurn>
            {
                new() { Id = Guid.NewGuid(), Sequence = 1 },
                new() { Id = Guid.NewGuid(), Sequence = 2 },
                new() { Id = Guid.NewGuid(), Sequence = 3 },
                new() { Id = Guid.NewGuid(), Sequence = 4 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { StatusCode = EvaluationResultStatus.NotApplicable, Score = 0.0m } },
                { 2, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.8m, Passed = true, ExecutionTimeMs = 100 } },
                { 3, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.5m, Passed = false, ExecutionTimeMs = 120 } },
                { 4, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.9m, Passed = true, ExecutionTimeMs = 110 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert: Average = (0.8 + 0.5 + 0.9) / 3 = 0.73333...
        // Pass rate = 2/3 = 66.7% > 60% threshold -> should pass
        _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
            aggregateResultId,
            It.Is<decimal>(d => Math.Abs((double)(d - 0.733333m)) < 0.00001), // average of 3 valid turns
            true, // passed (66.7% >= 60% threshold)
            EvaluationResultStatus.Completed,
            It.Is<string>(s => s.Contains("2/3")),
            330, // 100 + 120 + 110
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_NoNAResults_UsesAllTurns()
    {
        // Arrange: 3 turns, all valid (no NA)
        var aggregateResultId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestCase = new TestCase { Id = testCaseId },
            TestRunId = testRunId,
            AggregateResultId = aggregateResultId,
            PassingThreshold = 0.7m,
            Turns = new List<TestCaseTurn>
            {
                new() { Id = Guid.NewGuid(), Sequence = 1 },
                new() { Id = Guid.NewGuid(), Sequence = 2 },
                new() { Id = Guid.NewGuid(), Sequence = 3 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.85m, Passed = true, ExecutionTimeMs = 100 } },
                { 2, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.90m, Passed = true, ExecutionTimeMs = 150 } },
                { 3, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.86m, Passed = true, ExecutionTimeMs = 120 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert: (0.85 + 0.90 + 0.86) / 3 = 0.87
        _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
            aggregateResultId,
            0.87m, // average of all 3 turns
            true, // passed (100% pass rate)
            EvaluationResultStatus.Completed,
            It.Is<string>(s => s.Contains("3/3") && !s.Contains("excluded")),
            370, // 100 + 150 + 120
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAggregateResult_ErroredResults_NotTreatedAsNA()
    {
        // Arrange: Errored results should be included in calculations (not filtered like NA)
        var aggregateResultId = Guid.NewGuid();
        var testRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();

        var context = new MultiTurnExecutionContext
        {
            TestCase = new TestCase { Id = testCaseId },
            TestRunId = testRunId,
            AggregateResultId = aggregateResultId,
            PassingThreshold = 0.7m,
            Turns = new List<TestCaseTurn>
            {
                new() { Id = Guid.NewGuid(), Sequence = 1 },
                new() { Id = Guid.NewGuid(), Sequence = 2 }
            },
            TurnResults = new Dictionary<int, EvaluationResult>
            {
                { 1, new EvaluationResult { StatusCode = EvaluationResultStatus.Completed, Score = 0.9m, Passed = true, ExecutionTimeMs = 100 } },
                { 2, new EvaluationResult { StatusCode = EvaluationResultStatus.Errored, Score = 0.0m, Passed = false, ExecutionTimeMs = 50 } }
            }
        };

        var input = new CreateAggregateResultInput
        {
            Context = context,
            TestRunId = testRunId
        };

        // Act
        await _activities.CreateAggregateResult(input, CancellationToken.None);

        // Assert: Both results included, average = (0.9 + 0.0) / 2 = 0.45
        _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
            aggregateResultId,
            0.45m, // average includes errored result
            false, // failed (1/2 = 50% < 70% threshold)
            EvaluationResultStatus.Completed,
            It.Is<string>(s => s.Contains("1/2")),
            150, // both execution times included
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
