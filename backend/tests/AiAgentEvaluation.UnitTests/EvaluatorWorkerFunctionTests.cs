using AiAgentEvaluation.Functions;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Text.Json;

namespace AiAgentEvaluation.UnitTests;

/// <summary>
/// Unit tests for EvaluatorWorkerFunction (Phase 1 - New Architecture)
/// Tests evaluator job processing, LLM Judge calls, and result updates
/// </summary>
public class EvaluatorWorkerFunctionTests
{
    private readonly Mock<ILogger<EvaluatorWorkerFunction>> _mockLogger;
    private readonly Mock<IDataverseService> _mockDataverseService;
    private readonly Mock<ILLMJudgeService> _mockLLMJudgeService;
    private readonly Mock<ILLMJudgeServiceFactory> _mockLLMJudgeServiceFactory;
    private readonly EvaluatorWorkerFunction _evaluatorWorker;

    public EvaluatorWorkerFunctionTests()
    {
        _mockLogger = new Mock<ILogger<EvaluatorWorkerFunction>>();
        _mockDataverseService = new Mock<IDataverseService>();
        _mockLLMJudgeService = new Mock<ILLMJudgeService>();
        _mockLLMJudgeServiceFactory = new Mock<ILLMJudgeServiceFactory>();

        _mockLLMJudgeServiceFactory
            .Setup(x => x.CreateJudgeServiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockLLMJudgeService.Object);

        _evaluatorWorker = new EvaluatorWorkerFunction(
            _mockLogger.Object,
            _mockDataverseService.Object,
            _mockLLMJudgeServiceFactory.Object);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_SuccessfulEvaluation_UpdatesResultToCompleted()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluatorTypeId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        var pendingResult = new EvaluationResult
        {
            Id = resultId,
            EvaluationRunId = evaluationRunId,
            TestCaseId = testCaseId,
            EvaluatorTypeId = evaluatorTypeId,
            StatusCode = EvaluationResultStatus.Pending,
            ActualResponse = "Product X is an advanced AI solution",
            ConversationId = "conv-123"
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution"
        };

        var evaluatorType = new EvaluatorType
        {
            Id = evaluatorTypeId,
            Name = "Relevancy"
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.95m,
            Reasoning = "Highly relevant response",
            ExecutionTimeMs = 800
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingResult);

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCase);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorType);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.UpdateEvaluationResultAsync(resultId, It.IsAny<UpdateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(evaluationRunId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - Result updated to Completed
        _mockDataverseService.Verify(
            x => x.UpdateEvaluationResultAsync(
                resultId,
                It.Is<UpdateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Completed &&
                    r.Score == judgeResponse.Score &&
                    r.Passed == true &&
                    r.Reasoning == judgeResponse.Reasoning),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Completion counter incremented (not failed)
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(evaluationRunId, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_LowScore_UpdatesWithPassedFalse()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluatorTypeId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        var pendingResult = new EvaluationResult
        {
            Id = resultId,
            EvaluationRunId = evaluationRunId,
            TestCaseId = testCaseId,
            EvaluatorTypeId = evaluatorTypeId,
            StatusCode = EvaluationResultStatus.Pending,
            ActualResponse = "I don't know",
            ConversationId = "conv-123"
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution"
        };

        var evaluatorType = new EvaluatorType
        {
            Id = evaluatorTypeId,
            Name = "Accuracy"
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.15m, // Low score
            Reasoning = "Response does not address the question",
            ExecutionTimeMs = 800
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingResult);

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCase);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorType);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.UpdateEvaluationResultAsync(resultId, It.IsAny<UpdateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(evaluationRunId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - Result updated with Passed = false
        _mockDataverseService.Verify(
            x => x.UpdateEvaluationResultAsync(
                resultId,
                It.Is<UpdateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Completed &&
                    r.Score == judgeResponse.Score &&
                    r.Passed == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_AlreadyCompleted_SkipsProcessing()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        var completedResult = new EvaluationResult
        {
            Id = resultId,
            EvaluationRunId = evaluationRunId,
            StatusCode = EvaluationResultStatus.Completed, // Already completed
            Score = 0.95m,
            Passed = true
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedResult);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - No LLM Judge call made (idempotency)
        _mockLLMJudgeService.Verify(
            x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - No result update made
        _mockDataverseService.Verify(
            x => x.UpdateEvaluationResultAsync(It.IsAny<Guid>(), It.IsAny<UpdateEvaluationResultRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_ResultNotFound_LogsErrorAndReturns()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvaluationResult?)null);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - No further processing
        _mockLLMJudgeService.Verify(
            x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_LLMJudgeFailure_UpdatesResultToErrored()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluatorTypeId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        var pendingResult = new EvaluationResult
        {
            Id = resultId,
            EvaluationRunId = evaluationRunId,
            TestCaseId = testCaseId,
            EvaluatorTypeId = evaluatorTypeId,
            StatusCode = EvaluationResultStatus.Pending,
            ActualResponse = "Product X is an advanced AI solution"
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            UserInput = "What is Product X?",
            ExpectedResponse = "Product X is an AI solution"
        };

        var evaluatorType = new EvaluatorType
        {
            Id = evaluatorTypeId,
            Name = "Relevancy"
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = false,
            ErrorMessage = "Rate limit exceeded"
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingResult);

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCase);

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorType);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.UpdateEvaluationResultAsync(resultId, It.IsAny<UpdateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(evaluationRunId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - Result updated to Errored
        _mockDataverseService.Verify(
            x => x.UpdateEvaluationResultAsync(
                resultId,
                It.Is<UpdateEvaluationResultRequest>(r =>
                    r.StatusCode == EvaluationResultStatus.Errored &&
                    r.ErrorMessage == judgeResponse.ErrorMessage),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert - Completion counter incremented as failed
        _mockDataverseService.Verify(
            x => x.IncrementCompletedTestCasesAsync(evaluationRunId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_MultiTurn_UsesTurnData()
    {
        // Arrange
        var resultId = Guid.NewGuid();
        var evaluationRunId = Guid.NewGuid();
        var testCaseId = Guid.NewGuid();
        var evaluatorTypeId = Guid.NewGuid();
        var turnId = Guid.NewGuid();

        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = resultId,
            EvaluationRunId = evaluationRunId
        };

        var pendingResult = new EvaluationResult
        {
            Id = resultId,
            EvaluationRunId = evaluationRunId,
            TestCaseId = testCaseId,
            EvaluatorTypeId = evaluatorTypeId,
            TestCaseTurnId = turnId, // Multi-turn scenario
            StatusCode = EvaluationResultStatus.Pending,
            ActualResponse = "Turn 2 response"
        };

        var testCase = new TestCase
        {
            Id = testCaseId,
            UserInput = "Turn 1 prompt",
            ExpectedResponse = "Turn 1 expected"
        };

        var turn = new TestCaseTurn
        {
            Id = turnId,
            TestCaseId = testCaseId,
            Sequence = 2,
            PromptText = "Turn 2 prompt",
            ExpectedResponse = "Turn 2 expected"
        };

        var evaluatorType = new EvaluatorType
        {
            Id = evaluatorTypeId,
            Name = "Relevancy"
        };

        var judgeResponse = new LLMJudgeResponse
        {
            Success = true,
            Score = 0.88m,
            Reasoning = "Good response",
            ExecutionTimeMs = 800
        };

        _mockDataverseService
            .Setup(x => x.GetEvaluationResultAsync(resultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingResult);

        _mockDataverseService
            .Setup(x => x.GetTestCaseAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testCase);

        _mockDataverseService
            .Setup(x => x.GetTestCaseTurnsAsync(testCaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestCaseTurn> { turn });

        _mockDataverseService
            .Setup(x => x.GetEvaluatorTypeAsync(evaluatorTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluatorType);

        _mockLLMJudgeService
            .Setup(x => x.EvaluateResponseAsync(
                It.Is<LLMJudgeRequest>(r =>
                    r.UserInput == turn.PromptText &&
                    r.ExpectedResponse == turn.ExpectedResponse),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(judgeResponse);

        _mockDataverseService
            .Setup(x => x.UpdateEvaluationResultAsync(resultId, It.IsAny<UpdateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDataverseService
            .Setup(x => x.IncrementCompletedTestCasesAsync(evaluationRunId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var messageBody = JsonSerializer.Serialize(message);

        // Act
        await _evaluatorWorker.ProcessEvaluatorJob(messageBody, CancellationToken.None);

        // Assert - Turn data used for evaluation
        _mockLLMJudgeService.Verify(
            x => x.EvaluateResponseAsync(
                It.Is<LLMJudgeRequest>(r =>
                    r.UserInput == turn.PromptText &&
                    r.ExpectedResponse == turn.ExpectedResponse),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessEvaluatorJob_InvalidJson_ThrowsException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";

        // Act & Assert - Should throw JsonException
        await Assert.ThrowsAsync<JsonException>(() => 
            _evaluatorWorker.ProcessEvaluatorJob(invalidJson, CancellationToken.None));

        // Verify no services were called
        _mockDataverseService.Verify(
            x => x.GetEvaluationResultAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
