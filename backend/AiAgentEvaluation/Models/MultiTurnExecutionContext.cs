namespace AiAgentEvaluation.Models;

/// <summary>
/// Context for tracking multi-turn test case execution
/// </summary>
public class MultiTurnExecutionContext
{
    /// <summary>
    /// The test case being executed
    /// </summary>
    public TestCase TestCase { get; set; } = null!;

    /// <summary>
    /// Direct Line conversation ID shared across all turns
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Ordered list of turns to execute
    /// </summary>
    public List<TestCaseTurn> Turns { get; set; } = new();

    /// <summary>
    /// Results for each turn (indexed by sequence number)
    /// </summary>
    public Dictionary<int, EvaluationResult> TurnResults { get; set; } = new();

    /// <summary>
    /// Test run ID this execution belongs to
    /// </summary>
    public Guid TestRunId { get; set; }

    /// <summary>
    /// Total number of turns
    /// </summary>
    public int TotalTurns => Turns.Count;

    /// <summary>
    /// Number of turns completed
    /// </summary>
    public int CompletedTurns => TurnResults.Count;

    /// <summary>
    /// Number of passed turns
    /// </summary>
    public int PassedTurns => TurnResults.Values.Count(r => r.Passed == true);

    /// <summary>
    /// Number of failed turns
    /// </summary>
    public int FailedTurns => TurnResults.Values.Count(r => r.Passed == false);

    /// <summary>
    /// Overall pass rate for the test case
    /// </summary>
    public double PassRate => TotalTurns > 0 ? (double)PassedTurns / TotalTurns : 0.0;
}
