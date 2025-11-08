using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Azure.Identity;
using System.ServiceModel;

namespace AiAgentEvaluation.Services;

/// <summary>
/// Implementation of Dataverse service
/// </summary>
public class DataverseService : IDataverseService, IDisposable
{
    private readonly ILogger<DataverseService> _logger;
    private readonly AppSettings _settings;
    private readonly ServiceClient? _serviceClient;
    private readonly ResilientService _resilientService;
    private bool _disposed;

    public DataverseService(ILogger<DataverseService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);

        try
        {
            // Use managed identity for authentication when possible
            if (!string.IsNullOrEmpty(_settings.Dataverse.BaseUrl))
            {
                // Use managed identity with Dataverse URL
                var credential = new DefaultAzureCredential();
                var connectionString = $"Url={_settings.Dataverse.BaseUrl};AuthType=Managed Identity";
                
                _serviceClient = new ServiceClient(connectionString);
                
                if (!_serviceClient.IsReady)
                {
                    _logger.LogError("Failed to connect to Dataverse with managed identity: {Error}", _serviceClient.LastError);
                    throw new InvalidOperationException($"Failed to connect to Dataverse: {_serviceClient.LastError}");
                }
                
                _logger.LogInformation("Successfully connected to Dataverse with managed identity");
            }
            else if (!string.IsNullOrEmpty(_settings.Dataverse.ConnectionString))
            {
                // Fallback to connection string for local development
                _serviceClient = new ServiceClient(_settings.Dataverse.ConnectionString);
                
                if (!_serviceClient.IsReady)
                {
                    _logger.LogError("Failed to connect to Dataverse: {Error}", _serviceClient.LastError);
                    throw new InvalidOperationException($"Failed to connect to Dataverse: {_serviceClient.LastError}");
                }
                
                _logger.LogWarning("Using connection string for Dataverse - this should only be used for local development");
            }
            else
            {
                throw new InvalidOperationException("Dataverse BaseUrl is not configured. Please set Dataverse:BaseUrl for managed identity authentication.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Dataverse service");
            throw;
        }
    }

    public async Task<EvaluationRun?> GetEvaluationRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation run {EvaluationRunId}", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationrunsid", 
                    "mcicaps_name", 
                    "mcicaps_testset", 
                    "mcicaps_agentid",
                    "mcicaps_agentname",
                    "mcicaps_description",
                    "mcicaps_passingthreshold",
                    "mcicaps_overallpass",
                    "mcicaps_overallscore",
                    "mcicaps_similarityscore",
                    "mcicaps_accuracyscore", 
                    "mcicaps_relevancyscore",
                    "mcicaps_totaltestcases",
                    "mcicaps_completedtestcases",
                    "mcicaps_failedtestcases",
                    "mcicaps_starttime",
                    "mcicaps_endtime",
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluationrunsid", ConditionOperator.Equal, evaluationRunId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found", evaluationRunId);
                return null;
            }

            var entity = result.Entities[0];
            
            return new EvaluationRun
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                AgentId = entity.GetAttributeValue<string>("mcicaps_agentid") ?? string.Empty,
                AgentName = entity.GetAttributeValue<string>("mcicaps_agentname"),
                Description = entity.GetAttributeValue<string>("mcicaps_description"),
                PassingThreshold = entity.GetAttributeValue<decimal?>("mcicaps_passingthreshold"),
                OverallScore = entity.GetAttributeValue<decimal?>("mcicaps_overallscore"),
                OverallPass = entity.GetAttributeValue<bool?>("mcicaps_overallpass"),
                SimilarityScore = entity.GetAttributeValue<decimal?>("mcicaps_similarityscore"),
                AccuracyScore = entity.GetAttributeValue<decimal?>("mcicaps_accuracyscore"),
                RelevancyScore = entity.GetAttributeValue<decimal?>("mcicaps_relevancyscore"),
                TotalTestCases = entity.GetAttributeValue<int>("mcicaps_totaltestcases"),
                CompletedTestCases = entity.GetAttributeValue<int>("mcicaps_completedtestcases"),
                FailedTestCases = entity.GetAttributeValue<int>("mcicaps_failedtestcases"),
                StartTime = entity.GetAttributeValue<DateTime?>("mcicaps_starttime"),
                EndTime = entity.GetAttributeValue<DateTime?>("mcicaps_endtime"),
                StateCode = entity.GetAttributeValue<int>("statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
            };
        }, "GetEvaluationRun", 
        exception => exception is FaultException<OrganizationServiceFault>, // Dataverse-specific retry logic
        cancellationToken);
    }

    public async Task<List<TestCase>> GetTestCasesAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting test cases for evaluation run {EvaluationRunId}", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Query test cases directly associated with the evaluation run
            var query = new QueryExpression("mcicaps_testcases")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_testcasesid",
                    "mcicaps_name",
                    "mcicaps_userinput",
                    "mcicaps_expectedresponse",
                    "mcicaps_testtype",
                    "mcicaps_conversationid",
                    "mcicaps_testorder",
                    "mcicaps_metadata",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluationrun", ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                Orders = { new OrderExpression("mcicaps_testorder", OrderType.Ascending) }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var testCases = new List<TestCase>();
            
            foreach (var entity in result.Entities)
            {
                var testTypeValue = entity.GetAttributeValue<OptionSetValue>("mcicaps_testtype")?.Value ?? TestType.Similarity;
                
                testCases.Add(new TestCase
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                    EvaluationRunId = evaluationRunId,
                    UserInput = entity.GetAttributeValue<string>("mcicaps_userinput") ?? string.Empty,
                    ExpectedResponse = entity.GetAttributeValue<string>("mcicaps_expectedresponse") ?? string.Empty,
                    TestType = testTypeValue,
                    ConversationId = entity.GetAttributeValue<string>("mcicaps_conversationid"),
                    TestOrder = entity.GetAttributeValue<int?>("mcicaps_testorder"),
                    Metadata = entity.GetAttributeValue<string>("mcicaps_metadata"),
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
                });
            }
            
            _logger.LogInformation("Retrieved {Count} test cases for evaluation run {EvaluationRunId}", 
                testCases.Count, evaluationRunId);
            
            return testCases;
        }, "GetTestCases",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunStatusAsync(Guid evaluationRunId, int statusCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} status to {StatusCode}", evaluationRunId, statusCode);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["statuscode"] = new OptionSetValue(statusCode);
            
            // Set state based on status
            if (statusCode == EvaluationRunStatus.Running)
            {
                updateEntity["statecode"] = new OptionSetValue(1); // Active state
            }
            else if (statusCode == EvaluationRunStatus.Completed || statusCode == EvaluationRunStatus.Errored)
            {
                updateEntity["statecode"] = new OptionSetValue(1); // Keep active, use status to differentiate
            }

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} status to {StatusCode}", 
                evaluationRunId, statusCode);
        }, "UpdateEvaluationRunStatus",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunProgressAsync(Guid evaluationRunId, int completedTestCases, int failedTestCases, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} progress: completed={CompletedTestCases}, failed={FailedTestCases}", 
            evaluationRunId, completedTestCases, failedTestCases);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_completedtestcases"] = completedTestCases;
            updateEntity["mcicaps_failedtestcases"] = failedTestCases;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} progress", evaluationRunId);
        }, "UpdateEvaluationRunProgress",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunScoresAsync(Guid evaluationRunId, decimal? overallScore, decimal? similarityScore, decimal? accuracyScore, decimal? relevancyScore, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} scores", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            
            if (overallScore.HasValue)
                updateEntity["mcicaps_overallscore"] = overallScore.Value;
                
            if (similarityScore.HasValue)
                updateEntity["mcicaps_similarityscore"] = similarityScore.Value;
                
            if (accuracyScore.HasValue)
                updateEntity["mcicaps_accuracyscore"] = accuracyScore.Value;
                
            if (relevancyScore.HasValue)
                updateEntity["mcicaps_relevancyscore"] = relevancyScore.Value;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} scores", evaluationRunId);
        }, "UpdateEvaluationRunScores",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<Guid> CreateEvaluationResultAsync(CreateEvaluationResultRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating evaluation result for test case {TestCaseId}", request.TestCaseId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var resultEntity = new Entity("mcicaps_evaluationresults");
            resultEntity["mcicaps_testcase"] = new EntityReference("mcicaps_testcases", request.TestCaseId);
            resultEntity["mcicaps_evaluation"] = new EntityReference("mcicaps_evaluationruns", request.EvaluationRunId);
            resultEntity["mcicaps_actualresponse"] = request.ActualResponse;
            resultEntity["mcicaps_score"] = request.Score;
            resultEntity["mcicaps_passed"] = request.Passed;
            resultEntity["mcicaps_reasoning"] = request.Reasoning ?? string.Empty;
            resultEntity["mcicaps_executiontimems"] = request.ExecutionTimeMs;
            
            if (!string.IsNullOrEmpty(request.ErrorMessage))
                resultEntity["mcicaps_errormessage"] = request.ErrorMessage;

            var resultId = await Task.Run(() => _serviceClient.Create(resultEntity), ct);
            
            _logger.LogInformation("Successfully created evaluation result {ResultId} for test case {TestCaseId}", 
                resultId, request.TestCaseId);
            
            return resultId;
        }, "CreateEvaluationResult",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<TestCase?> GetTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting test case {TestCaseId}", testCaseId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_testcases")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_testcasesid",
                    "mcicaps_name",
                    "mcicaps_userinput",
                    "mcicaps_expectedresponse",
                    "mcicaps_testtype",
                    "mcicaps_conversationid",
                    "mcicaps_testorder",
                    "mcicaps_metadata",
                    "mcicaps_evaluationrun",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_testcasesid", ConditionOperator.Equal, testCaseId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Test case {TestCaseId} not found", testCaseId);
                return null;
            }

            var entity = result.Entities[0];
            var testTypeValue = entity.GetAttributeValue<OptionSetValue>("mcicaps_testtype")?.Value ?? TestType.Similarity;
            
            return new TestCase
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                EvaluationRunId = entity.GetAttributeValue<EntityReference>("mcicaps_evaluationrun")?.Id ?? Guid.Empty,
                UserInput = entity.GetAttributeValue<string>("mcicaps_userinput") ?? string.Empty,
                ExpectedResponse = entity.GetAttributeValue<string>("mcicaps_expectedresponse") ?? string.Empty,
                TestType = testTypeValue,
                ConversationId = entity.GetAttributeValue<string>("mcicaps_conversationid"),
                TestOrder = entity.GetAttributeValue<int?>("mcicaps_testorder"),
                Metadata = entity.GetAttributeValue<string>("mcicaps_metadata"),
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
            };
        }, "GetTestCase",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task SetEvaluationRunStartTimeAsync(Guid evaluationRunId, DateTime startTime, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting evaluation run {EvaluationRunId} start time to {StartTime}", evaluationRunId, startTime);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_starttime"] = startTime;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} start time", evaluationRunId);
        }, "SetEvaluationRunStartTime",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task SetEvaluationRunEndTimeAsync(Guid evaluationRunId, DateTime endTime, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting evaluation run {EvaluationRunId} end time to {EndTime}", evaluationRunId, endTime);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_endtime"] = endTime;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} end time", evaluationRunId);
        }, "SetEvaluationRunEndTime",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluationRun>> GetRunningEvaluationRunsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting running evaluation runs");
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationrunsid", 
                    "mcicaps_name", 
                    "mcicaps_agentid",
                    "mcicaps_passingthreshold",
                    "mcicaps_overallpass",
                    "mcicaps_overallscore",
                    "mcicaps_similarityscore",
                    "mcicaps_accuracyscore", 
                    "mcicaps_relevancyscore",
                    "mcicaps_totaltestcases",
                    "mcicaps_completedtestcases",
                    "mcicaps_failedtestcases",
                    "mcicaps_starttime",
                    "mcicaps_endtime",
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("statuscode", ConditionOperator.Equal, EvaluationRunStatus.Running)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var evaluationRuns = new List<EvaluationRun>();
            
            foreach (var entity in result.Entities)
            {
                evaluationRuns.Add(new EvaluationRun
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                    AgentId = entity.GetAttributeValue<string>("mcicaps_agentid") ?? string.Empty,
                    PassingThreshold = entity.GetAttributeValue<decimal?>("mcicaps_passingthreshold"),
                    OverallScore = entity.GetAttributeValue<decimal?>("mcicaps_overallscore"),
                    OverallPass = entity.GetAttributeValue<bool?>("mcicaps_overallpass"),
                    SimilarityScore = entity.GetAttributeValue<decimal?>("mcicaps_similarityscore"),
                    AccuracyScore = entity.GetAttributeValue<decimal?>("mcicaps_accuracyscore"),
                    RelevancyScore = entity.GetAttributeValue<decimal?>("mcicaps_relevancyscore"),
                    TotalTestCases = entity.GetAttributeValue<int>("mcicaps_totaltestcases"),
                    CompletedTestCases = entity.GetAttributeValue<int>("mcicaps_completedtestcases"),
                    FailedTestCases = entity.GetAttributeValue<int>("mcicaps_failedtestcases"),
                    StartTime = entity.GetAttributeValue<DateTime?>("mcicaps_starttime"),
                    EndTime = entity.GetAttributeValue<DateTime?>("mcicaps_endtime"),
                    StateCode = entity.GetAttributeValue<int>("statecode"),
                    StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
                });
            }
            
            _logger.LogInformation("Retrieved {Count} running evaluation runs", evaluationRuns.Count);
            
            return evaluationRuns;
        }, "GetRunningEvaluationRuns",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluationResult>> GetEvaluationResultsAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation results for evaluation run {EvaluationRunId}", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_evaluationresults")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationresultid",
                    "mcicaps_name",
                    "mcicaps_evaluationrun",
                    "mcicaps_testcase",
                    "mcicaps_actualresponse",
                    "mcicaps_score",
                    "mcicaps_reasoning",
                    "mcicaps_conversationid",
                    "mcicaps_executiontime",
                    "mcicaps_errormessage",
                    "mcicaps_metadata",
                    "statecode",
                    "statuscode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluationrun", ConditionOperator.Equal, evaluationRunId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var evaluationResults = new List<EvaluationResult>();
            
            foreach (var entity in result.Entities)
            {
                evaluationResults.Add(new EvaluationResult
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                    EvaluationRunId = entity.GetAttributeValue<EntityReference>("mcicaps_evaluationrun")?.Id ?? Guid.Empty,
                    TestCaseId = entity.GetAttributeValue<EntityReference>("mcicaps_testcase")?.Id ?? Guid.Empty,
                    ActualResponse = entity.GetAttributeValue<string>("mcicaps_actualresponse"),
                    Score = entity.GetAttributeValue<decimal?>("mcicaps_score"),
                    Reasoning = entity.GetAttributeValue<string>("mcicaps_reasoning"),
                    ConversationId = entity.GetAttributeValue<string>("mcicaps_conversationid"),
                    ExecutionTimeMs = entity.GetAttributeValue<int?>("mcicaps_executiontime"),
                    ErrorMessage = entity.GetAttributeValue<string>("mcicaps_errormessage"),
                    Metadata = entity.GetAttributeValue<string>("mcicaps_metadata"),
                    StateCode = entity.GetAttributeValue<int>("statecode"),
                    StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
                });
            }
            
            _logger.LogInformation("Retrieved {Count} evaluation results for evaluation run {EvaluationRunId}", 
                evaluationResults.Count, evaluationRunId);
            
            return evaluationResults;
        }, "GetEvaluationResults",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunCompletionAsync(Guid evaluationRunId, int statusCode, decimal? overallScore, bool? overallPass, DateTime endTime, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} completion: status={StatusCode}, score={OverallScore}, pass={OverallPass}", 
            evaluationRunId, statusCode, overallScore, overallPass);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["statuscode"] = new OptionSetValue(statusCode);
            updateEntity["mcicaps_endtime"] = endTime;
            
            if (overallScore.HasValue)
                updateEntity["mcicaps_overallscore"] = overallScore.Value;
                
            if (overallPass.HasValue)
                updateEntity["mcicaps_overallpass"] = overallPass.Value;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} completion", evaluationRunId);
        }, "UpdateEvaluationRunCompletion",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_serviceClient == null || !_serviceClient.IsReady)
            {
                _logger.LogWarning("Dataverse service client is not ready");
                return false;
            }

            // Perform a simple WhoAmI request to verify connectivity
            await Task.Run(() => _serviceClient.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest()), cancellationToken);
            
            _logger.LogDebug("Dataverse health check passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dataverse health check failed");
            return false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _serviceClient?.Dispose();
            _disposed = true;
        }
    }
}