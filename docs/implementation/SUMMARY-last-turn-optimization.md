# Multi-Turn Aggregate Optimization - Executive Summary

**Date Created**: November 23, 2025  
**Last Updated**: November 26, 2025  
**Status**: ⚠️ **PROPOSED - NOT YET IMPLEMENTED**  
**Estimated Effort**: 2-3 weeks  
**Impact**: High (Performance + Reliability)

---

## ⚠️ Implementation Status

**THIS OPTIMIZATION HAS NOT BEEN IMPLEMENTED YET.**

The current codebase (as of November 26, 2025) still uses the timer-based approach for calculating multi-turn aggregates. This document describes a proposed enhancement that would provide near real-time aggregate completion.

See the detailed implementation plan in `last-turn-aggregate-optimization.md` for step-by-step instructions.

---

---

## The Idea

> "We know when we have processed the last turn. Why don't we include something in the evaluator queue message to signal it's the last turn for a multi-turn, and have it update the aggregate for that evaluator type. That way the aggregate is getting updated in closer to real time and not waiting on a timer job to hopefully pick it up."

---

## Current Problem

**Timer-Based Polling**: `AggregateEvaluationProcessorFunction` runs every 30 seconds, scans all pending aggregates, checks if turns are complete, then calculates aggregate score.

**Issues**:
- ⏱️ **Latency**: 0-30 second lag after last turn completes
- 🐌 **Scalability**: Queries 2000+ pending aggregates every cycle
- ❓ **Uncertainty**: Hard to know when runs are "truly" complete
- 💸 **Cost**: Continuous polling even when idle

---

## Proposed Solution

**Event-Driven Optimization**: Mark last-turn evaluator messages with `isLastTurn=true`, trigger immediate aggregate calculation when all evaluators complete.

**Flow**:
1. `MultiTurnAgentWorkerFunction` sets `isLastTurn=true` on Turn N messages
2. `EvaluatorWorkerFunction` detects flag after completing evaluation
3. Checks if all turns complete for this evaluator → triggers aggregate calculation
4. Timer function remains as fallback safety net

---

## Key Benefits

✅ **Near real-time completion**: Aggregates update in <1 second (vs 0-30s)  
✅ **Lower load**: Timer processes <5 aggregates/cycle (vs 20)  
✅ **Better UX**: Evaluation runs show "Completed" faster  
✅ **Cleaner architecture**: Event-driven vs polling  

---

## Implementation Highlights

### 1. Extend Queue Message
```csharp
public class EvaluatorJobMessage
{
    // Existing fields...
    
    // NEW: Multi-turn aggregate optimization
    public bool IsMultiTurn { get; set; }
    public Guid? TestCaseId { get; set; }
    public Guid? EvaluatorTypeId { get; set; }
    public int? TurnSequence { get; set; }
    public int? TotalTurns { get; set; }
}
```

### 2. Check on EVERY Turn (not just last)
```csharp
// After UpdateEvaluationResultAsync succeeds for ANY turn
if (message.IsMultiTurn && allTurnsCompleteForEvaluator)
{
    await _dataverseService.CalculateAndUpdateAggregateAsync(...);
    _logger.LogInformation("Aggregate calculated on turn {Turn} completion", message.TurnSequence);
}
```

**Why check every turn?** Evaluators run in parallel and may complete out of order (Turn 3 before Turn 2).

### 3. Keep Timer as Fallback
- Don't remove `AggregateEvaluationProcessorFunction`
- Add check to skip already-processed aggregates
- Log method used: "last-turn optimization" vs "timer fallback"

---

## The "Blank Reasoning" Problem

> "We have to be really crisp on the reasoning and score coming back though. We haven't really figured out why some are just blank."

### Root Causes Identified

1. **JSON Parsing Failures**: Azure OpenAI/Copilot returns `"reasoning": null` → defaults to empty string
2. **Empty Responses**: LLM returns success but no content → not validated before saving
3. **Regex Fallback Weaknesses**: Regex parser may fail silently with empty reasoning
4. **Missing Validation**: No checks before writing to Dataverse

### Fixes Proposed

**Validate at Parse Time**:
```csharp
if (string.IsNullOrWhiteSpace(reasoning))
{
    _logger.LogWarning("Reasoning empty/null in response. Score={Score}", score);
    reasoning = $"[No reasoning provided by judge. Score: {score}]";
}
```

**Validate at Save Time**:
```csharp
if (string.IsNullOrWhiteSpace(judgeResponse.Reasoning))
{
    _logger.LogWarning("Empty reasoning for result {ResultId}", result.Id);
    judgeResponse.Reasoning = $"[Judge provided score {judgeResponse.Score} but no reasoning]";
}
```

**Validate Empty LLM Responses**:
```csharp
if (string.IsNullOrWhiteSpace(messageContent))
{
    return new LLMJudgeResponse
    {
        Success = false,
        ErrorMessage = "Azure OpenAI returned empty response",
        Reasoning = "[Empty response from Azure OpenAI]"
    };
}
```

---

## Testing Strategy

### Unit Tests
- ✅ Last turn flag detection and aggregate trigger
- ✅ Empty reasoning handling in all parsers
- ✅ Idempotency checks for duplicate aggregate calculations

### Integration Tests
- ✅ End-to-end multi-turn with last-turn optimization
- ✅ Aggregate calculated in <30 seconds (proof of optimization)
- ✅ Empty response handling from live LLM services

### Monitoring
- **Metric**: `AggregateCal calculationMethod` (LastTurnOptimization vs TimerFallback)
- **Target**: >95% via last-turn optimization
- **Alert**: Empty reasoning rate >1%

---

## Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| Race conditions with concurrent evaluators | Medium | Idempotent Dataverse updates + timer fallback |
| Incomplete turn detection | Low | Timer catches missed aggregates within 30s |
| Performance impact from extra queries | Low | Only runs on last turn + indexed queries |
| Empty reasoning not caught | Medium | Multiple validation layers + monitoring |

---

## Rollout Plan

**Week 1**: Code changes + unit tests  
**Week 2**: Integration tests + staging deployment  
**Week 3**: Production deployment + monitoring

---

## Success Metrics

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| Aggregate latency | 0-30s | <1s | <1s |
| Timer load | 20 aggregates/cycle | <5/cycle | <5/cycle |
| Empty reasoning rate | Unknown | Tracked | <1% |
| Optimization coverage | 0% | 95%+ | >95% |
| Out-of-order handling | ❌ N/A | ✅ Automatic | 100% |

---

## Approval & Next Steps

**Questions for Review**:
1. ✅ Does this align with current architecture patterns?
2. ✅ Are the empty reasoning fixes comprehensive enough?
3. ✅ Is 2-3 week timeline realistic?
4. ✅ Should we prioritize empty reasoning fixes first?

**Next Actions**:
1. Team review of implementation plan
2. Create GitHub issues for tracking
3. Break down into sprint-sized tasks
4. Begin implementation Phase 1

---

## Full Documentation

📄 **Detailed Plan**: `docs/implementation/last-turn-aggregate-optimization.md`  
📊 **Architecture**: `docs/ARCHITECTURE.md` (to be updated)  
📋 **PRD**: `docs/prd/PRD-Agent-Evaluation-Function.md` (to be updated)
