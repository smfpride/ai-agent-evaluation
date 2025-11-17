# Deployment Record: N:N Intersect Table Primary Key Column Fix

**Date**: November 16, 2025  
**Time**: 20:35 UTC  
**Environment**: Staging (func-aiestagingmbwc)  
**Deployment Number**: 5 of 5 (final fix for Story 09 N:N relationship implementation)

## Problem Summary

After migrating from 1:N to N:N relationship between evaluation runs and evaluator types, the `GetEvaluatorTypesForRunAsync()` query failed with:

```
'mcicaps_EvaluationRuns_mcicaps_Evaluato' entity doesn't contain attribute with Name = 'mcicaps_evaluationrunid'
```

**Root Cause**: Used entity logical name `evaluationrunid` instead of primary key column name `evaluationrunsid` in the N:N intersect table query.

## Technical Details

### Dataverse N:N Relationship Pattern

According to [Microsoft Docs - Many-to-many relationships](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/queryexpression/join-tables#many-to-many-relationships):

> An intersect table typically has just four columns, but only two of them are important. **The two important columns match the primary key columns of the participating tables.**

Example from documentation:
```csharp
// TeamMembership intersect table has: systemuserid, teamid
LinkEntity linkedTeamMemberShip = query.AddLink(
      linkToEntityName: "teammembership",
      linkFromAttributeName: "systemuserid",
      linkToAttributeName: "systemuserid");  // ← Primary key column name!
```

### Our Relationship

- **Tables**: `mcicaps_evaluationruns` ↔ `mcicaps_evaluatortypes`
- **Intersect Table**: `mcicaps_evaluationruns_mcicaps_evaluato`
- **Primary Key Columns**:
  - `mcicaps_evaluationrunsid` (note the 's' - this is the PRIMARY KEY column)
  - `mcicaps_evaluatortypeid`

### The Fix

**Before (WRONG)**:
```csharp
new ConditionExpression(Prefixed("evaluationrunid"), ConditionOperator.Equal, evaluationRunId)
// Generated: mcicaps_evaluationrunid (doesn't exist in intersect table)
```

**After (CORRECT)**:
```csharp
new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
// Generated: mcicaps_evaluationrunsid (primary key column name - exists in intersect table)
```

The intersect table columns are named after the **primary key columns** of the participating tables, NOT the entity logical names.

## Code Changes

**File**: `backend/AiAgentEvaluation/Services/DataverseService.cs`  
**Method**: `GetEvaluatorTypesForRunAsync()` (lines 945-1005)  
**Change**: Changed `evaluationrunid` → `evaluationrunsid` in LinkCriteria condition

Added detailed comments explaining N:N intersect table pattern:
```csharp
// Filter on evaluationruns primary key column in intersect table
// Note: Uses "evaluationrunsid" (with 's') - the PRIMARY KEY column name
new ConditionExpression(Prefixed("evaluationrunsid"), ConditionOperator.Equal, evaluationRunId)
```

## Testing Results

### Unit Tests
```
Test summary: total: 133, failed: 0, succeeded: 133, skipped: 0, duration: 121.4s
```

### Build
```
Build succeeded in 4.3s
```

### Deployment
```
Deployment completed successfully.
[2025-11-16T20:36:05.251Z] Syncing triggers...
```

### Health Verification
```bash
$ curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic
{"status":"Healthy","timestamp":"2025-11-16T20:36:48.3029105Z","version":"1.0.0","message":"Function app is running"}
```

## Deployment Timeline

| Deployment | Time (UTC) | Issue | Status |
|------------|-----------|-------|--------|
| 1 | 20:04 | `evaluatortype` field missing from test cases | Fixed, deployed |
| 2 | 20:11 | N:N intersect table double-prefixing | Fixed, deployed |
| 3 | 20:20 | Hardcoded prefixes defeating config system | Fixed, deployed |
| 4 | 20:28 | Redundant `Table()` and `Field()` helpers | Fixed, deployed |
| **5** | **20:35** | **Wrong column name in intersect table** | **FIXED** ✅ |

## Lessons Learned

### Critical Insight
**N:N intersect table columns use PRIMARY KEY column names, not entity logical names.**

This is THE authoritative pattern from Microsoft Docs and must be followed exactly:
- ❌ DON'T use entity logical name: `evaluationrunid`
- ✅ DO use primary key column name: `evaluationrunsid`

### Dataverse N:N Best Practices

1. **Always query intersect table metadata** to verify column names before writing LinkEntity queries
2. **Primary key column names** may differ from entity logical names (e.g., `evaluationrunsid` vs `evaluationrunid`)
3. **Follow Microsoft documentation examples exactly** - the `TeamMembership` pattern is the gold standard
4. **Test with actual data** - Dataverse will report exact missing column names in error messages

### Documentation Research Pattern

When encountering Dataverse query errors:
1. Search Microsoft Docs for the specific relationship type (1:N, N:N)
2. Find working code examples in official documentation
3. Compare example patterns to your code
4. Follow the documented pattern exactly (don't assume naming conventions)

## Verification Steps

### Immediate Verification
- [x] Build successful
- [x] All 133 unit tests pass
- [x] Deployment successful
- [x] Health endpoint responding

### Functional Verification (TODO)
- [ ] Create test evaluation run with N:N relationship
- [ ] Start evaluation run
- [ ] Verify `GetEvaluatorTypesForRunAsync()` returns evaluator types
- [ ] Confirm no more "doesn't contain attribute" errors in Application Insights

## Related Documentation

- [Microsoft Docs: Many-to-many relationships with QueryExpression](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/queryexpression/join-tables#many-to-many-relationships)
- `docs/stories/09-multi-evaluator-support.md` - Story 09 implementation
- `docs/ARCHITECTURE.md` - Updated section 5.3 with N:N relationship patterns
- `docs/prd/PRD-Agent-Evaluation-Function.md` - Updated section 3.3.4 with N:N data model

## Sign-off

**Deployed by**: GitHub Copilot Agent  
**Verified by**: Automated health checks  
**Status**: ✅ **RESOLVED** - N:N relationship query fixed using correct primary key column names

---

## Follow-up: Sixth Deployment - CreateEvaluationResult Fix

**Time**: 20:44 UTC  
**Issue**: After functional testing revealed error in `CreateEvaluationResultAsync()`

### Problem
```
'mcicaps_evaluationresults' entity doesn't contain attribute with Name = 'mcicaps_evaluatortype'
ErrorCode: 0x80041103
```

### Root Cause
The method still tried to set the `evaluatortype` lookup field, which doesn't exist in the N:N model. Evaluator types are now associated with evaluation runs via intersect table, not with individual results.

### Fix Applied
Removed evaluator type field assignment from `CreateEvaluationResultAsync()` (line ~480):
```csharp
// REMOVED
if (request.EvaluatorTypeId.HasValue)
    resultEntity[Prefixed("evaluatortype")] = new EntityReference(Prefixed("evaluatortype"), request.EvaluatorTypeId.Value);
```

Added explanatory comment that evaluator type context is maintained at run level, not per-result.

### Deployment Results
✅ Build: Succeeded (4.5s)  
✅ Tests: 133/133 passed  
✅ Deployed: 20:44:09 UTC  
✅ Health: 200 OK

### Complete Migration Status

All N:N relationship field references removed:
- ✅ `GetTestCasesAsync()` - evaluatortype field removed
- ✅ `GetTestCaseAsync()` - evaluatortype field removed  
- ✅ `GetEvaluatorTypesForRunAsync()` - fixed to use primary key column names
- ✅ `CreateEvaluationResultAsync()` - evaluatortype field removed

**Total Deployments**: 6 (20:04, 20:11, 20:20, 20:28, 20:35, 20:44 UTC)

---

## Follow-up: Seventh Deployment - Durable Function Timer Fix

**Time**: 20:59 UTC  
**Issue**: Multi-turn orchestrator failing with missing activity error at 20:47 UTC

### Problem
```
Task 'CreateTurnDelay' (#2) failed with an unhandled exception
Exception: DurableTask.Core.Exceptions.TaskFailedException
Location: MultiTurnDurableOrchestrator.cs line 201
```

### Root Cause
The orchestrator was calling a non-existent activity `CreateTurnDelay` to add delays between turns. This activity was never implemented.

### Fix Applied
Replaced activity call with proper Durable Functions timer API:
```csharp
// WRONG - activity doesn't exist
await context.CallActivityAsync("CreateTurnDelay", 2);

// CORRECT - use built-in durable function timer
await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(2), CancellationToken.None);
```

### Test Updates
Updated `MultiTurnDurableOrchestratorTests.cs` to verify `CreateTimer` instead of `CallActivityAsync`:
```csharp
// Old expectation
mockContext.Verify(x => x.CallActivityAsync("CreateTurnDelay", ...), Times.Once);

// New expectation
mockContext.Verify(x => x.CreateTimer(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
```

### Deployment Results
✅ Build: Succeeded (6.1s)  
✅ Tests: 133/133 passed  
✅ Deployed: 20:59:31 UTC  
✅ Health: 200 OK

### Why CreateTimer?
Durable Functions provide `CreateTimer()` for orchestrator-safe delays that:
- Work correctly with replay/checkpointing
- Don't require separate activity functions
- Are the recommended pattern per Microsoft Durable Functions documentation

**Total Deployments**: 7 (20:04, 20:11, 20:20, 20:28, 20:35, 20:44, 20:59 UTC)

---

## Follow-up: Eighth Deployment - Monitor Function NotApplicable Fix

**Time**: 21:10 UTC  
**Issue**: Evaluation runs not completing when some test cases are NotApplicable

### Problem
Monitor function was waiting for 15 completed test cases, but only 11 were completed. The remaining 4 were marked as NotApplicable (StatusCode 778760004) and the monitor wasn't counting them toward completion.

### Root Cause
`MonitorFunction.cs` line 167 was only counting Completed (778760002) and Failed (778760003) results:
```csharp
var totalCompleted = completedCount + failedCount;
if (totalCompleted < evaluationRun.TotalTestCases)
```

NotApplicable results (778760004) were not included in the completion check, causing the run to appear stuck even though all applicable work was done.

### Fix Applied
Updated completion logic to include NotApplicable results:
```csharp
// BEFORE
var totalCompleted = completedCount + failedCount;

// AFTER
var notApplicableCount = testCaseResults.Count(r => r.StatusCode == 778760004);
var totalCompleted = completedCount + failedCount + notApplicableCount;
```

Also updated logging to show NotApplicable count and included NA results in final metrics calculation.

### Why NotApplicable Matters
Test cases are marked NotApplicable (StatusCode 778760004) when:
- Multi-turn tests have all turns missing expected responses
- Test cases don't meet criteria for evaluation with specific evaluator types
- These are terminal states that should count toward completion

### Deployment Results
✅ Build: Succeeded (8.9s)  
✅ Tests: 133/133 passed  
✅ Deployed: 21:10:43 UTC  
✅ Health: 200 OK

**Total Deployments**: 8 (20:04, 20:11, 20:20, 20:28, 20:35, 20:44, 20:59, 21:10 UTC)

**Next Steps**: Monitor should now properly complete evaluation runs that have NotApplicable test cases.
