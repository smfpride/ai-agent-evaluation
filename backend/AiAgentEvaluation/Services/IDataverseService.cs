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
    Task UpdateEvaluationRunProgressAsync(Guid evaluationRunId, int completedTestCases, int failedTestCases, int notApplicableTestCases, CancellationToken cancellationToken = default);
    Task UpdateEvaluationRunScoresAsync(Guid evaluationRunId, decimal? overallScore, CancellationToken cancellationToken = default);
    Task<Guid> CreateEvaluationResultAsync(CreateEvaluationResultRequest request, CancellationToken cancellationToken = default);
    Task<TestCase?> GetTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default);
    Task SetEvaluationRunStartTimeAsync(Guid evaluationRunId, DateTime startTime, CancellationToken cancellationToken = default);
    Task SetEvaluationRunEndTimeAsync(Guid evaluationRunId, DateTime endTime, CancellationToken cancellationToken = default);
    Task<List<EvaluationRun>> GetRunningEvaluationRunsAsync(CancellationToken cancellationToken = default);
    Task<List<EvaluationResult>> GetEvaluationResultsAsync(Guid evaluationRunId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Efficiently gets evaluation result counts without loading all records into memory.
    /// Uses aggregate queries to count directly in Dataverse.
    /// Returns (completedCount, failedCount, notApplicableCount)
    /// </summary>
    Task<(int completedCount, int failedCount, int notApplicableCount)> GetEvaluationResultCountsAsync(
        Guid evaluationRunId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Efficiently gets count of evaluation results for a specific status code.
    /// Only counts test-case level results (excludes individual turn results).
    /// Uses aggregate query to count directly in Dataverse without loading records into memory.
    /// </summary>
    Task<int> GetEvaluationResultCountAsync(
        Guid evaluationRunId,
        int statusCode,
        CancellationToken cancellationToken = default);
    
    Task UpdateEvaluationRunCompletionAsync(Guid evaluationRunId, int statusCode, decimal? overallScore, bool? overallPass, DateTime endTime, CancellationToken cancellationToken = default);
    Task IncrementCompletedTestCasesAsync(Guid evaluationRunId, bool failed, CancellationToken cancellationToken = default);
    Task<EvaluatorType?> GetEvaluatorTypeAsync(Guid evaluatorTypeId, CancellationToken cancellationToken = default);
    Task<List<EvaluatorType>> GetEvaluatorTypesForRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default);
    Task<JudgeAgent?> GetJudgeAgentAsync(Guid judgeAgentId, CancellationToken cancellationToken = default);
    Task<AgentToEval?> GetAgentToEvalAsync(Guid agentToEvalId, CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<List<TestCaseTurn>> GetTestCaseTurnsAsync(Guid testCaseId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Batch-fetches all turns for test cases in a test set in a single optimized query
    /// Returns a dictionary mapping testCaseId to its list of turns
    /// Used by orchestrator to avoid N+1 query problem when processing multi-turn test cases
    /// Uses a join to testcases table instead of IN operator to handle large test sets
    /// </summary>
    Task<Dictionary<Guid, List<TestCaseTurn>>> GetAllTurnsForTestCasesAsync(
        Guid testSetId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the aggregate result for a test case in an evaluation run
    /// Used to check if orchestration already started (idempotency)
    /// </summary>
    Task<EvaluationResult?> GetAggregateResultForTestCaseAsync(
        Guid evaluationRunId, 
        Guid testCaseId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets ALL aggregate results for a test case in an evaluation run (multi-evaluator support).
    /// Used for idempotency checks to ensure all evaluators have been processed.
    /// </summary>
    Task<List<EvaluationResult>> GetAggregateResultsForTestCaseAsync(
        Guid evaluationRunId, 
        Guid testCaseId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ALL aggregate results for an entire evaluation run (batch query optimization).
    /// Used for idempotency checks when scheduling multiple multi-turn orchestrations.
    /// Returns only aggregate results (IsAggregate=true), not individual turn results.
    /// </summary>
    Task<List<EvaluationResult>> GetAggregateResultsForEvaluationRunAsync(
        Guid evaluationRunId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing aggregate result with final scores and status
    /// Used by CreateAggregateResult activity to update placeholder
    /// </summary>
    Task UpdateAggregateResultAsync(
        Guid resultId,
        decimal score,
        bool passed,
        int statusCode,
        string reasoning,
        int executionTimeMs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single evaluation result by ID with related data (Phase 1 - New Architecture)
    /// Used by EvaluatorWorkerFunction to retrieve pending result for evaluation
    /// </summary>
    Task<EvaluationResult?> GetEvaluationResultAsync(Guid resultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing evaluation result (Phase 1 - New Architecture)
    /// Used by EvaluatorWorkerFunction to update pending results to Completed or Errored
    /// </summary>
    Task UpdateEvaluationResultAsync(Guid resultId, UpdateEvaluationResultRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pending aggregate evaluation results (where IsAggregate = true and StatusCode = Pending)
    /// Used by AggregateEvaluationProcessorFunction to find aggregates ready for completion
    /// </summary>
    Task<List<EvaluationResult>> GetPendingAggregateResultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all turn results for a specific test case, evaluation run, and evaluator type
    /// Used by AggregateEvaluationProcessorFunction to check if all turns are completed
    /// </summary>
    Task<List<EvaluationResult>> GetTurnResultsForTestCaseAsync(
        Guid testCaseId,
        Guid evaluationRunId,
        Guid evaluatorTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if all turn results are completed for a specific test case, evaluation run, and evaluator type.
    /// Used to determine when to trigger aggregate calculation for last turn optimization.
    /// </summary>
    /// <param name="testCaseId">Test case ID</param>
    /// <param name="evaluationRunId">Evaluation run ID</param>
    /// <param name="evaluatorTypeId">Evaluator type ID</param>
    /// <param name="expectedTurnCount">Expected number of turns</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if all turns are completed (not pending), false otherwise</returns>
    Task<bool> CheckIfAllTurnsCompleteForEvaluatorAsync(
        Guid testCaseId,
        Guid evaluationRunId,
        Guid evaluatorTypeId,
        int expectedTurnCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates and updates the aggregate result for a multi-turn test case and evaluator.
    /// Called by EvaluatorWorkerFunction when last turn completes (optimization).
    /// </summary>
    /// <param name="aggregateResultId">ID of the pending aggregate result to update</param>
    /// <param name="testCaseId">Test case ID</param>
    /// <param name="evaluationRunId">Evaluation run ID</param>
    /// <param name="evaluatorTypeId">Evaluator type ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CalculateAndUpdateAggregateAsync(
        Guid aggregateResultId,
        Guid testCaseId,
        Guid evaluationRunId,
        Guid evaluatorTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the pending aggregate result for a test case and evaluator type combination.
    /// Used to find the aggregate result to update when the last turn completes.
    /// </summary>
    Task<EvaluationResult?> GetAggregateResultForTestCaseAndEvaluatorAsync(
        Guid testCaseId,
        Guid evaluationRunId,
        Guid evaluatorTypeId,
        CancellationToken cancellationToken = default);
}