using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Timer Monitor Function for tracking evaluation run completion and calculating metrics
/// </summary>
public class MonitorFunction
{
    private readonly ILogger<MonitorFunction> _logger;
    private readonly IDataverseService _dataverseService;
    private readonly IServiceBusService _serviceBusService;
    private readonly AppSettings _settings;
    private const decimal DefaultPassingThreshold = 0.7m; // 70% default threshold

    public MonitorFunction(
        ILogger<MonitorFunction> logger,
        IDataverseService dataverseService,
        IServiceBusService serviceBusService,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _serviceBusService = serviceBusService;
        _settings = settings.Value;
    }

    [Function("MonitorEvaluationRuns")]
    public async Task MonitorEvaluationRuns(
        [TimerTrigger("0 */1 * * * *")] TimerInfo myTimer, // Every 1 minute
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Monitor function executed at: {Timestamp}", startTime);

        try
        {
            // Get all running evaluation runs
            var runningEvaluations = await _dataverseService.GetRunningEvaluationRunsAsync(cancellationToken);
            
            if (runningEvaluations.Count == 0)
            {
                _logger.LogInformation("No running evaluation runs found");
                return;
            }

            _logger.LogInformation("Found {Count} running evaluation runs to monitor", runningEvaluations.Count);

            // Process each running evaluation
            foreach (var evaluationRun in runningEvaluations)
            {
                try
                {
                    await ProcessEvaluationRunAsync(evaluationRun, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "Error processing evaluation run {EvaluationRunId} ({EvaluationRunName})", 
                        evaluationRun.Id, evaluationRun.Name);
                    // Continue processing other evaluation runs
                }
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Monitor function completed successfully in {Duration}ms", duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in monitor function");
        }
    }

    /// <summary>
    /// Process a single evaluation run to check completion and calculate metrics
    /// </summary>
    private async Task ProcessEvaluationRunAsync(EvaluationRun evaluationRun, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing evaluation run {EvaluationRunId} ({EvaluationRunName}): {CompletedTestCases}/{TotalTestCases} completed",
            evaluationRun.Id, evaluationRun.Name, evaluationRun.CompletedTestCases, evaluationRun.TotalTestCases);

        // Skip if TotalTestCases hasn't been initialized yet by orchestrator (race condition)
        if (evaluationRun.TotalTestCases == 0)
        {
            _logger.LogInformation("Evaluation run {EvaluationRunId} has TotalTestCases=0, skipping (orchestrator still initializing)",
                evaluationRun.Id);
            return;
        }

        // Check if evaluation has made no progress for extended period (progress-based timeout)
        // This allows evaluations to run indefinitely as long as they make continuous progress
        var maxNoProgressMinutes = _settings.Evaluation.MaxNoProgressMinutes;
        
        if (evaluationRun.LastProgressUpdate.HasValue)
        {
            var timeSinceProgress = DateTime.UtcNow - evaluationRun.LastProgressUpdate.Value;
            if (timeSinceProgress.TotalMinutes > maxNoProgressMinutes)
            {
                _logger.LogWarning(
                    "Evaluation run {EvaluationRunId} has made no progress for {Minutes} minutes (threshold: {MaxMinutes}), marking as errored",
                    evaluationRun.Id, timeSinceProgress.TotalMinutes, maxNoProgressMinutes);
                
                await _dataverseService.UpdateEvaluationRunCompletionAsync(
                    evaluationRun.Id,
                    EvaluationRunStatus.Errored,
                    null,
                    false,
                    DateTime.UtcNow,
                    cancellationToken);
                return;
            }
        }
        else if (evaluationRun.StartTime.HasValue)
        {
            // If no progress update yet, use start time as baseline
            var timeSinceStart = DateTime.UtcNow - evaluationRun.StartTime.Value;
            if (timeSinceStart.TotalMinutes > maxNoProgressMinutes)
            {
                _logger.LogWarning(
                    "Evaluation run {EvaluationRunId} has had no progress since start ({Minutes} minutes, threshold: {MaxMinutes}), marking as errored",
                    evaluationRun.Id, timeSinceStart.TotalMinutes, maxNoProgressMinutes);
                
                await _dataverseService.UpdateEvaluationRunCompletionAsync(
                    evaluationRun.Id,
                    EvaluationRunStatus.Errored,
                    null,
                    false,
                    DateTime.UtcNow,
                    cancellationToken);
                return;
            }
        }

        // Use efficient database-side counting instead of loading all results into memory
        // Count test case level results only (excludes individual turn results):
        // - Single-turn: TestCaseTurnId is null AND isaggregate is false
        // - Multi-turn: isaggregate is true (the aggregate result for the entire multi-turn sequence)
        var completedCount = await _dataverseService.GetEvaluationResultCountAsync(
            evaluationRun.Id, 
            778760002, // Completed
            cancellationToken);
            
        var failedCount = await _dataverseService.GetEvaluationResultCountAsync(
            evaluationRun.Id,
            778760003, // Failed
            cancellationToken);
            
        var notApplicableCount = await _dataverseService.GetEvaluationResultCountAsync(
            evaluationRun.Id,
            778760004, // Not Applicable
            cancellationToken);
            
        // For pending count, we need to sum Created (1) and Execute (778760001) statuses
        var createdCount = await _dataverseService.GetEvaluationResultCountAsync(
            evaluationRun.Id,
            1, // Created
            cancellationToken);
            
        var executeCount = await _dataverseService.GetEvaluationResultCountAsync(
            evaluationRun.Id,
            778760001, // Execute
            cancellationToken);
            
        var pendingCount = createdCount + executeCount;

        _logger.LogInformation("Evaluation run {EvaluationRunId} progress: {CompletedCount} completed, {FailedCount} failed, {NotApplicableCount} not applicable, {PendingCount} pending (excluding individual turn results)",
            evaluationRun.Id, completedCount, failedCount, notApplicableCount, pendingCount);

        // Update the counts in the evaluation run
        await _dataverseService.UpdateEvaluationRunProgressAsync(evaluationRun.Id, completedCount, failedCount, notApplicableCount, cancellationToken);

        // Check if all test cases are completed (include completed, failed, and not applicable)
        var totalCompleted = completedCount + failedCount + notApplicableCount;
        if (totalCompleted < evaluationRun.TotalTestCases)
        {
            _logger.LogInformation("Evaluation run {EvaluationRunId} still in progress: {CompletedTestCases}/{TotalTestCases}",
                evaluationRun.Id, totalCompleted, evaluationRun.TotalTestCases);
            return;
        }

        _logger.LogInformation("Evaluation run {EvaluationRunId} completed all test cases, calculating final metrics",
            evaluationRun.Id);
        
        // Now load results only for final metrics calculation (when evaluation is complete)
        var results = await _dataverseService.GetEvaluationResultsAsync(evaluationRun.Id, cancellationToken);
        
        // Filter to only test case level results (same logic as before)
        var testCaseResults = results.Where(r => 
            r.IsAggregate == true ||  // Multi-turn aggregate result
            (r.TestCaseTurnId == null && r.IsAggregate == false)  // Single-turn result
        ).ToList();
        
        // Filter to only completed, failed, and not applicable results for metrics calculation
        var finalResults = testCaseResults.Where(r => 
            r.StatusCode == 778760002 || r.StatusCode == 778760003 || r.StatusCode == 778760004).ToList();
        
        if (finalResults.Count == 0)
        {
            _logger.LogWarning("No completed test case results found for completed evaluation run {EvaluationRunId}",
                evaluationRun.Id);
            
            await _dataverseService.UpdateEvaluationRunCompletionAsync(
                evaluationRun.Id,
                EvaluationRunStatus.Errored,
                null,
                false,
                DateTime.UtcNow,
                cancellationToken);
            return;
        }

        // Calculate overall metrics from completed/failed test case level results only
        var metrics = CalculateMetrics(finalResults, evaluationRun);
        
        // Determine pass/fail status based on threshold
        var passingThreshold = evaluationRun.PassingThreshold ?? DefaultPassingThreshold;
        var overallPass = metrics.OverallScore >= passingThreshold;

        // Update evaluation run with completion status and metrics
        await _dataverseService.UpdateEvaluationRunScoresAsync(
            evaluationRun.Id,
            metrics.OverallScore,
            cancellationToken);

        await _dataverseService.UpdateEvaluationRunCompletionAsync(
            evaluationRun.Id,
            EvaluationRunStatus.Completed,
            metrics.OverallScore,
            overallPass,
            DateTime.UtcNow,
            cancellationToken);

        _logger.LogInformation(
            "Evaluation run {EvaluationRunId} completed successfully: OverallScore={OverallScore:F3}, Pass={Pass}",
            evaluationRun.Id, metrics.OverallScore, overallPass);
    }

    /// <summary>
    /// Calculate overall metrics from evaluation results
    /// </summary>
    private EvaluationMetrics CalculateMetrics(List<EvaluationResult> results, EvaluationRun evaluationRun)
    {
        // Filter out results with errors, null scores, or NotApplicable status
        // Only include Completed and Errored results in the overall score calculation
        var validResults = results.Where(r => 
            r.Score.HasValue && 
            string.IsNullOrEmpty(r.ErrorMessage) &&
            r.StatusCode != EvaluationResultStatus.NotApplicable).ToList();
        
        if (validResults.Count == 0)
        {
            _logger.LogWarning("No valid results found for evaluation run {EvaluationRunId}", evaluationRun.Id);
            return new EvaluationMetrics();
        }

        // Calculate overall average score
        var overallScore = validResults.Average(r => r.Score!.Value);

        var metrics = new EvaluationMetrics
        {
            OverallScore = overallScore,
            TotalResults = results.Count,
            ValidResults = validResults.Count,
            ErrorCount = results.Count - validResults.Count
        };

        _logger.LogInformation(
            "Calculated metrics for evaluation run {EvaluationRunId}: " +
            "Total={Total}, Valid={Valid}, Errors={Errors}, Overall Score={OverallScore}",
            evaluationRun.Id,
            metrics.TotalResults, metrics.ValidResults, metrics.ErrorCount, metrics.OverallScore);

        return metrics;
    }

    /// <summary>
    /// Internal class for storing calculated metrics
    /// </summary>
    private class EvaluationMetrics
    {
        public decimal OverallScore { get; set; }
        public int TotalResults { get; set; }
        public int ValidResults { get; set; }
        public int ErrorCount { get; set; }
    }
}