# Multi-Turn Support - Staging Deployment Validation

## Deployment Information
- **Date:** November 9, 2025
- **Environment:** Staging (rg-agent-eval-staging)
- **Function App:** func-aiestagingmbwc
- **Version:** 1.0.0 (Multi-Turn Support - Phase 5)

## ✅ Completed Steps

### Phase 1-4: Implementation ✅
- [x] Created data models (TestCaseTurn, MultiTurnExecutionContext)
- [x] Updated TestCase and EvaluationResult models
- [x] Implemented MultiTurnExecutionService
- [x] Created MultiTurnWorkerFunction
- [x] Updated DataverseService with multi-turn methods
- [x] Updated OrchestratorFunction to handle multi-turn test cases
- [x] Created unit tests (105 tests passing)
- [x] Created integration tests (MultiTurnIntegrationTests.cs)

### Phase 5: Deployment ✅
- [x] Built application in Release mode
- [x] Deployed to staging Function App (func-aiestagingmbwc)
- [x] Verified health endpoint responds
- [x] Confirmed new function deployed: ProcessMultiTurnTestCase

## 🔨 In Progress

### Test Data Creation
- [ ] Create Test Case 1: Blue Shirts Shopping (2 turns)
- [ ] Create Test Case 2: Order Status Inquiry (3 turns)
- [ ] Verify test cases in Dataverse UI
- [ ] Verify turns are ordered by mcicaps_sequence field

## 📋 Validation Checklist

### Infrastructure Validation

#### ✅ Function App Deployment
- [x] Function app is running (health check passed)
- [x] All functions deployed successfully:
  - [x] ProcessMultiTurnTestCase (new)
  - [x] ProcessTestCase (existing)
  - [x] StartEvaluationRun
  - [x] GetEvaluationRunStatus
  - [x] MonitorEvaluationRuns
  - [x] Health endpoints

#### ⏳ Dataverse Schema (Already Complete)
- [x] `mcicaps_ismultiturn` field exists on `mcicaps_testcases`
- [x] `mcicaps_testcaseturn` table exists with all fields
- [x] New fields on `mcicaps_evaluationresults`:
  - [x] `mcicaps_testcaseturnid` (Lookup)
  - [x] `mcicaps_conversationid` (Text)
  - [x] `mcicaps_isaggregate` (Boolean)
- [x] Relationships established correctly

#### ⏳ Service Bus
- [ ] Test case queue accepts multi-turn messages
- [ ] Multi-turn worker queue processing

#### ⏳ Application Insights
- [ ] Logs showing multi-turn execution
- [ ] Custom metrics for multi-turn scenarios
- [ ] Conversation tracking visible

---

## 🧪 Test Execution Plan

### Test 1: Single 2-Turn Test Case

**Setup:**
1. Create "Blue Shirts Shopping" test case in Dataverse
2. Add 2 turns with proper sequence numbers
3. Ensure Direct Line endpoint is configured

**Execution:**
```bash
# Start evaluation run
curl -X POST https://func-aiestagingmbwc.azurewebsites.net/api/evaluations/start \
  -H "Content-Type: application/json" \
  -d '{"testRunId": "<test-run-id>"}'
```

**Expected Results:**
- ✅ Test case executes both turns
- ✅ ConversationId created on Turn 1
- ✅ Same ConversationId used for Turn 2
- ✅ 2 individual results created (`isaggregate = false`)
- ✅ 1 aggregate result created (`isaggregate = true`)
- ✅ All results share same conversationId
- ✅ Turns executed in correct order (Sequence 1, then 2)

**Validation Queries:**

```kusto
# Application Insights - Execution Trace
traces
| where message contains "Blue Shirts Shopping" or message contains "multi-turn"
| where timestamp > ago(30m)
| project timestamp, message, severityLevel
| order by timestamp asc
```

```sql
-- Dataverse - Results Verification
SELECT 
    r.mcicaps_name,
    r.mcicaps_testcaseturnid,
    r.mcicaps_conversationid,
    r.mcicaps_isaggregate,
    r.mcicaps_passed,
    t.mcicaps_sequence
FROM mcicaps_evaluationresults r
LEFT JOIN mcicaps_testcaseturn t ON r.mcicaps_testcaseturnid = t.mcicaps_testcaseturnid
WHERE r.mcicaps_testcaseid = '<test-case-id>'
ORDER BY t.mcicaps_sequence ASC, r.mcicaps_isaggregate ASC
```

---

### Test 2: Single 3-Turn Test Case

**Setup:**
1. Create "Order Status Inquiry" test case
2. Add 3 turns with sequence 1, 2, 3
3. Configure expected responses

**Expected Results:**
- ✅ All 3 turns execute sequentially
- ✅ Conversation context maintained throughout
- ✅ 3 individual results + 1 aggregate result
- ✅ Pass rate calculated correctly

---

### Test 3: Mixed Test Run (Single + Multi-Turn)

**Setup:**
1. Ensure existing single-turn test cases exist
2. Add multi-turn test cases
3. Create test run with both types

**Expected Results:**
- ✅ Single-turn tests execute normally
- ✅ Multi-turn tests execute with context
- ✅ Both types complete successfully
- ✅ Correct result counts for each type
- ✅ Monitoring shows both execution types

---

### Test 4: Failure Handling

**Setup:**
1. Create multi-turn test case with likely failing turn (Turn 2)
2. Configure strict expected response

**Expected Results:**
- ✅ Turn 1 executes
- ✅ Turn 2 fails evaluation
- ✅ Turn 3 still executes (continue on failure)
- ✅ Aggregate shows 1/3 passed
- ✅ Error logged but execution continues

---

## 🔍 Monitoring Validation

### Application Insights Queries

**Multi-Turn Execution Overview:**
```kusto
traces
| where message contains "MultiTurn" or message contains "multi-turn"
| where timestamp > ago(1h)
| summarize Count=count() by severityLevel, bin(timestamp, 5m)
| render timechart
```

**Conversation ID Tracking:**
```kusto
traces
| where customDimensions contains "ConversationId"
| where timestamp > ago(1h)
| extend ConversationId = tostring(customDimensions.ConversationId)
| extend TestCaseId = tostring(customDimensions.TestCaseId)
| project timestamp, ConversationId, TestCaseId, message
| order by timestamp asc
```

**Turn Success Rate:**
```kusto
customMetrics
| where name == "MultiTurnSequence.PassRate"
| where timestamp > ago(1h)
| summarize AvgPassRate=avg(value) by bin(timestamp, 10m)
| render timechart
```

**Errors and Warnings:**
```kusto
traces
| where severityLevel >= 3  // Warning or higher
| where message contains "MultiTurn" or message contains "turn"
| where timestamp > ago(1h)
| project timestamp, severityLevel, message
| order by timestamp desc
```

---

## 🐛 Known Issues / Notes

### 1. Health Endpoint Response
- **Issue:** Detailed health endpoint didn't return output in PowerShell
- **Status:** Basic health works fine, detailed may have output buffering issue
- **Impact:** None - basic health sufficient for validation
- **Action:** Monitor via Application Insights instead

### 2. Turn Name Field
- **Note:** `mcicaps_name` is auto-generated globally (Turn-00001, Turn-00002, etc.)
- **Important:** Always use `mcicaps_sequence` for ordering, NOT `mcicaps_name`
- **Status:** Documented in setup guide
- **Action:** Verify queries use correct field

---

## 📊 Success Metrics

### Quantitative Goals
- [ ] 100% of multi-turn test cases execute successfully
- [ ] 0% conversation context loss between turns
- [ ] < 2 seconds additional latency per turn
- [ ] 100% accuracy in turn sequencing
- [ ] Aggregate results match individual results

### Qualitative Goals
- [ ] Logs clearly show multi-turn execution flow
- [ ] Easy to identify which turn failed
- [ ] Conversation IDs traceable across all turns
- [ ] Monitoring dashboards show multi-turn metrics
- [ ] Error messages are actionable

---

## 🎯 Next Actions

### Immediate (Today)
1. [ ] Create test data in Dataverse (2 test cases)
2. [ ] Run Test 1: 2-Turn Test Case
3. [ ] Verify results in Dataverse
4. [ ] Review Application Insights logs
5. [ ] Run Test 2: 3-Turn Test Case

### Short Term (This Week)
6. [ ] Run Test 3: Mixed test run
7. [ ] Run Test 4: Failure handling test
8. [ ] Document any issues found
9. [ ] Fine-tune error handling if needed
10. [ ] Create monitoring dashboard

### Before Production
11. [ ] Complete all validation tests
12. [ ] Performance testing (10+ multi-turn cases)
13. [ ] Security review
14. [ ] Documentation review
15. [ ] Production deployment checklist

---

## 📝 Deployment Notes

### Configuration
- No new configuration settings required
- Existing Dataverse connection used
- Existing Service Bus queues support multi-turn
- Existing OpenAI deployment used for evaluation

### Backward Compatibility
- ✅ Existing single-turn test cases unaffected
- ✅ Existing orchestration flow preserved
- ✅ New functionality is additive, not breaking

### Rollback Plan
If issues occur:
1. Redeploy previous version without multi-turn support
2. Single-turn tests continue working
3. Multi-turn test cases remain in Dataverse for future use
4. No data loss - schema changes are non-breaking

---

## ✅ Sign-Off

- [ ] **Development:** All code complete and tested
- [ ] **Deployment:** Successfully deployed to staging
- [ ] **Testing:** Test data created and initial tests pass
- [ ] **Monitoring:** Application Insights shows healthy execution
- [ ] **Documentation:** Setup guides and validation docs complete
- [ ] **Ready for Production:** All validation tests pass

**Deployment Status:** 🟡 In Progress (Phase 5)
**Next Milestone:** Complete test data creation and validation
