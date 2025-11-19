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
        _logger.LogInformation("Aggregate evaluation processor triggered at {Time}", DateTime.UtcNow);

        try
        {
            // Get all pending aggregate results
            var pendingAggregates = await _dataverseService.GetPendingAggregateResultsAsync(cancellationToken);
            
            if (!pendingAggregates.Any())
            {
                _logger.LogInformation("No pending aggregate results to process");
                return;
            }

            _logger.LogInformation("Found {Count} pending aggregate results", pendingAggregates.Count);

            foreach (var aggregate in pendingAggregates)
            {
                try
                {
                    await ProcessSingleAggregate(aggregate, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "Error processing aggregate result {ResultId} for test case {TestCaseId}",
                        aggregate.Id, aggregate.TestCaseId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in aggregate evaluation processor");
        }
    }

    private async Task ProcessSingleAggregate(EvaluationResult aggregate, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing aggregate result {ResultId} for test case {TestCaseId}, run {RunId}",
            aggregate.Id, aggregate.TestCaseId, aggregate.EvaluationRunId);

        // Validate required fields
        if (aggregate.TestCaseId == Guid.Empty || !aggregate.EvaluatorTypeId.HasValue)
        {
            _logger.LogWarning(
                "Aggregate {ResultId} missing required fields: TestCaseId={TestCaseId}, EvaluatorTypeId={EvaluatorTypeId}",
                aggregate.Id, aggregate.TestCaseId, aggregate.EvaluatorTypeId);
            return;
        }

        // Get all turn results for this test case and evaluation run
        var turnResults = await _dataverseService.GetTurnResultsForTestCaseAsync(
            aggregate.TestCaseId,
            aggregate.EvaluationRunId,
            aggregate.EvaluatorTypeId.Value,
            cancellationToken);

        if (!turnResults.Any())
        {
            _logger.LogWarning(
                "No turn results found for aggregate {ResultId}, test case {TestCaseId}",
                aggregate.Id, aggregate.TestCaseId);
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
            
            // Get passing threshold from evaluation run
            var evaluationRun = await _dataverseService.GetEvaluationRunAsync(
                aggregate.EvaluationRunId,
                cancellationToken);
            var passingThreshold = evaluationRun?.PassingThreshold ?? 0.7m;
            
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
