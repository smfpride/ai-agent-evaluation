using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for interacting with Dataverse
/// </summary>
public interface IDataverseService
{
    Task<EvaluationRun?> GetEvaluationRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default);
    Task<List<TestCase>> GetTestCasesAsync(Guid evaluationRunId, CancellationToken cancellationToken = default);
    Task UpdateEvaluationRunStatusAsync(Guid evaluationRunId, int statusCode, CancellationToken cancellationToken = default);
    Task SetEvaluationRunTotalTestCasesAsync(Guid evaluationRunId, int totalTestCases, CancellationToken cancellationToken = default);
    Task UpdateEvaluationRunProgressAsync(Guid evaluationRunId, int completedTestCases, int failedTestCases, CancellationToken cancellationToken = default);
    Task UpdateEvaluationRunScoresAsync(Guid evaluationRunId, decimal? overallScore, CancellationToken cancellationToken = default);
    Task<Guid> CreateEvaluationResultAsync(CreateEvaluationResultRequest request, CancellationToken cancellationToken = default);
    Task<TestCase?> GetTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default);
    Task SetEvaluationRunStartTimeAsync(Guid evaluationRunId, DateTime startTime, CancellationToken cancellationToken = default);
    Task SetEvaluationRunEndTimeAsync(Guid evaluationRunId, DateTime endTime, CancellationToken cancellationToken = default);
    Task<List<EvaluationRun>> GetRunningEvaluationRunsAsync(CancellationToken cancellationToken = default);
    Task<List<EvaluationResult>> GetEvaluationResultsAsync(Guid evaluationRunId, CancellationToken cancellationToken = default);
    Task UpdateEvaluationRunCompletionAsync(Guid evaluationRunId, int statusCode, decimal? overallScore, bool? overallPass, DateTime endTime, CancellationToken cancellationToken = default);
    Task IncrementCompletedTestCasesAsync(Guid evaluationRunId, bool failed, CancellationToken cancellationToken = default);
    Task<EvaluatorType?> GetEvaluatorTypeAsync(Guid evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<AgentToEval?> GetAgentToEvalAsync(Guid agentToEvalId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<List<TestCaseTurn>> GetTestCaseTurnsAsync(Guid testCaseId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAggregateEvaluationResultAsync(MultiTurnExecutionContext context, CancellationToken cancellationToken = default);
}