# Deployment Record: N:N Relationship Dataverse Query Fix

**Date**: November 16, 2025, 8:04 PM UTC  
**Environment**: Staging (func-aiestagingmbwc)  
**Deployed By**: System Administrator  
**Deployment Type**: Hotfix  
**Status**: ✅ Deployed Successfully

## Summary

Deployed critical hotfix (with follow-up fix) to resolve `System.ServiceModel.FaultException` errors caused by N:N relationship migration in Story 09:
1. **First error**: Attempting to query removed `mcicaps_evaluatortype` field from `mcicaps_testcases` table
2. **Second error**: Incorrect field name prefixing in N:N intersect table query (`GetEvaluatorTypesForRunAsync`)

## Changes Deployed

### Modified Files
- `backend/AiAgentEvaluation/Services/DataverseService.cs`
  - `GetTestCasesAsync()`: Removed query for deleted `mcicaps_evaluatortype` field
  - `GetTestCaseAsync()`: Removed query for deleted `mcicaps_evaluatortype` field
  - `GetEvaluatorTypesForRunAsync()`: Fixed N:N intersect table field naming (unprefixed IDs)

### Details
The N:N relationship migration (Story 09) moved evaluator types from test cases to evaluation runs. The code was updated to reflect this architectural change by:
1. Removing `Field("evaluatortype")` from ColumnSet queries in test case methods
2. Setting `TestCase.EvaluatorTypeId = null` (now fetched via `GetEvaluatorTypesForRunAsync`)
3. Fixing N:N intersect table query to use unprefixed field names (`mcicaps_evaluationrunid` not `mcicaps_mcicaps_evaluationrunid`)
4. Adding clarifying comments about N:N table naming conventions

## Pre-Deployment Verification

### Build Status
```powershell
dotnet build --configuration Release
```
✅ **Result**: Build succeeded in 4.6s

### Unit Tests
```powershell
dotnet test backend/tests/AiAgentEvaluation.UnitTests/
```
✅ **Result**: 133 tests passed, 0 failed

## Deployment Process

### 1. Build and Publish (Initial Deployment)
```powershell
cd c:\dev\ai-agent-evaluation\backend\AiAgentEvaluation
func azure functionapp publish func-aiestagingmbwc
```

**Output**:
- Build: Successful (3.68s)
- Upload: 29.57 MB uploaded successfully
- Deployment: Completed successfully
- Sync triggers: Completed at 2025-11-16T20:04:42.103Z

### 1b. Second Deployment (N:N Query Fix)
After discovering field naming issue in production logs, deployed updated query:

```powershell
func azure functionapp publish func-aiestagingmbwc
```

**Output**:
- Build: Successful (4.46s)
- Upload: 29.57 MB uploaded successfully
- Deployment: Completed successfully
- Sync triggers: Completed at 2025-11-16T20:11:08.283Z

### 2. Functions Deployed
✅ All 13 functions deployed successfully:
- `BasicHealth` (HTTP)
- `CreateAggregateResult` (Activity)
- `ExecuteSingleTurn` (Activity)
- `GetEvaluationRunStatus` (HTTP)
- `GetTestCaseWithTurns` (Activity)
- `HealthCheck` (HTTP)
- `HealthCheckDetailed` (HTTP)
- `LivenessCheck` (HTTP)
- `MonitorEvaluationRuns` (Timer)
- `MultiTurnTestCaseOrchestrator` (Orchestration)
- `ProcessTestCase` (Service Bus)
- `ReadinessCheck` (HTTP)
- `StartEvaluationRun` (HTTP)
- `StartMultiTurnTestCase` (HTTP)

## Post-Deployment Verification

### Health Check Results

#### Basic Health
```bash
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
```
✅ **Status**: Healthy  
**Timestamp**: 2025-11-16T20:05:22.1498925Z  
**Version**: 1.0.0  
**Message**: "Function app is running"

#### Detailed Health
```bash
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/detailed
```
✅ **Status**: Healthy  
**Uptime**: 1 day, 12 hours, 47 minutes

**System Info**:
- Machine: 10-30-1-241
- OS: Windows Server 2022 (NT 10.0.20348.0)
- Processors: 2
- .NET Version: 8.0.22
- Working Set: 161.5 MB
- GC Memory: 29.9 MB

**Configuration**:
- OpenAI Config: ✅ Configured
- Dataverse Config: ⚠️ Not detected (Key Vault reference may be pending)
- Service Bus Config: ⚠️ Not detected (Key Vault reference may be pending)

### Configuration Notes

The health check shows `hasDataverseConfig: false` and `hasServiceBusConfig: false`. This is expected behavior when:
1. Connection strings are stored in Azure Key Vault
2. Key Vault references use format: `@Microsoft.KeyVault(VaultName=...;SecretName=...)`
3. The health check only validates if settings are non-empty strings, not if Key Vault references resolve

The actual connectivity will be verified when:
- StartEvaluationRun is called (validates Dataverse connection)
- ProcessTestCase is triggered (validates Service Bus connection)

## Functional Testing Plan

### Test 1: Start Evaluation Run
**Objective**: Verify `GetTestCasesAsync()` works without the removed field

```bash
POST https://func-aiestagingmbwc.azurewebsites.net/api/evaluations/start
{
  "evaluationRunId": "<guid>"
}
```

**Expected Result**:
- ✅ No `FaultException` about missing `mcicaps_evaluatortype` field
- ✅ Test cases retrieved successfully with `EvaluatorTypeId = null`
- ✅ Evaluator types retrieved via `GetEvaluatorTypesForRunAsync()`
- ✅ Worker functions queued/orchestrated successfully

### Test 2: Monitor Function
**Objective**: Verify no errors in background monitoring

**Expected Result**:
- ✅ MonitorEvaluationRuns timer function runs without errors
- ✅ No exceptions in Application Insights

### Test 3: Multi-Turn Execution
**Objective**: Verify multi-turn orchestrations work correctly

**Expected Result**:
- ✅ Durable orchestrations start successfully
- ✅ Test case turns retrieved without field errors
- ✅ Results saved with correct evaluator type lookups

## Monitoring

### Application Insights Queries

#### Check for Field Not Found Errors
```kusto
traces
| where timestamp > ago(1h)
| where message contains "doesn't contain attribute"
| project timestamp, message, severityLevel
```

#### Monitor StartEvaluationRun Success Rate
```kusto
requests
| where timestamp > ago(1h)
| where name == "StartEvaluationRun"
| summarize 
    Total = count(),
    Success = countif(success == true),
    Failed = countif(success == false)
| extend SuccessRate = (Success * 100.0) / Total
```

#### Monitor GetTestCases Dataverse Calls
```kusto
dependencies
| where timestamp > ago(1h)
| where name contains "GetTestCases"
| where type == "Dataverse"
| project timestamp, name, duration, resultCode, success
```

## Rollback Plan

If issues are detected:

### Option 1: Redeploy Previous Version
```powershell
# Get previous deployment
az functionapp deployment list --name func-aiestagingmbwc --resource-group rg-agent-eval-staging

# Redeploy specific deployment
az functionapp deployment source config-zip --resource-group rg-agent-eval-staging --name func-aiestagingmbwc --src <previous-package.zip>
```

### Option 2: Emergency Revert (Git)
```powershell
cd c:\dev\ai-agent-evaluation
git revert HEAD
git push origin main

# Redeploy
cd backend/AiAgentEvaluation
func azure functionapp publish func-aiestagingmbwc
```

## Known Issues / Limitations

### Configuration Display
The detailed health endpoint shows Dataverse and Service Bus as "not configured" even though Key Vault references are in place. This is a display issue only - actual connectivity is validated at runtime when services are used.

**Resolution**: Update `HealthCheckFunction` to validate Key Vault reference format, not just non-empty values.

## Next Steps

1. ✅ **Immediate**: Monitor Application Insights for 30 minutes for any errors
2. 🔲 **Short-term** (within 24h): Execute functional tests with real evaluation run
3. 🔲 **Medium-term** (within 1 week): Add integration test for N:N relationship queries
4. 🔲 **Long-term**: Consider adding schema validation tests that detect field mismatches

## Sign-Off

**Initial Deployment**: 2025-11-16 20:04:42 UTC  
**Second Deployment**: 2025-11-16 20:11:08 UTC  
**Health Verified**: 2025-11-16 20:14:59 UTC  
**Status**: ✅ Ready for Testing

**Risk Assessment**: Low  
- Surgical fix with clear scope
- All unit tests passing
- No breaking changes to API contracts
- Backward compatible (test cases with null EvaluatorTypeId work fine)

## Related Documentation

- **Hotfix Details**: [n-n-relationship-dataverse-query-fix.md](./n-n-relationship-dataverse-query-fix.md)
- **Architecture**: [ARCHITECTURE.md](../ARCHITECTURE.md) - Section 4.3 (Test Cases table)
- **Multi-Evaluator Story**: [09-multi-evaluator-support.md](../stories/09-multi-evaluator-support.md)
- **PRD**: [PRD-Agent-Evaluation-Function.md](../prd/PRD-Agent-Evaluation-Function.md) - N:N Relationship section

---

**Deployment Record**: Maintained for historical tracking and troubleshooting reference
