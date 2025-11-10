using AiAgentEvaluation.Models;
using AiAgentEvaluation.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
    private readonly string? _connectionError;
    private bool _disposed;

    public DataverseService(ILogger<DataverseService> logger, IOptions<AppSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        
        // Initialize resilient service
        _resilientService = new ResilientService(_logger, settings);

        // CRITICAL: Skip ServiceClient initialization entirely if connection string is not configured
        // This prevents SDK from making authentication attempts to cached/default Dataverse instances
        if (string.IsNullOrWhiteSpace(_settings.Dataverse.ConnectionString))
        {
            _connectionError = "Dataverse ConnectionString is not configured. Dataverse operations will not be available. Please configure DataverseConnectionString with OAuth authentication (AuthenticationType=OAuth).";
            _logger.LogWarning(_connectionError);
            _serviceClient = null;
            return;
        }

        try
        {
            _logger.LogInformation("Attempting to connect to Dataverse with connection string: {ConnectionString}", 
                _settings.Dataverse.ConnectionString.Substring(0, Math.Min(50, _settings.Dataverse.ConnectionString.Length)) + "...");

            // Dataverse only supports OAuth, Certificate, ClientSecret, or Office365 authentication
            // Managed Identity is NOT supported and not on the roadmap
            _serviceClient = new ServiceClient(_settings.Dataverse.ConnectionString);
            
            if (!_serviceClient.IsReady)
            {
                var detailedError = _serviceClient.LastError ?? "Unknown error";
                var connectionState = _serviceClient.IsReady ? "Ready" : "Not Ready";
                _connectionError = $"Failed to connect to Dataverse: {detailedError}";
                
                _logger.LogError("Dataverse connection failed. State: {State}, Error: {Error}, LastException: {LastException}", 
                    connectionState, detailedError, _serviceClient.LastException?.ToString() ?? "None");
                
                // Log additional diagnostics
                _logger.LogError("ServiceClient details - IsReady: {IsReady}, ActiveAuthenticationType: {AuthType}, ConnectedOrgFriendlyName: {OrgName}",
                    _serviceClient.IsReady,
                    _serviceClient.ActiveAuthenticationType,
                    _serviceClient.ConnectedOrgFriendlyName ?? "None");
                
                // Dispose the failed client to prevent background authentication retries
                _serviceClient.Dispose();
                _serviceClient = null;
                
                return;
            }
            
            _logger.LogInformation("Successfully connected to Dataverse using OAuth authentication. Org: {OrgName}, AuthType: {AuthType}", 
                _serviceClient.ConnectedOrgFriendlyName, _serviceClient.ActiveAuthenticationType);
        }
        catch (Exception ex)
        {
            _connectionError = $"Error initializing Dataverse service: {ex.Message}";
            _logger.LogError(ex, "Error initializing Dataverse service. Connection string length: {Length}, InnerException: {InnerException}", 
                _settings.Dataverse.ConnectionString?.Length ?? 0, ex.InnerException?.Message ?? "None");
            
            // Dispose the client to prevent background authentication retries
            if (_serviceClient != null)
            {
                _serviceClient.Dispose();
                _serviceClient = null;
            }
            
            // Don't throw - let the service degrade gracefully
        }
    }

    /// <summary>
    /// Safely retrieves a decimal value from a Dataverse entity, handling type conversion from various numeric types
    /// </summary>
    private static decimal? GetDecimalValue(Entity entity, string attributeName)
    {
        if (!entity.Contains(attributeName) || entity[attributeName] == null)
        {
            return null;
        }

        var value = entity[attributeName];
        
        // Handle all numeric types that Dataverse might return (decimal, double, float, int, long, etc.)
        return value switch
        {
            decimal d => d,
            double dbl => Convert.ToDecimal(dbl),
            float f => Convert.ToDecimal(f),
            int i => Convert.ToDecimal(i),
            long l => Convert.ToDecimal(l),
            short s => Convert.ToDecimal(s),
            byte b => Convert.ToDecimal(b),
            _ => null
        };
    }

    /// <summary>
    /// Safely retrieves an integer value from a Dataverse entity, handling OptionSetValue conversion
    /// </summary>
    private static int GetIntValue(Entity entity, string attributeName, int defaultValue = 0)
    {
        if (!entity.Contains(attributeName) || entity[attributeName] == null)
        {
            return defaultValue;
        }

        var value = entity[attributeName];
        
        // Handle both int and OptionSetValue types that Dataverse might return
        return value switch
        {
            int i => i,
            OptionSetValue osv => osv.Value,
            _ => defaultValue
        };
    }

    public async Task<EvaluationRun?> GetEvaluationRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation run {EvaluationRunId}", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationrunsid", 
                    "mcicaps_runname", 
                    "mcicaps_testset", 
                    "mcicaps_agenttoeval",
                    "mcicaps_description",
                    "mcicaps_passingthreshold",
                    "mcicaps_overallpass",
                    "mcicaps_overallscore",
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
                Name = entity.GetAttributeValue<string>("mcicaps_runname") ?? string.Empty,
                Description = entity.GetAttributeValue<string>("mcicaps_description"),
                TestSetId = entity.GetAttributeValue<EntityReference>("mcicaps_testset")?.Id,
                AgentToEvalId = entity.GetAttributeValue<EntityReference>("mcicaps_agenttoeval")?.Id,
                PassingThreshold = GetDecimalValue(entity, "mcicaps_passingthreshold"),
                OverallScore = GetDecimalValue(entity, "mcicaps_overallscore"),
                OverallPass = entity.GetAttributeValue<bool?>("mcicaps_overallpass"),
                TotalTestCases = entity.GetAttributeValue<int>("mcicaps_totaltestcases"),
                CompletedTestCases = entity.GetAttributeValue<int>("mcicaps_completedtestcases"),
                FailedTestCases = entity.GetAttributeValue<int>("mcicaps_failedtestcases"),
                StartTime = entity.GetAttributeValue<DateTime?>("mcicaps_starttime"),
                EndTime = entity.GetAttributeValue<DateTime?>("mcicaps_endtime"),
                StateCode = GetIntValue(entity, "statecode"),
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
            // First, get the evaluation run to retrieve the test set
            var evaluationRunQuery = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet("mcicaps_testset"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluationrunsid", ConditionOperator.Equal, evaluationRunId)
                    }
                }
            };

            var evaluationRunResult = await Task.Run(() => _serviceClient.RetrieveMultiple(evaluationRunQuery), ct);
            
            if (evaluationRunResult.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found", evaluationRunId);
                return new List<TestCase>();
            }

            var testSetReference = evaluationRunResult.Entities[0].GetAttributeValue<EntityReference>("mcicaps_testset");
            
            if (testSetReference == null)
            {
                _logger.LogWarning("No test set associated with evaluation run {EvaluationRunId}", evaluationRunId);
                return new List<TestCase>();
            }

            // Now query test cases by test set
            var query = new QueryExpression("mcicaps_testcases")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_testcasesid",
                    "mcicaps_testcasename",
                    "mcicaps_prompt",
                    "mcicaps_expectedresponse",
                    "mcicaps_evaluatortype",
                    "mcicaps_ismultiturn",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_testset", ConditionOperator.Equal, testSetReference.Id),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var testCases = new List<TestCase>();
            
            foreach (var entity in result.Entities)
            {
                var evaluatorTypeRef = entity.GetAttributeValue<EntityReference>("mcicaps_evaluatortype");
                
                testCases.Add(new TestCase
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>("mcicaps_testcasename") ?? string.Empty,
                    EvaluationRunId = evaluationRunId,
                    UserInput = entity.GetAttributeValue<string>("mcicaps_prompt") ?? string.Empty,
                    ExpectedResponse = entity.GetAttributeValue<string>("mcicaps_expectedresponse") ?? string.Empty,
                    EvaluatorTypeId = evaluatorTypeRef?.Id,
                    ConversationId = null, // ConversationId is on EvaluationResult, not TestCase
                    IsMultiTurn = entity.GetAttributeValue<bool>("mcicaps_ismultiturn"),
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
            // Note: In Dataverse, statecode 0 = Active, 1 = Inactive (counterintuitive!)
            if (statusCode == EvaluationRunStatus.Running)
            {
                updateEntity["statecode"] = new OptionSetValue(0); // Active state (0)
            }
            else if (statusCode == EvaluationRunStatus.Completed || statusCode == EvaluationRunStatus.Errored)
            {
                updateEntity["statecode"] = new OptionSetValue(0); // Keep active (0), use status to differentiate
            }

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} status to {StatusCode}", 
                evaluationRunId, statusCode);
        }, "UpdateEvaluationRunStatus",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task SetEvaluationRunTotalTestCasesAsync(Guid evaluationRunId, int totalTestCases, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting evaluation run {EvaluationRunId} total test cases to {TotalTestCases}", evaluationRunId, totalTestCases);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_totaltestcases"] = totalTestCases;
            // Initialize counters to 0
            updateEntity["mcicaps_completedtestcases"] = 0;
            updateEntity["mcicaps_failedtestcases"] = 0;
            updateEntity["mcicaps_progresspercentage"] = 0.00m;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} total test cases to {TotalTestCases}", evaluationRunId, totalTestCases);
        }, "SetEvaluationRunTotalTestCases",
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
            // First retrieve the total test cases to calculate percentage
            var query = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet("mcicaps_totaltestcases"),
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
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress update", evaluationRunId);
                return;
            }

            var totalTestCases = result.Entities[0].GetAttributeValue<int>("mcicaps_totaltestcases");

            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_completedtestcases"] = completedTestCases;
            updateEntity["mcicaps_failedtestcases"] = failedTestCases;

            // Calculate and update progress percentage
            if (totalTestCases > 0)
            {
                var progressPercentage = ((decimal)completedTestCases / totalTestCases) * 100;
                updateEntity["mcicaps_progresspercentage"] = Math.Round(progressPercentage, 2);
                
                _logger.LogInformation("Calculated progress percentage for {EvaluationRunId}: {ProgressPercentage}%", 
                    evaluationRunId, Math.Round(progressPercentage, 2));
            }
            else
            {
                _logger.LogWarning("Total test cases is 0 for evaluation run {EvaluationRunId}, cannot calculate progress percentage", 
                    evaluationRunId);
            }

            // Update last progress timestamp
            updateEntity["mcicaps_lastprogressupdate"] = DateTime.UtcNow;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} progress", evaluationRunId);
        }, "UpdateEvaluationRunProgress",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunScoresAsync(Guid evaluationRunId, decimal? overallScore, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} scores", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            
            // Convert decimal to double for Float fields in Dataverse
            if (overallScore.HasValue)
                updateEntity["mcicaps_overallscore"] = Convert.ToDouble(overallScore.Value);

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
            
            // Handle multi-turn fields
            if (request.TestCaseTurnId.HasValue)
                resultEntity["mcicaps_testcaseturnid"] = new EntityReference("mcicaps_testcaseturn", request.TestCaseTurnId.Value);
            
            resultEntity["mcicaps_isaggregate"] = request.IsAggregate;
            
            // Convert decimal to double for Float field in Dataverse
            var scoreAsDouble = Convert.ToDouble(request.Score);
            _logger.LogInformation("DataverseService - Score conversion: decimal {DecimalScore} -> double {DoubleScore}", 
                request.Score, scoreAsDouble);
            
            resultEntity["mcicaps_score"] = scoreAsDouble;
            resultEntity["mcicaps_passed"] = request.Passed;
            resultEntity["mcicaps_reasoning"] = request.Reasoning ?? string.Empty;
            resultEntity["mcicaps_executiontime"] = request.ExecutionTimeMs;
            
            if (!string.IsNullOrEmpty(request.ConversationId))
                resultEntity["mcicaps_conversationid"] = request.ConversationId;
            
            if (!string.IsNullOrEmpty(request.ErrorMessage))
                resultEntity["mcicaps_errormessage"] = request.ErrorMessage;
            
            if (!string.IsNullOrEmpty(request.Metadata))
                resultEntity["mcicaps_metadata"] = request.Metadata;

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
                    "mcicaps_testcasename",
                    "mcicaps_prompt",
                    "mcicaps_expectedresponse",
                    "mcicaps_evaluatortype",
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
            var evaluatorTypeRef = entity.GetAttributeValue<EntityReference>("mcicaps_evaluatortype");
            
            return new TestCase
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("mcicaps_testcasename") ?? string.Empty,
                EvaluationRunId = Guid.Empty, // TestCase doesn't have direct link to EvaluationRun
                UserInput = entity.GetAttributeValue<string>("mcicaps_prompt") ?? string.Empty,
                ExpectedResponse = entity.GetAttributeValue<string>("mcicaps_expectedresponse") ?? string.Empty,
                EvaluatorTypeId = evaluatorTypeRef?.Id,
                ConversationId = null, // ConversationId is on EvaluationResult, not TestCase
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
                    "mcicaps_runname", 
                    "mcicaps_passingthreshold",
                    "mcicaps_overallpass",
                    "mcicaps_overallscore",
                    "mcicaps_totaltestcases",
                    "mcicaps_completedtestcases",
                    "mcicaps_failedtestcases",
                    "mcicaps_starttime",
                    "mcicaps_endtime",
                    "mcicaps_lastprogressupdate",
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
                    Name = entity.GetAttributeValue<string>("mcicaps_runname") ?? string.Empty,
                    PassingThreshold = GetDecimalValue(entity, "mcicaps_passingthreshold"),
                    OverallScore = GetDecimalValue(entity, "mcicaps_overallscore"),
                    OverallPass = entity.GetAttributeValue<bool?>("mcicaps_overallpass"),
                    TotalTestCases = entity.GetAttributeValue<int>("mcicaps_totaltestcases"),
                    CompletedTestCases = entity.GetAttributeValue<int>("mcicaps_completedtestcases"),
                    FailedTestCases = entity.GetAttributeValue<int>("mcicaps_failedtestcases"),
                    StartTime = entity.GetAttributeValue<DateTime?>("mcicaps_starttime"),
                    EndTime = entity.GetAttributeValue<DateTime?>("mcicaps_endtime"),
                    LastProgressUpdate = entity.GetAttributeValue<DateTime?>("mcicaps_lastprogressupdate"),
                    StateCode = GetIntValue(entity, "statecode"),
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
                    "mcicaps_evaluationresultsid",
                    "mcicaps_evaluationresultname",
                    "mcicaps_evaluation",
                    "mcicaps_testcase",
                    "mcicaps_testcaseturnid",
                    "mcicaps_isaggregate",
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
                        new ConditionExpression("mcicaps_evaluation", ConditionOperator.Equal, evaluationRunId)
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
                    Name = entity.GetAttributeValue<string>("mcicaps_evaluationresultname") ?? string.Empty,
                    EvaluationRunId = entity.GetAttributeValue<EntityReference>("mcicaps_evaluation")?.Id ?? Guid.Empty,
                    TestCaseId = entity.GetAttributeValue<EntityReference>("mcicaps_testcase")?.Id ?? Guid.Empty,
                    TestCaseTurnId = entity.GetAttributeValue<EntityReference>("mcicaps_testcaseturnid")?.Id,
                    IsAggregate = entity.GetAttributeValue<bool>("mcicaps_isaggregate"),
                    ActualResponse = entity.GetAttributeValue<string>("mcicaps_actualresponse"),
                    Score = GetDecimalValue(entity, "mcicaps_score"),
                    Reasoning = entity.GetAttributeValue<string>("mcicaps_reasoning"),
                    ConversationId = entity.GetAttributeValue<string>("mcicaps_conversationid"),
                    ExecutionTimeMs = entity.GetAttributeValue<int?>("mcicaps_executiontime"),
                    ErrorMessage = entity.GetAttributeValue<string>("mcicaps_errormessage"),
                    Metadata = entity.GetAttributeValue<string>("mcicaps_metadata"),
                    StateCode = GetIntValue(entity, "statecode"),
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
            
            // Convert decimal to double for Float field in Dataverse
            if (overallScore.HasValue)
                updateEntity["mcicaps_overallscore"] = Convert.ToDouble(overallScore.Value);
                
            if (overallPass.HasValue)
                updateEntity["mcicaps_overallpass"] = overallPass.Value;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} completion", evaluationRunId);
        }, "UpdateEvaluationRunCompletion",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task IncrementCompletedTestCasesAsync(Guid evaluationRunId, bool failed, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Incrementing completed test cases for evaluation run {EvaluationRunId}, failed={Failed}", 
            evaluationRunId, failed);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            // Retrieve current values
            var query = new QueryExpression("mcicaps_evaluationruns")
            {
                ColumnSet = new ColumnSet("mcicaps_completedtestcases", "mcicaps_failedtestcases", "mcicaps_totaltestcases"),
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
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress increment", evaluationRunId);
                return;
            }

            var entity = result.Entities[0];
            var completedTestCases = entity.GetAttributeValue<int>("mcicaps_completedtestcases");
            var failedTestCases = entity.GetAttributeValue<int>("mcicaps_failedtestcases");
            var totalTestCases = entity.GetAttributeValue<int>("mcicaps_totaltestcases");

            // Increment counters
            var newCompletedCount = completedTestCases + 1;
            var updateEntity = new Entity("mcicaps_evaluationruns", evaluationRunId);
            updateEntity["mcicaps_completedtestcases"] = newCompletedCount;
            
            if (failed)
            {
                updateEntity["mcicaps_failedtestcases"] = failedTestCases + 1;
            }

            // Calculate and update progress percentage
            if (totalTestCases > 0)
            {
                var progressPercentage = ((decimal)newCompletedCount / totalTestCases) * 100;
                updateEntity["mcicaps_progresspercentage"] = Math.Round(progressPercentage, 2);
            }

            // Update last progress timestamp
            updateEntity["mcicaps_lastprogressupdate"] = DateTime.UtcNow;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully incremented completed test cases for evaluation run {EvaluationRunId}: {CompletedCount}/{TotalCount}", 
                evaluationRunId, newCompletedCount, totalTestCases);
        }, "IncrementCompletedTestCases",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<EvaluatorType?> GetEvaluatorTypeAsync(Guid evaluatorTypeId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluator type {EvaluatorTypeId}", evaluatorTypeId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_evaluatortype")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluatortypeid",
                    "mcicaps_name",
                    "mcicaps_evaluatorprompt",
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluatortypeid", ConditionOperator.Equal, evaluatorTypeId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluator type {EvaluatorTypeId} not found", evaluatorTypeId);
                return null;
            }

            var entity = result.Entities[0];
            
            return new EvaluatorType
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                EvaluatorPrompt = entity.GetAttributeValue<string>("mcicaps_evaluatorprompt") ?? string.Empty,
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            };
        }, "GetEvaluatorType",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<AgentToEval?> GetAgentToEvalAsync(Guid agentToEvalId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting agent to eval {AgentToEvalId}", agentToEvalId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_agenttoeval")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_agenttoevalid",
                    "mcicaps_name",
                    "mcicaps_directlinespeechurl",
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_agenttoevalid", ConditionOperator.Equal, agentToEvalId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Agent to eval {AgentToEvalId} not found", agentToEvalId);
                return null;
            }

            var entity = result.Entities[0];
            
            return new AgentToEval
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                DirectLineSpeechUrl = entity.GetAttributeValue<string>("mcicaps_directlinespeechurl") ?? string.Empty,
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            };
        }, "GetAgentToEval",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_serviceClient == null || !_serviceClient.IsReady)
            {
                _logger.LogWarning("Dataverse service client is not ready: {Error}", _connectionError);
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

    public async Task<List<TestCaseTurn>> GetTestCaseTurnsAsync(Guid testCaseId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting turns for test case {TestCaseId}", testCaseId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression("mcicaps_testcaseturn")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_testcaseturnid",
                    "mcicaps_name",
                    "mcicaps_testcaseid",
                    "mcicaps_sequence",
                    "mcicaps_prompttext",
                    "mcicaps_expectedresponse",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_testcaseid", ConditionOperator.Equal, testCaseId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                Orders =
                {
                    new OrderExpression("mcicaps_sequence", OrderType.Ascending)
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var turns = new List<TestCaseTurn>();
            
            foreach (var entity in result.Entities)
            {
                var testCaseRef = entity.GetAttributeValue<EntityReference>("mcicaps_testcaseid");
                
                turns.Add(new TestCaseTurn
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>("mcicaps_name") ?? string.Empty,
                    TestCaseId = testCaseRef?.Id ?? testCaseId,
                    Sequence = entity.GetAttributeValue<int>("mcicaps_sequence"),
                    PromptText = entity.GetAttributeValue<string>("mcicaps_prompttext") ?? string.Empty,
                    ExpectedResponse = entity.GetAttributeValue<string>("mcicaps_expectedresponse"),
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                    ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
                });
            }
            
            _logger.LogInformation("Retrieved {Count} turns for test case {TestCaseId}", 
                turns.Count, testCaseId);
            
            return turns;
        }, "GetTestCaseTurns",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<Guid> CreateAggregateEvaluationResultAsync(MultiTurnExecutionContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating aggregate evaluation result for test case {TestCaseId}", context.TestCase.Id);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var resultEntity = new Entity("mcicaps_evaluationresults");
            resultEntity["mcicaps_testcase"] = new EntityReference("mcicaps_testcases", context.TestCase.Id);
            resultEntity["mcicaps_evaluation"] = new EntityReference("mcicaps_evaluationruns", context.TestRunId);
            resultEntity["mcicaps_isaggregate"] = true;
            
            if (!string.IsNullOrEmpty(context.ConversationId))
                resultEntity["mcicaps_conversationid"] = context.ConversationId;

            // Calculate aggregate metrics
            var passRate = context.PassRate;
            
            // Calculate average score from all turn results
            var turnScores = context.TurnResults.Values
                .Where(r => r.Score.HasValue)
                .Select(r => r.Score!.Value)
                .ToList();
            
            var averageScore = turnScores.Any() 
                ? Convert.ToDouble(turnScores.Average()) 
                : 0.0;
            
            resultEntity["mcicaps_score"] = averageScore;
            resultEntity["mcicaps_passed"] = context.PassedTurns == context.TotalTurns;

            // Create summary reasoning
            var summary = $"Multi-turn test case completed: {context.PassedTurns}/{context.TotalTurns} turns passed ({passRate:P0} pass rate), average score: {averageScore:F2}";
            resultEntity["mcicaps_reasoning"] = summary;

            // Calculate average execution time
            var avgExecutionTime = context.TurnResults.Values
                .Where(r => r.ExecutionTimeMs.HasValue)
                .Select(r => r.ExecutionTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average();
            resultEntity["mcicaps_executiontime"] = (int)avgExecutionTime;

            var resultId = await Task.Run(() => _serviceClient.Create(resultEntity), ct);
            
            _logger.LogInformation("Successfully created aggregate evaluation result {ResultId} for test case {TestCaseId}", 
                resultId, context.TestCase.Id);
            
            return resultId;
        }, "CreateAggregateEvaluationResult",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
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