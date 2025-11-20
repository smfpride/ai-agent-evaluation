# Multi-Turn Test Execution - Monitoring & Troubleshooting Runbook

**Last Updated**: November 19, 2025  
**Owner**: Platform Team  
**Applies To**: Story 11 - Multi-Turn Service Bus Migration

---

## Overview

This runbook provides operational guidance for monitoring, troubleshooting, and recovering from issues with multi-turn test case execution using the Service Bus-based architecture.

---

## Architecture Quick Reference

### Message Flow
```
OrchestratorFunction
  ↓ Queue Turn 1 message (one per test case)
multi-turn-agent-call-queue
  ↓ Trigger
MultiTurnAgentWorkerFunction
  ↓ Call Copilot Studio (reuse conversationId for Turn 2+)
  ↓ Fan out to evaluator queue (all evaluators)
  ↓ Queue next turn (if not last)
evaluator-processing-queue
  ↓ Trigger (parallel)
EvaluatorWorkerFunction
  ↓ Evaluate turn
AggregateEvaluationProcessorFunction (timer)
  ↓ Calculate aggregate when all turns complete
```

### Key Components
- **Queue**: `multi-turn-agent-call-queue` (MaxDeliveryCount: 10, LockDuration: 5min)
- **Worker**: `MultiTurnAgentWorkerFunction` (Service Bus triggered)
- **Retry Logic**: Exponential backoff (30s, 60s, 90s)
- **Dead Letter**: Automatic after 10 failed delivery attempts

---

## Normal Operations

### Health Checks

#### 1. Queue Depth Monitoring
```powershell
# Check multi-turn queue depth
az servicebus queue show \
  --resource-group rg-agent-eval-staging \
  --namespace-name sbus-aiestagingmbwc \
  --name multi-turn-agent-call-queue \
  --query "countDetails" -o table
```

**Normal State**:
- Active messages: 0-50 (depends on test volume)
- Dead letter messages: 0
- Scheduled messages: 0-10 (retry queue)

**Alert Thresholds**:
- Active messages > 100 for > 10 minutes → Backlog building
- Dead letter messages > 0 → Failures requiring investigation
- Scheduled messages > 50 → Many retries (possible Copilot issues)

#### 2. Function Health
```powershell
# Check function app health
curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
```

**Expected Response**:
```json
{
  "status": "Healthy",
  "version": "1.0.42",
  "buildTimestamp": "2025-11-19T23:30:00Z"
}
```

#### 3. Application Insights Queries

**Multi-Turn Execution Rate**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "MultiTurnAgentWorkerFunction"
| where message contains "Processing turn"
| summarize count() by bin(timestamp, 5m)
| render timechart
```

**Turn Success vs Failure**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "MultiTurnAgentWorkerFunction"
| where message contains "Turn execution" or message contains "after retries"
| extend Success = message contains "successful"
| summarize SuccessCount = countif(Success), FailureCount = countif(not(Success))
```

**Retry Rate**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Re-queuing multi-turn message"
| summarize RetryCount = count() by bin(timestamp, 5m)
| render timechart
```

---

## Common Issues & Troubleshooting

### Issue 1: Messages Stuck in Queue

**Symptoms**:
- Active message count increasing
- No messages being processed
- No errors in Application Insights

**Diagnosis**:
```kusto
// Check if function is being triggered
traces
| where timestamp > ago(15m)
| where operation_Name == "MultiTurnAgentWorkerFunction"
| summarize count()
```

**Resolution**:
1. **Check Function App Status**:
   ```powershell
   az functionapp show \
     --name func-aiestagingmbwc \
     --resource-group rg-agent-eval-staging \
     --query "state"
   ```
   - If stopped: Restart function app

2. **Check Service Bus Connection**:
   ```kusto
   exceptions
   | where timestamp > ago(15m)
   | where outerMessage contains "ServiceBus"
   ```
   - If connection issues: Verify Managed Identity RBAC

3. **Check Function Bindings**:
   - Verify `multi-turn-agent-call-queue` exists
   - Check Service Bus namespace connection string/Managed Identity

**Escalation**: If function is healthy but not triggering, contact Azure Support (Service Bus issue).

---

### Issue 2: High Dead Letter Queue Count

**Symptoms**:
- Messages in dead letter queue (DLQ)
- Evaluation runs not completing

**Diagnosis**:
```powershell
# Peek dead letter messages
az servicebus queue show \
  --resource-group rg-agent-eval-staging \
  --namespace-name sbus-aiestagingmbwc \
  --name multi-turn-agent-call-queue \
  --query "countDetails.deadLetterMessageCount"
```

**Common Causes**:
1. **Deserialization Failures**: Invalid message format
2. **Null Reference**: Missing required fields in message
3. **Dataverse Failures**: Test case or turn data not found
4. **Copilot Persistent Failures**: Agent unresponsive after retries

**Investigation Steps**:
```kusto
// Find errors before DLQ
exceptions
| where timestamp > ago(1h)
| where operation_Name == "MultiTurnAgentWorkerFunction"
| project timestamp, outerMessage, innermostMessage, customDimensions.prop__testCaseId
| order by timestamp desc
| take 20
```

**Resolution**:

1. **Retrieve DLQ Message**:
   ```powershell
   # Use Service Bus Explorer or Azure Portal
   # Navigate to Queue → Dead-letter queue → Peek messages
   ```

2. **Analyze Message**:
   - Check `testCaseId`, `turnSequence`, `evaluatorTypeIds`
   - Verify test case exists in Dataverse
   - Check turn data completeness

3. **Fix Root Cause**:
   - **Invalid Message**: Fix upstream code creating message
   - **Missing Data**: Update Dataverse test case
   - **Copilot Issue**: Check Copilot Studio agent health

4. **Requeue Message** (if fixable):
   ```csharp
   // Use Service Bus Explorer to move message back to active queue
   // OR manually recreate message via OrchestratorFunction
   ```

**Escalation**: If >10 DLQ messages and unknown cause, escalate to development team.

---

### Issue 3: Test Cases Not Completing

**Symptoms**:
- Evaluation run status stuck at "Running"
- Some test cases show no results
- No errors in logs

**Diagnosis**:
```kusto
// Check evaluation run progress
traces
| where timestamp > ago(1h)
| where message contains "StartEvaluationRun"
| extend EvaluationRunId = tostring(customDimensions.prop__evaluationRunId)
| extend TestCaseCount = toint(customDimensions.prop__totalTestCases)
| project timestamp, EvaluationRunId, TestCaseCount
```

```kusto
// Check for specific test case
traces
| where timestamp > ago(1h)
| where customDimensions.prop__testCaseId == "<test-case-guid>"
| project timestamp, message, operation_Name
| order by timestamp asc
```

**Common Causes**:
1. **Turn Not Queued**: Error after Copilot call but before queuing next turn
2. **Aggregate Not Calculated**: All turns complete but aggregate pending
3. **Silent Failure**: Exception caught but result not marked errored

**Resolution**:

1. **Check Last Turn Processed**:
   ```kusto
   traces
   | where customDimensions.prop__testCaseId == "<guid>"
   | where message contains "Turn execution"
   | project timestamp, message
   | order by timestamp desc
   ```

2. **Check Aggregate Status**:
   ```kusto
   traces
   | where customDimensions.prop__testCaseId == "<guid>"
   | where message contains "Processing aggregate"
   | project timestamp, message
   ```

3. **Manual Intervention** (if needed):
   - Query Dataverse for evaluation results
   - Identify missing turns or aggregates
   - Manually create error results for stuck turns
   - Run `MonitorFunction` to recalculate completion status

**Prevention**: Monitor timer function execution every 30 seconds.

---

### Issue 4: High Retry Rate

**Symptoms**:
- Many scheduled messages in queue
- Slow test case completion
- Copilot errors in logs

**Diagnosis**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Re-queuing multi-turn message"
| extend RetryCount = toint(customDimensions.prop__retryCount)
| summarize count() by RetryCount
```

**Common Causes**:
1. **Copilot Rate Limiting**: 429 errors from Direct Line API
2. **Copilot Service Degradation**: Timeouts or 500 errors
3. **Network Issues**: Transient connectivity problems

**Resolution**:

1. **Check Copilot Health**:
   ```kusto
   traces
   | where timestamp > ago(1h)
   | where message contains "Copilot call failed"
   | summarize count() by tostring(customDimensions.prop__statusCode)
   ```

2. **Rate Limiting** (429 errors):
   - Normal behavior, exponential backoff will handle
   - If persistent: Check Copilot Studio agent capacity

3. **Service Degradation** (500 errors):
   - Check Azure Service Health dashboard
   - Contact Copilot Studio support if widespread

4. **Adjust Retry Settings** (if needed):
   ```json
   // local.settings.json or App Settings
   {
     "Evaluation": {
       "RetryDelaySeconds": 60  // Increase from 30s
     },
     "AzureOpenAI": {
       "MaxRetryAttempts": 5  // Increase from 3
     }
   }
   ```

**Escalation**: If >50% of messages retrying, escalate to Copilot Studio team.

---

### Issue 5: Conversation Context Lost

**Symptoms**:
- Turn 2+ responses don't reference Turn 1 context
- Agent appears to "forget" previous conversation
- Test assertions fail for multi-turn dialog

**Diagnosis**:
```kusto
traces
| where timestamp > ago(1h)
| where customDimensions.prop__testCaseId == "<guid>"
| where message contains "conversationId"
| project timestamp, message, customDimensions.prop__conversationId, customDimensions.prop__turnSequence
| order by timestamp asc
```

**Common Causes**:
1. **ConversationId Not Passed**: Turn 2+ message missing conversationId
2. **Token Expired**: DirectLine token expired between turns
3. **Copilot Studio Issue**: Agent not maintaining state

**Resolution**:

1. **Verify Message Contents**:
   - Check Turn 1 message: `conversationId` should be null
   - Check Turn 2+ message: `conversationId` should be populated from Turn 1 response

2. **Check Token**:
   ```kusto
   traces
   | where message contains "DirectLine token"
   | where customDimensions.prop__testCaseId == "<guid>"
   | project timestamp, message
   ```

3. **Test Manually**:
   - Use Copilot Studio web chat to verify agent maintains context
   - If works manually but not in automation: Bug in MultiTurnAgentWorkerFunction

**Escalation**: If conversationId is passed correctly but context still lost, contact Copilot Studio team.

---

## Performance Optimization

### Monitoring Performance Metrics

**Average Turn Latency**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Turn execution completed"
| extend LatencyMs = toint(customDimensions.prop__executionTimeMs)
| summarize avg(LatencyMs), percentile(LatencyMs, 95)
```

**Target**: <20 seconds per turn (P95)

**Message Throughput**:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Processing turn"
| summarize count() by bin(timestamp, 1m)
| render timechart
```

**Target**: 100+ messages/minute

### Tuning Recommendations

1. **Increase Concurrency** (if CPU/memory allows):
   ```json
   // host.json
   {
     "extensions": {
       "serviceBus": {
         "maxConcurrentCalls": 100  // Default: 16
       }
     }
   }
   ```

2. **Optimize Retry Delays** (for faster recovery):
   ```json
   // App Settings
   {
     "Evaluation:RetryDelaySeconds": 15  // Reduce from 30s
   }
   ```

3. **Scale Out Function App** (horizontal scaling):
   - Monitor CPU/memory metrics in Azure Portal
   - Adjust App Service Plan scale settings

---

## Disaster Recovery

### Scenario: Complete Service Outage

**Steps**:
1. **Stop New Runs**: Pause evaluation run creation
2. **Drain Queue**: Let active messages complete (timeout: 10 minutes)
3. **Investigate Root Cause**: Check function app, Service Bus, Dataverse health
4. **Fix and Restart**: Deploy fix, restart function app
5. **Reprocess Failed Runs**: Manually re-trigger stuck evaluation runs

### Scenario: Data Corruption

**Steps**:
1. **Identify Scope**: Which evaluation runs affected?
2. **Export Data**: Back up Dataverse records
3. **Clean Up**: Delete corrupt results
4. **Rerun Tests**: Re-trigger evaluation runs
5. **Verify**: Check aggregate calculations

### Backup Strategy
- **Dataverse**: Daily automated backups (7-day retention)
- **Service Bus**: Dead letter queue messages retained for 14 days
- **Application Insights**: Logs retained for 90 days

---

## Monitoring Dashboard

### Key Metrics to Display

1. **Multi-Turn Queue Depth**:
   - Chart: Active, Scheduled, Dead Letter
   - Refresh: Every 1 minute

2. **Turn Execution Rate**:
   - Chart: Turns/minute (success vs failure)
   - Refresh: Every 5 minutes

3. **Retry Rate**:
   - Chart: Retry count distribution
   - Refresh: Every 5 minutes

4. **Average Turn Latency**:
   - Chart: P50, P95, P99 latency
   - Refresh: Every 5 minutes

5. **Dead Letter Alerts**:
   - Alert: Any message in DLQ
   - Severity: High

### Sample Azure Monitor Alerts

**Alert 1: High Queue Depth**
- Condition: Active messages > 100 for 10 minutes
- Action: Email ops team
- Severity: Warning

**Alert 2: Dead Letter Messages**
- Condition: Dead letter count > 0
- Action: Email ops team + create incident
- Severity: Error

**Alert 3: High Failure Rate**
- Condition: Failure rate > 10% over 15 minutes
- Action: Email ops team
- Severity: Warning

---

## Support Contacts

- **Platform Team**: platform@company.com
- **Copilot Studio Support**: copilot-support@microsoft.com
- **Azure Support**: https://portal.azure.com/#blade/Microsoft_Azure_Support/HelpAndSupportBlade

---

## Related Documentation

- [Story 11: Multi-Turn Service Bus Migration](../stories/11-multi-turn-service-bus-migration.md)
- [Architecture Documentation](../ARCHITECTURE.md)
- [PRD: Agent Evaluation Function](../prd/PRD-Agent-Evaluation-Function.md)
- [Deployment Checklist](../deployment/DEPLOYMENT_CHECKLIST.md)

---

**Revision History**:
- 2025-11-19: Initial version (Story 11 completion)
