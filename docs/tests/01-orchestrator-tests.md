# Test Cases: Orchestrator Function (StartEvaluationRun)

## Test Suite: HTTP Orchestrator Function
**Component:** StartEvaluationRun  
**Function Type:** HTTP Trigger  
**Timeout:** 5 minutes

### Positive Test Cases

#### TC-ORD-001: Successful Evaluation Run Initialization
**Scenario:** Valid evaluation run can be started successfully  
**Given:** 
- Valid evaluation run ID exists in Dataverse
- Evaluation run status is 1 (Created) or 778760001 (Execute/Run)
- Test set contains valid test cases
**When:** HTTP POST request sent to orchestrator endpoint
**Then:**
- HTTP 202 Accepted response returned
- Response contains evaluationRunId, totalTestCases, estimatedCompletionTime
- Evaluation run status updated to 778760002 (Running) in Dataverse
- All test case messages queued to Service Bus
- Response time < 30 seconds

**Test Data:**
```json
{
  "evaluationRunId": "550e8400-e29b-41d4-a716-446655440001",
  "options": {
    "priority": "normal",
    "timeoutMinutes": 5,
    "retryEnabled": true
  }
}
```

#### TC-ORD-002: Large Test Set Orchestration (50K Test Cases)
**Scenario:** System handles maximum test case volume  
**Given:**
- Evaluation run with 50,000 test cases
- Test cases include all types (778760000, 778760001, 778760002)
**When:** Orchestration initiated
**Then:**
- All 50,000 messages queued successfully
- Response time < 30 seconds
- No message loss or duplication
- Proper distribution across test types

#### TC-ORD-003: Concurrent Evaluation Runs
**Scenario:** Multiple evaluation runs can be started simultaneously  
**Given:** 10 different evaluation runs ready for execution
**When:** All 10 runs started within 1 minute
**Then:**
- All runs accept and queue successfully
- No resource conflicts or deadlocks
- Each run maintains independent state

### Negative Test Cases

#### TC-ORD-004: Invalid Evaluation Run ID
**Scenario:** Non-existent evaluation run ID provided  
**Given:** Evaluation run ID that doesn't exist in Dataverse
**When:** HTTP POST request sent
**Then:**
- HTTP 404 Not Found response
- Error message: "Evaluation run not found"
- No status changes in Dataverse

#### TC-ORD-005: Evaluation Run Already Running
**Scenario:** Attempt to start already running evaluation  
**Given:** Evaluation run status is 778760002 (Running)
**When:** Start evaluation request sent
**Then:**
- HTTP 409 Conflict response
- Error message: "Evaluation run is already in progress"
- No duplicate queue messages created

#### TC-ORD-006: Empty Test Set
**Scenario:** Evaluation run has no associated test cases  
**Given:** Evaluation run exists but linked test set is empty
**When:** Orchestration initiated
**Then:**
- HTTP 400 Bad Request response
- Error message: "No test cases found for evaluation run"
- Status remains unchanged

#### TC-ORD-007: Service Bus Queue Unavailable
**Scenario:** Service Bus connection fails during queuing  
**Given:** Service Bus queue is unavailable or full
**When:** Orchestrator attempts to queue messages
**Then:**
- HTTP 503 Service Unavailable response
- Evaluation run status updated to 778760003 (Errored)
- Detailed error logged for troubleshooting

### Error Handling Test Cases

#### TC-ORD-008: Dataverse Connection Failure
**Scenario:** Dataverse becomes unavailable during orchestration  
**Given:** Dataverse connection fails after evaluation run retrieval
**When:** Status update attempted
**Then:**
- Retry logic activated with exponential backoff
- If retries fail, HTTP 503 returned
- Error details logged with correlation ID

#### TC-ORD-009: Partial Queue Failure
**Scenario:** Some messages fail to queue due to transient errors  
**Given:** 1000 test cases to queue, 50 fail due to transient errors
**When:** Queuing process continues
**Then:**
- Failed messages retried automatically
- Success rate tracked and logged
- If retry limit exceeded, evaluation marked as errored

#### TC-ORD-010: Request Timeout
**Scenario:** Orchestration takes longer than timeout limit  
**Given:** Very large test set causing slow processing
**When:** Function execution exceeds 5-minute timeout
**Then:**
- HTTP 408 Request Timeout returned
- Partial progress preserved
- Cleanup logic executed for incomplete work

### Data Validation Test Cases

#### TC-ORD-011: Malformed Request Body
**Scenario:** Invalid JSON in request body  
**Given:** Malformed JSON payload
**When:** Request processed
**Then:**
- HTTP 400 Bad Request response
- Validation error details provided
- No processing attempted

#### TC-ORD-012: Missing Required Fields
**Scenario:** Request missing evaluationRunId  
**Given:** Request body without evaluationRunId field
**When:** Request processed
**Then:**
- HTTP 400 Bad Request response
- Field validation error message
- No database queries executed

#### TC-ORD-013: Invalid Test Case Data
**Scenario:** Test cases contain invalid or missing data  
**Given:** Test cases with empty prompts or invalid test types
**When:** Orchestration processes test set
**Then:**
- Invalid test cases skipped with warnings
- Valid test cases processed normally
- Summary includes validation errors