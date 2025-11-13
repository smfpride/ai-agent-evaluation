namespace AiAgentEvaluation.Services;

/// <summary>
/// Factory interface for creating LLM Judge services based on configuration
/// </summary>
public interface ILLMJudgeServiceFactory
{
    /// <summary>
    /// Creates an appropriate LLM Judge service based on the evaluator type's judge agent configuration
    /// </summary>
    /// <param name="evaluatorTypeId">The evaluator type ID that references a judge agent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An instance of ILLMJudgeService configured for the appropriate judge type</returns>
    Task<ILLMJudgeService> CreateJudgeServiceAsync(
        Guid? evaluatorTypeId,
        CancellationToken cancellationToken = default);
}
