# User Stories: Worker Function (ProcessTestCase)

## Epic: Test Case Processing and Evaluation
As a system, I want to process individual test cases through automated evaluation so that I can provide consistent scoring and feedback for agent responses.

### Story 1: Process Service Bus Messages
**As a** worker function  
**I want to** receive test case messages from Service Bus queue  
**So that** I can process them for evaluation

**Acceptance Criteria:**
- [ ] Listen to Service Bus queue for test case messages
- [ ] Parse message content (evaluationRunId, testCaseId, prompt, expectedResponse, testType)
- [ ] Handle message deserialization errors gracefully
- [ ] Support concurrent processing (up to 1000 instances)
- [ ] Implement dead letter queue handling

### Story 2: Load Type-Specific Judge Prompts
**As a** worker function  
**I want to** load appropriate evaluation prompts from Blob Storage based on test type  
**So that** I can apply the correct evaluation criteria

**Acceptance Criteria:**
- [ ] Load prompts from Blob Storage container 'evaluation-prompts'
- [ ] Support all test types: 778760000 (Similarity), 778760001 (Accuracy), 778760002 (Relevancy)
- [ ] Cache prompts in memory for performance
- [ ] Handle prompt loading failures with fallback
- [ ] Support prompt versioning and updates

**Prompt Files:**
- `778760000-judge-prompt.txt` - Similarity evaluation
- `778760001-judge-prompt.txt` - Accuracy evaluation  
- `778760002-judge-prompt.txt` - Relevancy evaluation

### Story 3: Execute Test Against Copilot Studio Agent
**As a** worker function  
**I want to** send test prompts to Copilot Studio via Direct Line API  
**So that** I can capture agent responses for evaluation

**Acceptance Criteria:**
- [ ] Establish conversation session with Copilot Studio agent
- [ ] Send user prompt and receive agent response
- [ ] Handle conversation state management
- [ ] Implement retry logic for API failures
- [ ] Manage conversation timeouts (5 minutes max)
- [ ] Support rate limiting and throttling

### Story 4: Evaluate Response Using LLM Judge
**As a** worker function  
**I want to** evaluate agent responses using Azure OpenAI with type-specific criteria  
**So that** I can provide objective scoring

**Acceptance Criteria:**
- [ ] Send evaluation request to Azure OpenAI with appropriate prompt template
- [ ] Include test case context (prompt, expected response, actual response)
- [ ] Parse JSON response with score (0-1), passed (boolean), reasoning, and evaluation
- [ ] Handle LLM Judge API errors and rate limiting
- [ ] Normalize scores to 0-1 scale consistently across test types
- [ ] Capture detailed reasoning for transparency

**Evaluation Criteria by Test Type:**
- **Similarity (778760000)**: Semantic meaning alignment
- **Accuracy (778760001)**: Factual correctness
- **Relevancy (778760002)**: Relevance to user prompt

### Story 5: Store Evaluation Results
**As a** worker function  
**I want to** save evaluation results to Dataverse  
**So that** they can be aggregated and reported

**Acceptance Criteria:**
- [ ] Create evaluation result record in `mcicaps_evaluationresults` table
- [ ] Include all fields: actualresponse, score, passed, evaluation, reasoning, testcase
- [ ] Link result to original test case and evaluation run
- [ ] Update evaluation run progress counter atomically
- [ ] Handle concurrent writes without conflicts

### Story 6: Track Processing Progress
**As a** worker function  
**I want to** update evaluation run progress  
**So that** the system can track completion status

**Acceptance Criteria:**
- [ ] Increment completed test case counter for evaluation run
- [ ] Check if all test cases are completed
- [ ] Update evaluation run status if processing is complete
- [ ] Calculate preliminary metrics (average score, pass rate)
- [ ] Handle race conditions in progress updates

### Story 7: Handle Processing Errors
**As a** worker function  
**I want to** handle errors gracefully during test case processing  
**So that** individual failures don't stop the entire evaluation run

**Acceptance Criteria:**
- [ ] Catch and log all processing errors with context
- [ ] Continue processing other test cases on individual failures
- [ ] Send failed messages to dead letter queue after retry attempts
- [ ] Update evaluation run with error counts and details
- [ ] Implement exponential backoff for transient failures
- [ ] Provide detailed error context for debugging