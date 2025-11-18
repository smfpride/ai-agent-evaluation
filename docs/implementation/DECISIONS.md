# Implementation Decisions - Async Multi-Evaluator Architecture

**Date**: November 18, 2025  
**Status**: ✅ All Decisions Approved - Ready for Implementation  
**Decision Maker**: User (Product Owner)

---

## Executive Summary

All critical architectural decisions have been made. The system will implement a **hard cutover** to the new async multi-evaluator architecture with **robust retry guarantees** and **multi-turn support included** from day one.

---

## Decision 1: Multi-Turn Migration Approach

### Question
Should the async architecture be applied to multi-turn tests, or should multi-turn continue using the current inline evaluation approach?

### Options Considered
- **Option A**: Migrate multi-turn to async architecture (complex Durable Functions coordination)
- **Option B**: Keep multi-turn as-is (simpler, lower risk)

### ✅ Decision: **Option A - Migrate Multi-Turn**

**Rationale**:
- Consistency: Both single-turn and multi-turn use same architecture
- Performance: Multi-turn benefits from same 5-10× throughput improvements
- Maintainability: Single code path easier to maintain than two different approaches
- User experience: All evaluation types get same performance benefits

**Implications**:
- ✅ MultiTurnDurableActivities updated to use agent-call-queue and evaluator-processing-queue
- ✅ Orchestrator adds polling logic to wait for async evaluations to complete
- ✅ ExecuteSingleTurn activity creates pending results and queues evaluator jobs
- ✅ Increased complexity in orchestrator (polling + timeout handling)
- ✅ All evaluation types included in Phase 1-2 (no separate Phase 3)

**Implementation Impact**:
- Extends Phase 2 development by ~3 days
- Requires additional integration tests for multi-turn coordination
- Orchestrator must poll Dataverse to check evaluation completion status
- Timeout handling: If evaluations don't complete within 10 minutes, mark aggregate as Errored

---

## Decision 2: Pending Result Handling Strategy

### Question
How should the system handle pending results that may not complete (orphaned results)?

### Options Considered
- **Option A**: Automatic cleanup job (timer function queries and marks old pending results as errored)
- **Option B**: Extend MonitorFunction to detect and cleanup orphaned results
- **Option C**: Accept orphaned results (no cleanup)

### ✅ Decision: **Robust Retry - No Orphaned Results**

**Rationale**:
- System guarantee: Every pending result MUST eventually become Completed, Errored, or NotApplicable
- Reliability: Retry with exponential backoff ensures transient failures don't create orphans
- Operational simplicity: No cleanup jobs needed if system never creates orphans
- Data integrity: No partial results in Dataverse

**Implications**:
- ✅ Implement exponential backoff retry logic in both AgentWorker and EvaluatorWorker
- ✅ Configure Service Bus DeliveryCount max 10 (system retries message up to 10 times)
- ✅ Function-level retries: 3 attempts with delays (5s, 15s, 45s)
- ✅ Only mark as Errored after exhausting ALL retries (10 Service Bus × 3 function = 30 total attempts)
- ✅ Dead letter queue monitored 24/7 with Application Insights alerts (DLQ depth > 0)
- ✅ Manual intervention required for dead-lettered messages (investigate root cause, fix, replay)

**Retry Logic Details**:

**Agent Worker**:
```
Attempt 1 → 5s delay → Attempt 2 → 15s delay → Attempt 3 → 45s delay → Fail
    ↓
Service Bus redelivers (backoff: 1s, 2s, 4s, 8s, ...)
    ↓
After 10 Service Bus deliveries → Dead Letter Queue → ALERT
```

**Evaluator Worker**:
```
Same retry pattern as Agent Worker
```

**Dead Letter Queue Handling**:
- Application Insights alert triggers on DLQ depth > 0
- On-call engineer investigates:
  - Check Application Insights for exceptions/errors
  - Review Copilot Studio or Azure OpenAI service health
  - Check Dataverse connectivity
  - Verify Managed Identity permissions
- After fixing root cause: Manually replay messages from DLQ
- Messages replay through normal queue (same retry logic applies)

**No Cleanup Job Needed**:
- System guarantees eventual completion or explicit error state
- No orphaned pending results possible (Service Bus ensures delivery or dead letter)
- Simpler architecture: No timer functions, no background cleanup processes

---

## Decision 3: Deployment Strategy

### Question
Should the new architecture be rolled out gradually with feature flags, or deployed as a hard cutover?

### Options Considered
- **Option A**: Feature flag with percentage-based rollout (10% → 50% → 100%)
- **Option B**: Per-run configuration in Dataverse (flexible but complex)
- **Option C**: Hard cutover (single deployment replaces old system)

### ✅ Decision: **Option C - Hard Cutover**

**Rationale**:
- Clean codebase: No feature flag logic, no dual-mode support
- Faster implementation: No need to maintain backward compatibility
- Easier maintenance: Single code path (new architecture only)
- Reduced complexity: No routing logic between old and new flows
- Confident in testing: Thorough staging validation removes need for gradual rollout

**Implications**:
- ✅ Remove WorkerFunction.cs in same deployment as new workers
- ✅ Remove test-case-processing queue from main.bicep
- ✅ Remove ProcessTestCaseMessage model (no longer used)
- ✅ Single deployment replaces entire evaluation execution path
- ❌ NO feature flags in code (cleaner implementation)
- ❌ NO backward compatibility (cannot run old and new simultaneously)
- ⚠️ Requires comprehensive staging validation before production

**Deployment Requirements**:
1. **Pre-Deployment**:
   - Ensure NO evaluation runs in progress
   - MonitorFunction must detect and fail any runs started before deployment
   - Complete staging validation (24-hour soak test)
   
2. **Deployment Window**:
   - Schedule during low-usage hours
   - Deploy infrastructure first (queues)
   - Deploy function app second (new workers)
   - Verify deployment with smoke tests immediately
   
3. **Post-Deployment**:
   - Run smoke tests (10 single-turn, 5 multi-turn)
   - Monitor Application Insights for 48 hours
   - Watch dead letter queue depth (must be 0)
   - Track error rates and throughput metrics

**Risk Mitigation**:
- Thorough staging testing (unit + integration + load + soak)
- Deployment during low-usage window
- Immediate smoke tests post-deployment
- Rollback plan: Keep backup of old code, redeploy if critical issues within 24 hours
- 24/7 on-call coverage for first week post-deployment

**No Backward Compatibility Needed**:
- Cleaner codebase without legacy code
- Single execution path easier to debug
- No confusion about which flow is active
- Faster bug fixes (no need to update two code paths)

---

## Summary of Key Changes

### Architecture Changes
| Component | Before | After |
|-----------|--------|-------|
| **Queues** | 1 (test-case-processing) | 2 (agent-call-queue + evaluator-processing-queue) |
| **Workers** | 1 (WorkerFunction) | 2 (AgentWorker + EvaluatorWorker) |
| **Single-Turn** | Synchronous (agent + evals inline) | Asynchronous (agent → pending → evals) |
| **Multi-Turn** | Synchronous (inline in activity) | Asynchronous (same as single-turn) |
| **Retry Strategy** | Service Bus only | Service Bus + function-level exponential backoff |
| **Error Handling** | All-or-nothing per test | Granular per evaluator |

### Code Changes
| File | Action | Purpose |
|------|--------|---------|
| `WorkerFunction.cs` | ❌ **DELETE** | Replaced by AgentWorker + EvaluatorWorker |
| `ProcessTestCaseMessage` | ❌ **DELETE** | Replaced by AgentCallMessage + EvaluatorJobMessage |
| `AgentWorkerFunction.cs` | ✅ **CREATE** | Handles Copilot Studio calls |
| `EvaluatorWorkerFunction.cs` | ✅ **CREATE** | Handles LLM Judge evaluations |
| `OrchestratorFunction.cs` | ✏️ **UPDATE** | Routes to agent-call-queue |
| `MultiTurnDurableActivities.cs` | ✏️ **UPDATE** | Uses new queue architecture |
| `MultiTurnDurableOrchestrator.cs` | ✏️ **UPDATE** | Polls for evaluation completion |
| `main.bicep` | ✏️ **UPDATE** | Remove old queue, add 2 new queues |

### Infrastructure Changes
| Resource | Before | After |
|----------|--------|-------|
| `test-case-processing` queue | ✅ Exists | ❌ **REMOVED** |
| `agent-call-queue` | ❌ Does not exist | ✅ **CREATED** |
| `evaluator-processing-queue` | ❌ Does not exist | ✅ **CREATED** |

### Timeline Changes
| Phase | Original | Updated | Change |
|-------|----------|---------|--------|
| Phase 1 | 2 weeks | 2 weeks | Same |
| Phase 2 | 2 weeks (single-turn only) | 1 week (all types, hard cutover) | ✅ Faster |
| Phase 3 | TBD (multi-turn optional) | 1 week (staging + production) | ✅ Included |
| Phase 4 | 1 week (deprecation) | N/A (no deprecation needed) | ✅ Eliminated |
| **Total** | **5 weeks** (single-turn only) | **4 weeks** (everything) | **1 week faster** |

---

## Implementation Checklist

### Phase 1: Infrastructure & New Functions (2 weeks)
- [ ] Update `main.bicep`:
  - [ ] Add `agent-call-queue` (DeliveryCount: 10, LockDuration: 5m)
  - [ ] Add `evaluator-processing-queue` (DeliveryCount: 10, LockDuration: 5m)
  - [ ] Remove `test-case-processing` queue
- [ ] Create message models:
  - [ ] `AgentCallMessage` (in ApiModels.cs)
  - [ ] `EvaluatorJobMessage` (in ApiModels.cs)
  - [ ] Delete `ProcessTestCaseMessage`
- [ ] Create `AgentWorkerFunction.cs`:
  - [ ] Service Bus trigger on agent-call-queue
  - [ ] Call Copilot Studio with retry (exponential backoff)
  - [ ] Create pending results for each evaluator
  - [ ] Handle NA scenarios (create NA result, skip queue)
  - [ ] Queue to evaluator-processing-queue
  - [ ] Error handling: All evaluators marked Errored if agent fails
- [ ] Create `EvaluatorWorkerFunction.cs`:
  - [ ] Service Bus trigger on evaluator-processing-queue
  - [ ] Retrieve pending result from Dataverse
  - [ ] Check if already completed (idempotency)
  - [ ] Call LLM Judge with retry (exponential backoff)
  - [ ] Update result to Completed or Errored
  - [ ] Increment completion counter
- [ ] Update Dataverse service:
  - [ ] `GetEvaluationResultAsync(Guid resultId)` - Retrieve single result
  - [ ] `UpdateEvaluationResultAsync(Guid resultId, UpdateEvaluationResultRequest)` - Update result
- [ ] Update Service Bus service:
  - [ ] `SendAgentCallMessageAsync(AgentCallMessage)`
  - [ ] `SendEvaluatorJobMessageAsync(EvaluatorJobMessage)`
  - [ ] Remove `SendTestCaseMessageAsync` (no longer used)
- [ ] Write unit tests (>80% coverage):
  - [ ] `AgentWorkerFunctionTests.cs`
  - [ ] `EvaluatorWorkerFunctionTests.cs`
  - [ ] Mock all dependencies (Copilot, Dataverse, Service Bus, LLM Judge)
  - [ ] Test success, failure, retry, NA scenarios
- [ ] Delete old code:
  - [ ] Delete `WorkerFunction.cs`
  - [ ] Delete `ProcessTestCaseMessage` model

### Phase 2: Update All Functions (1 week)
- [ ] Update `OrchestratorFunction.StartEvaluationRun`:
  - [ ] Single-turn: Queue to agent-call-queue
  - [ ] Multi-turn: Start orchestration with agent-call support
  - [ ] Remove all references to test-case-processing queue
  - [ ] Remove ProcessTestCaseMessage routing logic
- [ ] Update `MultiTurnDurableOrchestrator`:
  - [ ] Pass agent-call-queue info to activities
  - [ ] Add polling logic after each turn to wait for evaluations
  - [ ] Check all evaluators completed (StatusCode != Pending)
  - [ ] Timeout after 10 minutes (mark aggregate as Errored)
- [ ] Update `MultiTurnDurableActivities.ExecuteSingleTurn`:
  - [ ] Call Copilot Studio
  - [ ] Create pending results for each evaluator
  - [ ] Queue to evaluator-processing-queue
  - [ ] Poll Dataverse until all evaluators complete
  - [ ] Return results to orchestrator
- [ ] Integration tests:
  - [ ] End-to-end single-turn test (1 test × 5 evaluators)
  - [ ] End-to-end multi-turn test (5 turns × 3 evaluators)
  - [ ] Verify conversation context preserved in multi-turn
  - [ ] Verify aggregate calculation correct

### Phase 3: Staging Validation & Production (1 week)
- [ ] Deploy to staging:
  - [ ] Infrastructure (queues)
  - [ ] Function app (new workers, updated orchestrator)
- [ ] Run comprehensive tests:
  - [ ] Unit tests (all pass)
  - [ ] Integration tests (all pass)
  - [ ] Load test: 1,000 test cases × 5 evaluators = 5,000 units
  - [ ] Measure throughput improvement (target: ≥3×)
  - [ ] Verify error rate ≤5%
  - [ ] Confirm zero orphaned pending results
  - [ ] 24-hour soak test (continuous evaluation runs)
- [ ] Verify retry logic:
  - [ ] Simulate Copilot Studio transient failures
  - [ ] Simulate Azure OpenAI rate limiting
  - [ ] Verify exponential backoff works correctly
  - [ ] Check dead letter queue (should be empty)
- [ ] Deploy to production:
  - [ ] Schedule during low-usage window
  - [ ] Ensure NO runs in progress
  - [ ] Deploy infrastructure first
  - [ ] Deploy function app second
  - [ ] Run smoke tests (10 single + 5 multi-turn)
  - [ ] Monitor Application Insights for 48 hours
  - [ ] Watch dead letter queue depth (alerts if > 0)

---

## Success Criteria

### Technical Metrics
- ✅ **Throughput**: 5-10× improvement for multi-evaluator scenarios
- ✅ **Error rate**: ≤5% (same or better than before)
- ✅ **Orphaned results**: Zero (robust retry guarantees)
- ✅ **Multi-turn**: Conversation context preserved correctly
- ✅ **Aggregate results**: Calculated correctly for multi-turn
- ✅ **Dead letter queue**: Depth = 0 (or alerts triggered immediately)

### Test Coverage
- ✅ **Unit tests**: >80% coverage on all new code
- ✅ **Integration tests**: End-to-end validation (single + multi-turn)
- ✅ **Load tests**: 5,000 evaluation units complete successfully
- ✅ **Soak test**: 24 hours continuous operation without errors

### Operational Readiness
- ✅ **Monitoring**: Application Insights metrics for new queues/workers
- ✅ **Alerts**: Dead letter queue depth > 0
- ✅ **Runbooks**: Dead letter queue investigation and replay procedures
- ✅ **Documentation**: Architecture, PRD, and stories updated

---

## Risk Assessment

### Critical Risks (High Priority)
1. **Hard cutover with no rollback**
   - Mitigation: Thorough staging validation (24-hour soak test)
   - Mitigation: Smoke tests immediately post-deployment
   - Mitigation: Keep backup of old code for emergency redeploy

2. **Multi-turn orchestrator polling complexity**
   - Mitigation: Comprehensive integration tests
   - Mitigation: Timeout handling (10-minute limit)
   - Mitigation: Clear error messages for timeout scenarios

3. **Dead letter queue requires 24/7 monitoring**
   - Mitigation: Application Insights alerts (DLQ depth > 0)
   - Mitigation: On-call coverage for first week
   - Mitigation: Runbook for DLQ investigation and replay

### Moderate Risks
1. **Azure OpenAI rate limiting behavior**
   - Mitigation: Load test with realistic concurrency
   - Mitigation: Monitor for 429 responses
   - Mitigation: Retry logic handles transient rate limits

2. **Dataverse query performance (evaluator worker)**
   - Mitigation: Optimize queries with proper filters
   - Mitigation: Load test with realistic data volumes
   - Mitigation: Monitor query execution times

### Low Risks
1. **Service Bus queue depth**
   - Mitigation: Monitor queue depth in Application Insights
   - Mitigation: Scale evaluator workers if needed

---

## Rollback Plan

**Scenario**: Critical issues found within 24 hours of production deployment

**Steps**:
1. Assess severity:
   - Critical: Blocking all evaluation runs → Immediate rollback
   - High: Elevated error rate (>20%) → Investigate, may rollback
   - Medium: Isolated failures → Fix forward

2. Rollback procedure (if needed):
   - Redeploy backup of old code (WorkerFunction + test-case-processing queue)
   - Update Bicep to restore old queue
   - Verify old flow works correctly
   - Mark pending results from failed run as Errored
   - Communicate issue to users

3. Post-rollback:
   - Root cause analysis in staging environment
   - Fix issues
   - Re-test comprehensively
   - Schedule new production deployment

**Important**: After 24 hours, rollback becomes more complex (pending results in Dataverse). Focus on fixing forward at that point.

---

## Approval

**Decisions Approved By**: User (Product Owner)  
**Date**: November 18, 2025  
**Status**: ✅ Ready for Phase 1 Implementation

**Next Steps**:
1. Begin Phase 1: Infrastructure and new functions (2 weeks)
2. Phase 2: Update all functions (1 week)
3. Phase 3: Staging validation and production deployment (1 week)

**Total Timeline**: 4 weeks to production

---

## Document History

| Date | Author | Changes |
|------|--------|---------|
| 2025-11-18 | Planning Agent | Initial decisions document created |
| 2025-11-18 | Planning Agent | All decisions approved by user |
