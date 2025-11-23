using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;

namespace AiAgentEvaluation.Functions;

/// <summary>
/// Timer-triggered function for processing pending aggregate evaluation results
/// Checks for aggregate results where IsAggregate=true and all associated turn results are completed
/// Calculates final aggregate scores and marks aggregates as complete
/// </summary>
public class AggregateEvaluationProcessorFunction
{
    private readonly ILogger<AggregateEvaluationProcessorFunction> _logger;
    private readonly IDataverseService _dataverseService;

    public AggregateEvaluationProcessorFunction(
        ILogger<AggregateEvaluationProcessorFunction> logger,
        IDataverseService dataverseService)
    {
        _logger = logger;
        _dataverseService = dataverseService;
    }

    /// <summary>
    /// Timer trigger that runs every 30 seconds to process pending aggregate results
    /// </summary>
    [Function("ProcessPendingAggregates")]
    public async Task ProcessPendingAggregates(
        [TimerTrigger("*/30 * * * * *")] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("=== Aggregate evaluation processor triggered at {Time} ===", DateTime.UtcNow);

        try
        {
            // Get all pending aggregate results
            var pendingAggregates = await _dataverseService.GetPendingAggregateResultsAsync(cancellationToken);
            
            _logger.LogInformation("Query returned {Count} pending aggregates", pendingAggregates.Count);
            
            if (!pendingAggregates.Any())
            {
                _logger.LogInformation("No pending aggregate results to process - processor idle");
                return;
            }

            // CRITICAL: Process only first 20 aggregates per run to avoid timeout with large result sets
            // With 30 sec timeout per aggregate and 5 parallel, this ensures we complete in ~2 minutes
            // Function runs every 30 seconds, so it will catch up over multiple runs
            var batchSize = 20;
            var batch = pendingAggregates.Take(batchSize).ToList();
            
            _logger.LogInformation(
                "Processing batch of {BatchSize} aggregates out of {TotalCount} pending",
                batch.Count, pendingAggregates.Count);

            // Process aggregates in parallel for better performance (limit to 5 concurrent to reduce load)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 5,
                CancellationToken = cancellationToken
            };
            
            await Parallel.ForEachAsync(
                batch,
                parallelOptions,
                async (aggregate, ct) =>
                {
                    try
                    {
                        // Add per-aggregate timeout to prevent one slow query from blocking everything
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        
                        await ProcessSingleAggregate(aggregate, linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning(
                            "Aggregate {ResultId} processing timed out after 30 seconds - skipping",
                            aggregate.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Error processing aggregate result {ResultId} for test case {TestCaseId}",
                            aggregate.Id, aggregate.TestCaseId);
                    }
                });
                
            _logger.LogInformation(
                "Completed processing batch - {BatchSize} processed, {Remaining} still pending",
                batch.Count, pendingAggregates.Count - batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in aggregate evaluation processor");
        }
    }

    private async Task ProcessSingleAggregate(EvaluationResult aggregate, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing aggregate result {ResultId} (IsAggregate={IsAggregate}, StatusCode={StatusCode}) for test case {TestCaseId}, run {RunId}, evaluator {EvaluatorId}",
            aggregate.Id, aggregate.IsAggregate, aggregate.StatusCode, aggregate.TestCaseId, aggregate.EvaluationRunId, aggregate.EvaluatorTypeId);

        // Validate required fields
        if (aggregate.TestCaseId == Guid.Empty || !aggregate.EvaluatorTypeId.HasValue)
        {
            _logger.LogWarning(
                "Aggregate {ResultId} missing required fields: TestCaseId={TestCaseId}, EvaluatorTypeId={EvaluatorTypeId}",
                aggregate.Id, aggregate.TestCaseId, aggregate.EvaluatorTypeId);
            return;
        }

        // CRITICAL: Check if the evaluation run is still active
        // If the run has completed/errored/stopped, mark orphaned aggregates as NotApplicable
        // This prevents 2000+ pending aggregates from clogging the queue when runs fail
        var evaluationRun = await _dataverseService.GetEvaluationRunAsync(
            aggregate.EvaluationRunId,
            cancellationToken);
        
        if (evaluationRun == null)
        {
            _logger.LogWarning(
                "Evaluation run {RunId} not found for aggregate {ResultId} - marking as NotApplicable",
                aggregate.EvaluationRunId, aggregate.Id);
            
            await _dataverseService.UpdateEvaluationResultAsync(
                aggregate.Id,
                new UpdateEvaluationResultRequest
                {
                    StatusCode = EvaluationResultStatus.NotApplicable,
                    ErrorMessage = "Evaluation run not found",
                    Reasoning = "Orphaned aggregate - parent run no longer exists"
                },
                cancellationToken);
            return;
        }

        // Check if run is in a terminal state (not Running)
        if (evaluationRun.StatusCode != EvaluationRunStatus.Running)
        {
            _logger.LogInformation(
                "Evaluation run {RunId} is in terminal state {Status} - marking aggregate {ResultId} as NotApplicable",
                aggregate.EvaluationRunId, evaluationRun.StatusCode, aggregate.Id);
            
            await _dataverseService.UpdateEvaluationResultAsync(
                aggregate.Id,
                new UpdateEvaluationResultRequest
                {
                    StatusCode = EvaluationResultStatus.NotApplicable,
                    ErrorMessage = $"Evaluation run ended in state: {evaluationRun.StatusCode}",
                    Reasoning = $"Orphaned aggregate - parent run stopped in state {evaluationRun.StatusCode} before aggregate could complete"
                },
                cancellationToken);
            return;
        }

        // Get the test case to find expected number of turns
        var testCase = await _dataverseService.GetTestCaseAsync(aggregate.TestCaseId, cancellationToken);
        if (testCase == null)
        {
            _logger.LogWarning(
                "Test case {TestCaseId} not found for aggregate {ResultId}",
                aggregate.TestCaseId, aggregate.Id);
            return;
        }

        // Get turn count for this test case
        var turns = await _dataverseService.GetTestCaseTurnsAsync(aggregate.TestCaseId, cancellationToken);
        var expectedTurnCount = turns?.Count ?? 0;
        
        if (expectedTurnCount == 0)
        {
            _logger.LogWarning(
                "Test case {TestCaseId} has no turns defined for aggregate {ResultId}",
                aggregate.TestCaseId, aggregate.Id);
            return;
        }

        // Get all turn results for this test case + evaluation run + evaluator type
        var turnResults = await _dataverseService.GetTurnResultsForTestCaseAsync(
            aggregate.TestCaseId,
            aggregate.EvaluationRunId,
            aggregate.EvaluatorTypeId.Value,
            cancellationToken);

        if (!turnResults.Any())
        {
            _logger.LogInformation(
                "No turn results found yet for aggregate {ResultId}, test case {TestCaseId}, run {RunId}, evaluator {EvaluatorId} - orchestration may still be creating them",
                aggregate.Id, aggregate.TestCaseId, aggregate.EvaluationRunId, aggregate.EvaluatorTypeId);
            return;
        }

        // Check if we have all expected turns
        if (turnResults.Count < expectedTurnCount)
        {
            _logger.LogInformation(
                "Aggregate {ResultId} only has {ActualCount} of {ExpectedCount} turn results - orchestration still in progress",
                aggregate.Id, turnResults.Count, expectedTurnCount);
            return;
        }

        // Check if all turn results are completed (not pending)
        var pendingTurns = turnResults.Where(r => r.StatusCode == EvaluationResultStatus.Pending).ToList();
        
        if (pendingTurns.Any())
        {
            _logger.LogInformation(
                "Aggregate {ResultId} still has {PendingCount} pending turns out of {TotalCount}",
                aggregate.Id, pendingTurns.Count, turnResults.Count);
            return;
        }

        // All turns are completed, calculate aggregate
        _logger.LogInformation(
            "All {Count} turns completed for aggregate {ResultId}, calculating aggregate score",
            turnResults.Count, aggregate.Id);

        var completedTurns = turnResults.Where(r => r.StatusCode == EvaluationResultStatus.Completed).ToList();
        var erroredTurns = turnResults.Where(r => r.StatusCode == EvaluationResultStatus.Errored).ToList();
        var notApplicableTurns = turnResults.Where(r => r.StatusCode == EvaluationResultStatus.NotApplicable).ToList();

        // Calculate aggregate score and pass rate
        decimal aggregateScore;
        bool aggregatePassed;
        string? errorMessage = null;
        int statusCode;

        if (erroredTurns.Any())
        {
            // If any turn errored, aggregate is errored
            aggregateScore = 0m;
            aggregatePassed = false;
            errorMessage = $"{erroredTurns.Count} of {turnResults.Count} turns errored";
            statusCode = EvaluationResultStatus.Errored;
            
            _logger.LogWarning(
                "Aggregate {ResultId} marked as Errored: {ErrorMessage}",
                aggregate.Id, errorMessage);
        }
        else if (notApplicableTurns.Count == turnResults.Count)
        {
            // All turns were NA, aggregate is NA
            aggregateScore = 0m;
            aggregatePassed = false;
            statusCode = EvaluationResultStatus.NotApplicable;
            
            _logger.LogInformation(
                "Aggregate {ResultId} marked as NotApplicable: all turns were NA",
                aggregate.Id);
        }
        else if (completedTurns.Any())
        {
            // Calculate average score from completed turns
            aggregateScore = completedTurns.Average(r => r.Score ?? 0m);
            
            // Get passing threshold from evaluation run (already fetched earlier, but fetch again for latest data)
            var runForThreshold = await _dataverseService.GetEvaluationRunAsync(
                aggregate.EvaluationRunId,
                cancellationToken);
            var passingThreshold = runForThreshold?.PassingThreshold ?? 0.7m;
            
            aggregatePassed = aggregateScore >= passingThreshold;
            statusCode = EvaluationResultStatus.Completed;
            
            _logger.LogInformation(
                "Aggregate {ResultId} calculated: Score={Score:F2}, Passed={Passed}, Threshold={Threshold:F2}",
                aggregate.Id, aggregateScore, aggregatePassed, passingThreshold);
        }
        else
        {
            // No completed turns (shouldn't happen, but handle it)
            aggregateScore = 0m;
            aggregatePassed = false;
            errorMessage = "No completed turns to calculate aggregate";
            statusCode = EvaluationResultStatus.Errored;
            
            _logger.LogWarning(
                "Aggregate {ResultId} has no completed turns",
                aggregate.Id);
        }

        // Calculate aggregate execution time
        var totalExecutionTime = turnResults.Sum(r => r.ExecutionTimeMs ?? 0);

        // Update aggregate result
        await _dataverseService.UpdateEvaluationResultAsync(
            aggregate.Id,
            new UpdateEvaluationResultRequest
            {
                StatusCode = statusCode,
                Score = aggregateScore,
                Passed = aggregatePassed,
                ErrorMessage = errorMessage,
                ExecutionTimeMs = totalExecutionTime,
                Reasoning = $"Aggregate of {turnResults.Count} turns: {completedTurns.Count} completed, {erroredTurns.Count} errored, {notApplicableTurns.Count} NA"
            },
            cancellationToken);

        // Increment completion counter for the evaluation run
        await _dataverseService.IncrementCompletedTestCasesAsync(
            aggregate.EvaluationRunId,
            !aggregatePassed,
            cancellationToken);

        _logger.LogInformation(
            "Successfully processed aggregate result {ResultId}: Status={Status}, Score={Score:F2}, Passed={Passed}",
            aggregate.Id, 
            EvaluationResultStatus.GetName(statusCode),
            aggregateScore,
            aggregatePassed);
    }
}
