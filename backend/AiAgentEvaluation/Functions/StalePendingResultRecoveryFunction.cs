using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Timer-triggered function for recovering stale pending results
/// Identifies results stuck in Pending status and re-queues them for processing
/// Uses ActualResponse field presence to determine which stage failed (MCS vs LLM Judge)
/// Leverages existing idempotency checks in worker functions to prevent duplicate work
/// </summary>
public class StalePendingResultRecoveryFunction
{
    private readonly ILogger<StalePendingResultRecoveryFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IServiceBusService _serviceBusService;
    private readonly AppSettings _settings;

    public StalePendingResultRecoveryFunction(
        ILogger<StalePendingResultRecoveryFunction> logger,
        IDataverseService dataverseService,
        IServiceBusService serviceBusService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _serviceBusService = serviceBusService;
        _settings = settings.Value;
    }

    /// <summary>
    /// Runs every 5 minutes to check for stalled evaluation runs
    /// Detects stalls by monitoring LastProgressUpdate - if no progress for threshold, triggers recovery
    /// </summary>
    [Function("RecoverStalePendingResults")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting stalled evaluation run detection at {Time}", DateTime.UtcNow);

        try
        {
            // Get stall threshold from configuration (default: 60 minutes with no progress)
            var stallThresholdMinutes = _settings.Evaluation?.StalePendingThresholdMinutes ?? 60;
            var stallThreshold = DateTime.UtcNow.AddMinutes(-stallThresholdMinutes);

            _logger.LogInformation(
                "Checking for evaluation runs with no progress since {StallThreshold} ({ThresholdMinutes} minutes ago)",
                stallThreshold, stallThresholdMinutes);

            // Get running evaluation runs
            var runningRuns = await _dataverseService.GetRunningEvaluationRunsAsync(cancellationToken);
            
            if (runningRuns.Count == 0)
            {
                _logger.LogInformation("No running evaluation runs found");
                return;
            }

            _logger.LogInformation("Found {Count} running evaluation runs to check", runningRuns.Count);

            var stalledRuns = new List<EvaluationRun>();
            
            foreach (var run in runningRuns)
            {
                // Check if run has stalled (no progress for threshold period)
                var lastProgress = run.LastProgressUpdate ?? run.StartTime ?? run.CreatedOn;
                
                if (lastProgress < stallThreshold)
                {
                    var totalProcessed = run.CompletedTestCases + run.FailedTestCases + run.NotApplicableTestCases;
                    
                    // Only consider it stalled if there are still pending test cases
                    if (totalProcessed < run.TotalTestCases)
                    {
                        stalledRuns.Add(run);
                        _logger.LogWarning(
                            "Detected stalled run {RunId} ({RunName}): LastProgress={LastProgress}, " +
                            "Processed={Processed}/{Total}, NoProgressFor={NoProgressMinutes} minutes",
                            run.Id, run.Name, lastProgress, totalProcessed, run.TotalTestCases,
                            (DateTime.UtcNow - lastProgress).TotalMinutes);
                    }
                }
            }

            if (stalledRuns.Count == 0)
            {
                _logger.LogInformation("No stalled evaluation runs detected");
                return;
            }

            _logger.LogWarning("Found {Count} stalled evaluation runs requiring recovery", stalledRuns.Count);

            var totalRecoveredCount = 0;
            var totalMcsRecoveryCount = 0;
            var totalEvaluatorRecoveryCount = 0;

            // Process each stalled run
            foreach (var stalledRun in stalledRuns)
            {
                try
                {
                    _logger.LogInformation(
                        "Recovering stalled run {RunId} ({RunName})",
                        stalledRun.Id, stalledRun.Name);

                    var (recovered, mcsRecovery, evaluatorRecovery) = 
                        await RecoverStalledRunAsync(stalledRun, cancellationToken);

                    totalRecoveredCount += recovered;
                    totalMcsRecoveryCount += mcsRecovery;
                    totalEvaluatorRecoveryCount += evaluatorRecovery;

                    _logger.LogInformation(
                        "Recovery completed for run {RunId}: Recovered={Recovered}, MCS={McsRecovery}, Evaluator={EvaluatorRecovery}",
                        stalledRun.Id, recovered, mcsRecovery, evaluatorRecovery);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recovering stalled run {RunId}", stalledRun.Id);
                    // Continue with other runs
                }
            }

            _logger.LogInformation(
                "Stalled run recovery completed: StalledRuns={StalledRuns}, TotalRecovered={TotalRecovered}, " +
                "MCS={McsRecovery}, Evaluator={EvaluatorRecovery}",
                stalledRuns.Count, totalRecoveredCount, totalMcsRecoveryCount, totalEvaluatorRecoveryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during stalled run detection");
            throw;
        }
    }

    /// <summary>
    /// Recovers all stuck pending results for a stalled evaluation run
    /// Returns tuple of (total recovered, MCS recoveries, evaluator recoveries)
    /// </summary>
    private async Task<(int recovered, int mcsRecovery, int evaluatorRecovery)> RecoverStalledRunAsync(
        EvaluationRun stalledRun,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recovering stalled run {RunId} - getting all pending results", stalledRun.Id);

        // Get all results for this run
        var results = await _dataverseService.GetEvaluationResultsAsync(stalledRun.Id, cancellationToken);

        // Filter for pending results (non-aggregate only - aggregates handled by aggregate processor)
        var pendingResults = results
            .Where(r => r.StatusCode == EvaluationResultStatus.Pending && !r.IsAggregate)
            .ToList();

        if (pendingResults.Count == 0)
        {
            _logger.LogInformation("No pending results found for stalled run {RunId}", stalledRun.Id);
            return (0, 0, 0);
        }

        _logger.LogInformation(
            "Found {Count} pending results to recover for run {RunId}",
            pendingResults.Count, stalledRun.Id);

        var recoveredCount = 0;
        var mcsRecoveryCount = 0;
        var evaluatorRecoveryCount = 0;

        foreach (var result in pendingResults)
        {
            try
            {
                // Determine failure point by checking ActualResponse presence
                if (string.IsNullOrEmpty(result.ActualResponse))
                {
                    // No ActualResponse = MCS call failed or never completed
                    await RecoverMcsCallAsync(result, cancellationToken);
                    mcsRecoveryCount++;
                }
                else
                {
                    // Has ActualResponse but no Reasoning = LLM Judge failed or never completed
                    await RecoverEvaluatorCallAsync(result, cancellationToken);
                    evaluatorRecoveryCount++;
                }

                recoveredCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recovering result {ResultId}", result.Id);
                // Continue with other results
            }
        }

        return (recoveredCount, mcsRecoveryCount, evaluatorRecoveryCount);
    }

    /// <summary>
    /// Recovers an MCS call that failed or never completed
    /// Re-queues to agent-call-queue for single-turn or multi-turn-agent-call-queue for multi-turn
    /// </summary>
    private async Task RecoverMcsCallAsync(EvaluationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recovering MCS call for result {ResultId} (TestCase={TestCaseId}, Turn={TurnId})",
            result.Id,
            result.TestCaseId,
            result.TestCaseTurnId);

        // Load related data to reconstruct the message
        var testCase = await _dataverseService.GetTestCaseAsync(result.TestCaseId, cancellationToken);
        if (testCase == null)
        {
            _logger.LogError("Test case {TestCaseId} not found, cannot recover result {ResultId}",
                result.TestCaseId, result.Id);
            return;
        }

        var evaluationRun = await _dataverseService.GetEvaluationRunAsync(result.EvaluationRunId, cancellationToken);
        if (evaluationRun == null)
        {
            _logger.LogError("Evaluation run {RunId} not found, cannot recover result {ResultId}",
                result.EvaluationRunId, result.Id);
            return;
        }

        var agentToEval = await _dataverseService.GetAgentToEvalAsync(evaluationRun.AgentToEvalId!.Value, cancellationToken);
        if (agentToEval == null)
        {
            _logger.LogError("Agent to eval {AgentId} not found, cannot recover result {ResultId}",
                evaluationRun.AgentToEvalId, result.Id);
            return;
        }

        // Get evaluator types for this run
        var evaluatorTypes = await _dataverseService.GetEvaluatorTypesForRunAsync(result.EvaluationRunId, cancellationToken);

        string userInput;
        string expectedResponse;

        if (result.TestCaseTurnId.HasValue)
        {
            // Multi-turn: Load turn data
            var turns = await _dataverseService.GetTestCaseTurnsAsync(result.TestCaseId, cancellationToken);
            var turn = turns.FirstOrDefault(t => t.Id == result.TestCaseTurnId.Value);

            if (turn == null)
            {
                _logger.LogError("Test case turn {TurnId} not found, cannot recover result {ResultId}",
                    result.TestCaseTurnId.Value, result.Id);
                return;
            }

            userInput = turn.PromptText;
            expectedResponse = turn.ExpectedResponse ?? string.Empty;
        }
        else
        {
            // Single-turn: Use test case data
            userInput = testCase.UserInput;
            expectedResponse = testCase.ExpectedResponse ?? string.Empty;
        }

        // Re-queue to agent worker (single or multi-turn)
        // Note: Worker functions check StatusCode before processing (idempotency)
        var message = new AgentCallMessage
        {
            EvaluationRunId = result.EvaluationRunId,
            TestCaseId = result.TestCaseId,
            DirectLineUrl = agentToEval.DirectLineSpeechUrl,
            UserInput = userInput,
            ExpectedResponse = expectedResponse,
            EvaluatorTypeIds = evaluatorTypes.Select(e => e.Id).ToList(),
            ConversationId = result.ConversationId,
            DirectLineToken = null, // Will be refreshed by worker
            PassingThreshold = evaluationRun.PassingThreshold ?? 0.5m,
            TestCaseTurnId = result.TestCaseTurnId,
            RetryCount = 0 // Reset retry count for recovery
        };

        await _serviceBusService.SendAgentCallMessageAsync(message, DateTimeOffset.UtcNow, cancellationToken);

        _logger.LogInformation("Re-queued MCS call for result {ResultId}", result.Id);
    }

    /// <summary>
    /// Recovers an evaluator call that failed or never completed
    /// Re-queues to evaluator-processing-queue
    /// </summary>
    private async Task RecoverEvaluatorCallAsync(EvaluationResult result, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recovering evaluator call for result {ResultId} (TestCase={TestCaseId}, Evaluator={EvaluatorId})",
            result.Id,
            result.TestCaseId,
            result.EvaluatorTypeId);

        // Re-queue to evaluator worker
        // Note: Worker function checks StatusCode before processing (idempotency)
        var message = new EvaluatorJobMessage
        {
            EvaluationResultId = result.Id,
            EvaluationRunId = result.EvaluationRunId,
            RetryCount = 0 // Reset retry count for recovery
        };

        await _serviceBusService.SendEvaluatorJobMessageAsync(message, DateTimeOffset.UtcNow, cancellationToken);

        _logger.LogInformation("Re-queued evaluator call for result {ResultId}", result.Id);
    }
}
