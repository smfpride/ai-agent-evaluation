# Story 11: Multi-Turn Service Bus Migration - Staging Deployment Guide

**Date**: November 19, 2025  
**Story**: 11-multi-turn-service-bus-migration  
**Phase**: 4 - Staging Validation  
**Environment**: func-aiestagingmbwc (staging)

---

## Pre-Deployment Checklist

### 1. Verify Local Build and Tests

```powershell
# Navigate to function app directory
cd c:\dev\ai-agent-evaluation\backend\AiAgentEvaluation

# Build in Release mode
dotnet build --configuration Release

# Expected output: Build succeeded, 0 Error(s), 0 Warning(s)
```

### 2. Run All Unit Tests

```powershell
# Run unit tests
cd c:\dev\ai-agent-evaluation\backend\tests\AiAgentEvaluation.UnitTests
dotnet test --configuration Release

# Expected output: All tests passed
# Look for: MultiTurnAgentWorkerFunctionTests - 7 tests should pass
```

### 3. Review Changed Files

**New Files**:
- ✅ `backend/tests/AiAgentEvaluation.UnitTests/MultiTurnAgentWorkerFunctionTests.cs` (800 lines)

**Modified Files**:
- ✅ `backend/infrastructure/main.bicep` - Queue already exists (lines 137-152)
- ✅ `backend/AiAgentEvaluation/Models/ApiModels.cs` - Message model already exists
- ✅ `backend/AiAgentEvaluation/Functions/MultiTurnAgentWorkerFunction.cs` - Already implemented
- ✅ `backend/AiAgentEvaluation/Services/IServiceBusService.cs` - Methods already exist
- ✅ `backend/AiAgentEvaluation/Services/ServiceBusService.cs` - Methods already implemented
- ✅ `backend/AiAgentEvaluation/Functions/OrchestratorFunction.cs` - Already using Service Bus pattern

**Note**: Core implementation was already complete. This deployment verifies the Service Bus pattern works in staging.

---

## Deployment Steps

### Step 1: Update Version Tracking

**CRITICAL**: Update deployment version in `HealthCheckFunction.cs` before deploying.

```powershell
# Open HealthCheckFunction.cs
code backend\AiAgentEvaluation\Functions\HealthCheckFunction.cs
```

Update these lines (around line 16-17):
```csharp
private static readonly string BuildTimestamp = "2025-11-19T[CURRENT_UTC_TIME]Z";
private static readonly string DeploymentVersion = "1.0.18"; // Increment from current
```

### Step 2: Build Release Package

```powershell
cd backend\AiAgentEvaluation
dotnet build --configuration Release

# Verify output
ls bin\Release\net8.0
# Should see AiAgentEvaluation.dll and dependencies
```

### Step 3: Deploy to Staging

```powershell
# Deploy function app
func azure functionapp publish func-aiestagingmbwc

# Expected output:
# - Syncing triggers...
# - Functions in func-aiestagingmbwc:
#   - MultiTurnAgentWorkerFunction (Service Bus trigger)
#   - ProcessMultiTurnAgentCall
#   - (other existing functions...)
```

**Deployment checklist**:
- [ ] Deployment completed without errors
- [ ] All functions synced successfully
- [ ] Triggers registered

### Step 4: Verify Deployment

#### 4.1 Basic Health Check

```powershell
# Check basic health endpoint
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic

# Expected JSON response:
{
  "status": "Healthy",
  "version": "1.0.18",
  "buildTimestamp": "2025-11-19T...",
  "environment": "staging"
}
```

**Verify**:
- [ ] Status is "Healthy"
- [ ] Version matches what you set in HealthCheckFunction.cs
- [ ] BuildTimestamp matches deployment time

#### 4.2 Comprehensive Health Check

```powershell
# Check full health (includes service connections)
curl https://func-aiestagingmbwc.azurewebsites.net/api/health

# Expected: All services healthy (Dataverse, Azure OpenAI, Service Bus)
```

#### 4.3 Verify Functions in Azure Portal

Navigate to: https://portal.azure.com
1. Go to **func-aiestagingmbwc** Function App
2. Click **Functions** in left menu
3. Verify these functions exist:
   - ✅ **ProcessMultiTurnAgentCall** - ServiceBusTrigger on `multi-turn-agent-call-queue`
   - ✅ **ProcessAgentCall** - ServiceBusTrigger on `agent-call-queue`
   - ✅ **ProcessEvaluatorJob** - ServiceBusTrigger on `evaluator-processing-queue`
   - ✅ **StartEvaluationRun** - HTTP trigger
   - ✅ Other existing functions

#### 4.4 Check Service Bus Queue

Azure Portal → Service Bus Namespace → Queues
- [ ] `multi-turn-agent-call-queue` exists
- [ ] Active message count: 0
- [ ] Dead-letter message count: 0

---

## Test Data Preparation

### Create Multi-Turn Test Cases in Dataverse

You'll need test cases with different characteristics to validate all scenarios.

#### Test Case Distribution

**Total**: 50+ multi-turn test cases

| Turn Count | Evaluators | Test Cases | Purpose |
|------------|-----------|------------|---------|
| 2 turns    | 1         | 10         | Basic two-turn flow |
| 2 turns    | 3         | 10         | Multi-evaluator fan-out |
| 3 turns    | 2         | 10         | Medium conversation |
| 5 turns    | 2         | 10         | Long conversation |
| 3 turns    | 5         | 5          | Max evaluators |
| 2 turns (NA) | 3       | 5          | NA scenario (no expected response on Turn 2) |

#### Sample Test Case Structure

**Test Case**: "Fashion Assistant - 2 Turn"
- **IsMultiTurn**: `true`
- **Evaluators**: Similarity, Accuracy, Relevancy (3)
- **Turns**:
  - Turn 1 (Sequence=1): "What colors are trending this season?" → Expected: "Earth tones and pastels..."
  - Turn 2 (Sequence=2): "Tell me more about pastels" → Expected: "Pastel colors include soft pink..."

**Test Case**: "Product Support - NA Scenario"
- **IsMultiTurn**: `true`
- **Evaluators**: Similarity (requires expected response)
- **Turns**:
  - Turn 1: "I need help with Product X" → Expected: "I can help..."
  - Turn 2: "Thank you" → Expected: `null` (no expected response → NA result)

---

## Comprehensive Test Execution

### Test Scenario 1: Basic Multi-Turn Flow (10 test cases, 2 turns, 1 evaluator)

**Purpose**: Verify conversation context preservation

```powershell
# Create evaluation run in Dataverse with these test cases
# POST to /api/evaluations/start with evaluationRunId

curl -X POST https://func-aiestagingmbwc.azurewebsites.net/api/evaluations/start `
  -H "Content-Type: application/json" `
  -d '{"evaluationRunId": "[GUID]", "timeoutMinutes": 10}'

# Expected: HTTP 202 Accepted
# Response includes: totalTestCases = 10 × 2 turns × 1 evaluator + 10 aggregates = 30 units
```

**Validation**:
- [ ] All 10 test cases complete
- [ ] All Turn 1 executions have `conversationId` populated
- [ ] All Turn 2 executions reuse same `conversationId` from Turn 1
- [ ] All aggregate results calculated correctly
- [ ] No stuck test cases
- [ ] Completion time < 5 minutes

### Test Scenario 2: Multi-Evaluator Fan-Out (10 test cases, 2 turns, 3 evaluators)

**Purpose**: Verify efficient fan-out to multiple evaluators

```powershell
# Run evaluation with multi-evaluator test cases
# Expected units: 10 × 2 turns × 3 evaluators + 10 × 3 aggregates = 90 units
```

**Validation**:
- [ ] Copilot Studio called **once per turn** (not once per evaluator)
- [ ] All 3 evaluators receive same actualResponse for each turn
- [ ] All 30 aggregate results (10 test cases × 3 evaluators) calculated
- [ ] Performance: Agent call is expensive, evaluations run in parallel

### Test Scenario 3: Long Conversations (10 test cases, 5 turns, 2 evaluators)

**Purpose**: Verify context preservation across many turns

```powershell
# Expected units: 10 × 5 turns × 2 evaluators + 10 × 2 aggregates = 120 units
```

**Validation**:
- [ ] All 5 turns execute sequentially with same conversationId
- [ ] Turn 5 actualResponse shows context from previous turns
- [ ] All aggregates calculate average across 5 turns correctly
- [ ] No timeout errors (each turn should be independent)

### Test Scenario 4: Retry Logic (Simulate Copilot Failure)

**Purpose**: Verify exponential backoff retry

**Note**: This requires temporarily making Copilot Studio unavailable or using invalid Direct Line URL.

```powershell
# Create test case with invalid/unreachable Direct Line URL
# Run evaluation
```

**Validation**:
- [ ] Turn fails on first attempt
- [ ] Message re-queued with 30s delay (RetryCount=1)
- [ ] Second attempt after 30s
- [ ] Message re-queued with 60s delay (RetryCount=2)
- [ ] Third attempt after 60s
- [ ] Message re-queued with 90s delay (RetryCount=3)
- [ ] After 3 retries, error results created for all evaluators
- [ ] Next turn NOT queued (error stops progression)
- [ ] Dead letter queue remains empty (handled gracefully)

### Test Scenario 5: NA Scenarios (5 test cases with missing expected responses)

**Purpose**: Verify NA result handling

```powershell
# Test cases where Turn 2 has no expected response
# But evaluator requires expected response (e.g., Similarity)
```

**Validation**:
- [ ] Turn 1 executes normally
- [ ] Turn 2 executes (Copilot called)
- [ ] NA results created immediately (StatusCode = 778760003)
- [ ] NA results NOT queued to evaluator queue
- [ ] Completion counter incremented (not marked as failed)
- [ ] Next turn still queued (NA doesn't stop progression)
- [ ] Aggregate excludes NA results from average

---

## 24-Hour Monitoring Plan

### Application Insights Queries

#### Query 1: Multi-Turn Agent Worker Executions

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Processing multi-turn agent call"
| project timestamp, message, severityLevel, customDimensions
| order by timestamp desc
```

**Look for**:
- [ ] All turns execute successfully
- [ ] Conversation context logged correctly
- [ ] No unexpected exceptions

#### Query 2: Retry Attempts

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Retrying turn"
| project timestamp, message, customDimensions.RetryCount, customDimensions.TestCaseId
| summarize RetryCount = count() by bin(timestamp, 1h)
| render timechart
```

**Look for**:
- [ ] Retry rate < 5% of total turns
- [ ] No infinite retry loops
- [ ] Exponential backoff delays respected

#### Query 3: Error Results

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Max retries exceeded" or message contains "Agent call failed"
| project timestamp, message, severityLevel, customDimensions
```

**Look for**:
- [ ] Error rate < 2% of total turns
- [ ] All errors have clear error messages
- [ ] No stuck orchestrations

#### Query 4: NA Results

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Creating NA result"
| summarize NACount = count() by bin(timestamp, 1h)
| render timechart
```

**Look for**:
- [ ] NA results created as expected (for test cases with missing expected responses)
- [ ] NA results not queued to evaluator queue

#### Query 5: Performance Metrics

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Multi-turn agent call processing completed"
| extend TotalTime = todouble(customDimensions.TotalTime)
| summarize 
    AvgTime = avg(TotalTime),
    P50 = percentile(TotalTime, 50),
    P95 = percentile(TotalTime, 95),
    P99 = percentile(TotalTime, 99),
    MaxTime = max(TotalTime)
```

**Target metrics**:
- [ ] Average turn time: < 20 seconds
- [ ] P95 turn time: < 30 seconds
- [ ] P99 turn time: < 45 seconds
- [ ] Max turn time: < 60 seconds

### Service Bus Metrics

Azure Portal → Service Bus Namespace → Metrics

#### Metric 1: Active Messages (multi-turn-agent-call-queue)

**Chart**: Active Messages over 24 hours

**Look for**:
- [ ] Queue depth returns to 0 after each evaluation run
- [ ] No messages stuck in queue for > 30 minutes
- [ ] Queue drains at expected rate (~3-5 messages/second)

#### Metric 2: Dead-Letter Messages

**Chart**: Dead-letter message count over 24 hours

**Look for**:
- [ ] Dead-letter count = 0 (all errors handled gracefully)
- [ ] If dead letters exist, investigate with Service Bus Explorer

#### Metric 3: Incoming Messages

**Chart**: Incoming messages to multi-turn-agent-call-queue

**Look for**:
- [ ] Message rate matches test execution schedule
- [ ] No unexpected message spikes

### Dataverse Validation

#### Query Evaluation Results

```sql
-- Check all multi-turn test cases completed
SELECT 
    tc.mcicaps_name,
    tc.mcicaps_ismultiturn,
    COUNT(DISTINCT tct.mcicaps_testcaseturnid) AS TurnCount,
    COUNT(er.mcicaps_evaluationresultsid) AS ResultCount,
    COUNT(CASE WHEN er.mcicaps_isaggregate = 1 THEN 1 END) AS AggregateCount
FROM mcicaps_testcases tc
INNER JOIN mcicaps_evaluationresults er ON er.mcicaps_testcase = tc.mcicaps_testcasesid
LEFT JOIN mcicaps_testcaseturn tct ON tct.mcicaps_testcaseid = tc.mcicaps_testcasesid
WHERE tc.mcicaps_ismultiturn = 1
    AND er.mcicaps_evaluation = '[EVALUATION_RUN_ID]'
GROUP BY tc.mcicaps_testcasesid, tc.mcicaps_name, tc.mcicaps_ismultiturn
```

**Validation**:
- [ ] ResultCount = (TurnCount × EvaluatorCount) + EvaluatorCount (for aggregates)
- [ ] All aggregates have StatusCode = Completed (2)
- [ ] No results stuck in Pending (1) after run completes

---

## Regression Testing

### Test Single-Turn Cases (Baseline)

**Purpose**: Ensure multi-turn changes didn't break single-turn functionality

```powershell
# Run 20 single-turn test cases
# Expected: All complete successfully, same performance as before
```

**Validation**:
- [ ] Single-turn tests use `agent-call-queue` (not multi-turn queue)
- [ ] Performance unchanged from baseline
- [ ] No errors in single-turn execution
- [ ] AgentWorkerFunction still works correctly

### Test Aggregate Calculation

**Purpose**: Ensure timer function still works correctly

```powershell
# Run evaluation with multi-turn test cases
# Wait for aggregate calculation (timer runs every 30 seconds)
```

**Validation**:
- [ ] AggregateEvaluationProcessorFunction still running on schedule
- [ ] Aggregates calculated correctly from turn results
- [ ] NA results excluded from aggregate average
- [ ] Completion counter incremented correctly

### Test Monitor Function

**Purpose**: Ensure evaluation run monitoring still works

```powershell
# Start evaluation run
# Let it run to completion
# Check MonitorFunction logs
```

**Validation**:
- [ ] MonitorFunction detects completion correctly
- [ ] EvaluationRun status updated to Completed (778760004)
- [ ] EndTime set correctly
- [ ] OverallScore calculated correctly

---

## Success Criteria Validation

### Phase 4 Acceptance Criteria (from Story 11)

- [ ] **All staging tests complete successfully** - 50+ test cases, various turn counts
- [ ] **No stuck test cases** - All tests reach terminal state (Completed/Errored)
- [ ] **Zero dead letter messages** (or alerts triggered if any appear)
- [ ] **Performance meets targets** - Average turn time < 20 seconds
- [ ] **Aggregate scores match expected values** - Manual verification of sample cases

### Additional Validation

- [ ] **Conversation context preserved** - Verified by checking conversationId in results
- [ ] **Retry logic works** - Verified by simulating failures
- [ ] **NA handling correct** - Verified by test cases with missing expected responses
- [ ] **Multi-evaluator fan-out efficient** - One agent call per turn, parallel evaluations
- [ ] **No regressions** - Single-turn, aggregates, and monitor function unchanged

---

## Monitoring Dashboard Setup

### Create Application Insights Dashboard

1. Go to Azure Portal → Application Insights → Dashboards
2. Create new dashboard: "Story 11 - Multi-Turn Service Bus Monitoring"
3. Add tiles:

#### Tile 1: Multi-Turn Executions (Last 24h)
```kusto
traces
| where timestamp > ago(24h)
| where message contains "Processing multi-turn agent call"
| summarize Count = count() by bin(timestamp, 1h)
| render timechart
```

#### Tile 2: Success Rate
```kusto
traces
| where timestamp > ago(24h)
| where message contains "Multi-turn agent call processing completed" or message contains "Max retries exceeded"
| summarize 
    Total = count(),
    Successful = countif(message contains "completed"),
    Failed = countif(message contains "Max retries exceeded")
| extend SuccessRate = (Successful * 100.0) / Total
| project SuccessRate
```

#### Tile 3: Average Turn Execution Time
```kusto
traces
| where timestamp > ago(24h)
| where message contains "Multi-turn agent call processing completed"
| extend TotalTime = todouble(customDimensions.TotalTime)
| summarize AvgTime = avg(TotalTime) by bin(timestamp, 1h)
| render timechart
```

#### Tile 4: Dead Letter Queue Depth
```
Service Bus Metric: Dead-letter message count
Resource: multi-turn-agent-call-queue
Aggregation: Max
Time range: Last 24 hours
```

---

## Troubleshooting Guide

### Issue 1: Test Cases Stuck in Pending

**Symptoms**:
- Evaluation run shows incomplete after 30+ minutes
- Results remain in Pending status

**Investigation**:
1. Check Service Bus queue depth:
   - If messages in queue → Processing slow, check worker function logs
   - If queue empty → Messages never queued, check OrchestratorFunction logs

2. Check Application Insights for errors:
   ```kusto
   exceptions
   | where timestamp > ago(1h)
   | where cloud_RoleName == "func-aiestagingmbwc"
   | project timestamp, type, outerMessage, problemId
   ```

3. Check dead letter queue:
   - Azure Portal → Service Bus → Queue → Dead-letter messages
   - If messages in DLQ → Permanent failure, investigate message content

**Resolution**:
- Retry: Update stuck results to Execute status and re-queue
- Fix: If code issue, deploy fix and retry

### Issue 2: Conversation Context Lost

**Symptoms**:
- Turn 2+ results show agent didn't remember Turn 1

**Investigation**:
1. Check Turn 1 result in Dataverse:
   - Does `mcicaps_conversationid` have a value?
   - Check Turn 1 logs for conversationId capture

2. Check Turn 2 message:
   - Was conversationId passed in message?
   - Check OrchestratorFunction logs for message content

3. Check CopilotStudioService:
   - Was conversationId passed to SendMessageAsync?
   - Check Copilot response for same conversationId

**Resolution**:
- If Turn 1 conversationId not captured → Code issue, verify CopilotDirectLineResponse parsing
- If conversationId not passed → Message serialization issue
- If Copilot ignored conversationId → Direct Line API issue, check token validity

### Issue 3: High Retry Rate

**Symptoms**:
- Many turns retrying multiple times
- Slow overall completion

**Investigation**:
1. Check retry reasons:
   ```kusto
   traces
   | where timestamp > ago(1h)
   | where message contains "Retrying turn"
   | project timestamp, customDimensions.ErrorMessage
   | summarize Count = count() by tostring(customDimensions.ErrorMessage)
   ```

2. Common causes:
   - **429 Rate Limit** → Copilot Studio throttling, need to slow down requests
   - **Timeout** → Copilot taking too long, increase timeout setting
   - **Connection Error** → Network issue, transient

**Resolution**:
- If rate limiting → Add delay between test case queuing
- If timeout → Increase `CopilotStudio__TimeoutSeconds` app setting
- If transient → Expected, retries will handle

### Issue 4: Dead Letter Messages

**Symptoms**:
- Messages in dead letter queue

**Investigation**:
1. Use Service Bus Explorer to peek dead letter messages
2. Check `DeadLetterReason` and `DeadLetterErrorDescription` properties
3. Review message content to identify test case

**Resolution**:
- If MaxDeliveryCount exceeded → Code bug, fix and reprocess
- If poison message → Invalid message format, investigate source
- If code exception → Check exception type, fix bug

---

## Rollback Plan

### If Critical Issues Discovered

**Step 1: Assess Impact**
- How many test cases affected?
- Is single-turn functionality working?
- Can we continue with Durable Functions temporarily?

**Step 2: Decision Point**

**Option A: Continue with fixes** (if issues are minor)
- Deploy hotfix to staging
- Re-run affected tests
- Continue monitoring

**Option B: Rollback to previous version** (if critical)
```powershell
# Rollback requires redeploying previous Git commit
git log --oneline -10  # Find commit before Story 11 implementation

# Note: This would require un-implementing Story 11 changes
# Better option: Keep current code, don't use multi-turn tests yet
```

**Step 3: Communication**
- Document issue in `docs/archive-fixes/`
- Update Story 11 status
- Plan fix and re-deployment

---

## Sign-Off Checklist

### Before Proceeding to Phase 5 (Production)

- [ ] All 50+ staging test cases completed successfully
- [ ] 24-hour monitoring completed with no issues
- [ ] Success rate > 95%
- [ ] Average turn time < 20 seconds
- [ ] Dead letter queue depth = 0
- [ ] No regressions in single-turn tests
- [ ] Aggregate calculation working correctly
- [ ] Monitor function working correctly
- [ ] Conversation context preserved in all multi-turn tests
- [ ] Retry logic validated with simulated failures
- [ ] NA scenarios handled correctly
- [ ] Performance meets targets
- [ ] Team review completed and approved

### Approval

**Reviewed by**: ___________________  
**Date**: ___________________  
**Approved for Production**: [ ] Yes [ ] No  
**Notes**: _____________________________________________________

---

## Next Steps

After successful staging validation:

1. **Update Story 11** - Mark Phase 4 checkboxes as completed
2. **Proceed to Phase 5** - Cleanup & Production Deployment
3. **Create Production Deployment Plan** - Similar checklist for production
4. **Schedule Production Deployment** - Low-usage window
5. **Phase 6** - Documentation updates (Architecture.md, PRD, runbooks)

---

**Deployment Guide Version**: 1.0  
**Last Updated**: November 19, 2025  
**Story**: 11-multi-turn-service-bus-migration  
**Environment**: Staging (func-aiestagingmbwc)
