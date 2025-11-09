using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
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
    private readonly ILLMJudgeService _llmJudgeService;
    private const int MaxRunningTimeMinutes = 120; // 2 hours max runtime
    private const int MaxNoProgressMinutes = 30; // 30 minutes with no progress = stalled
    private const decimal DefaultPassingThreshold = 0.7m; // 70% default threshold

    public MonitorFunction(
        ILogger<MonitorFunction> logger,
        IDataverseService dataverseService,
        IServiceBusService serviceBusService,
        ILLMJudgeService llmJudgeService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
        _serviceBusService = serviceBusService;
        _llmJudgeService = llmJudgeService;
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

        // Check if evaluation has stalled (running longer than max time)
        if (evaluationRun.StartTime.HasValue)
        {
            var runningTime = DateTime.UtcNow - evaluationRun.StartTime.Value;
            if (runningTime.TotalMinutes > MaxRunningTimeMinutes)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} has been running for {Minutes} minutes, marking as errored",
                    evaluationRun.Id, runningTime.TotalMinutes);
                
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

        // Check if evaluation has made no progress for extended period
        if (evaluationRun.LastProgressUpdate.HasValue)
        {
            var timeSinceProgress = DateTime.UtcNow - evaluationRun.LastProgressUpdate.Value;
            if (timeSinceProgress.TotalMinutes > MaxNoProgressMinutes)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} has made no progress for {Minutes} minutes, marking as errored",
                    evaluationRun.Id, timeSinceProgress.TotalMinutes);
                
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
            if (timeSinceStart.TotalMinutes > MaxNoProgressMinutes)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} has had no progress since start ({Minutes} minutes), marking as errored",
                    evaluationRun.Id, timeSinceStart.TotalMinutes);
                
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

        // Get all evaluation results to calculate actual counts
        var results = await _dataverseService.GetEvaluationResultsAsync(evaluationRun.Id, cancellationToken);
        
        // Calculate completed and failed counts from actual results
        var completedCount = results.Count;
        var failedCount = results.Count(r => !string.IsNullOrEmpty(r.ErrorMessage) || !r.Score.HasValue);

        _logger.LogInformation("Evaluation run {EvaluationRunId} progress: {CompletedCount}/{TotalTestCases} completed, {FailedCount} failed",
            evaluationRun.Id, completedCount, evaluationRun.TotalTestCases, failedCount);

        // Update the counts in the evaluation run
        await _dataverseService.UpdateEvaluationRunProgressAsync(evaluationRun.Id, completedCount, failedCount, cancellationToken);

        // Check if all test cases are completed
        if (completedCount < evaluationRun.TotalTestCases)
        {
            _logger.LogInformation("Evaluation run {EvaluationRunId} still in progress: {CompletedTestCases}/{TotalTestCases}",
                evaluationRun.Id, completedCount, evaluationRun.TotalTestCases);
            return;
        }

        _logger.LogInformation("Evaluation run {EvaluationRunId} completed all test cases, calculating final metrics",
            evaluationRun.Id);
        
        if (results.Count == 0)
        {
            _logger.LogWarning("No evaluation results found for completed evaluation run {EvaluationRunId}",
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

        // Calculate overall metrics
        var metrics = CalculateMetrics(results, evaluationRun);
        
        // Determine pass/fail status based on threshold
        var passingThreshold = evaluationRun.PassingThreshold ?? DefaultPassingThreshold;
        var overallPass = metrics.OverallScore >= passingThreshold;

        // Update evaluation run with completion status and metrics
        await _dataverseService.UpdateEvaluationRunScoresAsync(
            evaluationRun.Id,
            metrics.OverallScore,
            metrics.SimilarityScore,
            metrics.AccuracyScore,
            metrics.RelevancyScore,
            cancellationToken);

        await _dataverseService.UpdateEvaluationRunCompletionAsync(
            evaluationRun.Id,
            EvaluationRunStatus.Completed,
            metrics.OverallScore,
            overallPass,
            DateTime.UtcNow,
            cancellationToken);

        _logger.LogInformation(
            "Evaluation run {EvaluationRunId} completed successfully: OverallScore={OverallScore:F3}, Pass={Pass}, " +
            "SimilarityScore={SimilarityScore:F3}, AccuracyScore={AccuracyScore:F3}, RelevancyScore={RelevancyScore:F3}",
            evaluationRun.Id, metrics.OverallScore, overallPass,
            metrics.SimilarityScore, metrics.AccuracyScore, metrics.RelevancyScore);
    }

    /// <summary>
    /// Calculate overall metrics from evaluation results
    /// </summary>
    private EvaluationMetrics CalculateMetrics(List<EvaluationResult> results, EvaluationRun evaluationRun)
    {
        // Filter out results with errors or null scores
        var validResults = results.Where(r => r.Score.HasValue && string.IsNullOrEmpty(r.ErrorMessage)).ToList();
        
        if (validResults.Count == 0)
        {
            _logger.LogWarning("No valid results found for evaluation run {EvaluationRunId}", evaluationRun.Id);
            return new EvaluationMetrics();
        }

        // Get test cases to determine test types
        var testCases = _dataverseService.GetTestCasesAsync(evaluationRun.Id).GetAwaiter().GetResult();
        var testCaseDict = testCases.ToDictionary(tc => tc.Id, tc => tc.TestType);

        // Calculate overall average score
        var overallScore = validResults.Average(r => r.Score!.Value);

        // Calculate type-specific scores
        var similarityResults = validResults.Where(r => 
            testCaseDict.TryGetValue(r.TestCaseId, out var type) && type == TestType.Similarity).ToList();
        var accuracyResults = validResults.Where(r => 
            testCaseDict.TryGetValue(r.TestCaseId, out var type) && type == TestType.Accuracy).ToList();
        var relevancyResults = validResults.Where(r => 
            testCaseDict.TryGetValue(r.TestCaseId, out var type) && type == TestType.Relevancy).ToList();

        var metrics = new EvaluationMetrics
        {
            OverallScore = overallScore,
            SimilarityScore = similarityResults.Any() ? similarityResults.Average(r => r.Score!.Value) : (decimal?)null,
            AccuracyScore = accuracyResults.Any() ? accuracyResults.Average(r => r.Score!.Value) : (decimal?)null,
            RelevancyScore = relevancyResults.Any() ? relevancyResults.Average(r => r.Score!.Value) : (decimal?)null,
            TotalResults = results.Count,
            ValidResults = validResults.Count,
            ErrorCount = results.Count - validResults.Count,
            SimilarityCount = similarityResults.Count,
            AccuracyCount = accuracyResults.Count,
            RelevancyCount = relevancyResults.Count
        };

        _logger.LogInformation(
            "Calculated metrics for evaluation run {EvaluationRunId}: " +
            "Total={Total}, Valid={Valid}, Errors={Errors}, " +
            "Similarity={SimilarityCount}, Accuracy={AccuracyCount}, Relevancy={RelevancyCount}",
            evaluationRun.Id,
            metrics.TotalResults, metrics.ValidResults, metrics.ErrorCount,
            metrics.SimilarityCount, metrics.AccuracyCount, metrics.RelevancyCount);

        return metrics;
    }

    /// <summary>
    /// Internal class for storing calculated metrics
    /// </summary>
    private class EvaluationMetrics
    {
        public decimal OverallScore { get; set; }
        public decimal? SimilarityScore { get; set; }
        public decimal? AccuracyScore { get; set; }
        public decimal? RelevancyScore { get; set; }
        public int TotalResults { get; set; }
        public int ValidResults { get; set; }
        public int ErrorCount { get; set; }
        public int SimilarityCount { get; set; }
        public int AccuracyCount { get; set; }
        public int RelevancyCount { get; set; }
    }

    [Function("HealthCheck")]
    public async Task<string> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] 
        Microsoft.Azure.Functions.Worker.Http.HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Health check requested");
        
        try
        {
            // Perform health checks for all dependencies
            var dataverseHealthTask = _dataverseService.CheckHealthAsync(cancellationToken);
            var serviceBusHealthTask = _serviceBusService.CheckHealthAsync(cancellationToken);
            var azureOpenAIHealthTask = _llmJudgeService.CheckHealthAsync(cancellationToken);

            await Task.WhenAll(dataverseHealthTask, serviceBusHealthTask, azureOpenAIHealthTask);

            var dataverseHealthy = await dataverseHealthTask;
            var serviceBusHealthy = await serviceBusHealthTask;
            var azureOpenAIHealthy = await azureOpenAIHealthTask;

            var allHealthy = dataverseHealthy && serviceBusHealthy && azureOpenAIHealthy;
            
            var healthStatus = new
            {
                status = allHealthy ? "Healthy" : "Degraded",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development",
                services = new
                {
                    dataverse = dataverseHealthy ? "Healthy" : "Unhealthy",
                    serviceBus = serviceBusHealthy ? "Healthy" : "Unhealthy",
                    azureOpenAI = azureOpenAIHealthy ? "Healthy" : "Unhealthy"
                }
            };

            _logger.LogInformation("Health check completed: Overall={Status}, Dataverse={Dataverse}, ServiceBus={ServiceBus}, AzureOpenAI={AzureOpenAI}",
                healthStatus.status, healthStatus.services.dataverse, healthStatus.services.serviceBus, 
                healthStatus.services.azureOpenAI);

            return System.Text.Json.JsonSerializer.Serialize(healthStatus, new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            
            var errorStatus = new
            {
                status = "Unhealthy",
                timestamp = DateTime.UtcNow,
                error = ex.Message
            };

            return System.Text.Json.JsonSerializer.Serialize(errorStatus, new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
            });
        }
    }
}