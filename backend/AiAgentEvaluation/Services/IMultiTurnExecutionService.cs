using AiAgentEvaluation.Models;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Service for executing multi-turn test cases
/// </summary>
public interface IMultiTurnExecutionService
{
    /// <summary>
    /// Executes a multi-turn test case
    /// </summary>
    Task<MultiTurnExecutionContext> ExecuteTestCaseAsync(
        TestCase testCase,
        Guid testRunId,
        string directLineUrl,
        decimal passingThreshold,
        CancellationToken cancellationToken = default);
}
