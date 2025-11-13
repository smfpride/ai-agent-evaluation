using AiAgentEvaluation.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Factory for creating LLM Judge service instances based on Judge Agent configuration
/// </summary>
public class LLMJudgeServiceFactory : ILLMJudgeServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<LLMJudgeServiceFactory> _logger;
    private readonly AppSettings _settings;

    public LLMJudgeServiceFactory(
        IServiceProvider serviceProvider,
        IDataverseService dataverseService,
        ILogger<LLMJudgeServiceFactory> logger,
        IOptions<AppSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _dataverseService = dataverseService;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<ILLMJudgeService> CreateJudgeServiceAsync(
        Guid? evaluatorTypeId,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch EvaluatorType with Judge Agent lookup
        if (!evaluatorTypeId.HasValue)
        {
            _logger.LogWarning(
                "No evaluator type specified, falling back to default Azure OpenAI configuration");
            return CreateDefaultAzureOpenAIJudge();
        }

        var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(
            evaluatorTypeId.Value, cancellationToken);

        if (evaluatorType == null)
        {
            throw new InvalidOperationException(
                $"EvaluatorType {evaluatorTypeId} not found in Dataverse");
        }

        // 2. Check for Judge Agent configuration
        if (!evaluatorType.JudgeAgentId.HasValue)
        {
            _logger.LogWarning(
                "EvaluatorType '{EvaluatorName}' has no Judge Agent configured, falling back to default Azure OpenAI",
                evaluatorType.Name);
            return CreateDefaultAzureOpenAIJudge();
        }

        // 3. Fetch Judge Agent
        var judgeAgent = await _dataverseService.GetJudgeAgentAsync(
            evaluatorType.JudgeAgentId.Value, cancellationToken);

        if (judgeAgent == null)
        {
            throw new InvalidOperationException(
                $"Judge Agent {evaluatorType.JudgeAgentId} not found in Dataverse");
        }

        _logger.LogInformation(
            "Creating judge service: Evaluator='{EvaluatorName}', Judge='{JudgeName}', Type={JudgeType}",
            evaluatorType.Name, judgeAgent.Name, JudgeType.GetName(judgeAgent.JudgeType));

        // 4. Create appropriate service based on judge type
        return judgeAgent.JudgeType switch
        {
            JudgeType.AzureOpenAI => CreateAzureOpenAIJudge(judgeAgent),
            JudgeType.CopilotStudio => CreateCopilotStudioJudge(judgeAgent),
            _ => throw new NotSupportedException(
                $"Judge type {judgeAgent.JudgeType} is not supported")
        };
    }

    private ILLMJudgeService CreateAzureOpenAIJudge(JudgeAgent judgeAgent)
    {
        // Validate Azure OpenAI configuration
        if (string.IsNullOrEmpty(judgeAgent.AoaiEndpoint))
        {
            throw new InvalidOperationException(
                $"Judge Agent '{judgeAgent.Name}' is configured for Azure OpenAI " +
                $"but has no AOAI Endpoint");
        }

        if (string.IsNullOrEmpty(judgeAgent.AoaiDeploymentName))
        {
            throw new InvalidOperationException(
                $"Judge Agent '{judgeAgent.Name}' is configured for Azure OpenAI " +
                $"but has no AOAI Deployment Name");
        }

        _logger.LogInformation(
            "Creating Azure OpenAI judge: Endpoint={Endpoint}, Deployment={Deployment}",
            judgeAgent.AoaiEndpoint, judgeAgent.AoaiDeploymentName);

        // Resolve dependencies from DI container
        var logger = _serviceProvider.GetRequiredService<ILogger<AzureOpenAIJudgeService>>();
        var dataverseService = _serviceProvider.GetRequiredService<IDataverseService>();
        var settings = _serviceProvider.GetRequiredService<IOptions<AppSettings>>();

        return new AzureOpenAIJudgeService(logger, settings, dataverseService, judgeAgent);
    }

    private ILLMJudgeService CreateCopilotStudioJudge(JudgeAgent judgeAgent)
    {
        // Validate Copilot Studio configuration
        if (string.IsNullOrEmpty(judgeAgent.DirectLineUrl))
        {
            throw new InvalidOperationException(
                $"Judge Agent '{judgeAgent.Name}' is configured for Copilot Studio " +
                $"but has no Direct Line URL");
        }

        // Model name is optional - if not specified, agent routes to default topic
        var modelInfo = string.IsNullOrEmpty(judgeAgent.ModelName) 
            ? "(default topic)" 
            : judgeAgent.ModelName;

        _logger.LogInformation(
            "Creating Copilot Studio judge: DirectLineUrl={Url}, Model={Model}",
            judgeAgent.DirectLineUrl, modelInfo);

        // Resolve dependencies from DI container
        var logger = _serviceProvider.GetRequiredService<ILogger<CopilotStudioJudgeService>>();
        var copilotService = _serviceProvider.GetRequiredService<ICopilotStudioService>();
        var dataverseService = _serviceProvider.GetRequiredService<IDataverseService>();
        var settings = _serviceProvider.GetRequiredService<IOptions<AppSettings>>();

        return new CopilotStudioJudgeService(logger, settings, copilotService, dataverseService, judgeAgent);
    }

    private ILLMJudgeService CreateDefaultAzureOpenAIJudge()
    {
        // Create a default judge agent using app settings
        var defaultJudgeAgent = new JudgeAgent
        {
            Id = Guid.Empty,
            Name = "Default Azure OpenAI Judge (from AppSettings)",
            JudgeType = JudgeType.AzureOpenAI,
            AoaiEndpoint = _settings.AzureOpenAI.Endpoint,
            AoaiDeploymentName = _settings.AzureOpenAI.DeploymentName
        };

        _logger.LogInformation(
            "Creating default Azure OpenAI judge from AppSettings: Endpoint={Endpoint}, Deployment={Deployment}",
            defaultJudgeAgent.AoaiEndpoint, defaultJudgeAgent.AoaiDeploymentName);

        var logger = _serviceProvider.GetRequiredService<ILogger<AzureOpenAIJudgeService>>();
        var dataverseService = _serviceProvider.GetRequiredService<IDataverseService>();
        var settings = _serviceProvider.GetRequiredService<IOptions<AppSettings>>();

        return new AzureOpenAIJudgeService(logger, settings, dataverseService, defaultJudgeAgent);
    }
}
