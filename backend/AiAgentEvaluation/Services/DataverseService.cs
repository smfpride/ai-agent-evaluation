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

    /// <summary>
    /// Helper method to add configured schema prefix to Dataverse logical names (tables, fields, etc.)
    /// </summary>
    private string Prefixed(string name) => $"{_settings.Dataverse.SchemaPrefixWithUnderscore}{name}";

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
            _connectionError = "Dataverse ConnectionString is not configured. Dataverse operations will not be available. Please configure DataverseConnectionString with ClientSecret authentication (AuthType=ClientSecret).";
            _logger.LogWarning(_connectionError);
            _serviceClient = null;
            return;
        }

        try
        {
            _logger.LogInformation("Attempting to connect to Dataverse with connection string: {ConnectionString}", 
                _settings.Dataverse.ConnectionString.Substring(0, Math.Min(50, _settings.Dataverse.ConnectionString.Length)) + "...");

            // Dataverse supports ClientSecret, Certificate, or Office365 authentication
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
            
            _logger.LogInformation("Successfully connected to Dataverse using ClientSecret authentication. Org: {OrgName}, AuthType: {AuthType}", 
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
            var query = new QueryExpression(Prefixed("evaluationruns"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationrunsid"), 
                    Prefixed("runname"), 
                    Prefixed("testset"), 
                    Prefixed("agenttoeval"),
                    Prefixed("description"),
                    Prefixed("passingthreshold"),
                    Prefixed("overallpass"),
                    Prefixed("overallscore"),
                    Prefixed("totaltestcases"),
                    Prefixed("completedtestcases"),
                    Prefixed("failedtestcases"),
                    Prefixed("starttime"),
                    Prefixed("endtime"),
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
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
                Name = entity.GetAttributeValue<string>(Prefixed("runname")) ?? string.Empty,
                Description = entity.GetAttributeValue<string>(Prefixed("description")),
                TestSetId = entity.GetAttributeValue<EntityReference>(Prefixed("testset"))?.Id,
                AgentToEvalId = entity.GetAttributeValue<EntityReference>(Prefixed("agenttoeval"))?.Id,
                PassingThreshold = GetDecimalValue(entity, Prefixed("passingthreshold")),
                OverallScore = GetDecimalValue(entity, Prefixed("overallscore")),
                OverallPass = entity.GetAttributeValue<bool?>(Prefixed("overallpass")),
                TotalTestCases = entity.GetAttributeValue<int>(Prefixed("totaltestcases")),
                CompletedTestCases = entity.GetAttributeValue<int>(Prefixed("completedtestcases")),
                FailedTestCases = entity.GetAttributeValue<int>(Prefixed("failedtestcases")),
                StartTime = entity.GetAttributeValue<DateTime?>(Prefixed("starttime")),
                EndTime = entity.GetAttributeValue<DateTime?>(Prefixed("endtime")),
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
            var evaluationRunResult = await Task.Run(() => _serviceClient.Retrieve(Prefixed("evaluationruns"), evaluationRunId, new ColumnSet(Prefixed("testset"))), ct);
            
            if (evaluationRunResult == null)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found", evaluationRunId);
                return new List<TestCase>();
            }

            var testSetReference = evaluationRunResult.GetAttributeValue<EntityReference>(Prefixed("testset"));
            
            if (testSetReference == null)
            {
                _logger.LogWarning("No test set associated with evaluation run {EvaluationRunId}", evaluationRunId);
                return new List<TestCase>();
            }

            // Now query test cases by test set
            var query = new QueryExpression(Prefixed("testcases"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("testcasesid"),
                    Prefixed("testcasename"),
                    Prefixed("prompt"),
                    Prefixed("expectedresponse"),
                    Prefixed("ismultiturn"),
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("testset"), ConditionOperator.Equal, testSetReference.Id),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                PageInfo = new PagingInfo
                {
                    Count = 5000, // Maximum page size
                    PageNumber = 1
                }
            };

            var testCases = new List<TestCase>();
            
            // Retrieve all pages of test cases
            EntityCollection result;
            do
            {
                result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
                
                foreach (var entity in result.Entities)
                {
                    testCases.Add(new TestCase
                    {
                        Id = entity.Id,
                        Name = entity.GetAttributeValue<string>(Prefixed("testcasename")) ?? string.Empty,
                        EvaluationRunId = evaluationRunId,
                        UserInput = entity.GetAttributeValue<string>(Prefixed("prompt")) ?? string.Empty,
                        ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")) ?? string.Empty,
                        EvaluatorTypeId = null, // Evaluator types are now fetched via GetEvaluatorTypesForRunAsync (N:N relationship)
                        ConversationId = null, // ConversationId is on EvaluationResult, not TestCase
                        IsMultiTurn = entity.GetAttributeValue<bool>(Prefixed("ismultiturn")),
                        CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
                    });
                }
                
                // Check if there are more pages
                if (result.MoreRecords)
                {
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = result.PagingCookie;
                }
            } while (result.MoreRecords);
            
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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("totaltestcases")] = totalTestCases;
            // Initialize counters to 0
            updateEntity[Prefixed("completedtestcases")] = 0;
            updateEntity[Prefixed("failedtestcases")] = 0;
            updateEntity[Prefixed("progresspercentage")] = 0.00m;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} total test cases to {TotalTestCases}", evaluationRunId, totalTestCases);
        }, "SetEvaluationRunTotalTestCases",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    /// <summary>
    /// Helper method to calculate test case counts from actual results
    /// Only counts test case level results (not individual turns):
    /// - Single-turn: TestCaseTurnId is null AND IsAggregate is false
    /// - Multi-turn: IsAggregate is true (aggregate result for entire sequence)
    /// </summary>
    private (int completed, int failed, int notApplicable) CalculateTestCaseCounts(List<EvaluationResult> results)
    {
        var testCaseResults = results.Where(r => 
            r.IsAggregate == true ||  // Multi-turn aggregate result
            (r.TestCaseTurnId == null && r.IsAggregate == false)  // Single-turn result
        ).ToList();
        
        var completed = testCaseResults.Count(r => r.StatusCode == 778760002);
        var failed = testCaseResults.Count(r => r.StatusCode == 778760003);
        var notApplicable = testCaseResults.Count(r => r.StatusCode == 778760004);
        
        return (completed, failed, notApplicable);
    }

    public async Task UpdateEvaluationRunProgressAsync(Guid evaluationRunId, int completedTestCases, int failedTestCases, int notApplicableTestCases, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} progress: completed={CompletedTestCases}, failed={FailedTestCases}, notApplicable={NotApplicableTestCases}", 
            evaluationRunId, completedTestCases, failedTestCases, notApplicableTestCases);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            // First retrieve the total test cases to calculate percentage
            var query = new QueryExpression(Prefixed("evaluationruns"))
            {
                ColumnSet = new ColumnSet(Prefixed("totaltestcases")),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress update", evaluationRunId);
                return;
            }

            var totalTestCases = result.Entities[0].GetAttributeValue<int>(Prefixed("totaltestcases"));

            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("completedtestcases")] = completedTestCases;
            updateEntity[Prefixed("failedtestcases")] = failedTestCases;
            updateEntity[Prefixed("natestcases")] = notApplicableTestCases;

            // Calculate and update progress percentage (include completed, failed, and not applicable)
            if (totalTestCases > 0)
            {
                var totalProcessed = completedTestCases + failedTestCases + notApplicableTestCases;
                var progressPercentage = ((decimal)totalProcessed / totalTestCases) * 100;
                updateEntity[Prefixed("progresspercentage")] = Math.Round(progressPercentage, 2);
                
                _logger.LogInformation("Calculated progress percentage for {EvaluationRunId}: {TotalProcessed}/{TotalTestCases} = {ProgressPercentage}%", 
                    evaluationRunId, totalProcessed, totalTestCases, Math.Round(progressPercentage, 2));
            }
            else
            {
                _logger.LogWarning("Total test cases is 0 for evaluation run {EvaluationRunId}, cannot calculate progress percentage", 
                    evaluationRunId);
            }

            // Update last progress timestamp
            updateEntity[Prefixed("lastprogressupdate")] = DateTime.UtcNow;

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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            
            // Convert decimal to double for Float fields in Dataverse
            if (overallScore.HasValue)
                updateEntity[Prefixed("overallscore")] = Convert.ToDouble(overallScore.Value);

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
            var resultEntity = new Entity(Prefixed("evaluationresults"));
            resultEntity[Prefixed("testcase")] = new EntityReference(Prefixed("testcases"), request.TestCaseId);
            resultEntity[Prefixed("evaluation")] = new EntityReference(Prefixed("evaluationruns"), request.EvaluationRunId);
            resultEntity[Prefixed("actualresponse")] = request.ActualResponse;
            
            // Set EvaluatorType on individual results for tracking which evaluator produced each result
            if (request.EvaluatorTypeId.HasValue)
                resultEntity[Prefixed("evaluatortypeid")] = new EntityReference(Prefixed("evaluatortype"), request.EvaluatorTypeId.Value);
            
            // Handle multi-turn fields
            if (request.TestCaseTurnId.HasValue)
                resultEntity[Prefixed("testcaseturnid")] = new EntityReference(Prefixed("testcaseturn"), request.TestCaseTurnId.Value);
            
            resultEntity[Prefixed("isaggregate")] = request.IsAggregate;
            
            // Convert decimal to double for Float field in Dataverse
            var scoreAsDouble = Convert.ToDouble(request.Score);
            _logger.LogInformation("DataverseService - Score conversion: decimal {DecimalScore} -> double {DoubleScore}", 
                request.Score, scoreAsDouble);
            
            resultEntity[Prefixed("score")] = scoreAsDouble;
            resultEntity[Prefixed("passed")] = request.Passed;
            resultEntity[Prefixed("reasoning")] = request.Reasoning ?? string.Empty;
            resultEntity[Prefixed("executiontime")] = request.ExecutionTimeMs;
            
            // Set MCS execution time if value is provided
            if (request.McsExecutionTimeMs.HasValue)
            {
                resultEntity[Prefixed("mcsexecutiontime")] = request.McsExecutionTimeMs.Value;
                _logger.LogInformation("DataverseService - Setting McsExecutionTime: {McsExecutionTimeMs}ms for TestCase {TestCaseId}", 
                    request.McsExecutionTimeMs.Value, request.TestCaseId);
            }
            else
            {
                _logger.LogWarning("DataverseService - McsExecutionTimeMs is null for TestCase {TestCaseId}", request.TestCaseId);
            }
            
            if (!string.IsNullOrEmpty(request.ConversationId))
                resultEntity[Prefixed("conversationid")] = request.ConversationId;
            
            if (!string.IsNullOrEmpty(request.ErrorMessage))
                resultEntity[Prefixed("errormessage")] = request.ErrorMessage;
            
            if (!string.IsNullOrEmpty(request.Metadata))
                resultEntity[Prefixed("metadata")] = request.Metadata;

            // Set StatusCode if provided (for lifecycle tracking)
            if (request.StatusCode.HasValue)
                resultEntity["statuscode"] = new OptionSetValue(request.StatusCode.Value);

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
            var query = new QueryExpression(Prefixed("testcases"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("testcasesid"),
                    Prefixed("testcasename"),
                    Prefixed("prompt"),
                    Prefixed("expectedresponse"),
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("testcasesid"), ConditionOperator.Equal, testCaseId)
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
            
            return new TestCase
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("testcasename")) ?? string.Empty,
                EvaluationRunId = Guid.Empty, // TestCase doesn't have direct link to EvaluationRun
                UserInput = entity.GetAttributeValue<string>(Prefixed("prompt")) ?? string.Empty,
                ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")) ?? string.Empty,
                EvaluatorTypeId = null, // Evaluator types are now fetched via GetEvaluatorTypesForRunAsync (N:N relationship)
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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("starttime")] = startTime;

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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("endtime")] = endTime;

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
            var query = new QueryExpression(Prefixed("evaluationruns"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationrunsid"), 
                    Prefixed("runname"), 
                    Prefixed("passingthreshold"),
                    Prefixed("overallpass"),
                    Prefixed("overallscore"),
                    Prefixed("totaltestcases"),
                    Prefixed("completedtestcases"),
                    Prefixed("failedtestcases"),
                    Prefixed("starttime"),
                    Prefixed("endtime"),
                    Prefixed("lastprogressupdate"),
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
                    Name = entity.GetAttributeValue<string>(Prefixed("runname")) ?? string.Empty,
                    PassingThreshold = GetDecimalValue(entity, Prefixed("passingthreshold")),
                    OverallScore = GetDecimalValue(entity, Prefixed("overallscore")),
                    OverallPass = entity.GetAttributeValue<bool?>(Prefixed("overallpass")),
                    TotalTestCases = entity.GetAttributeValue<int>(Prefixed("totaltestcases")),
                    CompletedTestCases = entity.GetAttributeValue<int>(Prefixed("completedtestcases")),
                    FailedTestCases = entity.GetAttributeValue<int>(Prefixed("failedtestcases")),
                    StartTime = entity.GetAttributeValue<DateTime?>(Prefixed("starttime")),
                    EndTime = entity.GetAttributeValue<DateTime?>(Prefixed("endtime")),
                    LastProgressUpdate = entity.GetAttributeValue<DateTime?>(Prefixed("lastprogressupdate")),
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
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("evaluationresultname"),
                    Prefixed("evaluation"),
                    Prefixed("testcase"),
                    Prefixed("testcaseturnid"),
                    Prefixed("isaggregate"),
                    Prefixed("actualresponse"),
                    Prefixed("score"),
                    Prefixed("reasoning"),
                    Prefixed("conversationid"),
                    Prefixed("executiontime"),
                    Prefixed("mcsexecutiontime"),
                    Prefixed("errormessage"),
                    Prefixed("metadata"),
                    "statecode",
                    "statuscode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId)
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
                    Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
                    EvaluationRunId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluation"))?.Id ?? Guid.Empty,
                    TestCaseId = entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty,
                    TestCaseTurnId = entity.GetAttributeValue<EntityReference>(Prefixed("testcaseturnid"))?.Id,
                    IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
                    ActualResponse = entity.GetAttributeValue<string>(Prefixed("actualresponse")),
                    Score = GetDecimalValue(entity, Prefixed("score")),
                    Reasoning = entity.GetAttributeValue<string>(Prefixed("reasoning")),
                    ConversationId = entity.GetAttributeValue<string>(Prefixed("conversationid")),
                    ExecutionTimeMs = entity.GetAttributeValue<int?>(Prefixed("executiontime")),
                    McsExecutionTimeMs = entity.GetAttributeValue<int?>(Prefixed("mcsexecutiontime")),
                    ErrorMessage = entity.GetAttributeValue<string>(Prefixed("errormessage")),
                    Metadata = entity.GetAttributeValue<string>(Prefixed("metadata")),
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
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity["statuscode"] = new OptionSetValue(statusCode);
            updateEntity[Prefixed("endtime")] = endTime;
            
            // Convert decimal to double for Float field in Dataverse
            if (overallScore.HasValue)
                updateEntity[Prefixed("overallscore")] = Convert.ToDouble(overallScore.Value);
                
            if (overallPass.HasValue)
                updateEntity[Prefixed("overallpass")] = overallPass.Value;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} completion", evaluationRunId);
        }, "UpdateEvaluationRunCompletion",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task IncrementCompletedTestCasesAsync(Guid evaluationRunId, bool failed, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating test case counts for evaluation run {EvaluationRunId} based on actual results", 
            evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            // Get all evaluation results to calculate actual counts
            var results = await GetEvaluationResultsAsync(evaluationRunId, ct);
            
            // Calculate counts from actual results using shared logic
            var (completedCount, failedCount, notApplicableCount) = CalculateTestCaseCounts(results);
            
            _logger.LogInformation("Recalculated counts for {EvaluationRunId}: completed={Completed}, failed={Failed}, notApplicable={NotApplicable}", 
                evaluationRunId, completedCount, failedCount, notApplicableCount);

            // Retrieve total test cases for percentage calculation
            var query = new QueryExpression(Prefixed("evaluationruns"))
            {
                ColumnSet = new ColumnSet(Prefixed("totaltestcases")),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress update", evaluationRunId);
                return;
            }

            var totalTestCases = result.Entities[0].GetAttributeValue<int>(Prefixed("totaltestcases"));

            // Update with actual counts
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("completedtestcases")] = completedCount;
            updateEntity[Prefixed("failedtestcases")] = failedCount;
            updateEntity[Prefixed("natestcases")] = notApplicableCount;

            // Calculate and update progress percentage (include all processed test cases)
            if (totalTestCases > 0)
            {
                var totalProcessed = completedCount + failedCount + notApplicableCount;
                var progressPercentage = ((decimal)totalProcessed / totalTestCases) * 100;
                updateEntity[Prefixed("progresspercentage")] = Math.Round(progressPercentage, 2);
                
                _logger.LogInformation("Progress for {EvaluationRunId}: {TotalProcessed}/{TotalTestCases} = {ProgressPercentage}%", 
                    evaluationRunId, totalProcessed, totalTestCases, Math.Round(progressPercentage, 2));
            }

            // Update last progress timestamp
            updateEntity[Prefixed("lastprogressupdate")] = DateTime.UtcNow;

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated test case counts for evaluation run {EvaluationRunId} from actual results", 
                evaluationRunId);
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
            var query = new QueryExpression(Prefixed("evaluatortype"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluatortypeid"),
                    Prefixed("name"),
                    Prefixed("evaluatorprompt"),
                    Prefixed("requiresexpectedresponse"),
                    Prefixed("judgeagentid"),
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluatortypeid"), ConditionOperator.Equal, evaluatorTypeId),
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
                Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                EvaluatorPrompt = entity.GetAttributeValue<string>(Prefixed("evaluatorprompt")) ?? string.Empty,
                RequiresExpectedResponse = entity.GetAttributeValue<bool>(Prefixed("requiresexpectedresponse")),
                JudgeAgentId = entity.Contains(Prefixed("judgeagentid")) 
                    ? entity.GetAttributeValue<EntityReference>(Prefixed("judgeagentid"))?.Id 
                    : null,
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            };
        }, "GetEvaluatorType",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluatorType>> GetEvaluatorTypesForRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluator types for evaluation run {EvaluationRunId}", evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Query the N:N join table to get evaluator types associated with this run
            // Note: N:N intersect table has schema prefix on table name but NOT on field names
            var query = new QueryExpression(Prefixed("evaluatortype"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluatortypeid"),
                    Prefixed("name"),
                    Prefixed("evaluatorprompt"),
                    Prefixed("requiresexpectedresponse"),
                    Prefixed("judgeagentid"),
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                LinkEntities =
                {
                    // First link: From evaluatortype to the N:N intersect table
                    new LinkEntity
                    {
                        LinkFromEntityName = Prefixed("evaluatortype"),
                        // Primary key column of evaluatortype table
                        LinkFromAttributeName = Prefixed("evaluatortypeid"),
                        // N:N intersect table name
                        LinkToEntityName = Prefixed("evaluationruns_mcicaps_evaluato"),
                        // Primary key column in intersect table (matches evaluatortype primary key)
                        LinkToAttributeName = Prefixed("evaluatortypeid"),
                        JoinOperator = JoinOperator.Inner,
                        LinkCriteria = new FilterExpression
                        {
                            Conditions =
                            {
                                // Filter on evaluationruns primary key column in intersect table
                                // Note: Uses "evaluationrunsid" (with 's') - the PRIMARY KEY column name
                                new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
                            }
                        }
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            _logger.LogInformation("Found {Count} evaluator types for evaluation run {EvaluationRunId}", 
                result.Entities.Count, evaluationRunId);

            return result.Entities.Select(entity => new EvaluatorType
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                EvaluatorPrompt = entity.GetAttributeValue<string>(Prefixed("evaluatorprompt")) ?? string.Empty,
                RequiresExpectedResponse = entity.GetAttributeValue<bool>(Prefixed("requiresexpectedresponse")),
                JudgeAgentId = entity.Contains(Prefixed("judgeagentid")) 
                    ? entity.GetAttributeValue<EntityReference>(Prefixed("judgeagentid"))?.Id 
                    : null,
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            }).ToList();
        }, "GetEvaluatorTypesForRun",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<JudgeAgent?> GetJudgeAgentAsync(Guid judgeAgentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting judge agent {JudgeAgentId}", judgeAgentId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression(Prefixed("judgeagent"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("judgeagentid"),
                    Prefixed("name"),
                    Prefixed("judgetype"),
                    Prefixed("aoaiendpoint"),
                    Prefixed("aoaideploymentname"),
                    Prefixed("directlineurl"),
                    Prefixed("modelname"),
                    Prefixed("description"),
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("judgeagentid"), ConditionOperator.Equal, judgeAgentId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogWarning("Judge agent {JudgeAgentId} not found", judgeAgentId);
                return null;
            }

            var entity = result.Entities[0];
            
            return new JudgeAgent
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                JudgeType = entity.GetAttributeValue<OptionSetValue>(Prefixed("judgetype"))?.Value ?? JudgeType.AzureOpenAI,
                AoaiEndpoint = entity.GetAttributeValue<string>(Prefixed("aoaiendpoint")),
                AoaiDeploymentName = entity.GetAttributeValue<string>(Prefixed("aoaideploymentname")),
                DirectLineUrl = entity.GetAttributeValue<string>(Prefixed("directlineurl")),
                ModelName = entity.GetAttributeValue<string>(Prefixed("modelname")),
                Description = entity.GetAttributeValue<string>(Prefixed("description")),
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            };
        }, "GetJudgeAgent",
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
            var query = new QueryExpression(Prefixed("agenttoeval"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("agenttoevalid"),
                    Prefixed("name"),
                    Prefixed("directlinespeechurl"),
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("agenttoevalid"), ConditionOperator.Equal, agentToEvalId),
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
                Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                DirectLineSpeechUrl = entity.GetAttributeValue<string>(Prefixed("directlinespeechurl")) ?? string.Empty,
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
            var query = new QueryExpression(Prefixed("testcaseturn"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("testcaseturnid"),
                    Prefixed("name"),
                    Prefixed("testcaseid"),
                    Prefixed("sequence"),
                    Prefixed("prompttext"),
                    Prefixed("expectedresponse"),
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("testcaseid"), ConditionOperator.Equal, testCaseId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                Orders =
                {
                    new OrderExpression(Prefixed("sequence"), OrderType.Ascending)
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            var turns = new List<TestCaseTurn>();
            
            foreach (var entity in result.Entities)
            {
                var testCaseRef = entity.GetAttributeValue<EntityReference>(Prefixed("testcaseid"));
                var promptText = entity.GetAttributeValue<string>(Prefixed("prompttext")) ?? string.Empty;
                var sequence = entity.GetAttributeValue<int>(Prefixed("sequence"));
                
                _logger.LogInformation("Retrieved turn from Dataverse: sequence={Sequence}, promptText='{PromptText}'", 
                    sequence, promptText);
                
                turns.Add(new TestCaseTurn
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                    TestCaseId = testCaseRef?.Id ?? testCaseId,
                    Sequence = sequence,
                    PromptText = promptText,
                    ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")),
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
            var resultEntity = new Entity(Prefixed("evaluationresults"));
            resultEntity[Prefixed("testcase")] = new EntityReference(Prefixed("testcases"), context.TestCase.Id);
            resultEntity[Prefixed("evaluation")] = new EntityReference(Prefixed("evaluationruns"), context.TestRunId);
            resultEntity[Prefixed("isaggregate")] = true;
            
            if (!string.IsNullOrEmpty(context.ConversationId))
                resultEntity[Prefixed("conversationid")] = context.ConversationId;

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
            
            // Check if average score meets the passing threshold
            var passedOverall = averageScore >= Convert.ToDouble(context.PassingThreshold);
            
            resultEntity[Prefixed("score")] = averageScore;
            resultEntity[Prefixed("passed")] = passedOverall;

            // Create summary reasoning
            var summary = $"Multi-turn test case completed: {context.PassedTurns}/{context.TotalTurns} turns passed ({passRate:P0} pass rate), average score: {averageScore:F2}, threshold: {context.PassingThreshold:F2}, overall: {(passedOverall ? "PASSED" : "FAILED")}";
            resultEntity[Prefixed("reasoning")] = summary;

            // Calculate average execution time
            var avgExecutionTime = context.TurnResults.Values
                .Where(r => r.ExecutionTimeMs.HasValue)
                .Select(r => r.ExecutionTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average();
            resultEntity[Prefixed("executiontime")] = (int)avgExecutionTime;

            var resultId = await Task.Run(() => _serviceClient.Create(resultEntity), ct);
            
            _logger.LogInformation("Successfully created aggregate evaluation result {ResultId} for test case {TestCaseId}", 
                resultId, context.TestCase.Id);
            
            return resultId;
        }, "CreateAggregateEvaluationResult",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<EvaluationResult?> GetAggregateResultForTestCaseAsync(
        Guid evaluationRunId, 
        Guid testCaseId, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting aggregate result for test case {TestCaseId} in evaluation run {EvaluationRunId}", 
            testCaseId, evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("evaluationresultname"),
                    Prefixed("evaluation"),
                    Prefixed("testcase"),
                    Prefixed("isaggregate"),
                    Prefixed("score"),
                    Prefixed("passed"),
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression(Prefixed("testcase"), ConditionOperator.Equal, testCaseId),
                        new ConditionExpression(Prefixed("isaggregate"), ConditionOperator.Equal, true)
                    }
                },
                TopCount = 1
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            if (result.Entities.Count == 0)
            {
                _logger.LogInformation("No aggregate result found for test case {TestCaseId} in evaluation run {EvaluationRunId}", 
                    testCaseId, evaluationRunId);
                return null;
            }

            var entity = result.Entities[0];
            
            var evaluationResult = new EvaluationResult
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
                EvaluationRunId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluation"))?.Id ?? Guid.Empty,
                TestCaseId = entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
                Score = GetDecimalValue(entity, Prefixed("score")),
                Passed = entity.GetAttributeValue<bool?>(Prefixed("passed")),
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
            };
            
            _logger.LogInformation("Found aggregate result {ResultId} with StatusCode {StatusCode} for test case {TestCaseId}", 
                evaluationResult.Id, evaluationResult.StatusCode, testCaseId);
            
            return evaluationResult;
        }, "GetAggregateResultForTestCase",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }
    
    /// <summary>
    /// Gets ALL aggregate results for a test case in an evaluation run.
    /// Used for multi-evaluator idempotency checks.
    /// </summary>
    public async Task<List<EvaluationResult>> GetAggregateResultsForTestCaseAsync(
        Guid evaluationRunId, 
        Guid testCaseId, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all aggregate results for test case {TestCaseId} in evaluation run {EvaluationRunId}", 
            testCaseId, evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("evaluationresultname"),
                    Prefixed("evaluation"),
                    Prefixed("testcase"),
                    Prefixed("isaggregate"),
                    Prefixed("evaluatortypeid"),
                    Prefixed("score"),
                    Prefixed("passed"),
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression(Prefixed("testcase"), ConditionOperator.Equal, testCaseId),
                        new ConditionExpression(Prefixed("isaggregate"), ConditionOperator.Equal, true)
                    }
                }
                // No TopCount - return ALL aggregates for all evaluators
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), ct);
            
            _logger.LogInformation("Found {Count} aggregate results for test case {TestCaseId} in evaluation run {EvaluationRunId}", 
                result.Entities.Count, testCaseId, evaluationRunId);

            return result.Entities.Select(entity => new EvaluationResult
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
                EvaluationRunId = evaluationRunId,
                TestCaseId = testCaseId,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
                EvaluatorTypeId = entity.Contains(Prefixed("evaluatortypeid")) 
                    ? entity.GetAttributeValue<EntityReference>(Prefixed("evaluatortypeid"))?.Id 
                    : null,
                Score = entity.GetAttributeValue<decimal?>(Prefixed("score")),
                Passed = entity.GetAttributeValue<bool?>(Prefixed("passed")),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                StateCode = GetIntValue(entity, "statecode"),
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
            }).ToList();
        }, "GetAggregateResultsForTestCase",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluationResult>> GetAggregateResultsForEvaluationRunAsync(
        Guid evaluationRunId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Getting all aggregate results for evaluation run {EvaluationRunId} (batch query)", 
            evaluationRunId);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        return await _resilientService.ExecuteAsync(async cancellationToken =>
        {
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("evaluationresultname"),
                    Prefixed("evaluation"),
                    Prefixed("testcase"),
                    Prefixed("isaggregate"),
                    Prefixed("evaluatortypeid"),
                    Prefixed("score"),
                    Prefixed("passed"),
                    "statuscode",
                    "statecode",
                    "createdon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression(Prefixed("isaggregate"), ConditionOperator.Equal, true)
                    }
                }
            };

            var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), cancellationToken);
            
            _logger.LogInformation("Fetched {Count} aggregate results for evaluation run {EvaluationRunId}", 
                result.Entities.Count, evaluationRunId);

            return result.Entities.Select(entity => new EvaluationResult
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
                EvaluationRunId = evaluationRunId,
                TestCaseId = entity.Contains(Prefixed("testcase"))
                    ? entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty
                    : Guid.Empty,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
                EvaluatorTypeId = entity.Contains(Prefixed("evaluatortypeid")) 
                    ? entity.GetAttributeValue<EntityReference>(Prefixed("evaluatortypeid"))?.Id 
                    : null,
                Score = entity.GetAttributeValue<decimal?>(Prefixed("score")),
                Passed = entity.GetAttributeValue<bool?>(Prefixed("passed")),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                StateCode = GetIntValue(entity, "statecode"),
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
            }).ToList();
        }, "GetAggregateResultsForEvaluationRun",
        exception => exception is FaultException<OrganizationServiceFault>,
        ct);
    }

    public async Task UpdateAggregateResultAsync(
        Guid resultId,
        decimal score,
        bool passed,
        int statusCode,
        string reasoning,
        int executionTimeMs,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating aggregate result {ResultId} with score={Score}, passed={Passed}, statusCode={StatusCode}", 
            resultId, score, passed, statusCode);
        
        if (_serviceClient == null || !_serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse service client is not ready");
        }

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationresults"), resultId);
            
            // Convert decimal to double for Float field in Dataverse
            updateEntity[Prefixed("score")] = Convert.ToDouble(score);
            updateEntity[Prefixed("passed")] = passed;
            updateEntity[Prefixed("reasoning")] = reasoning;
            updateEntity[Prefixed("executiontime")] = executionTimeMs;
            updateEntity["statuscode"] = new OptionSetValue(statusCode);

            await Task.Run(() => _serviceClient.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated aggregate result {ResultId}", resultId);
        }, "UpdateAggregateResult",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    /// <summary>
    /// Gets a single evaluation result by ID (Phase 1 - New Architecture)
    /// </summary>
    public async Task<EvaluationResult?> GetEvaluationResultAsync(Guid resultId, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new QueryExpression("mcicaps_evaluationresults")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationresultsid",
                    "mcicaps_evaluationresultname",
                    "mcicaps_evaluation",
                    "mcicaps_testcase",
                    "mcicaps_evaluatortypeid",
                    "mcicaps_testcaseturnid",
                    "mcicaps_isaggregate",
                    "mcicaps_actualresponse",
                    "mcicaps_score",
                    "mcicaps_passed",
                    "mcicaps_reasoning",
                    "mcicaps_conversationid",
                    "mcicaps_executiontime",
                    "mcicaps_mcsexecutiontime",
                    "mcicaps_errormessage",
                    "mcicaps_metadata",
                    "statecode",
                    "statuscode",
                    "createdon",
                    "modifiedon"
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("mcicaps_evaluationresultsid", ConditionOperator.Equal, resultId)
                    }
                }
            };

            if (_serviceClient == null)
            {
                throw new InvalidOperationException("Dataverse service client is not initialized");
            }

            var results = await _serviceClient.RetrieveMultipleAsync(query, cancellationToken);
            
            if (results.Entities.Count == 0)
            {
                _logger.LogWarning("Evaluation result {ResultId} not found", resultId);
                return null;
            }

            var entity = results.Entities[0];
            return new EvaluationResult
            {
                Id = entity.Id,
                Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
                EvaluationRunId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluation"))?.Id ?? Guid.Empty,
                TestCaseId = entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty,
                EvaluatorTypeId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluatortypeid"))?.Id,
                TestCaseTurnId = entity.GetAttributeValue<EntityReference>(Prefixed("testcaseturnid"))?.Id,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
                ActualResponse = entity.GetAttributeValue<string>(Prefixed("actualresponse")),
                Score = GetDecimalValue(entity, Prefixed("score")),
                Passed = entity.GetAttributeValue<bool?>(Prefixed("passed")),
                Reasoning = entity.GetAttributeValue<string>(Prefixed("reasoning")),
                ConversationId = entity.GetAttributeValue<string>(Prefixed("conversationid")),
                ExecutionTimeMs = entity.GetAttributeValue<int?>(Prefixed("executiontime")),
                McsExecutionTimeMs = entity.GetAttributeValue<int?>(Prefixed("mcsexecutiontime")),
                ErrorMessage = entity.GetAttributeValue<string>(Prefixed("errormessage")),
                Metadata = entity.GetAttributeValue<string>(Prefixed("metadata")),
                StateCode = GetIntValue(entity, "statecode"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
                CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving evaluation result {ResultId}", resultId);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing evaluation result (Phase 1 - New Architecture)
    /// </summary>
    public async Task UpdateEvaluationResultAsync(Guid resultId, UpdateEvaluationResultRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = new Entity("mcicaps_evaluationresults")
            {
                Id = resultId
            };

            entity["statuscode"] = new OptionSetValue(request.StatusCode);

            if (request.Score.HasValue)
                entity["mcicaps_score"] = request.Score.Value;

            if (request.Passed.HasValue)
                entity["mcicaps_passed"] = request.Passed.Value;

            if (!string.IsNullOrWhiteSpace(request.Reasoning))
                entity["mcicaps_reasoning"] = request.Reasoning;

            if (request.ExecutionTimeMs.HasValue)
                entity["mcicaps_executiontime"] = request.ExecutionTimeMs.Value;

            if (!string.IsNullOrWhiteSpace(request.ErrorMessage))
                entity["mcicaps_errormessage"] = request.ErrorMessage;

            if (_serviceClient == null)
            {
                throw new InvalidOperationException("Dataverse service client is not initialized");
            }

            await _serviceClient.UpdateAsync(entity, cancellationToken);
            
            _logger.LogInformation(
                "Updated evaluation result {ResultId} to status {StatusCode}",
                resultId,
                EvaluationResultStatus.GetName(request.StatusCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating evaluation result {ResultId}", resultId);
            throw;
        }
    }

    public async Task<List<EvaluationResult>> GetPendingAggregateResultsAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceClient == null)
        {
            throw new InvalidOperationException("Dataverse service client is not initialized");
        }

        try
        {
            var query = new QueryExpression("mcicaps_evaluationresults")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationresultsid",
                    "mcicaps_evaluation",
                    "mcicaps_testcase",
                    "mcicaps_evaluatortypeid",
                    "mcicaps_isaggregate",
                    "statuscode"
                ),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            // Filter: IsAggregate = true AND StatusCode = Pending (1)
            query.Criteria.AddCondition("mcicaps_isaggregate", ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, EvaluationResultStatus.Pending);

            var results = await _serviceClient.RetrieveMultipleAsync(query, cancellationToken);
            
            _logger.LogInformation(
                "Found {Count} pending aggregate results",
                results.Entities.Count);

            return results.Entities.Select(entity => new EvaluationResult
            {
                Id = entity.GetAttributeValue<Guid>("mcicaps_evaluationresultsid"),
                EvaluationRunId = entity.GetAttributeValue<EntityReference>("mcicaps_evaluation")?.Id ?? Guid.Empty,
                TestCaseId = entity.GetAttributeValue<EntityReference>("mcicaps_testcase")?.Id ?? Guid.Empty,
                EvaluatorTypeId = entity.GetAttributeValue<EntityReference>("mcicaps_evaluatortypeid")?.Id,
                IsAggregate = entity.GetAttributeValue<bool>("mcicaps_isaggregate"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending aggregate results");
            throw;
        }
    }

    public async Task<List<EvaluationResult>> GetTurnResultsForTestCaseAsync(
        Guid testCaseId,
        Guid evaluationRunId,
        Guid evaluatorTypeId,
        CancellationToken cancellationToken = default)
    {
        if (_serviceClient == null)
        {
            throw new InvalidOperationException("Dataverse service client is not initialized");
        }

        try
        {
            _logger.LogInformation(
                "Querying turn results: TestCase={TestCaseId}, Run={RunId}, Evaluator={EvaluatorId}",
                testCaseId, evaluationRunId, evaluatorTypeId);
            
            var query = new QueryExpression("mcicaps_evaluationresults")
            {
                ColumnSet = new ColumnSet(
                    "mcicaps_evaluationresultsid",
                    "mcicaps_score",
                    "mcicaps_passed",
                    "mcicaps_executiontime",
                    "mcicaps_errormessage",
                    "mcicaps_isaggregate",
                    "statuscode"
                ),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            // Filter: TestCaseId AND EvaluationRunId AND EvaluatorTypeId AND IsAggregate = false
            // For lookup fields, use the relationship name directly
            query.Criteria.AddCondition("mcicaps_testcase", ConditionOperator.Equal, testCaseId);
            query.Criteria.AddCondition("mcicaps_evaluation", ConditionOperator.Equal, evaluationRunId);
            query.Criteria.AddCondition("mcicaps_evaluatortypeid", ConditionOperator.Equal, evaluatorTypeId);
            query.Criteria.AddCondition("mcicaps_isaggregate", ConditionOperator.Equal, false);

            var results = await _serviceClient.RetrieveMultipleAsync(query, cancellationToken);
            
            _logger.LogInformation(
                "Found {Count} turn results for test case {TestCaseId}, run {RunId}, evaluator {EvaluatorId}",
                results.Entities.Count, testCaseId, evaluationRunId, evaluatorTypeId);

            return results.Entities.Select(entity => new EvaluationResult
            {
                Id = entity.GetAttributeValue<Guid>("mcicaps_evaluationresultsid"),
                Score = entity.Contains("mcicaps_score") ? (decimal)entity.GetAttributeValue<double>("mcicaps_score") : (decimal?)null,
                Passed = entity.Contains("mcicaps_passed") ? entity.GetAttributeValue<bool>("mcicaps_passed") : (bool?)null,
                ExecutionTimeMs = entity.Contains("mcicaps_executiontime") ? entity.GetAttributeValue<int>("mcicaps_executiontime") : (int?)null,
                ErrorMessage = entity.Contains("mcicaps_errormessage") ? entity.GetAttributeValue<string>("mcicaps_errormessage") : null,
                IsAggregate = entity.GetAttributeValue<bool>("mcicaps_isaggregate"),
                StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error retrieving turn results for test case {TestCaseId}, run {RunId}, evaluator {EvaluatorId}",
                testCaseId, evaluationRunId, evaluatorTypeId);
            throw;
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
