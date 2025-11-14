# Dataverse Table Name Fix - Resolution Summary

## Issue Description
**Error:** `The entity with a name = 'mcicaps_testcaseturns' with namemapping = 'Logical' was not found in the MetadataCache`

**Root Cause:** The code was referencing the table name as `mcicaps_testcaseturns` (plural) when the actual table name in Dataverse is `mcicaps_testcaseturn` (singular).

## Resolution

### Code Changes ✅

**File:** `backend/AiAgentEvaluation/Services/DataverseService.cs`

#### Change 1: GetTestCaseTurnsAsync Query
**Line 999** - Fixed the QueryExpression table name:

```csharp
// Before:
var query = new QueryExpression("mcicaps_testcaseturns")

// After:
var query = new QueryExpression("mcicaps_testcaseturn")
```

#### Change 2: CreateEvaluationResultAsync EntityReference
**Line 475** - Fixed the EntityReference table name:

```csharp
// Before:
resultEntity["mcicaps_testcaseturnid"] = new EntityReference("mcicaps_testcaseturns", request.TestCaseTurnId.Value);

// After:
resultEntity["mcicaps_testcaseturnid"] = new EntityReference("mcicaps_testcaseturn", request.TestCaseTurnId.Value);
```

### Verification ✅

1. ✅ Searched entire codebase - no more references to plural form
2. ✅ Built solution successfully
3. ✅ Deployed to staging
4. ✅ Health check passed

## Impact

This fix resolves the error when:
- **GetTestCaseTurnsAsync** is called to retrieve turns for a multi-turn test case
- **CreateEvaluationResultAsync** is called with a TestCaseTurnId to create turn-specific results

Both operations will now correctly reference the `mcicaps_testcaseturn` table.

## Testing

The multi-turn functionality should now work correctly when:
1. Creating multi-turn test cases in Dataverse
2. Starting an evaluation run with multi-turn test cases
3. The system attempts to retrieve turns for execution
4. Results are saved with turn references

---

**Date:** November 9, 2025  
**Environment:** Staging  
**Status:** ✅ Fixed and Deployed
