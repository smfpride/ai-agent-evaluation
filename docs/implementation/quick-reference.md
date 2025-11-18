# Quick Reference: Async Multi-Evaluator Architecture

**Date**: November 18, 2025  
**Purpose**: Quick reference guide for implementation decisions

---

## Decision Tree: When to Use Which Architecture

```
Is this a NEW evaluation run?
    │
    ├─ YES → Check feature flag
    │         │
    │         ├─ USE_ASYNC_EVALUATION = true
    │         │   └─> Use NEW architecture (agent-call-queue + evaluator-processing-queue)
    │         │
    │         └─ USE_ASYNC_EVALUATION = false
    │             └─> Use OLD architecture (test-case-processing queue + WorkerFunction)
    │
    └─ NO (existing run in progress)
        └─> Continue with original architecture (don't change mid-run)
```

---

## Architecture Quick Comparison

| Aspect | Current Architecture | Proposed Architecture |
|--------|---------------------|----------------------|
| **Queues** | 1 queue (test-case-processing) | 2 queues (agent-call + evaluator-processing) |
| **Workers** | 1 type (WorkerFunction) | 2 types (AgentWorker + EvaluatorWorker) |
| **Messages** | 1 per test case | 1 agent msg + N evaluator msgs |
| **Worker Duration** | ~60s (agent + all evaluators) | ~10s (agent) + ~10s (each evaluator) |
| **Concurrency** | 100 test cases in flight | 100 agent calls + 500 evaluator calls |
| **Error Handling** | All-or-nothing per test | Granular per evaluator |
| **Throughput** | 7.6 units/sec | 16-25 units/sec (3× improvement) |
| **Complexity** | Low | Medium |
| **Status Quo** | Production | Planned |

---

## Message Flow Cheat Sheet

### Current Flow
```
Orchestrator → test-case-processing queue → Worker → Copilot Studio + OpenAI → Dataverse
                                            (holds slot for 60s)
```

### New Flow
```
Orchestrator → agent-call-queue → AgentWorker → Copilot Studio → Dataverse (pending)
                                  (releases after 10s)
                                                    ↓
                                  evaluator-processing-queue (×N)
                                                    ↓
                                  EvaluatorWorker → OpenAI → Dataverse (completed)
                                  (parallel, 10s each)
```

---

## EvaluationResult State Transitions

### Current
```
[Does not exist] → Completed (778760002)
                → Errored (778760003)
                → NotApplicable (778760004)
```

### Proposed
```
[Does not exist] → Pending (1) → Completed (778760002)
                              → Errored (778760003)
                → NotApplicable (778760004) [immediate, no queue]
```

---

## Key Files Reference

### Planning Documents (Read These First)
1. **Implementation Plan** (50+ pages): `docs/implementation/async-multi-evaluator-architecture-plan.md`
2. **Architecture Comparison**: `docs/implementation/async-architecture-comparison.md`
3. **Planning Summary**: `docs/implementation/planning-summary.md`
4. **This Quick Reference**: `docs/implementation/quick-reference.md`

### Current Implementation
- **Orchestrator**: `backend/AiAgentEvaluation/Functions/OrchestratorFunction.cs`
- **Worker**: `backend/AiAgentEvaluation/Functions/WorkerFunction.cs`
- **Multi-Turn**: `backend/AiAgentEvaluation/Functions/MultiTurnDurableActivities.cs`
- **Infrastructure**: `backend/infrastructure/main.bicep`

### Files to Create (Phase 1)
- **AgentWorker**: `backend/AiAgentEvaluation/Functions/AgentWorkerFunction.cs`
- **EvaluatorWorker**: `backend/AiAgentEvaluation/Functions/EvaluatorWorkerFunction.cs`
- **Tests**: `backend/tests/AiAgentEvaluation.UnitTests/AgentWorkerFunctionTests.cs`
- **Tests**: `backend/tests/AiAgentEvaluation.UnitTests/EvaluatorWorkerFunctionTests.cs`

### Files to Update (Phase 1)
- **Bicep**: `backend/infrastructure/main.bicep` (add 2 queues)
- **Models**: `backend/AiAgentEvaluation/Models/ApiModels.cs` (add AgentCallMessage, EvaluatorJobMessage)
- **Dataverse Interface**: `backend/AiAgentEvaluation/Services/IDataverseService.cs`
- **Dataverse Impl**: `backend/AiAgentEvaluation/Services/DataverseService.cs`
- **Service Bus Interface**: `backend/AiAgentEvaluation/Services/IServiceBusService.cs`
- **Service Bus Impl**: `backend/AiAgentEvaluation/Services/ServiceBusService.cs`

---

## Code Patterns Cheat Sheet

### Creating Pending Results (AgentWorker)
```csharp
// After Copilot Studio call succeeds
foreach (var evaluatorTypeId in message.EvaluatorTypeIds)
{
    // Check if NA
    if (evaluatorRequiresExpectedResponse && string.IsNullOrWhiteSpace(expectedResponse))
    {
        // Create NA result immediately (don't queue)
        await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
        {
            EvaluationRunId = message.EvaluationRunId,
            TestCaseId = message.TestCaseId,
            EvaluatorTypeId = evaluatorTypeId,
            StatusCode = EvaluationResultStatus.NotApplicable,
            Score = 0.0m,
            Passed = false,
            Reasoning = "Evaluator requires expected response but none provided",
            ActualResponse = null
        }, cancellationToken);
        
        await _dataverseService.IncrementCompletedTestCasesAsync(
            message.EvaluationRunId, failed: false, cancellationToken);
        
        continue; // Skip queue
    }
    
    // Create pending result
    var resultId = await _dataverseService.CreateEvaluationResultAsync(new CreateEvaluationResultRequest
    {
        EvaluationRunId = message.EvaluationRunId,
        TestCaseId = message.TestCaseId,
        EvaluatorTypeId = evaluatorTypeId,
        StatusCode = EvaluationResultStatus.Pending, // ← KEY: Status = 1
        ActualResponse = copilotResponse.Response,
        ConversationId = copilotResponse.ConversationId,
        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
        Metadata = copilotResponse.RawActivityJson
    }, cancellationToken);
    
    // Queue evaluator job
    await _serviceBusService.SendEvaluatorJobMessageAsync(new EvaluatorJobMessage
    {
        EvaluationResultId = resultId,
        EvaluationRunId = message.EvaluationRunId
    }, cancellationToken);
}
```

### Processing Evaluator Job (EvaluatorWorker)
```csharp
// Retrieve pending result
var result = await _dataverseService.GetEvaluationResultAsync(
    message.EvaluationResultId, cancellationToken);

// Check if already completed (idempotency)
if (result.StatusCode == EvaluationResultStatus.Completed)
{
    _logger.LogInformation("Result {ResultId} already completed, skipping", result.Id);
    return; // Idempotent
}

// Load related data (test case, turn, evaluator type)
var testCase = await _dataverseService.GetTestCaseAsync(result.TestCaseId, cancellationToken);
var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(
    result.EvaluatorTypeId.Value, cancellationToken);

// Call LLM Judge
var llmJudgeService = await _llmJudgeServiceFactory.CreateJudgeServiceAsync(
    result.EvaluatorTypeId.Value, cancellationToken);

var judgeResponse = await llmJudgeService.EvaluateResponseAsync(new LLMJudgeRequest
{
    EvaluatorTypeId = result.EvaluatorTypeId.Value,
    UserInput = testCase.UserInput, // or testCaseTurn.PromptText
    ExpectedResponse = testCase.ExpectedResponse, // or testCaseTurn.ExpectedResponse
    ActualResponse = result.ActualResponse, // ← From pending result
    ConversationId = result.ConversationId
}, cancellationToken);

// Update result to completed
await _dataverseService.UpdateEvaluationResultAsync(result.Id, new UpdateEvaluationResultRequest
{
    StatusCode = judgeResponse.Success 
        ? EvaluationResultStatus.Completed 
        : EvaluationResultStatus.Errored,
    Score = judgeResponse.Score,
    Passed = judgeResponse.Score >= passingThreshold,
    Reasoning = judgeResponse.Reasoning,
    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
}, cancellationToken);

// Increment completion counter
await _dataverseService.IncrementCompletedTestCasesAsync(
    message.EvaluationRunId, 
    failed: !judgeResponse.Success, 
    cancellationToken);
```

---

## Dataverse Service Changes

### New Interface Methods
```csharp
public interface IDataverseService
{
    // Existing methods...
    
    // NEW: Retrieve single evaluation result with related data
    Task<EvaluationResult?> GetEvaluationResultAsync(Guid resultId, CancellationToken cancellationToken = default);
    
    // NEW: Update existing evaluation result
    Task UpdateEvaluationResultAsync(Guid resultId, UpdateEvaluationResultRequest request, CancellationToken cancellationToken = default);
}

public class UpdateEvaluationResultRequest
{
    public int StatusCode { get; set; }
    public decimal? Score { get; set; }
    public bool? Passed { get; set; }
    public string? Reasoning { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

## Service Bus Service Changes

### New Interface Methods
```csharp
public interface IServiceBusService
{
    // Existing methods...
    
    // NEW: Send agent call message to agent-call-queue
    Task SendAgentCallMessageAsync(AgentCallMessage message, CancellationToken cancellationToken = default);
    
    // NEW: Send evaluator job message to evaluator-processing-queue
    Task SendEvaluatorJobMessageAsync(EvaluatorJobMessage message, CancellationToken cancellationToken = default);
}
```

---

## Infrastructure Changes (Bicep)

### Add Two New Queues
```bicep
// Existing queue (keep for backward compatibility during migration)
resource testCaseProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'test-case-processing'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    requiresDuplicateDetection: false
  }
}

// NEW: Agent call queue
resource agentCallQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'agent-call-queue'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    requiresDuplicateDetection: false
  }
}

// NEW: Evaluator processing queue
resource evaluatorProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'evaluator-processing-queue'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    requiresDuplicateDetection: false
  }
}
```

---

## Monitoring Queries

### Application Insights - Find Pending Results Taking Too Long
```kusto
customMetrics
| where name == "EvaluationResult.PendingDuration"
| where value > 1800000 // 30 minutes in ms
| summarize count() by bin(timestamp, 5m)
| render timechart
```

### Application Insights - Compare Old vs New Flow Performance
```kusto
customMetrics
| where name in ("TestCase.Executed", "AgentCall.ExecutionTime", "EvaluatorJob.ExecutionTime")
| summarize avg(value), p95(value), p99(value) by name, bin(timestamp, 1h)
| render timechart
```

### Dataverse - Find Orphaned Pending Results
```sql
-- Query to find pending results older than 1 hour
SELECT TOP 100
    mcicaps_evaluationresultsid,
    mcicaps_evaluationresultname,
    createdon,
    statuscode
FROM mcicaps_evaluationresults
WHERE statuscode = 1  -- Pending
    AND createdon < DATEADD(hour, -1, GETUTCDATE())
ORDER BY createdon DESC
```

---

## Deployment Configuration

### Hard Cutover Approach (No Feature Flags)
**Decision**: Hard cutover deployment with no backward compatibility

### Environment Variables (local.settings.json)
```json
{
  "Values": {
    // Retry configuration for evaluator workers
    "EVALUATOR_MAX_RETRY_ATTEMPTS": "10",
    "EVALUATOR_RETRY_BASE_DELAY_SECONDS": "5",
    
    // Dead letter queue monitoring
    "DLQ_ALERT_THRESHOLD": "0",  // Alert on ANY message in DLQ
    "DLQ_MONITORING_ENABLED": "true"
  }
}
```

### Code Pattern (OrchestratorFunction)
```csharp
// In OrchestratorFunction.StartEvaluationRun
// NO FEATURE FLAGS - Always use new architecture

if (testCase.IsMultiTurn)
{
    // Start Durable orchestration with agent-call-queue support
    await durableClient.ScheduleNewOrchestrationInstanceAsync(
        "MultiTurnTestCaseOrchestrator",
        new MultiTurnOrchestratorInput
        {
            TestCaseId = testCase.Id,
            DirectLineUrl = directLineUrl,
            EvaluatorTypeIds = evaluatorTypeIds,
            // ... other fields
        });
}
else
{
    // Queue to agent-call-queue for single-turn
    await _serviceBusService.SendAgentCallMessageAsync(
        new AgentCallMessage
        {
            TestCaseId = testCase.Id,
            DirectLineUrl = directLineUrl,
            EvaluatorTypeIds = evaluatorTypeIds,
            // ... other fields
        }, cancellationToken);
}
```

---

## Testing Checklist

### Phase 1: Infrastructure & Functions
- [ ] Deploy infrastructure with 2 new queues to staging
- [ ] Verify queues exist and accessible with Managed Identity
- [ ] Unit test AgentWorkerFunction (mock Copilot Studio, Dataverse, Service Bus)
- [ ] Unit test EvaluatorWorkerFunction (mock Dataverse, LLM Judge)
- [ ] Integration test: Manual message to agent-call-queue → Verify workflow completes
- [ ] Integration test: Verify pending result created correctly
- [ ] Integration test: Verify evaluator job processes and updates result

### Phase 2: Single-Turn Migration
- [ ] Enable feature flag for 1 test case
- [ ] Verify results match old flow
- [ ] Load test: 100 test cases × 5 evaluators
- [ ] Measure throughput improvement (target: 3×)
- [ ] Monitor error rate (must be ≤ old flow)
- [ ] Rollout to 10% of runs
- [ ] Monitor for 48 hours
- [ ] Rollout to 50% of runs
- [ ] Monitor for 48 hours
- [ ] Rollout to 100% of runs

### Phase 3: Multi-Turn (Optional)
- [ ] Update MultiTurnDurableActivities.ExecuteSingleTurn
- [ ] Create pending results after Copilot call
- [ ] Queue evaluator jobs
- [ ] Orchestrator polls for completion
- [ ] Test with 10-turn conversation
- [ ] Verify aggregate result calculation

---

## Troubleshooting Guide

### Issue: Pending results never complete
**Check**:
- Is `evaluator-processing-queue` processing messages?
- Are there errors in EvaluatorWorkerFunction logs?
- Check dead letter queue for failed evaluator jobs

**Fix**: Replay messages from dead letter queue after fixing underlying issue

---

### Issue: Agent call succeeds, but no evaluator jobs queued
**Check**:
- AgentWorkerFunction logs for errors when creating pending results
- Service Bus connection string/Managed Identity permissions
- Dataverse create permissions

**Fix**: Check Application Insights for exceptions, verify RBAC roles

---

### Issue: Evaluator worker retrieves wrong data
**Check**:
- Is GetEvaluationResultAsync returning related data (TestCase, EvaluatorType)?
- Are TestCaseTurnId and TestCaseId correct in pending result?

**Fix**: Verify Dataverse query includes related entities

---

### Issue: Progress tracking shows incorrect counts
**Check**:
- Are pending results counted as completed? (They shouldn't be)
- Is IncrementCompletedTestCasesAsync called only by EvaluatorWorker?
- MonitorFunction query filters for StatusCode = Completed (778760002)

**Fix**: Update MonitorFunction to only count Completed, Errored, and NotApplicable results

---

## Open Questions Tracker

| Question | Options | Recommended | Decided | Notes |
|----------|---------|-------------|---------|-------|
| Multi-turn approach? | A: Migrate<br>B: Keep as-is | **B** | ☐ | Defer to Phase 3 |
| Pending cleanup? | A: Timer function<br>B: MonitorFunction<br>C: No cleanup | **B** | ☐ | Extend existing pattern |
| Feature flag? | A: Simple on/off<br>B: Per-run config<br>C: Percentage rollout | **C** | ☐ | Gradual migration |

---

## Success Metrics Dashboard

Track these metrics in Application Insights:

| Metric | Baseline (Old) | Target (New) | Status |
|--------|----------------|--------------|--------|
| Throughput (units/sec) | 7.6 | 20+ | ☐ |
| Time-to-first-result (sec) | 20 | 20 | ☐ |
| Error rate (%) | 5% | ≤5% | ☐ |
| Worker slot utilization (%) | 60% | 95% | ☐ |
| Cost per unit ($) | $0.03 | $0.025 | ☐ |

---

## Phase Completion Checklist

### Phase 1: Infrastructure & Functions ✓
- [ ] Queues deployed
- [ ] Functions implemented
- [ ] Unit tests pass (>80% coverage)
- [ ] Integration tests pass
- [ ] Documentation updated

### Phase 2: Single-Turn Migration ✓
- [ ] Feature flag implemented
- [ ] 10% rollout successful
- [ ] 50% rollout successful
- [ ] 100% rollout successful
- [ ] Performance targets met

### Phase 3: Multi-Turn (Optional) ✓
- [ ] Orchestrator updated
- [ ] Multi-turn tests pass
- [ ] Aggregate calculation correct
- [ ] Rollout complete

### Phase 4: Deprecation ✓
- [ ] Old queue removed
- [ ] WorkerFunction removed
- [ ] Documentation updated
- [ ] No production issues

---

**Quick Reference Version**: 1.0  
**Last Updated**: November 18, 2025  
**Status**: Ready for implementation
