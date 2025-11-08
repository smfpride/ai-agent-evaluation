# User Stories: API Integration

## Epic: External System Integration
As a system, I want to integrate seamlessly with Azure services and Copilot Studio so that I can provide comprehensive evaluation capabilities.

### Story 1: Dataverse Integration
**As a** system  
**I want to** read evaluation configurations and store results in Dataverse  
**So that** the evaluation system integrates with the existing data platform

**Acceptance Criteria:**
- [ ] Connect to Dataverse using Managed Identity authentication
- [ ] Read from `mcicaps_evaluationruns` table for run configurations
- [ ] Read from `mcicaps_testcases` table for test case definitions
- [ ] Write to `mcicaps_evaluationresults` table for individual results
- [ ] Update evaluation run status and metrics
- [ ] Handle Dataverse rate limiting and throttling
- [ ] Implement connection pooling for performance

**Table Operations:**
- **mcicaps_evaluationruns**: Read config, update status and results
- **mcicaps_testcases**: Read test definitions
- **mcicaps_evaluationresults**: Write evaluation outcomes

### Story 2: Azure Service Bus Integration
**As a** system  
**I want to** use Service Bus for reliable message queuing  
**So that** test cases can be processed asynchronously and at scale

**Acceptance Criteria:**
- [ ] Send test case messages to queue with proper formatting
- [ ] Receive and process messages in worker functions
- [ ] Handle message serialization/deserialization
- [ ] Implement dead letter queue processing
- [ ] Support auto-scaling based on queue depth
- [ ] Use Managed Identity for authentication

**Message Schema:**
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid", 
  "testCaseName": "string",
  "prompt": "string",
  "expectedResponse": "string",
  "testType": 778760000|778760001|778760002,
  "retryCount": 0,
  "enqueuedAt": "datetime"
}
```

### Story 3: Copilot Studio Direct Line API Integration
**As a** system  
**I want to** communicate with Copilot Studio agents via Direct Line API  
**So that** I can send prompts and receive responses for evaluation

**Acceptance Criteria:**
- [ ] Establish conversation sessions with Copilot Studio agents
- [ ] Send user messages and receive agent responses
- [ ] Handle conversation state and context
- [ ] Manage conversation lifecycle (start, message exchange, cleanup)
- [ ] Implement retry logic for API failures
- [ ] Handle rate limiting and throttling
- [ ] Support conversation timeouts

**API Workflow:**
1. Start conversation session
2. Send user prompt
3. Receive agent response  
4. End conversation session
5. Extract response text for evaluation

### Story 4: Azure OpenAI Integration
**As a** system  
**I want to** use Azure OpenAI as the LLM Judge  
**So that** I can provide consistent and objective evaluation of agent responses

**Acceptance Criteria:**
- [ ] Connect to Azure OpenAI using Managed Identity
- [ ] Send evaluation prompts with test case context
- [ ] Receive structured JSON responses with scores and reasoning
- [ ] Handle rate limiting and quota management
- [ ] Implement retry logic with exponential backoff
- [ ] Support different deployment models (gpt-4o)
- [ ] Parse and validate LLM Judge responses

**Request Format:**
- Prompt template with test case data
- Expected JSON response format
- Temperature and other model parameters

### Story 5: Azure Blob Storage Integration
**As a** system  
**I want to** store and retrieve evaluation prompt templates from Blob Storage  
**So that** prompts can be updated without redeploying functions

**Acceptance Criteria:**
- [ ] Read prompt templates from 'evaluation-prompts' container
- [ ] Support different prompts for each test type
- [ ] Cache prompts in memory for performance
- [ ] Handle blob access errors gracefully
- [ ] Support prompt versioning and updates
- [ ] Use Managed Identity for authentication

**Blob Structure:**
```
evaluation-prompts/
├── 778760000-judge-prompt.txt (Similarity)
├── 778760001-judge-prompt.txt (Accuracy)  
├── 778760002-judge-prompt.txt (Relevancy)
└── metadata.json
```

### Story 6: Azure Key Vault Integration
**As a** system  
**I want to** retrieve secrets and connection strings from Key Vault  
**So that** sensitive configuration is stored securely

**Acceptance Criteria:**
- [ ] Retrieve Dataverse connection strings
- [ ] Get Copilot Studio Direct Line secrets
- [ ] Access Service Bus connection strings
- [ ] Use Managed Identity for Key Vault access
- [ ] Cache secrets appropriately for performance
- [ ] Handle secret rotation and updates
- [ ] Log access without exposing secret values

### Story 7: Application Insights Integration
**As a** system  
**I want to** send telemetry and logs to Application Insights  
**So that** the system can be monitored and debugged effectively

**Acceptance Criteria:**
- [ ] Send structured logs with correlation IDs
- [ ] Track custom metrics (processing rates, success rates)
- [ ] Monitor function performance and duration
- [ ] Alert on error thresholds and anomalies
- [ ] Provide dashboard views for system health
- [ ] Include evaluation run context in telemetry