# Deployment Guide: Last-Turn Aggregate Optimization

**Feature**: Event-driven multi-turn aggregate calculation  
**Version**: 1.0.70  
**Deployment Date**: TBD  
**Branch**: `copilot/optimize-last-turn-aggregate`

## Overview

This deployment implements the last-turn aggregate optimization feature that eliminates the 30-second delay in multi-turn aggregate calculations by triggering them immediately when the final turn's evaluation completes.

### Key Changes

1. **EvaluatorJobMessage Enhancement**: Added multi-turn metadata fields (IsMultiTurn, TestCaseId, EvaluatorTypeId, TurnSequence, TotalTurns)
2. **MultiTurnAgentWorkerFunction**: Sets multi-turn flags when queuing evaluator jobs
3. **EvaluatorWorkerFunction**: Checks for completion after every turn and triggers aggregate calculation
4. **DataverseService**: New methods for checking turn completion and calculating aggregates
5. **AggregateEvaluationProcessorFunction**: Remains as fallback safety net with updated logging

### Benefits

- **Faster Results**: Aggregates calculated immediately instead of waiting up to 30 seconds
- **Better User Experience**: Near real-time feedback on multi-turn test completion
- **Reduced Resource Usage**: Timer function only processes edge cases
- **Improved Observability**: Enhanced logging for aggregate calculation triggers

---

## Pre-Deployment Verification

### 1. Code Quality Checks

**Build Verification**:
```powershell
cd c:\dev\ai-agent-evaluation\backend\AiAgentEvaluation
dotnet build --configuration Release
```
Expected: Build succeeds with zero errors

**Unit Tests**:
```powershell
cd c:\dev\ai-agent-evaluation\backend\tests\AiAgentEvaluation.UnitTests
dotnet test --configuration Release
```
Expected: All 125+ tests pass

**Integration Tests** (optional, requires Azure resources):
```powershell
cd c:\dev\ai-agent-evaluation\backend\tests\AiAgentEvaluation.IntegrationTests
dotnet test --configuration Release
```

### 2. Configuration Review

**No new configuration settings required** - feature uses existing infrastructure:
- ✅ Service Bus connection (evaluator-processing-queue already exists)
- ✅ Dataverse connection (no schema changes required)
- ✅ Application Insights (existing logging infrastructure)

**Verify existing settings in Azure**:
```powershell
az functionapp config appsettings list --name func-aiestagingmbwc --resource-group rg-agent-eval-staging
```

Required settings should include:
- `ServiceBus__Namespace`
- `DataverseConnectionString` (from Key Vault)
- `APPLICATIONINSIGHTS_CONNECTION_STRING`

### 3. Branch Status

**Verify branch is clean and up to date**:
```powershell
cd c:\dev\ai-agent-evaluation
git status
git log --oneline -5
```

Current HEAD: `b4f89b3` - "Address code review feedback: enhance comments, add test for race condition"

**Files Changed** (9 files, 1081 insertions, 24 deletions):
- `AggregateEvaluationProcessorFunction.cs` (+4/-0)
- `EvaluatorWorkerFunction.cs` (+77 new lines)
- `MultiTurnAgentWorkerFunction.cs` (+17/-2)
- `ApiModels.cs` (+34 new fields)
- `DataverseService.cs` (+229 new methods)
- `IDataverseService.cs` (+48 interface definitions)
- Test files (+658 new tests)

### 4. Backward Compatibility

**This deployment is backward compatible**:
- ✅ New fields in `EvaluatorJobMessage` have default values (IsMultiTurn = false)
- ✅ Existing single-turn evaluation flow unchanged
- ✅ Timer function remains operational as fallback
- ✅ No database schema changes required

**In-flight evaluations**: Safe to deploy - existing queue messages will be processed correctly with default values.

---

## Deployment Steps

### Step 1: Update Version Numbers

**✅ COMPLETED** - Version updated to 1.0.70 in:
- `backend/AiAgentEvaluation/Functions/HealthCheckFunction.cs`
- `backend/AiAgentEvaluation/Functions/BasicHealthFunction.cs`

### Step 2: Build and Package

```powershell
cd c:\dev\ai-agent-evaluation\backend\AiAgentEvaluation

# Clean previous builds
Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
Remove-Item -Force deploy.zip -ErrorAction SilentlyContinue

# Build with all dependencies including native libraries
dotnet publish --configuration Release --runtime win-x64 --self-contained false --output .\publish

# Verify critical files are included
Test-Path .\publish\AiAgentEvaluation.dll
Test-Path .\publish\host.json
Test-Path .\publish\bin\runtimes\win-x64\native\grpc_csharp_ext.x64.dll

# Create deployment package
cd publish
Compress-Archive -Path * -DestinationPath ..\deploy.zip -Force
cd ..
```

Expected output: `deploy.zip` created (~50-60 MB)

### Step 3: Deploy to Staging

```powershell
# Deploy to Azure Function App
az functionapp deployment source config-zip `
  --resource-group rg-agent-eval-staging `
  --name func-aiestagingmbwc `
  --src deploy.zip
```

Expected: Deployment succeeds with message "Deployment successful"

### Step 4: Verify Deployment

**Wait for app restart** (typically 10-15 seconds):
```powershell
Start-Sleep -Seconds 15
```

**Check basic health endpoint**:
```powershell
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
```

Expected response:
```json
{
  "status": "Healthy",
  "timestamp": "2025-11-27T20:30:00Z",
  "buildTimestamp": "2025-11-27T20:15:00Z",
  "version": "1.0.70"
}
```

**Verify version matches**: Check that `version` is `1.0.70` and `buildTimestamp` matches deployment time.

**Check comprehensive health endpoint**:
```powershell
curl https://func-aiestagingmbwc.azurewebsites.net/api/health
```

Expected: All components show "Healthy" status

### Step 5: Verify Functions Loaded

**Check Azure Portal**:
1. Navigate to Function App: `func-aiestagingmbwc`
2. Select "Functions" blade
3. Verify all functions appear:
   - OrchestratorFunction
   - MultiTurnAgentWorkerFunction
   - EvaluatorWorkerFunction
   - AggregateEvaluationProcessorFunction
   - MonitorFunction
   - HealthCheckFunction
   - BasicHealthFunction

**Check Application Insights**:
```powershell
# Query for startup logs
az monitor app-insights query `
  --app <app-insights-resource-name> `
  --analytics-query "traces | where timestamp > ago(5m) | where message contains 'Function' | order by timestamp desc | take 20"
```

Look for successful function initialization messages.

---

## Post-Deployment Validation

### Validation Test 1: Single-Turn Evaluation (Baseline)

**Objective**: Verify single-turn flow still works correctly

**Steps**:
1. Start an evaluation run with single-turn test cases
2. Monitor completion via Azure Portal or API
3. Verify results are generated correctly

**Expected Behavior**:
- Single-turn tests complete normally
- `IsMultiTurn` flag is false in queue messages
- No aggregate calculation triggered (single-turn doesn't use aggregates)

### Validation Test 2: Multi-Turn Evaluation (New Behavior)

**Objective**: Verify event-driven aggregate calculation

**Steps**:
1. Start an evaluation run with multi-turn test cases (3+ turns)
2. Monitor Application Insights for log messages:
   - "Queuing evaluator job for multi-turn test case"
   - "Checking if all turns complete for aggregate calculation"
   - "All turns complete - calculating aggregate"
3. Measure time from last turn completion to aggregate result

**Expected Behavior**:
- Aggregate calculated within 2-5 seconds of last turn completion (not 30 seconds)
- Application Insights shows "All turns complete - calculating aggregate" message
- Aggregate result has correct pass rate and overall score

**Validation Query** (Application Insights):
```kusto
traces
| where timestamp > ago(30m)
| where message contains "aggregate" or message contains "All turns complete"
| order by timestamp desc
```

### Validation Test 3: Timer Function Fallback

**Objective**: Verify timer function still handles edge cases

**Steps**:
1. Wait 60 seconds after multi-turn run completes
2. Check AggregateEvaluationProcessorFunction logs

**Expected Behavior**:
- Timer function logs show minimal processing: "No pending aggregates to process" or "Processed 0 aggregates"
- Timer function only processes orphaned aggregates (if any race conditions occurred)

### Validation Test 4: Performance Comparison

**Objective**: Measure improvement in aggregate calculation latency

**Metrics to Compare** (before vs after deployment):

| Metric | Before (Timer-based) | After (Event-driven) | Target |
|--------|---------------------|---------------------|--------|
| Avg aggregate latency | ~15-30 seconds | < 5 seconds | < 10 seconds |
| Timer function processing | 20 aggregates/minute | 0-2 aggregates/minute | < 5/minute |
| User wait time | Up to 30 seconds | Near real-time | < 5 seconds |

**Application Insights Query**:
```kusto
customMetrics
| where name == "AggregateCalculationTime"
| where timestamp > ago(1h)
| summarize avg(value), max(value), min(value) by bin(timestamp, 5m)
```

---

## Rollback Procedure

### When to Rollback

Rollback if you observe:
- ❌ Aggregate results not being calculated
- ❌ Increased error rate in EvaluatorWorkerFunction
- ❌ Duplicate aggregate calculations
- ❌ Function app crashes or high memory usage

### Rollback Steps

**Option 1: Redeploy Previous Version** (Recommended)

```powershell
# Checkout previous stable version
cd c:\dev\ai-agent-evaluation
git checkout main  # or previous release tag

# Update version numbers to rollback version
# Edit HealthCheckFunction.cs and BasicHealthFunction.cs
# Set DeploymentVersion to "1.0.69-rollback"

# Build and deploy
cd backend/AiAgentEvaluation
dotnet publish --configuration Release --runtime win-x64 --self-contained false --output .\publish
cd publish
Compress-Archive -Path * -DestinationPath ..\deploy.zip -Force
cd ..
az functionapp deployment source config-zip --resource-group rg-agent-eval-staging --name func-aiestagingmbwc --src deploy.zip

# Verify rollback
Start-Sleep -Seconds 15
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
```

**Option 2: Azure Portal Slot Swap** (If staging slot was used)

1. Navigate to Function App in Azure Portal
2. Select "Deployment slots"
3. Click "Swap" to swap staging/production slots
4. Verify health check shows previous version

**Option 3: Azure Deployment History** (Last resort)

1. Navigate to Function App → Deployment Center
2. Select previous successful deployment
3. Click "Redeploy"

### Post-Rollback Verification

```powershell
# Verify previous version is running
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
# Check version is 1.0.69 or earlier

# Monitor for 5-10 minutes to ensure stability
# Check Application Insights for error rate
```

---

## Monitoring and Observability

### Key Metrics to Monitor

**Application Insights Custom Metrics**:
- `AggregateCalculationTime`: Time to calculate aggregate (should decrease from ~30s to <5s)
- `TurnCompletionCheckTime`: Time to check if all turns complete (should be <1s)
- `EvaluatorWorkerFunction.Duration`: Should remain consistent

**Log Analytics Queries**:

**1. Aggregate Calculation Frequency**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "All turns complete - calculating aggregate"
| summarize count() by bin(timestamp, 5m)
```

**2. Timer Function Activity** (should be minimal):
```kusto
traces
| where timestamp > ago(1h)
| where message contains "AggregateEvaluationProcessorFunction"
| where message contains "Processed"
| project timestamp, message
```

**3. Error Rate**:
```kusto
traces
| where timestamp > ago(1h)
| where severityLevel >= 3  // Warning or higher
| where message contains "aggregate" or message contains "EvaluatorWorkerFunction"
| summarize count() by severityLevel, bin(timestamp, 5m)
```

### Success Criteria

After 24 hours of deployment, verify:
- ✅ Multi-turn aggregate latency < 10 seconds (avg)
- ✅ Timer function processing < 5 aggregates/hour
- ✅ No increase in error rate
- ✅ User feedback shows improved responsiveness
- ✅ All existing single-turn and multi-turn tests pass

---

## Troubleshooting

### Issue: Aggregates Not Being Calculated

**Symptoms**:
- Multi-turn evaluation results show individual turn scores but no aggregate
- Application Insights logs don't show "All turns complete" message

**Diagnosis**:
```kusto
// Check if turn completion check is happening
traces
| where timestamp > ago(30m)
| where message contains "Checking if all turns complete"
| project timestamp, message
```

**Possible Causes**:
1. `IsMultiTurn` flag not being set correctly
2. Dataverse query for turn completion failing
3. Race condition between parallel evaluators

**Resolution**:
1. Check MultiTurnAgentWorkerFunction logs for "Queuing evaluator job for multi-turn"
2. Verify `CheckIfAllTurnsCompleteForEvaluatorAsync` is being called
3. If issue persists, timer function will catch it within 30 seconds

### Issue: Duplicate Aggregate Calculations

**Symptoms**:
- Multiple aggregate results for same test case + evaluator + run
- Application Insights shows "All turns complete" multiple times

**Diagnosis**:
```kusto
traces
| where timestamp > ago(30m)
| where message contains "All turns complete - calculating aggregate"
| extend testCaseId = extract("testCaseId: ([a-f0-9\\-]+)", 1, message)
| summarize count() by testCaseId
| where count_ > 1
```

**Possible Causes**:
1. Race condition between EvaluatorWorkerFunction instances
2. Retry logic triggering duplicate calculations

**Resolution**:
- Dataverse `CreateAggregateResult` includes duplicate prevention via unique constraint
- Duplicates will be logged but not create multiple records
- Monitor for frequency - occasional duplicates are acceptable

### Issue: Increased Error Rate

**Symptoms**:
- EvaluatorWorkerFunction failures increase
- "Failed to check turn completion" errors in logs

**Diagnosis**:
```kusto
exceptions
| where timestamp > ago(30m)
| where outerMessage contains "EvaluatorWorkerFunction"
| summarize count() by outerMessage
```

**Possible Causes**:
1. Dataverse connectivity issues
2. Unexpected data format in queue messages
3. Missing test case or evaluator type data

**Resolution**:
1. Check Dataverse service health
2. Verify queue message schema matches expected format
3. Enable detailed logging for affected test cases
4. Consider rollback if error rate > 5%

---

## Post-Deployment Checklist

### Immediate (0-1 hour)

- [ ] Health check shows version 1.0.70
- [ ] All functions appear in Azure Portal
- [ ] No startup errors in Application Insights
- [ ] Single-turn evaluation test passes
- [ ] Multi-turn evaluation test passes
- [ ] Aggregate calculated within 10 seconds of last turn

### Short-term (1-24 hours)

- [ ] No increase in overall error rate
- [ ] Timer function processing minimal aggregates (< 5/hour)
- [ ] Average aggregate latency < 10 seconds
- [ ] User feedback positive (if applicable)
- [ ] No duplicate aggregate issues

### Long-term (1-7 days)

- [ ] Stable performance over 7 days
- [ ] Cost metrics show no unexpected increases
- [ ] Application Insights shows consistent patterns
- [ ] Update main branch documentation to reflect deployment
- [ ] Close related GitHub issues/PRs

---

## Documentation Updates

After successful deployment:

1. **Update ARCHITECTURE.md**: Mark "Last-Turn Aggregate Optimization" as deployed
2. **Update PRD**: Document new behavior in Section 3.3.4
3. **Update Story 06**: Add deployment date and production validation results
4. **Create Deployment Record**: Save this document with actual timestamps and results

---

## Related Documentation

- **Implementation Plan**: `docs/implementation/last-turn-aggregate-optimization.md`
- **Executive Summary**: `docs/implementation/SUMMARY-last-turn-optimization.md`
- **Architecture**: `docs/ARCHITECTURE.md` (Section 5.3)
- **PRD**: `docs/prd/PRD-Agent-Evaluation-Function.md` (Section 3.3.4)
- **Story 06**: `docs/stories/06-multi-turn-support.md`

---

## Sign-off

**Prepared By**: GitHub Copilot Agent  
**Reviewed By**: [Pending]  
**Approved By**: [Pending]  
**Deployment Executed By**: [Pending]  
**Deployment Date**: [Pending]  
