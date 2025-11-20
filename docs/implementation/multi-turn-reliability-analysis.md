# Multi-Turn Reliability Analysis & Architecture Options

**Date**: November 19, 2025  
**Status**: Analysis & Recommendation  
**Priority**: CRITICAL - Multi-turn tests consistently getting stuck

---

## Executive Summary

**Current State**: Multi-turn test cases using Durable Functions orchestrations are consistently getting stuck and not completing, while single-turn tests using pure Service Bus queues work reliably.

**Root Cause**: Durable Functions add complexity, hidden failure modes, and difficult observability that make debugging and recovery challenging.

**Recommendation**: **Option 3 - Pure Service Bus with Dataverse State** - Eliminate Durable Functions entirely and use the proven Service Bus pattern that works for single-turn.

---

## Current Architecture Problems

### Critical Issues with Durable Functions Approach

#### 1. **Silent Orchestration Failures**
**Problem**: Orchestrations can fail to start or stop mid-execution without surfacing errors clearly.

**Evidence**:
```csharp
// OrchestratorFunction.cs lines 242-272
try {
    var scheduleResult = await durableClient.ScheduleNewOrchestrationInstanceAsync(
        "MultiTurnTestCaseOrchestrator",
        orchestratorInput,
        new StartOrchestrationOptions { InstanceId = instanceId });
    
    _logger.LogWarning("=== ORCHESTRATION SCHEDULED SUCCESSFULLY ===");
} catch (Exception ex) {
    _logger.LogError(ex, "=== ORCHESTRATION SCHEDULING FAILED ===");
    // No cleanup needed since aggregates weren't created yet
    // Continue with other test cases ← SILENT FAILURE
}
```

**Impact**: Failed orchestrations are logged but evaluation continues, leaving incomplete results.

#### 2. **Complex State Management**
**Problem**: Durable Functions maintain state in Azure Storage (tables/blobs), creating multiple failure points.

**Failure Modes**:
- Storage account throttling (429 errors)
- Storage unavailability during Azure incidents
- State corruption requiring manual intervention
- Replay logic bugs causing incorrect behavior

**Example**: Orchestrator state stored in `DurableTaskPartitions` table. If storage is unavailable when orchestrator tries to checkpoint, the entire orchestration fails.

#### 3. **Race Conditions in Idempotency Checks**
**Problem**: Checking if orchestration exists before starting creates race conditions.

```csharp
// OrchestratorFunction.cs lines 197-215
var orchestrationMetadata = await durableClient.GetInstanceAsync(instanceId);
if (orchestrationMetadata != null) {
    if (orchestrationMetadata.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
        orchestrationMetadata.RuntimeStatus == OrchestrationRuntimeStatus.Pending) {
        _logger.LogWarning("Orchestration already running, skipping");
        continue; // ← Can miss orchestrations that haven't checkpointed yet
    }
}
```

**Impact**: Can create duplicate orchestrations OR skip orchestrations that should run.

#### 4. **Difficult Observability**
**Problem**: Orchestration status is spread across multiple sources.

**What you need to check**:
- Application Insights logs (replay-safe logs hide actual execution)
- Azure Storage tables (instance state)
- Dataverse (aggregate results)
- Service Bus queues (evaluator jobs)

**Reality**: No single view shows "which multi-turn tests are actually running right now."

#### 5. **No Orchestration-Level Retries**
**Problem**: If orchestration fails to start or gets abandoned, there's no retry mechanism.

**Service Bus has**: MaxDeliveryCount (10 retries) + Dead Letter Queue  
**Durable Functions has**: Manual intervention required if orchestration fails

#### 6. **Aggregate Placeholder Timing Issue**
**Problem**: Aggregates created AFTER orchestration starts, but orchestration can fail.

```csharp
// MultiTurnDurableOrchestrator.cs lines 116-126
foreach (var evaluatorTypeId in input.EvaluatorTypeIds) {
    var aggregateId = await context.CallActivityAsync<Guid>(
        "CreatePlaceholderAggregate",
        new CreatePlaceholderAggregateInput { ... });
    
    aggregateResultIds[evaluatorTypeId] = aggregateId;
}
```

**Impact**: If orchestration fails during execution, you have placeholder aggregates that never get completed (orphaned pending results).

#### 7. **Async Complexity with Polling**
**Problem**: Multi-turn uses async architecture (queues evaluator jobs) but then orchestrator must poll for completion.

```csharp
// MultiTurnDurableActivities.cs lines 156-158
// After queuing evaluator jobs:
// 3. Polls Dataverse until all evaluators complete (with 10-minute timeout)
```

**Impact**: Orchestrator holds state while waiting for async work, defeating the purpose of async architecture.

---

## Why Single-Turn Works Reliably

### Service Bus Queue Pattern (Currently Used for Single-Turn)

```
OrchestratorFunction
  ↓ Queue message
agent-call-queue
  ↓ Trigger
AgentWorkerFunction
  ↓ Call Copilot
  ↓ Create pending results
  ↓ Queue evaluator jobs
evaluator-processing-queue
  ↓ Trigger (parallel)
EvaluatorWorkerFunction (×N)
  ↓ Evaluate
  ↓ Update result
  ↓ Increment counter
```

### Why This Works

✅ **Visibility**: Message in queue = work pending. Empty queue = all done.  
✅ **Retries**: Service Bus handles automatically (10 attempts with exponential backoff)  
✅ **Dead Letter**: Failed messages go to DLQ for investigation (not lost)  
✅ **Atomicity**: Each message is a unit of work (no complex state)  
✅ **Progress Tracking**: Queue depth + completed results = progress  
✅ **Observability**: Service Bus metrics + Application Insights = complete picture  
✅ **Scalability**: 1000+ concurrent workers, auto-scaling  
✅ **Proven**: This pattern works perfectly for single-turn tests

---

## Recommended Architecture: Pure Service Bus with Dataverse State

### Approach
**Eliminate Durable Functions entirely.** Use the EXACT same pattern that works for single-turn, adapted for multi-turn context.

### Core Insight
Multi-turn is just "single-turn executed N times with shared conversation context." The ONLY difference is:
1. Reuse conversationId from Turn 1 in subsequent turns
2. Calculate aggregate result after all turns complete

**Both can be handled with Service Bus queues + Dataverse state.**

### Architecture

```
OrchestratorFunction
  ↓ For each multi-turn test case:
      ↓ Create execution context in Dataverse (ONE per test case)
      ↓ Create aggregate placeholders (ONE per evaluator)
      ↓ Queue Turn 1 message (ONE message per test case, not per evaluator)
      
multi-turn-agent-call-queue
  ↓ Trigger
MultiTurnAgentWorkerFunction
  ↓ Get turn details from Dataverse
  ↓ Get execution context (with conversationId if not Turn 1)
  ↓ Call Copilot Studio (with conversationId for Turn 2+)
  ↓ IF Turn 1: Store conversationId in context
  ↓ For EACH evaluator:
      ↓ Create pending result
      ↓ Queue evaluator job
  ↓ IF last turn:
      ↓ Mark context as completed
  ↓ ELSE:
      ↓ Queue next turn message
      
multi-turn-agent-call-queue (Turn 2)
  ↓ Trigger... (repeat for each turn)

evaluator-processing-queue
  ↓ Trigger (parallel, existing)
EvaluatorWorkerFunction (existing, NO CHANGES)
  ↓ Evaluate turn
  ↓ Update result

AggregateEvaluationProcessorFunction (timer, existing, NO CHANGES)
  ↓ Every 30 seconds
  ↓ Find pending aggregates with all turns completed
  ↓ Calculate aggregate
  ↓ Update aggregate result
  ↓ Increment completion counter
```

**Key Simplification**: One message per test case per turn, NOT per evaluator. The agent worker fans out to all evaluators after calling Copilot once.

### Data Model Changes

**NO NEW TABLES OR FIELDS REQUIRED!** ✅

All context is passed in Service Bus messages:
- **Conversation ID**: Passed from Turn 1 response to Turn 2+ messages
- **DirectLine Token**: Passed from Turn 1 response to Turn 2+ messages  
- **Turn Sequence**: Tracked in message
- **Total Turns**: Tracked in message

Existing `mcicaps_conversationid` field in evaluation results already stores per-turn conversation ID, which is sufficient for reporting and analysis.

### Message Schema

#### MultiTurnAgentCallMessage
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "evaluatorTypeIds": ["guid", "guid"],  // All evaluators for this test case
  "turnSequence": 1,
  "totalTurns": 3,
  "directLineUrl": "string",
  "conversationId": "string?",   // null for Turn 1, populated from Copilot response for Turn 2+
  "directLineToken": "string?",  // null for Turn 1, populated from Copilot response for Turn 2+
  "passingThreshold": 0.7,
  "retryCount": 0                // Incremented on each retry, max 3 (configurable)
}
```

**Key Design**:
- All state flows through messages (no Dataverse state table needed)
- Turn 1 message has `conversationId` and `directLineToken` as null
- Turn 2+ messages receive these values from previous turn's Copilot response
- `retryCount` tracks exponential backoff retries for Copilot failures
- Retry delay formula: `(retryCount + 1) × RetryDelaySeconds` (e.g., 30s, 60s, 90s)
- `AggregateEvaluationProcessorFunction` (existing timer) handles aggregate calculation automatically

### Function Implementations

#### 1. OrchestratorFunction Changes
```csharp
// For each multi-turn test case:
foreach (var testCase in multiTurnTestCases) {
    // Create aggregate placeholders (ONE per evaluator)
    foreach (var evaluatorType in evaluatorTypes) {
        await _dataverseService.CreateEvaluationResultAsync(
            new CreateEvaluationResultRequest {
                EvaluationRunId = request.EvaluationRunId,
                TestCaseId = testCase.Id,
                EvaluatorTypeId = evaluatorType.Id,
                IsAggregate = true,
                StatusCode = EvaluationResultStatus.Pending
            }, cancellationToken);
    }
    
    // Queue ONE message for Turn 1 (includes all evaluators)
    await _serviceBusService.SendMultiTurnAgentCallMessageAsync(
        new MultiTurnAgentCallMessage {
            EvaluationRunId = request.EvaluationRunId,
            TestCaseId = testCase.Id,
            EvaluatorTypeIds = evaluatorTypes.Select(e => e.Id).ToList(),
            TurnSequence = 1,
            TotalTurns = testCase.Turns.Count,
            DirectLineUrl = directLineUrl,
            ConversationId = null,  // Will be populated after Turn 1
            DirectLineToken = null, // Will be populated after Turn 1
            PassingThreshold = passingThreshold,
            RetryCount = 0
        }, cancellationToken);
}
```

#### 2. MultiTurnAgentWorkerFunction (NEW)
```csharp
[Function("ProcessMultiTurnAgentCall")]
public async Task ProcessMultiTurnAgentCall(
    [ServiceBusTrigger("multi-turn-agent-call-queue", Connection = "ServiceBusNamespace")] 
    string messageBody,
    CancellationToken cancellationToken)
{
    var message = JsonSerializer.Deserialize<MultiTurnAgentCallMessage>(messageBody);
    var stopwatch = Stopwatch.StartNew();
    
    _logger.LogInformation(
        "Processing multi-turn agent call: TestCase={TestCaseId}, Turn={TurnSequence}/{TotalTurns}, Evaluators={Count}, RetryCount={RetryCount}",
        message.TestCaseId, message.TurnSequence, message.TotalTurns, message.EvaluatorTypeIds.Count, message.RetryCount);
    
    // 1. Get turn details from Dataverse
    var turn = await _dataverseService.GetTestCaseTurnAsync(
        message.TestCaseId, message.TurnSequence, cancellationToken);
    
    // 2. Call Copilot Studio (use conversationId/token from message for Turn 2+)
    var copilotResponse = await _copilotStudioService.SendMessageAsync(
        message.DirectLineUrl,
        turn.PromptText,
        message.ConversationId,  // null for Turn 1, populated for Turn 2+
        message.DirectLineToken, // null for Turn 1, populated for Turn 2+
        cancellationToken);
    
    if (!copilotResponse.Success) {
        _logger.LogWarning(
            "Copilot call failed for test case {TestCaseId}, turn {TurnSequence}: {ErrorMessage}, RetryCount={RetryCount}",
            message.TestCaseId, message.TurnSequence, copilotResponse.ErrorMessage, message.RetryCount);
        
        // Implement exponential backoff retry: (retryCount + 1) * RetryDelaySeconds
        if (message.RetryCount < _maxRetryCount) {
            var baseDelaySeconds = _settings.Evaluation.RetryDelaySeconds;
            var delaySeconds = (message.RetryCount + 1) * baseDelaySeconds;
            var delay = TimeSpan.FromSeconds(delaySeconds);
            var scheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay);
            
            _logger.LogInformation(
                "Re-queuing multi-turn agent call for test case {TestCaseId}, turn {TurnSequence} with retry count {RetryCount}, delay {DelaySeconds}s, scheduled for {ScheduledTime}",
                message.TestCaseId,
                message.TurnSequence,
                message.RetryCount + 1,
                delaySeconds,
                scheduledEnqueueTime);
            
            // Re-queue the message with incremented retry count
            await _serviceBusService.SendMultiTurnAgentCallMessageAsync(
                new MultiTurnAgentCallMessage {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    EvaluatorTypeIds = message.EvaluatorTypeIds,
                    TurnSequence = message.TurnSequence,
                    TotalTurns = message.TotalTurns,
                    DirectLineUrl = message.DirectLineUrl,
                    ConversationId = message.ConversationId,
                    DirectLineToken = message.DirectLineToken,
                    PassingThreshold = message.PassingThreshold,
                    RetryCount = message.RetryCount + 1
                },
                scheduledEnqueueTime,
                cancellationToken);
            
            return;
        }
        
        // Max retries exceeded, create error results for ALL evaluators
        _logger.LogError(
            "Copilot call failed for test case {TestCaseId}, turn {TurnSequence} after {MaxRetries} retries: {ErrorMessage}",
            message.TestCaseId, message.TurnSequence, _maxRetryCount, copilotResponse.ErrorMessage);
        
        await HandleMultiTurnError(message, turn, copilotResponse.ErrorMessage, stopwatch, cancellationToken);
        return;
    }
    
    // 3. For EACH evaluator: Create pending result + queue evaluator job
    foreach (var evaluatorTypeId in message.EvaluatorTypeIds) {
        try {
            // Check for NA scenario
            var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(
                evaluatorTypeId, cancellationToken);
            
            if (evaluatorType?.RequiresExpectedResponse == true && 
                string.IsNullOrWhiteSpace(turn.ExpectedResponse)) {
                // Create NA result immediately (don't queue)
                await _dataverseService.CreateEvaluationResultAsync(
                    new CreateEvaluationResultRequest {
                        EvaluationRunId = message.EvaluationRunId,
                        TestCaseId = message.TestCaseId,
                        TestCaseTurnId = turn.Id,
                        EvaluatorTypeId = evaluatorTypeId,
                        StatusCode = EvaluationResultStatus.NotApplicable,
                        Score = 0.0m,
                        Passed = false,
                        Reasoning = $"Evaluator '{evaluatorType.Name}' requires expected response but none provided",
                        ActualResponse = copilotResponse.Response,
                        ConversationId = copilotResponse.ConversationId,
                        McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                        Metadata = copilotResponse.RawActivityJson
                    }, cancellationToken);
                
                await _dataverseService.IncrementCompletedTestCasesAsync(
                    message.EvaluationRunId, failed: false, cancellationToken);
                continue; // Skip queue for NA
            }
            
            // Create pending result
            var resultId = await _dataverseService.CreateEvaluationResultAsync(
                new CreateEvaluationResultRequest {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    TestCaseTurnId = turn.Id,
                    EvaluatorTypeId = evaluatorTypeId,
                    StatusCode = EvaluationResultStatus.Pending,
                    ActualResponse = copilotResponse.Response,
                    ConversationId = copilotResponse.ConversationId,
                    McsExecutionTimeMs = copilotResponse.ExecutionTimeMs,
                    Metadata = copilotResponse.RawActivityJson
                }, cancellationToken);
            
            // Queue evaluator job
            await _serviceBusService.SendEvaluatorJobMessageAsync(
                new EvaluatorJobMessage {
                    EvaluationResultId = resultId,
                    EvaluationRunId = message.EvaluationRunId
                }, cancellationToken);
        }
        catch (Exception ex) {
            _logger.LogError(ex, 
                "Error processing evaluator {EvaluatorId} for turn {TurnSequence}",
                evaluatorTypeId, message.TurnSequence);
            // Continue with other evaluators
        }
    }
    
    // 4. Decision: Queue next turn OR we're done
    if (message.TurnSequence < message.TotalTurns) {
        // Queue NEXT turn (with conversationId and token from this response)
        await _serviceBusService.SendMultiTurnAgentCallMessageAsync(
            new MultiTurnAgentCallMessage {
                EvaluationRunId = message.EvaluationRunId,
                TestCaseId = message.TestCaseId,
                EvaluatorTypeIds = message.EvaluatorTypeIds,
                TurnSequence = message.TurnSequence + 1,
                TotalTurns = message.TotalTurns,
                DirectLineUrl = message.DirectLineUrl,
                ConversationId = copilotResponse.ConversationId,      // Pass to next turn
                DirectLineToken = copilotResponse.DirectLineToken,    // Pass to next turn
                PassingThreshold = message.PassingThreshold,
                RetryCount = 0  // Reset retry count for next turn
            }, cancellationToken);
        
        _logger.LogInformation("Queued turn {NextTurn} of {TotalTurns}",
            message.TurnSequence + 1, message.TotalTurns);
    } else {
        // Last turn completed - aggregates will be calculated by timer function
        _logger.LogInformation(
            "Completed all {TotalTurns} turns for test case {TestCaseId} - aggregates will be calculated by AggregateEvaluationProcessorFunction",
            message.TotalTurns, message.TestCaseId);
    }
}

private async Task HandleMultiTurnError(
    MultiTurnAgentCallMessage message,
    TestCaseTurn turn,
    string errorMessage,
    Stopwatch stopwatch,
    CancellationToken cancellationToken)
{
    // Create error results for ALL evaluators (agent call failed after max retries)
    foreach (var evaluatorTypeId in message.EvaluatorTypeIds) {
        try {
            await _dataverseService.CreateEvaluationResultAsync(
                new CreateEvaluationResultRequest {
                    EvaluationRunId = message.EvaluationRunId,
                    TestCaseId = message.TestCaseId,
                    TestCaseTurnId = turn.Id,
                    EvaluatorTypeId = evaluatorTypeId,
                    StatusCode = EvaluationResultStatus.Errored,
                    Score = 0.0m,
                    Passed = false,
                    ErrorMessage = $"Agent call failed after {_maxRetryCount} retries: {errorMessage}",
                    Reasoning = "Agent call failed",
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds
                }, cancellationToken);
            
            await _dataverseService.IncrementCompletedTestCasesAsync(
                message.EvaluationRunId, failed: true, cancellationToken);
        }
        catch (Exception ex) {
            _logger.LogError(ex, 
                "Error creating error result for evaluator {EvaluatorId}",
                evaluatorTypeId);
        }
    }
}
```

#### 3. AggregateEvaluationProcessorFunction (EXISTING - NO CHANGES)
This function already handles aggregate calculation! It:
- Runs every 30 seconds (timer trigger)
- Finds pending aggregates
- Checks if all turn results are completed
- Calculates aggregate score
- Updates aggregate result
- Increments completion counter

**No changes needed!** It will work with the new Service Bus approach.

### Infrastructure Changes

#### New Resources
1. **Service Bus Queue**: `multi-turn-agent-call-queue`
   - Same configuration as existing queues
   - MaxDeliveryCount: 10
   - LockDuration: PT5M
   - Dead letter queue enabled

2. **Dataverse Table** (Option 3B): `mcicaps_multiturnexecutioncontext`
   - OR add fields to existing tables (Option 3A)

#### Removed Resources
1. **Azure Storage**: Durable Functions state (tables/blobs) - NO LONGER NEEDED
2. **host.json**: Remove `durableTask` configuration section

### Pros
- ✅ **PROVEN PATTERN**: Uses exact same approach that works for single-turn
- ✅ **SIMPLICITY**: No Durable Functions complexity, replay logic, or state management
- ✅ **RELIABILITY**: Service Bus retries + Dead Letter Queue + Exponential backoff for transient failures
- ✅ **VISIBILITY**: Queue depth = pending work. Empty queue = done.
- ✅ **OBSERVABILITY**: Service Bus metrics + Application Insights (no replay confusion)
- ✅ **SCALABILITY**: Same 1000+ concurrent workers as single-turn
- ✅ **DEBUGGABILITY**: Logs are real execution (not replay-safe abstractions)
- ✅ **IDEMPOTENCY**: Service Bus message deduplication
- ✅ **COST**: Remove Durable Functions storage transactions
- ✅ **MAINTENANCE**: One less Azure service to manage
- ✅ **CONSISTENCY**: Single pattern for all test types (single-turn and multi-turn)
- ✅ **RESILIENCE**: Application-level retries with exponential backoff (like evaluator workers)

### Cons
- ❌ **MIGRATION EFFORT**: Must replace existing Durable Functions code
- ❌ **SEQUENTIAL TURNS**: Turns execute one-at-a-time (but they MUST be sequential anyway!)
- ❌ **DATAVERSE READS**: Additional reads to get turn details (minimal cost)
- ❌ **NEW QUEUE**: Need to provision `multi-turn-agent-call-queue`

### Migration Complexity
**Medium-High** - Complete replacement of multi-turn execution path, but straightforward implementation.

### Estimated Implementation Time
**1-2 weeks**
- Week 1: Implement new queue, function, context management, testing
- Week 2: Staging validation, production deployment

### Risk Assessment
**LOW RISK** - Uses proven Service Bus pattern. If it works for single-turn, it will work for multi-turn.

---

## Why This Architecture?

1. **Proven Reliability**: Uses THE EXACT pattern that works flawlessly for single-turn. Why use a different approach for multi-turn?

2. **Simplicity Wins**: Multi-turn is not fundamentally different from single-turn. It's just "call Copilot N times with shared context." Service Bus handles this perfectly.

3. **Eliminate Hidden Failures**: Durable Functions orchestrations fail silently. Service Bus failures are visible (dead letter queue).

4. **One Pattern to Rule Them All**: Single codebase pattern for all test types. Easier to understand, maintain, debug.

5. **Lower Risk**: If single-turn works, multi-turn will work with the same pattern.

6. **Faster Implementation**: 1-2 weeks vs 3-4 weeks for Durable Functions fixes.

7. **Efficient Message Design**: One message per test case per turn (not per evaluator), reducing queue overhead by N× (where N = evaluator count).

### What You Get

✅ **Multi-turn conversations work exactly like single-turn** (queue → worker → evaluate)  
✅ **Conversation context preserved** (stored in Dataverse, passed via messages)  
✅ **Sequential turn execution** (Turn 1 completes before Turn 2 starts)  
✅ **Parallel evaluations** (multiple evaluators process same turn simultaneously)  
✅ **Automatic retries** (Service Bus handles all retries + dead letter)  
✅ **Clear progress tracking** (queue depth + result count = progress)  
✅ **Easy debugging** (Application Insights logs are actual execution)  
✅ **No orchestration mysteries** (no replay, no instance IDs, no state corruption)

### What You Lose

❌ **Durable Functions orchestration** (good riddance!)  
❌ **Complexity** (also good riddance!)  
❌ **Hidden failures** (definitely good riddance!)

---

## Implementation Roadmap

### Phase 1: Infrastructure (1 day)
1. Add `multi-turn-agent-call-queue` to `main.bicep`
2. Deploy queue to staging
3. No Dataverse changes needed!

### Phase 2: Core Implementation (2-3 days)
1. Create `MultiTurnAgentCallMessage` model in `Models/ApiModels.cs` (with `RetryCount` field)
2. Create `MultiTurnAgentWorkerFunction.cs` (with exponential backoff retry logic)
3. Update `OrchestratorFunction.cs` multi-turn routing
4. Update `IServiceBusService` and `ServiceBusService` with multi-turn methods (support scheduled enqueue time)
5. Verify `AggregateEvaluationProcessorFunction` works (should be unchanged)
6. Add `GetTestCaseTurnAsync` to `IDataverseService` if not already present

### Phase 3: Testing (2-3 days)
1. Unit tests for new function (mock dependencies)
2. Integration tests (end-to-end multi-turn flow)
3. Load test (verify performance)
4. Error scenarios (Copilot failures, Service Bus retries)

### Phase 4: Staging Validation (2 days)
1. Deploy to staging
2. Run comprehensive test suite
3. Monitor Application Insights
4. Verify no stuck orchestrations
5. Check dead letter queue

### Phase 5: Cleanup & Production (2 days)
1. **Remove Durable Functions code**:
   - Delete `MultiTurnDurableOrchestrator.cs`
   - Delete `MultiTurnDurableActivities.cs`
   - Remove orchestration routing from `OrchestratorFunction.cs`
2. **Remove infrastructure**:
   - Remove `durableTask` section from `host.json`
   - No need to delete storage (can reuse for function state)
3. Deploy to production
4. Monitor closely for 48 hours

### Phase 6: Documentation Updates (1 day)
1. Update `docs/ARCHITECTURE.md` (remove Durable Functions section)
2. Update `docs/prd/PRD-Agent-Evaluation-Function.md`
3. Update `docs/stories/06-multi-turn-support.md` (new implementation notes)
4. Create `docs/implementation/multi-turn-service-bus-migration.md`

**Total Estimated Time**: 8-10 days (1.5 weeks) - even faster without Dataverse table!

---

## Next Steps

1. ✅ **Architecture decided**: Pure Service Bus approach
2. **Begin Phase 1**: Infrastructure changes (queue + Dataverse table)
3. **Phase 2**: Implement MultiTurnAgentWorkerFunction
4. **Phase 3**: Testing and validation
5. **Phase 4**: Remove Durable Functions code
6. **Phase 5**: Production deployment

---

## Design Decisions Made

1. ✅ **Message Efficiency**: One message per test case per turn (not per evaluator) - reduces message volume significantly
2. ✅ **Stateless Architecture**: All context flows through messages (no Dataverse state table needed) - maximum simplicity
3. ✅ **Conversation Context**: Passed from turn to turn via Service Bus messages
4. ✅ **DirectLine Token**: Passed in queue messages only (never persisted) - security best practice
5. ✅ **Existing Aggregation**: Reuse `AggregateEvaluationProcessorFunction` timer function - no new code needed
6. ✅ **Zero Schema Changes**: No new Dataverse tables or fields required!

---

**Prepared by**: GitHub Copilot  
**Date**: November 19, 2025  
**Status**: Ready for Review  
**Next Action**: Schedule architecture review meeting
