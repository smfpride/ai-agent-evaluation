# Test Cases: Worker Function (ProcessTestCase)

## Test Suite: Service Bus Worker Function
**Component:** ProcessTestCase  
**Function Type:** Service Bus Trigger  
**Timeout:** 5 minutes per test case

### Positive Test Cases

#### TC-WRK-001: Similarity Test Case Processing (Type 778760000)
**Scenario:** Successfully process similarity evaluation test case  
**Given:** 
- Valid Service Bus message with test type 778760000
- Similarity judge prompt available in Blob Storage
- Copilot Studio agent accessible via Direct Line API
**When:** Worker function processes message
**Then:**
- Prompt sent to Copilot Studio agent successfully
- Agent response received and captured
- Similarity evaluation performed using Azure OpenAI
- Score (0-1), passed status, reasoning captured
- Result stored in mcicaps_evaluationresults table
- Progress counter updated for evaluation run

**Test Message:**
```json
{
  "evaluationRunId": "550e8400-e29b-41d4-a716-446655440001",
  "testCaseId": "550e8400-e29b-41d4-a716-446655440002",
  "testCaseName": "Product Info Similarity Test",
  "prompt": "What are the features of Product X?",
  "expectedResponse": "Product X includes AI capabilities, cloud integration, and mobile support.",
  "testType": 778760000,
  "retryCount": 0,
  "enqueuedAt": "2024-11-07T14:00:00Z"
}
```

#### TC-WRK-002: Accuracy Test Case Processing (Type 778760001)
**Scenario:** Successfully process accuracy evaluation test case  
**Given:** Test case with factual content requiring accuracy evaluation
**When:** Worker processes accuracy test
**Then:**
- Accuracy judge prompt loaded and applied
- Factual correctness evaluated appropriately
- Score reflects accuracy of information provided
- Reasoning explains factual assessment

#### TC-WRK-003: Relevancy Test Case Processing (Type 778760002)  
**Scenario:** Successfully process relevancy evaluation test case  
**Given:** Test case requiring relevancy assessment
**When:** Worker processes relevancy test  
**Then:**
- Relevancy judge prompt used for evaluation
- Response relevance to original prompt assessed
- Score reflects how well response addresses user intent
- Reasoning explains relevancy determination

#### TC-WRK-004: High Score Test Case (Score > 0.8)
**Scenario:** Test case receives high evaluation score  
**Given:** Agent provides excellent response matching expected criteria
**When:** LLM Judge evaluates response
**Then:**
- Score between 0.8-1.0 assigned
- Passed status set to true
- Detailed positive reasoning provided
- Result stored with high confidence indicators

#### TC-WRK-005: Low Score Test Case (Score < 0.3)
**Scenario:** Test case receives low evaluation score  
**Given:** Agent provides poor response not meeting criteria
**When:** LLM Judge evaluates response
**Then:**
- Score between 0.0-0.3 assigned  
- Passed status set to false
- Detailed explanation of deficiencies provided
- Areas for improvement identified in reasoning

### Error Handling Test Cases

#### TC-WRK-006: Copilot Studio API Failure
**Scenario:** Direct Line API becomes unavailable  
**Given:** Copilot Studio agent not responding to API calls
**When:** Worker attempts to send prompt
**Then:**
- Retry logic activated (up to 3 attempts)
- Exponential backoff applied between retries
- If all retries fail, test case marked as failed
- Error details logged with correlation ID
- Message sent to dead letter queue if retries exhausted

#### TC-WRK-007: Azure OpenAI Rate Limiting
**Scenario:** LLM Judge API returns rate limit error  
**Given:** Azure OpenAI quota exceeded or rate limited
**When:** Evaluation request sent to LLM Judge
**Then:**
- Rate limit error detected and handled
- Request retried after appropriate delay
- Exponential backoff applied for subsequent retries
- Circuit breaker activated if errors persist

#### TC-WRK-008: Invalid Judge Prompt Template
**Scenario:** Judge prompt template missing or corrupted  
**Given:** Prompt file not found in Blob Storage or contains invalid content
**When:** Worker attempts to load prompt template
**Then:**
- Error logged with specific prompt file details
- Fallback to default evaluation criteria
- Test case processing continues with basic evaluation
- Alert sent for prompt template issue

#### TC-WRK-009: Dataverse Write Failure
**Scenario:** Cannot store evaluation result in Dataverse  
**Given:** Dataverse connection issue or constraint violation
**When:** Worker attempts to save evaluation result
**Then:**
- Retry logic applied for transient errors
- Error details captured and logged
- Message remains in queue for reprocessing
- Evaluation run error counter incremented

### Data Processing Test Cases

#### TC-WRK-010: Large Response Processing
**Scenario:** Agent returns very long response (>2MB limit)  
**Given:** Copilot Studio returns response exceeding size limits
**When:** Worker processes response
**Then:**
- Response truncated to maximum allowed size
- Truncation noted in evaluation result
- Evaluation performed on truncated content
- Original size logged for analysis

#### TC-WRK-011: Empty Agent Response
**Scenario:** Agent returns empty or null response  
**Given:** Copilot Studio provides no response content
**When:** Worker evaluates empty response
**Then:**
- Empty response handled gracefully
- Low score assigned for lack of content
- Reasoning explains empty response issue
- Test case marked as completed with failure

#### TC-WRK-012: Malformed LLM Judge Response
**Scenario:** LLM Judge returns invalid JSON format  
**Given:** Azure OpenAI returns non-parseable response
**When:** Worker processes judge response
**Then:**
- JSON parsing error caught and handled
- Fallback scoring mechanism applied
- Error logged with original response content
- Test case processing completes with default score

#### TC-WRK-013: Unicode and Special Character Handling
**Scenario:** Test case contains special characters and Unicode  
**Given:** Prompt and expected response contain emojis, special symbols
**When:** Worker processes test case
**Then:**
- Special characters preserved through all processing steps
- Copilot Studio handles Unicode correctly
- LLM Judge evaluation includes special character context
- Result stored with proper character encoding

### Performance Test Cases

#### TC-WRK-014: Concurrent Processing Validation
**Scenario:** Multiple workers process test cases simultaneously  
**Given:** 100 test cases queued simultaneously
**When:** Multiple worker instances process concurrently
**Then:**
- No race conditions or data conflicts occur
- Each test case processed exactly once
- Progress updates handled atomically
- All results stored correctly

#### TC-WRK-015: Processing Time Limits
**Scenario:** Test case processing within time limits  
**Given:** Normal test case processing scenario
**When:** Worker processes single test case
**Then:**
- Total processing time < 5 minutes
- Copilot Studio response time < 2 minutes
- LLM Judge evaluation time < 1 minute
- Dataverse operations complete quickly

#### TC-WRK-016: Memory Usage Optimization
**Scenario:** Worker handles memory efficiently  
**Given:** Processing large evaluation batches
**When:** Worker processes multiple test cases
**Then:**
- Memory usage remains within allocated limits
- No memory leaks detected
- Garbage collection performs efficiently  
- Resource cleanup occurs properly

### Message Handling Test Cases

#### TC-WRK-017: Message Deserialization
**Scenario:** Various message formats handled correctly  
**Given:** Different valid message format variations
**When:** Worker deserializes Service Bus messages
**Then:**
- All required fields extracted correctly
- Optional fields handled appropriately
- Invalid messages rejected with clear errors
- Message correlation IDs maintained

#### TC-WRK-018: Retry Counter Tracking
**Scenario:** Message retry attempts tracked properly  
**Given:** Message with existing retry count
**When:** Processing fails and message retried
**Then:**
- Retry count incremented correctly
- Maximum retry limit enforced
- Message history maintained for debugging
- Dead letter queue used when retries exhausted

#### TC-WRK-019: Message Ordering Independence
**Scenario:** Message processing order doesn't affect results  
**Given:** Test cases from same evaluation run in random order
**When:** Workers process messages out of sequence
**Then:**
- Results identical regardless of processing order
- Progress tracking accurate despite ordering
- No dependencies between individual test cases
- Final results consistent across runs