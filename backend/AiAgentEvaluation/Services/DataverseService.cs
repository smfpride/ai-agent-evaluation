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
    private ServiceClient? _serviceClient;
    private readonly ResilientService _resilientService;
    private string? _connectionError;
    private bool _disposed;
    private DateTime _lastConnectionAttempt = DateTime.MinValue;
    private readonly object _connectionLock = new object();

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

    /// <summary>
    /// Validates that the service client is ready, attempting reconnection if necessary
    /// </summary>
    private void EnsureClientReady()
    {
        // If client is ready, we're good
        if (_serviceClient != null && _serviceClient.IsReady)
        {
            return;
        }

        // If no connection string configured, fail permanently
        if (string.IsNullOrWhiteSpace(_settings.Dataverse.ConnectionString))
        {
            throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
        }

        // Attempt reconnection (with throttling - max once per 30 seconds)
        lock (_connectionLock)
        {
            // Double-check after acquiring lock
            if (_serviceClient != null && _serviceClient.IsReady)
            {
                return;
            }

            // Throttle reconnection attempts to prevent overwhelming the service
            var timeSinceLastAttempt = DateTime.UtcNow - _lastConnectionAttempt;
            if (timeSinceLastAttempt.TotalSeconds < 30)
            {
                throw new InvalidOperationException($"Dataverse service client is not ready. Last connection attempt was {timeSinceLastAttempt.TotalSeconds:F0}s ago. Connection error: {_connectionError}");
            }

            _lastConnectionAttempt = DateTime.UtcNow;
            _logger.LogWarning("Attempting to reconnect to Dataverse after connection failure. Last error: {Error}", _connectionError);

            try
            {
                // Dispose old client if exists
                if (_serviceClient != null)
                {
                    _serviceClient.Dispose();
                    _serviceClient = null;
                }

                // Create new client
                _serviceClient = new ServiceClient(_settings.Dataverse.ConnectionString);

                if (!_serviceClient.IsReady)
                {
                    var detailedError = _serviceClient.LastError ?? "Unknown error";
                    _connectionError = $"Failed to reconnect to Dataverse: {detailedError}";
                    
                    _logger.LogError("Dataverse reconnection failed. Error: {Error}, LastException: {LastException}", 
                        detailedError, _serviceClient.LastException?.ToString() ?? "None");
                    
                    _serviceClient.Dispose();
                    _serviceClient = null;
                    
                    throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}");
                }

                _logger.LogInformation("Successfully reconnected to Dataverse. Org: {OrgName}", 
                    _serviceClient.ConnectedOrgFriendlyName);
                
                // Clear error on successful reconnection
                _connectionError = null;
            }
            catch (Exception ex)
            {
                _connectionError = $"Error reconnecting to Dataverse: {ex.Message}";
                _logger.LogError(ex, "Failed to reconnect to Dataverse");
                
                if (_serviceClient != null)
                {
                    _serviceClient.Dispose();
                    _serviceClient = null;
                }
                
                throw new InvalidOperationException($"Dataverse service client is not ready. Connection error: {_connectionError}", ex);
            }
        }
    }

    /// <summary>
    /// Generic pagination helper that retrieves all pages of results
    /// </summary>
    private async Task<List<Entity>> RetrieveAllPagesAsync(
        QueryExpression query,
        CancellationToken cancellationToken = default)
    {
        var allEntities = new List<Entity>();
        EntityCollection result;
        
        if (query.PageInfo == null)
        {
            query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
        }
        
        do
        {
            result = await Task.Run(() => _serviceClient!.RetrieveMultiple(query), cancellationToken);
            allEntities.AddRange(result.Entities);
            
            if (result.MoreRecords)
            {
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        } while (result.MoreRecords);
        
        return allEntities;
    }

    /// <summary>
    /// Generic retrieve method with error handling for not found scenarios
    /// </summary>
    private async Task<Entity?> RetrieveEntityAsync(
        string entityName,
        Guid id,
        ColumnSet columnSet,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() => _serviceClient!.Retrieve(entityName, id, columnSet), cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("does not exist") || ex.Message.Contains("not found"))
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a Dataverse entity to an EvaluationResult model (full mapping)
    /// </summary>
    private EvaluationResult MapToEvaluationResult(Entity entity)
    {
        return new EvaluationResult
        {
            Id = entity.Id,
            Name = entity.GetAttributeValue<string>(Prefixed("evaluationresultname")) ?? string.Empty,
            EvaluationRunId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluation"))?.Id ?? Guid.Empty,
            TestCaseId = entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty,
            EvaluatorTypeId = entity.Contains(Prefixed("evaluatortypeid")) 
                ? entity.GetAttributeValue<EntityReference>(Prefixed("evaluatortypeid"))?.Id 
                : null,
            TestCaseTurnId = entity.Contains(Prefixed("testcaseturnid"))
                ? entity.GetAttributeValue<EntityReference>(Prefixed("testcaseturnid"))?.Id
                : null,
            IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
            ActualResponse = entity.Contains(Prefixed("actualresponse")) 
                ? entity.GetAttributeValue<string>(Prefixed("actualresponse"))
                : null,
            Score = GetDecimalValue(entity, Prefixed("score")),
            Passed = entity.Contains(Prefixed("passed")) 
                ? entity.GetAttributeValue<bool?>(Prefixed("passed"))
                : null,
            Reasoning = entity.Contains(Prefixed("reasoning"))
                ? entity.GetAttributeValue<string>(Prefixed("reasoning"))
                : null,
            ConversationId = entity.Contains(Prefixed("conversationid"))
                ? entity.GetAttributeValue<string>(Prefixed("conversationid"))
                : null,
            ExecutionTimeMs = entity.Contains(Prefixed("executiontime")) 
                ? entity.GetAttributeValue<int?>(Prefixed("executiontime"))
                : null,
            McsExecutionTimeMs = entity.Contains(Prefixed("mcsexecutiontime"))
                ? entity.GetAttributeValue<int?>(Prefixed("mcsexecutiontime"))
                : null,
            ErrorMessage = entity.Contains(Prefixed("errormessage"))
                ? entity.GetAttributeValue<string>(Prefixed("errormessage"))
                : null,
            Metadata = entity.Contains(Prefixed("metadata"))
                ? entity.GetAttributeValue<string>(Prefixed("metadata"))
                : null,
            StateCode = GetIntValue(entity, "statecode"),
            StatusCode = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1,
            CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
            ModifiedOn = entity.Contains("modifiedon") 
                ? entity.GetAttributeValue<DateTime>("modifiedon")
                : default
        };
    }

    public async Task<EvaluationRun?> GetEvaluationRunAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation run {EvaluationRunId}", evaluationRunId);
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var columnSet = new ColumnSet(
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
            );

            var entity = await RetrieveEntityAsync(Prefixed("evaluationruns"), evaluationRunId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found", evaluationRunId);
                return null;
            }
            
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
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // First, get the evaluation run to retrieve the test set
            var evaluationRunResult = await Task.Run(() => _serviceClient!.Retrieve(Prefixed("evaluationruns"), evaluationRunId, new ColumnSet(Prefixed("testset"))), ct);
            
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

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            var testCases = allEntities.Select(entity =>
            {
                var isMultiTurn = entity.GetAttributeValue<bool>(Prefixed("ismultiturn"));
                var testCaseId = entity.Id;
                var testCaseName = entity.GetAttributeValue<string>(Prefixed("testcasename")) ?? string.Empty;
                
                _logger.LogWarning("[DATAVERSE] Test case {TestCaseId} ({Name}): ismultiturn field = {IsMultiTurn}, Contains field = {Contains}",
                    testCaseId, testCaseName, isMultiTurn, entity.Contains(Prefixed("ismultiturn")));
                
                return new TestCase
                {
                    Id = testCaseId,
                    Name = testCaseName,
                    EvaluationRunId = evaluationRunId,
                    UserInput = entity.GetAttributeValue<string>(Prefixed("prompt")) ?? string.Empty,
                    ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")) ?? string.Empty,
                    EvaluatorTypeId = null, // Evaluator types are now fetched via GetEvaluatorTypesForRunAsync (N:N relationship)
                    ConversationId = null, // ConversationId is on EvaluationResult, not TestCase
                    IsMultiTurn = isMultiTurn,
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon")
                };
            }).ToList();
            
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
        EnsureClientReady();

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

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} status to {StatusCode}", 
                evaluationRunId, statusCode);
        }, "UpdateEvaluationRunStatus",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task SetEvaluationRunTotalTestCasesAsync(Guid evaluationRunId, int totalTestCases, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting evaluation run {EvaluationRunId} total test cases to {TotalTestCases}", evaluationRunId, totalTestCases);
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("totaltestcases")] = totalTestCases;
            // Initialize counters to 0
            updateEntity[Prefixed("completedtestcases")] = 0;
            updateEntity[Prefixed("failedtestcases")] = 0;
            updateEntity[Prefixed("progresspercentage")] = 0.00m;

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
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
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            // Retrieve current counts and total test cases
            var columnSet = new ColumnSet(
                Prefixed("totaltestcases"),
                Prefixed("completedtestcases"),
                Prefixed("failedtestcases"),
                Prefixed("natestcases"));
            var entity = await RetrieveEntityAsync(Prefixed("evaluationruns"), evaluationRunId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress update", evaluationRunId);
                return;
            }

            var totalTestCases = entity.GetAttributeValue<int>(Prefixed("totaltestcases"));
            var currentCompletedCount = entity.GetAttributeValue<int>(Prefixed("completedtestcases"));
            var currentFailedCount = entity.GetAttributeValue<int>(Prefixed("failedtestcases"));
            var currentNACount = entity.GetAttributeValue<int>(Prefixed("natestcases"));

            // Check if counts have actually changed
            var countsChanged = completedTestCases != currentCompletedCount ||
                               failedTestCases != currentFailedCount ||
                               notApplicableTestCases != currentNACount;

            if (!countsChanged)
            {
                _logger.LogDebug(
                    "No progress change for {EvaluationRunId}: completed={Completed}, failed={Failed}, NA={NA}",
                    evaluationRunId, completedTestCases, failedTestCases, notApplicableTestCases);
                return; // No update needed - preserves LastProgressUpdate for stall detection
            }

            _logger.LogInformation(
                "Progress changed for {EvaluationRunId}: completed {OldCompleted}->{NewCompleted}, " +
                "failed {OldFailed}->{NewFailed}, NA {OldNA}->{NewNA}",
                evaluationRunId, currentCompletedCount, completedTestCases, 
                currentFailedCount, failedTestCases, currentNACount, notApplicableTestCases);

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

            // Update last progress timestamp ONLY when counts actually change
            updateEntity[Prefixed("lastprogressupdate")] = DateTime.UtcNow;

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} progress", evaluationRunId);
        }, "UpdateEvaluationRunProgress",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunScoresAsync(Guid evaluationRunId, decimal? overallScore, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} scores", evaluationRunId);
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            
            // Convert decimal to double for Float fields in Dataverse
            if (overallScore.HasValue)
                updateEntity[Prefixed("overallscore")] = Convert.ToDouble(overallScore.Value);

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} scores", evaluationRunId);
        }, "UpdateEvaluationRunScores",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<Guid> CreateEvaluationResultAsync(CreateEvaluationResultRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating evaluation result for test case {TestCaseId}", request.TestCaseId);
        EnsureClientReady();

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

            var resultId = await Task.Run(() => _serviceClient!.Create(resultEntity), ct);
            
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
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var columnSet = new ColumnSet(
                Prefixed("testcasesid"),
                Prefixed("testcasename"),
                Prefixed("prompt"),
                Prefixed("expectedresponse"),
                "createdon"
            );

            var entity = await RetrieveEntityAsync(Prefixed("testcases"), testCaseId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Test case {TestCaseId} not found", testCaseId);
                return null;
            }
            
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
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("starttime")] = startTime;

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} start time", evaluationRunId);
        }, "SetEvaluationRunStartTime",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task SetEvaluationRunEndTimeAsync(Guid evaluationRunId, DateTime endTime, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting evaluation run {EvaluationRunId} end time to {EndTime}", evaluationRunId, endTime);
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationruns"), evaluationRunId);
            updateEntity[Prefixed("endtime")] = endTime;

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully set evaluation run {EvaluationRunId} end time", evaluationRunId);
        }, "SetEvaluationRunEndTime",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluationRun>> GetRunningEvaluationRunsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting running evaluation runs");
        EnsureClientReady();

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
                },
                PageInfo = new PagingInfo { Count = 1000, PageNumber = 1 }
            };

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            var evaluationRuns = allEntities.Select(entity => new EvaluationRun
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
            }).ToList();
            
            _logger.LogInformation("Retrieved {Count} running evaluation runs", evaluationRuns.Count);
            
            return evaluationRuns;
        }, "GetRunningEvaluationRuns",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<List<EvaluationResult>> GetEvaluationResultsAsync(Guid evaluationRunId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation results for evaluation run {EvaluationRunId}", evaluationRunId);
        EnsureClientReady();

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
                },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            var evaluationResults = allEntities.Select(MapToEvaluationResult).ToList();
            
            _logger.LogInformation("Retrieved {Count} evaluation results for evaluation run {EvaluationRunId}", 
                evaluationResults.Count, evaluationRunId);
            
            return evaluationResults;
        }, "GetEvaluationResults",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    /// <summary>
    /// Efficiently gets evaluation result counts without loading all records into memory.
    /// Uses aggregate queries to count directly in Dataverse.
    /// </summary>
    public async Task<(int completedCount, int failedCount, int notApplicableCount)> GetEvaluationResultCountsAsync(
        Guid evaluationRunId, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation result counts for evaluation run {EvaluationRunId}", evaluationRunId);
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Build filter for test-case-level results only (exclude individual turn results)
            // Multi-turn: IsAggregate = true (aggregate result for entire sequence)
            // Single-turn: TestCaseTurnId = null AND IsAggregate = false
            var testCaseFilter = new FilterExpression(LogicalOperator.Or);
            testCaseFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, true);
            
            var singleTurnFilter = new FilterExpression(LogicalOperator.And);
            singleTurnFilter.AddCondition(Prefixed("testcaseturnid"), ConditionOperator.Null);
            singleTurnFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, false);
            testCaseFilter.AddFilter(singleTurnFilter);

            // Count completed results (statuscode = 778760002 = Completed)
            var completedQuery = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(Prefixed("evaluationresultsid")),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0), // Active
                        new ConditionExpression("statuscode", ConditionOperator.Equal, 778760002) // Completed
                    }
                }
            };
            completedQuery.Criteria.AddFilter(testCaseFilter);

            // Count errored results (statuscode = 778760003 = Errored)
            var failedQuery = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(Prefixed("evaluationresultsid")),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                        new ConditionExpression("statuscode", ConditionOperator.Equal, 778760003) // Errored
                    }
                }
            };
            var failedTestCaseFilter = new FilterExpression(LogicalOperator.Or);
            failedTestCaseFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, true);
            var failedSingleTurnFilter = new FilterExpression(LogicalOperator.And);
            failedSingleTurnFilter.AddCondition(Prefixed("testcaseturnid"), ConditionOperator.Null);
            failedSingleTurnFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, false);
            failedTestCaseFilter.AddFilter(failedSingleTurnFilter);
            failedQuery.Criteria.AddFilter(failedTestCaseFilter);

            // Count N/A results (statuscode = 778760004 = Not Applicable)
            var naQuery = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(Prefixed("evaluationresultsid")),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0),
                        new ConditionExpression("statuscode", ConditionOperator.Equal, 778760004) // NotApplicable
                    }
                }
            };
            var naTestCaseFilter = new FilterExpression(LogicalOperator.Or);
            naTestCaseFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, true);
            var naSingleTurnFilter = new FilterExpression(LogicalOperator.And);
            naSingleTurnFilter.AddCondition(Prefixed("testcaseturnid"), ConditionOperator.Null);
            naSingleTurnFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, false);
            naTestCaseFilter.AddFilter(naSingleTurnFilter);
            naQuery.Criteria.AddFilter(naTestCaseFilter);

            // Execute all counts in parallel using RetrieveMultiple with top=1 and count total records
            var completedTask = Task.Run(() =>
            {
                completedQuery.PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true };
                var result = _serviceClient!.RetrieveMultiple(completedQuery);
                return result.TotalRecordCount;
            }, ct);

            var failedTask = Task.Run(() =>
            {
                failedQuery.PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true };
                var result = _serviceClient!.RetrieveMultiple(failedQuery);
                return result.TotalRecordCount;
            }, ct);

            var naTask = Task.Run(() =>
            {
                naQuery.PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true };
                var result = _serviceClient!.RetrieveMultiple(naQuery);
                return result.TotalRecordCount;
            }, ct);

            await Task.WhenAll(completedTask, failedTask, naTask);

            var completedCount = completedTask.Result;
            var failedCount = failedTask.Result;
            var notApplicableCount = naTask.Result;

            _logger.LogInformation(
                "Result counts for {EvaluationRunId}: completed={Completed}, failed={Failed}, notApplicable={NotApplicable}",
                evaluationRunId, completedCount, failedCount, notApplicableCount);

            return (completedCount, failedCount, notApplicableCount);
        }, "GetEvaluationResultCounts",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    /// <summary>
    /// Efficiently gets count of evaluation results for a specific status code without loading records into memory.
    /// Uses ReturnTotalRecordCount to get count without fetching data.
    /// </summary>
    public async Task<int> GetEvaluationResultCountAsync(
        Guid evaluationRunId,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluation result count for run {EvaluationRunId} with status {StatusCode}", 
            evaluationRunId, statusCode);
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Build filter for test-case-level results only (exclude individual turn results)
            var testCaseFilter = new FilterExpression(LogicalOperator.Or);
            testCaseFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, true);
            
            var singleTurnFilter = new FilterExpression(LogicalOperator.And);
            singleTurnFilter.AddCondition(Prefixed("testcaseturnid"), ConditionOperator.Null);
            singleTurnFilter.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, false);
            testCaseFilter.AddFilter(singleTurnFilter);

            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(Prefixed("evaluationresultsid")),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0), // Active
                        new ConditionExpression("statuscode", ConditionOperator.Equal, statusCode)
                    }
                },
                PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true }
            };
            query.Criteria.AddFilter(testCaseFilter);

            var result = await Task.Run(() => _serviceClient!.RetrieveMultiple(query), ct);
            var count = result.TotalRecordCount;

            _logger.LogInformation("Found {Count} results for run {EvaluationRunId} with status {StatusCode}", 
                count, evaluationRunId, statusCode);

            return count;
        }, "GetEvaluationResultCount",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task UpdateEvaluationRunCompletionAsync(Guid evaluationRunId, int statusCode, decimal? overallScore, bool? overallPass, DateTime endTime, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating evaluation run {EvaluationRunId} completion: status={StatusCode}, score={OverallScore}, pass={OverallPass}", 
            evaluationRunId, statusCode, overallScore, overallPass);
        EnsureClientReady();

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

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated evaluation run {EvaluationRunId} completion", evaluationRunId);
        }, "UpdateEvaluationRunCompletion",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task IncrementCompletedTestCasesAsync(Guid evaluationRunId, bool failed, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating test case counts for evaluation run {EvaluationRunId} based on actual results", 
            evaluationRunId);
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            // Get counts efficiently without loading all results into memory
            var (completedCount, failedCount, notApplicableCount) = await GetEvaluationResultCountsAsync(evaluationRunId, ct);
            
            _logger.LogInformation("Retrieved counts for {EvaluationRunId}: completed={Completed}, failed={Failed}, notApplicable={NotApplicable}", 
                evaluationRunId, completedCount, failedCount, notApplicableCount);

            // Retrieve current counts and total test cases
            var columnSet = new ColumnSet(
                Prefixed("totaltestcases"),
                Prefixed("completedtestcases"),
                Prefixed("failedtestcases"),
                Prefixed("natestcases"));
            var entity = await RetrieveEntityAsync(Prefixed("evaluationruns"), evaluationRunId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Evaluation run {EvaluationRunId} not found for progress update", evaluationRunId);
                return;
            }

            var totalTestCases = entity.GetAttributeValue<int>(Prefixed("totaltestcases"));
            var currentCompletedCount = entity.GetAttributeValue<int>(Prefixed("completedtestcases"));
            var currentFailedCount = entity.GetAttributeValue<int>(Prefixed("failedtestcases"));
            var currentNACount = entity.GetAttributeValue<int>(Prefixed("natestcases"));

            // Check if counts have actually changed
            var countsChanged = completedCount != currentCompletedCount ||
                               failedCount != currentFailedCount ||
                               notApplicableCount != currentNACount;

            if (!countsChanged)
            {
                _logger.LogDebug(
                    "No progress change for {EvaluationRunId}: completed={Completed}, failed={Failed}, NA={NA}",
                    evaluationRunId, completedCount, failedCount, notApplicableCount);
                return; // No update needed - preserves LastProgressUpdate for stall detection
            }

            _logger.LogInformation(
                "Progress changed for {EvaluationRunId}: completed {OldCompleted}->{NewCompleted}, " +
                "failed {OldFailed}->{NewFailed}, NA {OldNA}->{NewNA}",
                evaluationRunId, currentCompletedCount, completedCount, 
                currentFailedCount, failedCount, currentNACount, notApplicableCount);

            // Update with new counts
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

            // Update last progress timestamp ONLY when counts actually change
            updateEntity[Prefixed("lastprogressupdate")] = DateTime.UtcNow;

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
            _logger.LogInformation("Successfully updated test case counts for evaluation run {EvaluationRunId} from actual results", 
                evaluationRunId);
        }, "IncrementCompletedTestCases",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<EvaluatorType?> GetEvaluatorTypeAsync(Guid evaluatorTypeId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting evaluator type {EvaluatorTypeId}", evaluatorTypeId);
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var columnSet = new ColumnSet(
                Prefixed("evaluatortypeid"),
                Prefixed("name"),
                Prefixed("evaluatorprompt"),
                Prefixed("requiresexpectedresponse"),
                Prefixed("judgeagentid"),
                "statecode",
                "statuscode",
                "createdon",
                "modifiedon"
            );

            var entity = await RetrieveEntityAsync(Prefixed("evaluatortype"), evaluatorTypeId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Evaluator type {EvaluatorTypeId} not found", evaluatorTypeId);
                return null;
            }
            
            // Check if active
            var stateCode = GetIntValue(entity, "statecode");
            if (stateCode != 0)
            {
                _logger.LogWarning("Evaluator type {EvaluatorTypeId} is not active (statecode={StateCode})", evaluatorTypeId, stateCode);
                return null;
            }
            
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
        EnsureClientReady();

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

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            _logger.LogInformation("Found {Count} evaluator types for evaluation run {EvaluationRunId}", 
                allEntities.Count, evaluationRunId);

            return allEntities.Select(entity => new EvaluatorType
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
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var columnSet = new ColumnSet(
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
            );

            var entity = await RetrieveEntityAsync(Prefixed("judgeagent"), judgeAgentId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Judge agent {JudgeAgentId} not found", judgeAgentId);
                return null;
            }
            
            // Check if active
            var stateCode = GetIntValue(entity, "statecode");
            if (stateCode != 0)
            {
                _logger.LogWarning("Judge agent {JudgeAgentId} is not active (statecode={StateCode})", judgeAgentId, stateCode);
                return null;
            }
            
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
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            var columnSet = new ColumnSet(
                Prefixed("agenttoevalid"),
                Prefixed("name"),
                Prefixed("directlinespeechurl"),
                "statecode",
                "statuscode",
                "createdon",
                "modifiedon"
            );

            var entity = await RetrieveEntityAsync(Prefixed("agenttoeval"), agentToEvalId, columnSet, ct);
            
            if (entity == null)
            {
                _logger.LogWarning("Agent to eval {AgentToEvalId} not found", agentToEvalId);
                return null;
            }
            
            // Check if active
            var stateCode = entity.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0;
            if (stateCode != 0)
            {
                _logger.LogWarning("Agent to eval {AgentToEvalId} is not active (statecode={StateCode})", agentToEvalId, stateCode);
                return null;
            }
            
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
        EnsureClientReady();

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
                },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            var turns = allEntities.Select(entity =>
            {
                var testCaseRef = entity.GetAttributeValue<EntityReference>(Prefixed("testcaseid"));
                var promptText = entity.GetAttributeValue<string>(Prefixed("prompttext")) ?? string.Empty;
                var sequence = entity.GetAttributeValue<int>(Prefixed("sequence"));
                
                _logger.LogInformation("Retrieved turn from Dataverse: sequence={Sequence}, promptText='{PromptText}'", 
                    sequence, promptText);
                
                return new TestCaseTurn
                {
                    Id = entity.Id,
                    Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                    TestCaseId = testCaseRef?.Id ?? testCaseId,
                    Sequence = sequence,
                    PromptText = promptText,
                    ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")),
                    CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                    ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
                };
            }).ToList();
            
            _logger.LogInformation("Retrieved {Count} turns for test case {TestCaseId}", 
                turns.Count, testCaseId);
            
            return turns;
        }, "GetTestCaseTurns",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    public async Task<Dictionary<Guid, List<TestCaseTurn>>> GetAllTurnsForTestCasesAsync(
        Guid testSetId, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Batch-fetching turns for test cases in test set {TestSetId}", testSetId);
        EnsureClientReady();

        return await _resilientService.ExecuteAsync(async ct =>
        {
            // Build query joining testcaseturn to testcases via testcaseid
            // This avoids the IN operator which can fail with large lists
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
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0) // Active only
                    }
                },
                Orders =
                {
                    new OrderExpression(Prefixed("testcaseid"), OrderType.Ascending),
                    new OrderExpression(Prefixed("sequence"), OrderType.Ascending)
                },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            
            // Add link to testcases table to filter by testset
            var testCaseLink = query.AddLink(
                Prefixed("testcases"),
                Prefixed("testcaseid"),
                Prefixed("testcasesid"),
                JoinOperator.Inner);
            testCaseLink.LinkCriteria.AddCondition(Prefixed("testset"), ConditionOperator.Equal, testSetId);
            testCaseLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active test cases only

            // Handle pagination using helper
            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            _logger.LogInformation("Retrieved {TotalTurns} turns for test set {TestSetId}", 
                allEntities.Count, testSetId);
            
            // Group turns by test case ID
            // LINQ grouping is more efficient and readable than parallelization here
            // This is in-memory object construction (no I/O), grouping is O(n), and we avoid
            // lock contention on the Dictionary that would be required for parallel writes
            var turnsByTestCase = allEntities
                .Select(entity =>
                {
                    var testCaseRef = entity.GetAttributeValue<EntityReference>(Prefixed("testcaseid"));
                    var testCaseId = testCaseRef?.Id ?? Guid.Empty;
                    
                    if (testCaseId == Guid.Empty)
                    {
                        _logger.LogWarning("Turn {TurnId} has invalid test case reference", entity.Id);
                        return null;
                    }
                    
                    return new TestCaseTurn
                    {
                        Id = entity.Id,
                        Name = entity.GetAttributeValue<string>(Prefixed("name")) ?? string.Empty,
                        TestCaseId = testCaseId,
                        Sequence = entity.GetAttributeValue<int>(Prefixed("sequence")),
                        PromptText = entity.GetAttributeValue<string>(Prefixed("prompttext")) ?? string.Empty,
                        ExpectedResponse = entity.GetAttributeValue<string>(Prefixed("expectedresponse")),
                        CreatedOn = entity.GetAttributeValue<DateTime>("createdon"),
                        ModifiedOn = entity.GetAttributeValue<DateTime>("modifiedon")
                    };
                })
                .Where(turn => turn != null)
                .Cast<TestCaseTurn>()
                .GroupBy(turn => turn.TestCaseId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList()
                );
            
            _logger.LogInformation("Retrieved {TotalTurns} turns for {TestCaseCount} test cases", 
                allEntities.Count, turnsByTestCase.Count);
            
            return turnsByTestCase;
        }, "GetAllTurnsForTestCases",
        exception => exception is FaultException<OrganizationServiceFault>,
        cancellationToken);
    }

    /// <summary>
    /// Gets the first aggregate result for a test case in an evaluation run.
    /// Legacy method for single-evaluator scenarios - delegates to GetAggregateResultsForTestCaseAsync.
    /// </summary>
    public async Task<EvaluationResult?> GetAggregateResultForTestCaseAsync(
        Guid evaluationRunId, 
        Guid testCaseId, 
        CancellationToken cancellationToken = default)
    {
        var results = await GetAggregateResultsForTestCaseAsync(evaluationRunId, testCaseId, cancellationToken);
        return results.FirstOrDefault();
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
        EnsureClientReady();

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
                },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            var allEntities = await RetrieveAllPagesAsync(query, ct);
            
            _logger.LogInformation("Found {Count} aggregate results for test case {TestCaseId} in evaluation run {EvaluationRunId}", 
                allEntities.Count, testCaseId, evaluationRunId);

            return allEntities.Select(entity => new EvaluationResult
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
        EnsureClientReady();

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
                },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };

            // Handle pagination using helper
            var allEntities = await RetrieveAllPagesAsync(query, cancellationToken);
            
            _logger.LogInformation("Fetched {Count} aggregate results for evaluation run {EvaluationRunId}", 
                allEntities.Count, evaluationRunId);

            return allEntities.Select(entity => new EvaluationResult
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
        EnsureClientReady();

        await _resilientService.ExecuteAsync(async ct =>
        {
            var updateEntity = new Entity(Prefixed("evaluationresults"), resultId);
            
            // Convert decimal to double for Float field in Dataverse
            updateEntity[Prefixed("score")] = Convert.ToDouble(score);
            updateEntity[Prefixed("passed")] = passed;
            updateEntity[Prefixed("reasoning")] = reasoning;
            updateEntity[Prefixed("executiontime")] = executionTimeMs;
            updateEntity["statuscode"] = new OptionSetValue(statusCode);

            await Task.Run(() => _serviceClient!.Update(updateEntity), ct);
            
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
            EnsureClientReady();

            var columnSet = new ColumnSet(
                Prefixed("evaluationresultsid"),
                Prefixed("evaluationresultname"),
                Prefixed("evaluation"),
                Prefixed("testcase"),
                Prefixed("evaluatortypeid"),
                Prefixed("testcaseturnid"),
                Prefixed("isaggregate"),
                Prefixed("actualresponse"),
                Prefixed("score"),
                Prefixed("passed"),
                Prefixed("reasoning"),
                Prefixed("conversationid"),
                Prefixed("executiontime"),
                Prefixed("mcsexecutiontime"),
                Prefixed("errormessage"),
                Prefixed("metadata"),
                "statecode",
                "statuscode",
                "createdon",
                "modifiedon"
            );

            var entity = await RetrieveEntityAsync(Prefixed("evaluationresults"), resultId, columnSet, cancellationToken);
            
            if (entity == null)
            {
                _logger.LogWarning("Evaluation result {ResultId} not found", resultId);
                return null;
            }
            
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
            var entity = new Entity(Prefixed("evaluationresults"))
            {
                Id = resultId
            };

            entity["statuscode"] = new OptionSetValue(request.StatusCode);

            if (request.Score.HasValue)
                entity[Prefixed("score")] = request.Score.Value;

            if (request.Passed.HasValue)
                entity[Prefixed("passed")] = request.Passed.Value;

            if (!string.IsNullOrWhiteSpace(request.Reasoning))
                entity[Prefixed("reasoning")] = request.Reasoning;

            if (request.ExecutionTimeMs.HasValue)
                entity[Prefixed("executiontime")] = request.ExecutionTimeMs.Value;

            if (!string.IsNullOrWhiteSpace(request.ErrorMessage))
                entity[Prefixed("errormessage")] = request.ErrorMessage;

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
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("evaluation"),
                    Prefixed("testcase"),
                    Prefixed("evaluatortypeid"),
                    Prefixed("isaggregate"),
                    "statuscode"
                ),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { Count = 500, PageNumber = 1 } // Limit page size to avoid timeout
            };

            // Filter: IsAggregate = true AND StatusCode = Pending (1)
            query.Criteria.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, EvaluationResultStatus.Pending);

            var allResults = await RetrieveAllPagesAsync(query, cancellationToken);
            
            _logger.LogInformation(
                "Found {Count} pending aggregate results",
                allResults.Count);

            return allResults.Select(entity => new EvaluationResult
            {
                Id = entity.GetAttributeValue<Guid>(Prefixed("evaluationresultsid")),
                EvaluationRunId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluation"))?.Id ?? Guid.Empty,
                TestCaseId = entity.GetAttributeValue<EntityReference>(Prefixed("testcase"))?.Id ?? Guid.Empty,
                EvaluatorTypeId = entity.GetAttributeValue<EntityReference>(Prefixed("evaluatortypeid"))?.Id,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
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
            
            var query = new QueryExpression(Prefixed("evaluationresults"))
            {
                ColumnSet = new ColumnSet(
                    Prefixed("evaluationresultsid"),
                    Prefixed("score"),
                    Prefixed("passed"),
                    Prefixed("executiontime"),
                    Prefixed("errormessage"),
                    Prefixed("isaggregate"),
                    "statuscode"
                ),
                Criteria = new FilterExpression(LogicalOperator.And),
                PageInfo = new PagingInfo { Count = 1000, PageNumber = 1 }
            };

            // Filter: TestCaseId AND EvaluationRunId AND EvaluatorTypeId AND IsAggregate = false
            // For lookup fields, use the relationship name directly
            query.Criteria.AddCondition(Prefixed("testcase"), ConditionOperator.Equal, testCaseId);
            query.Criteria.AddCondition(Prefixed("evaluation"), ConditionOperator.Equal, evaluationRunId);
            query.Criteria.AddCondition(Prefixed("evaluatortypeid"), ConditionOperator.Equal, evaluatorTypeId);
            query.Criteria.AddCondition(Prefixed("isaggregate"), ConditionOperator.Equal, false);

            var allResults = await RetrieveAllPagesAsync(query, cancellationToken);
            
            _logger.LogInformation(
                "Found {Count} turn results for test case {TestCaseId}, run {RunId}, evaluator {EvaluatorId}",
                allResults.Count, testCaseId, evaluationRunId, evaluatorTypeId);

            return allResults.Select(entity => new EvaluationResult
            {
                Id = entity.GetAttributeValue<Guid>(Prefixed("evaluationresultsid")),
                Score = entity.Contains(Prefixed("score")) ? (decimal)entity.GetAttributeValue<double>(Prefixed("score")) : (decimal?)null,
                Passed = entity.Contains(Prefixed("passed")) ? entity.GetAttributeValue<bool>(Prefixed("passed")) : (bool?)null,
                ExecutionTimeMs = entity.Contains(Prefixed("executiontime")) ? entity.GetAttributeValue<int>(Prefixed("executiontime")) : (int?)null,
                ErrorMessage = entity.Contains(Prefixed("errormessage")) ? entity.GetAttributeValue<string>(Prefixed("errormessage")) : null,
                IsAggregate = entity.GetAttributeValue<bool>(Prefixed("isaggregate")),
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
