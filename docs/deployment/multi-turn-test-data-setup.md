# Multi-Turn Test Data Setup Guide

## Overview
This guide provides step-by-step instructions for creating multi-turn test cases in Dataverse for the AI Agent Evaluation system.

## Prerequisites
- Access to Dataverse environment with appropriate permissions
- Test cases table (`mcicaps_testcases`) exists
- Test case turns table (`mcicaps_testcaseturn`) exists
- Evaluation results table (`mcicaps_evaluationresults`) exists

## Important Notes

### ⚠️ Critical: Turn Ordering
- **Use `mcicaps_sequence` field for sorting turns, NOT `mcicaps_name`**
- The `mcicaps_name` field is auto-generated with a global sequence number (e.g., "Turn-00001", "Turn-00002")
- This global sequence is NOT related to the turn order within a specific test case
- Always set and query by the `mcicaps_sequence` field for proper turn execution order

## Test Case 1: Blue Shirts Shopping (2 Turns)

### Step 1: Create the Test Case

1. Navigate to Dataverse → `mcicaps_testcases` table
2. Click **+ New**
3. Fill in the following fields:

| Field | Value |
|-------|-------|
| **Name** | Blue Shirts Shopping |
| **Is Multi-Turn** | Yes |
| **Test Type** | Similarity |
| **Direct Line Endpoint** | `<your-copilot-studio-endpoint>` |
| **Test Run** | Select your test run |

4. Click **Save**
5. Note the **Test Case ID** (you'll need this for creating turns)

### Step 2: Create Turn 1

1. Navigate to `mcicaps_testcaseturn` table
2. Click **+ New**
3. Fill in the following fields:

| Field | Value |
|-------|-------|
| **Name** | Auto-generated (e.g., "Turn-00001") - DO NOT SET MANUALLY |
| **Test Case** | Lookup to the test case created in Step 1 |
| **Sequence** | 1 ⚠️ **THIS IS THE FIELD USED FOR SORTING** |
| **Prompt Text** | I would like to see blue shirts for men |
| **Expected Response** | Opulent blue mens button down, cool sky mens t-shirt, Royal navy henley |

4. Click **Save**

### Step 3: Create Turn 2

1. Navigate to `mcicaps_testcaseturn` table
2. Click **+ New**
3. Fill in the following fields:

| Field | Value |
|-------|-------|
| **Name** | Auto-generated (e.g., "Turn-00002") - DO NOT SET MANUALLY |
| **Test Case** | Lookup to the test case created in Step 1 |
| **Sequence** | 2 ⚠️ **THIS IS THE FIELD USED FOR SORTING** |
| **Prompt Text** | I'm a size medium |
| **Expected Response** | Opulent blue mens button down, Royal navy henley |

4. Click **Save**

---

## Test Case 2: Order Status Inquiry (3 Turns)

### Step 1: Create the Test Case

1. Navigate to Dataverse → `mcicaps_testcases` table
2. Click **+ New**
3. Fill in the following fields:

| Field | Value |
|-------|-------|
| **Name** | Order Status Inquiry |
| **Is Multi-Turn** | Yes |
| **Test Type** | Accuracy |
| **Direct Line Endpoint** | `<your-copilot-studio-endpoint>` |
| **Test Run** | Select your test run |

4. Click **Save**
5. Note the **Test Case ID**

### Step 2: Create Turn 1

| Field | Value |
|-------|-------|
| **Name** | Auto-generated |
| **Test Case** | Lookup to test case |
| **Sequence** | 1 |
| **Prompt Text** | What's the status of my order? |
| **Expected Response** | I can help with that. What's your order number? |

### Step 3: Create Turn 2

| Field | Value |
|-------|-------|
| **Name** | Auto-generated |
| **Test Case** | Lookup to test case |
| **Sequence** | 2 |
| **Prompt Text** | ORDER-12345 |
| **Expected Response** | Your order ORDER-12345 is currently in transit |

### Step 4: Create Turn 3

| Field | Value |
|-------|-------|
| **Name** | Auto-generated |
| **Test Case** | Lookup to test case |
| **Sequence** | 3 |
| **Prompt Text** | When will it arrive? |
| **Expected Response** | Expected delivery is November 12, 2025 |

---

## Verification Steps

### 1. Verify Test Case Structure

Query to verify test case:
```sql
SELECT 
    mcicaps_name,
    mcicaps_ismultiturn,
    mcicaps_testtype
FROM mcicaps_testcases
WHERE mcicaps_name = 'Blue Shirts Shopping'
```

Expected result:
- `mcicaps_ismultiturn` = Yes (true)
- Related turns count should match expected number

### 2. Verify Turns Are Ordered Correctly

Query to verify turn order:
```sql
SELECT 
    mcicaps_name,
    mcicaps_sequence,
    mcicaps_prompttext
FROM mcicaps_testcaseturn
WHERE mcicaps_testcaseid = '<test-case-id>'
ORDER BY mcicaps_sequence ASC
```

Expected result:
- Turns ordered by `mcicaps_sequence` (1, 2, 3, ...)
- Note: `mcicaps_name` may be "Turn-00005", "Turn-00001", "Turn-00003" (global sequence)
- **Do NOT sort by mcicaps_name!**

### 3. View Test Case with Turns

In the Dataverse UI:
1. Open the test case record
2. Navigate to the **Test Case Turns** tab (related records)
3. Verify all turns are listed with correct sequence numbers
4. Verify prompt text and expected responses are correct

---

## Running a Multi-Turn Test

### Option 1: Via HTTP Trigger

```bash
# Start evaluation run
curl -X POST https://func-aiestagingmbwc.azurewebsites.net/api/evaluations/start \
  -H "Content-Type: application/json" \
  -d '{"testRunId": "<your-test-run-id>"}'
```

### Option 2: Via Service Bus (Direct)

For testing a specific multi-turn test case:

```powershell
# Azure CLI example
az servicebus queue send \
  --namespace-name sbus-aiestagingmbwc \
  --name testcasequeue \
  --body '{
    "testCaseId": "<test-case-id>",
    "testRunId": "<test-run-id>",
    "isMultiTurn": true
  }'
```

---

## Monitoring Multi-Turn Execution

### Application Insights Queries

**View all multi-turn test executions:**
```kusto
traces
| where message contains "multi-turn" or message contains "MultiTurn"
| where timestamp > ago(1h)
| project timestamp, message, severityLevel
| order by timestamp desc
```

**Track conversation context:**
```kusto
traces
| where message contains "ConversationId" or customDimensions contains "ConversationId"
| where timestamp > ago(1h)
| project timestamp, message, conversationId=tostring(customDimensions.ConversationId)
| order by timestamp desc
```

**View turn-by-turn execution:**
```kusto
customMetrics
| where name contains "Turn" or name contains "MultiTurn"
| where timestamp > ago(1h)
| project timestamp, name, value, customDimensions
| order by timestamp desc
```

### Dataverse Result Verification

After execution, check the `mcicaps_evaluationresults` table:

**Individual turn results:**
```sql
SELECT 
    mcicaps_testcaseid,
    mcicaps_testcaseturnid,
    mcicaps_conversationid,
    mcicaps_isaggregate,
    mcicaps_passed,
    mcicaps_score
FROM mcicaps_evaluationresults
WHERE mcicaps_testcaseid = '<test-case-id>'
    AND mcicaps_isaggregate = false
ORDER BY mcicaps_createdon ASC
```

**Aggregate result:**
```sql
SELECT 
    mcicaps_testcaseid,
    mcicaps_conversationid,
    mcicaps_isaggregate,
    mcicaps_passed,
    mcicaps_score,
    mcicaps_agentresponse
FROM mcicaps_evaluationresults
WHERE mcicaps_testcaseid = '<test-case-id>'
    AND mcicaps_isaggregate = true
```

Expected results:
- **N individual results** (one per turn) with `mcicaps_isaggregate = false`
- **1 aggregate result** with `mcicaps_isaggregate = true`
- All results share the same `mcicaps_conversationid`
- Turn results have `mcicaps_testcaseturnid` populated

---

## Troubleshooting

### Issue: Turns executing in wrong order

**Cause:** Turns may be sorted by `mcicaps_name` instead of `mcicaps_sequence`

**Solution:** 
1. Verify the FetchXML query in `DataverseService.GetTestCaseTurnsAsync` uses:
   ```xml
   <order attribute="mcicaps_sequence" descending="false" />
   ```
2. NOT:
   ```xml
   <order attribute="mcicaps_name" descending="false" />
   ```

### Issue: ConversationId not propagating to Turn 2+

**Cause:** ConversationId from Turn 1 not being stored or retrieved correctly

**Solution:**
1. Check Turn 1 result in Dataverse has `mcicaps_conversationid` populated
2. Verify `GetConversationIdFromPreviousTurnAsync` is querying correctly
3. Check logs for "ConversationId" retrieval messages

### Issue: Each turn starts a new conversation

**Symptoms:**
- Different `mcicaps_conversationid` for each turn result
- Agent doesn't remember context from previous turns

**Solution:**
1. Verify `MultiTurnExecutionService.ExecuteTestCaseAsync` is reusing `context.ConversationId`
2. Check that conversationId from Turn 1 is stored in context
3. Verify conversationId is passed to `SendMessageAsync` for Turn 2+

### Issue: Expected response doesn't match agent response

**Cause:** This is normal evaluation behavior - the agent may respond differently

**Solution:**
1. Review the similarity/accuracy score in the result
2. Adjust expected responses based on actual agent behavior
3. Consider using "Similarity" test type for more flexible matching
4. Review evaluation prompt templates for scoring logic

---

## Best Practices

1. **Start Simple:** Begin with 2-turn test cases, then expand to 3+ turns
2. **Test Incrementally:** Create one test case, run it, verify results, then create more
3. **Use Realistic Prompts:** Model test cases on actual user conversations
4. **Monitor Closely:** Watch Application Insights during initial test runs
5. **Verify ConversationId:** Always check that turns share the same conversation ID
6. **Document Expected Behavior:** Note what responses are realistic for your agent

---

## Next Steps

After creating test data:

1. ✅ Run a test evaluation to verify multi-turn execution
2. ✅ Check Application Insights for execution traces
3. ✅ Verify results in Dataverse
4. ✅ Review aggregate and individual turn metrics
5. ✅ Adjust expected responses based on actual results
6. ✅ Scale up to more complex multi-turn scenarios

---

## Support

For issues or questions:
- Check Application Insights logs
- Review the implementation in `MultiTurnExecutionService.cs`
- Consult the main story document: `docs/stories/06-multi-turn-support.md`
