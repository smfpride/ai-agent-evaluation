# User Stories: Orchestrator Function (StartEvaluationRun)

## Epic: Evaluation Run Orchestration
As a system administrator, I want to initiate automated evaluation runs for Copilot Studio agents so that I can systematically test and measure agent performance.

### Story 1: Initialize Evaluation Run
**As a** QA engineer  
**I want to** trigger an evaluation run via HTTP API  
**So that** I can start automated testing of a Copilot Studio agent

**Acceptance Criteria:**
- [ ] Accept HTTP POST request with evaluation run ID
- [ ] Validate that evaluation run exists in Dataverse
- [ ] Update evaluation run status to 778760002 (Running)
- [ ] Return HTTP 202 Accepted within 30 seconds
- [ ] Include tracking information in response

**Definition of Done:**
- Function responds within 30 seconds for up to 50K test cases
- Proper error handling for invalid evaluation run IDs
- Audit logging for all orchestration activities

### Story 2: Retrieve Test Set Configuration
**As a** system orchestrator  
**I want to** fetch all test cases associated with an evaluation run  
**So that** I can queue them for individual processing

**Acceptance Criteria:**
- [ ] Query Dataverse for test cases linked to evaluation run
- [ ] Support all test types (778760000=Similarity, 778760001=Accuracy, 778760002=Relevancy)
- [ ] Handle test sets with up to 50,000 test cases
- [ ] Validate test case data integrity

### Story 3: Queue Test Cases for Processing
**As a** system orchestrator  
**I want to** send test cases to Service Bus queue  
**So that** worker functions can process them concurrently

**Acceptance Criteria:**
- [ ] Create properly formatted queue messages for each test case
- [ ] Include all required fields: evaluationRunId, testCaseId, prompt, expectedResponse, testType
- [ ] Handle queue capacity limits gracefully
- [ ] Implement retry logic for queue failures
- [ ] Support batch queuing for performance

### Story 4: Provide Operation Tracking
**As a** client application  
**I want to** receive tracking information when I start an evaluation run  
**So that** I can monitor progress and completion

**Acceptance Criteria:**
- [ ] Return evaluation run ID in response
- [ ] Provide total test case count
- [ ] Include estimated completion time
- [ ] Provide status tracking URL
- [ ] Support concurrent evaluation runs (10+)

### Story 5: Handle Orchestration Errors
**As a** system  
**I want to** gracefully handle errors during orchestration  
**So that** the system remains stable and provides clear feedback

**Acceptance Criteria:**
- [ ] Return appropriate HTTP error codes (400, 404, 500)
- [ ] Log detailed error information
- [ ] Update evaluation run status to 778760003 (Errored) on failure
- [ ] Provide meaningful error messages in responses
- [ ] Implement circuit breaker for external dependencies