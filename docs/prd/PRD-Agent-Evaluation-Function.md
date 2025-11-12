# Product Requirements Document: AI Agent Evaluation Azure Function

## 1. Overview

### 1.1 Purpose
This document outlines the requirements for developing a .NET Azure Function that automates the evaluation of Copilot Studio agents using automated test cases, direct line API integration, and LLM-as-a-Judge evaluation methodology.

### 1.2 Objectives
- Automate agent testing and evaluation workflows
- Provide standardized scoring and feedback for agent responses
- Enable continuous evaluation and quality assurance for Copilot Studio agents
- Store evaluation results for analysis and reporting

## 2. Architecture Overview

### 2.1 High-Level Architecture
```
HTTP Trigger → Orchestrator Function → Service Bus Queue → Worker Functions → Copilot Studio (Direct Line API) → Azure OpenAI (Judge) → Results Storage → Status Update
```

### 2.2 Key Components
1. **Orchestrator Function**: HTTP-triggered coordinator (.NET 8, isolated process model)
2. **Azure Service Bus**: Message queues for test case distribution and scaling (2 queues: single-turn and multi-turn)
3. **Worker Functions**: Service Bus-triggered evaluation processors (single-turn and multi-turn)
4. **Durable Functions**: Long-running orchestrators for multi-turn conversations (alternative to Service Bus worker)
5. **Dataverse Integration**: Data source and result storage
6. **Copilot Studio Agent**: Target system under test via Direct Line API
7. **Azure OpenAI**: LLM Judge for response evaluation (Managed Identity authentication)
8. **Result Storage & Aggregation**: Evaluation outcomes and run completion tracking

### 2.3 Function App Architecture
- **HTTP Orchestrator Function**: `StartEvaluationRun` - Initiates evaluation runs
- **Service Bus Worker Function**: `ProcessTestCase` - Executes individual single-turn test evaluations
- **Multi-Turn Worker Function**: `ProcessMultiTurnTestCase` - Executes multi-turn conversation tests
- **Durable Functions Orchestrator**: `MultiTurnTestCaseOrchestrator` - Alternative long-running orchestration for multi-turn tests
- **Durable Functions Activities**: Helper activities for turn execution and result aggregation
- **Timer Function**: `MonitorEvaluationRuns` - Tracks completion and updates final status
- **Health Check Function**: `HealthCheck` - Verifies system connectivity and health

## 3. Functional Requirements

### 3.1 Core Workflow

#### 3.1.1 Orchestrator Function Workflow (`StartEvaluationRun`)
1. Receive evaluation run ID via HTTP trigger
2. Retrieve evaluation run from Dataverse
3. Update evaluation run status to `778760002` (Running)
4. Fetch all associated test cases from the test set
5. Create evaluation context and queue messages for each test case
6. Send test case messages to Azure Service Bus queue
7. Return HTTP 202 Accepted with operation tracking information

#### 3.1.2 Worker Function Workflow (`ProcessTestCase` - Single-Turn)
1. Receive test case message from Service Bus queue (`test-case-processing`)
2. Parse test case including EvaluatorType GUID reference
3. Load appropriate LLM Judge prompt template from Dataverse EvaluatorType record
4. Execute test case against Copilot Studio agent via Direct Line API
5. Evaluate agent response using Azure OpenAI Judge (Managed Identity) with type-specific criteria
6. Calculate pass/fail based on passing threshold
7. Store individual evaluation result in Dataverse
8. Atomically increment evaluation run progress counter
9. Handle errors and retry logic with exponential backoff

#### 3.1.3 Multi-Turn Worker Function Workflow (`ProcessMultiTurnTestCase`)
1. Receive multi-turn test case message from Service Bus queue (`multi-turn-test-case-processing`)
2. Fetch test case and all associated turns (ordered by sequence) from Dataverse
3. Execute turns sequentially while maintaining conversation context:
   - Turn 1: Create new conversation (conversationId = null)
   - Subsequent turns: Use conversationId from Turn 1 to maintain context
4. Evaluate each turn independently using Azure OpenAI Judge
5. Store per-turn evaluation results in Dataverse
6. Create aggregate result with overall pass rate and metrics
7. Atomically increment evaluation run progress counter
8. Continue execution even if individual turns fail (resilient design)

#### 3.1.4 Durable Functions Orchestrator Workflow (`MultiTurnTestCaseOrchestrator` - Alternative)
1. Started by Orchestrator Function for multi-turn test cases
2. Uses Durable Functions for unlimited execution time (no 5-minute limit)
3. Calls activities sequentially:
   - `GetTestCaseWithTurns`: Retrieve test case and turns from Dataverse
   - `ExecuteSingleTurn`: Execute each turn and evaluate (with conversation context)
   - `CreateAggregateResult`: Generate final multi-turn result
4. Maintains state and conversation context across activities
5. Supports pause/resume and retry logic
6. Ideal for very long multi-turn conversations (10+ turns)

#### 3.1.5 Monitoring Workflow (`MonitorEvaluationRuns`)
1. Timer-triggered function (every 1 minute)
2. Query evaluation runs with status `778760002` (Running)
3. Check completion status based on test case count vs results count
4. Detect stalls: No progress for 30+ minutes or total runtime > 120 minutes
5. Calculate overall metrics (average score, pass/fail rate)
6. Update evaluation run status to `778760004` (Completed) or `778760003` (Errored) as appropriate
7. Set end time and progress percentage to 100%

### 3.2 Data Schema Integration

#### 3.2.1 Input Tables
- **mcicaps_evaluationruns**: Source evaluation run configuration
  - Primary Key: `mcicaps_evaluationrunsid`
  - Key Fields: `mcicaps_testset`, `mcicaps_agenttoevalid` (lookup to AgentToEval), `mcicaps_passingthreshold`, `mcicaps_overallpass`, `mcicaps_overallscore`, `mcicaps_progresspercentage`, `mcicaps_starttime`, `mcicaps_endtime`, `mcicaps_lastprogressupdate`, `mcicaps_totaltestcases`, `mcicaps_completedtestcases`, `mcicaps_failedtestcases`, `statuscode`
  - Status Codes: `1` (Created), `778760001` (Execute/Run), `778760002` (Running), `778760003` (Errored), `778760004` (Completed)

- **mcicaps_agenttoevals**: Agent configuration (NEW)
  - Primary Key: `mcicaps_agenttoevalid`
  - Key Fields: `mcicaps_name`, `mcicaps_directlinespeechurl` (Direct Line endpoint)
  - Purpose: Centralized agent configuration referenced by evaluation runs
  
- **mcicaps_testcases**: Test case definitions
  - Primary Key: `mcicaps_testcasesid`
  - Key Fields: `mcicaps_prompt` (UserInput), `mcicaps_expectedresponse`, `mcicaps_testset`, `mcicaps_evaluatortypeid` (lookup to EvaluatorType), `mcicaps_ismultiturn`, `mcicaps_conversationid`
  - Multi-Turn Support: `mcicaps_ismultiturn` flag indicates conversation test cases

- **mcicaps_testcaseturns**: Multi-turn conversation turns (NEW)
  - Primary Key: `mcicaps_testcaseturnid`
  - Key Fields: `mcicaps_testcaseid` (parent test case), `mcicaps_sequence` (turn order), `mcicaps_prompttext` (user input for this turn), `mcicaps_expectedresponse` (expected response for this turn)
  - Relationship: N:1 with mcicaps_testcases

- **mcicaps_evaluatortypes**: Evaluation type definitions (NEW)
  - Primary Key: `mcicaps_evaluatortypeid` (GUID)
  - Key Fields: `mcicaps_name` (e.g., "Similarity", "Accuracy", "Relevancy"), `mcicaps_description`, `mcicaps_prompttemplate` (LLM Judge prompt text), `mcicaps_systemprompttemplate` (optional system prompt)
  - Purpose: Stores evaluation criteria and prompts in Dataverse (replaces Azure Blob Storage approach)

#### 3.2.2 Output Tables
- **mcicaps_evaluationresults**: Individual test results
  - Key Fields: `mcicaps_actualresponse`, `mcicaps_score`, `mcicaps_passed`, `mcicaps_evaluation`, `mcicaps_reasoning`, `mcicaps_testcase`, `mcicaps_testcaseturnid` (for multi-turn results), `mcicaps_conversationid`, `mcicaps_responsetimems`, `mcicaps_isaggregate` (for multi-turn aggregate results), `mcicaps_errormessage`
  - Multi-Turn Support: Individual turn results linked via `mcicaps_testcaseturnid`, aggregate results marked with `mcicaps_isaggregate = true`

#### 3.2.3 Message Queue Schema

**Single-Turn Service Bus Message** (`test-case-processing` queue):
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "directLineUrl": "string",
  "userInput": "string", 
  "expectedResponse": "string",
  "evaluatorTypeId": "guid",
  "conversationId": "string (optional)",
  "passingThreshold": 0.7,
  "retryCount": 0,
  "maxRetries": 3
}
```

**Multi-Turn Service Bus Message** (`multi-turn-test-case-processing` queue):
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "directLineUrl": "string",
  "evaluatorTypeId": "guid",
  "passingThreshold": 0.7,
  "retryCount": 0,
  "maxRetries": 3
}
```

**Note**: Multi-turn messages reference the test case ID only; individual turns are fetched from Dataverse.

### 3.3 API Integrations

#### 3.3.1 Azure Service Bus Integration
- Message queuing for test case distribution
- **Two queues**: `test-case-processing` (single-turn) and `multi-turn-test-case-processing` (multi-turn)
- Dead letter queue handling for failed messages
- Batch processing capabilities
- Automatic scaling based on queue depth
- Managed Identity authentication (no connection strings)

#### 3.3.2 Copilot Studio Direct Line API
- Establish conversation sessions
- Send prompts and receive responses
- Handle conversation state management
- Implement retry logic for API failures

#### 3.3.3 Azure OpenAI Judge Integration
- Evaluation prompts loaded from Dataverse EvaluatorType records (not Azure Blob Storage)
- Structured prompt templates for consistent evaluation
- Score normalization (0-1 scale) with type-specific criteria
- Reasoning capture for transparency and debugging
- Rate limiting and error handling with exponential backoff
- **Managed Identity authentication** (Cognitive Services OpenAI User role)

#### 3.3.4 Dataverse Prompt Template Integration
- Centralized storage for evaluation prompt templates in EvaluatorType records
- Version-controlled prompt management through Dataverse
- Easy updates without function redeployment
- Supports both prompt template and system prompt template
- Fallback to default embedded prompts if Dataverse template is unavailable

## 4. Technical Requirements

### 4.1 Azure Function Specifications

#### 4.1.1 Orchestrator Function (`StartEvaluationRun`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: HTTP (POST method)
- **Authentication**: Function-level authentication
- **Timeout**: 5 minutes (quick orchestration only)
- **Response**: HTTP 202 Accepted

#### 4.1.2 Worker Functions (`ProcessTestCase` and `ProcessMultiTurnTestCase`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: Service Bus Queue (2 separate queues)
- **Concurrency**: Up to 1000 concurrent instances per queue
- **Timeout**: 5 minutes per test case (single-turn), 15 minutes per test case (multi-turn)
- **Scaling**: Event-driven auto-scaling based on queue depth

#### 4.1.3 Monitor Function (`MonitorEvaluationRuns`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: Timer (every 1 minute)
- **Timeout**: 2 minutes
- **Host Version**: v4 for all functions

#### 4.1.4 Durable Functions Orchestrator (`MultiTurnTestCaseOrchestrator`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: Durable Functions orchestration
- **Timeout**: Unlimited (can run for hours)
- **Purpose**: Alternative to Service Bus worker for very long multi-turn conversations
- **Activities**: GetTestCaseWithTurns, ExecuteSingleTurn, CreateAggregateResult

### 4.2 Authentication & Security
- **Managed Identity**: Primary authentication method for ALL Azure services (no connection strings or API keys)
- **Dataverse**: OAuth connection string with ClientId + ClientSecret (Managed Identity not supported by Dataverse SDK)
- **Azure OpenAI**: Managed identity with Cognitive Services OpenAI User role
- **Service Bus**: Managed identity with Service Bus Data Owner role
- **Azure Blob Storage**: Managed identity with Storage Blob Data Contributor role (if used)
- **Key Vault**: Managed identity with Key Vault Secrets User role (for Dataverse OAuth credentials only)
- **Application Insights**: Managed identity with Monitoring Metrics Publisher role

### 4.3 Configuration Management
```json
{
  "DataverseConnectionString": "@Microsoft.KeyVault(VaultName=kv-name;SecretName=DataverseConnectionString)",
  "AzureOpenAIEndpoint": "https://your-openai.openai.azure.com/",
  "ServiceBus__Namespace": "yournamespace.servicebus.windows.net",
  "ServiceBus__QueueName": "test-case-processing",
  "ServiceBusNamespace__fullyQualifiedNamespace": "yournamespace.servicebus.windows.net",
  "KeyVaultUrl": "https://your-keyvault.vault.azure.net/",
  "DefaultEvaluationTimeoutMinutes": "5",
  "MaxConcurrentTestCases": "100",
  "CopilotStudio__TimeoutSeconds": "60",
  "CopilotStudio__BotResponseTimeoutSeconds": "60",
  "CopilotStudio__MaxRetryAttempts": "3",
  "AzureOpenAI__MaxRetryAttempts": "5",
  "AzureOpenAI__TimeoutSeconds": "60"
}
```

**Note**: All Azure service connections use managed identity. Only Dataverse requires OAuth connection string stored in Key Vault as it does not support Managed Identity authentication.

### 4.4 Managed Identity RBAC Requirements

The Function App's managed identity must be assigned the following roles:

#### 4.4.1 Azure OpenAI Service
- **Role**: `Cognitive Services OpenAI User`
- **Scope**: Azure OpenAI resource
- **Purpose**: Generate completions using the judge model

#### 4.4.2 Service Bus
- **Roles**: 
  - `Azure Service Bus Data Owner` (for both sending and receiving messages)
- **Scope**: Service Bus namespace
- **Purpose**: Send and receive test case messages from both queues

#### 4.4.3 Blob Storage (Optional - Not currently used)
- **Role**: `Storage Blob Data Contributor`
- **Scope**: Storage account or specific containers
- **Purpose**: Read evaluation prompt templates (if migrated from Dataverse to Blob Storage)
- **Note**: Currently prompts are stored in Dataverse EvaluatorType records

#### 4.4.4 Key Vault
- **Role**: `Key Vault Secrets User`
- **Scope**: Key Vault instance
- **Purpose**: Read Dataverse OAuth connection string (only external service credential)

#### 4.4.5 Dataverse
- **Setup**: OAuth Application User with ClientId and ClientSecret
- **Authentication**: Connection string stored in Key Vault
- **Roles**: 
  - Read access to `mcicaps_evaluationruns`, `mcicaps_testcases`, `mcicaps_testcaseturns`, `mcicaps_evaluatortypes`, `mcicaps_agenttoevals`
  - Create/Update access to `mcicaps_evaluationresults`
  - Update access to `mcicaps_evaluationruns` (for status and progress updates)
- **Purpose**: Query test data and store evaluation results
- **Note**: Managed Identity NOT supported by Microsoft Dataverse SDK

#### 4.4.6 Application Insights
- **Role**: `Monitoring Metrics Publisher`
- **Scope**: Application Insights resource
- **Purpose**: Send custom telemetry and metrics

## 5. Detailed Technical Design

### 5.1 Orchestrator Function Input/Output Schema

#### 5.1.1 Input Schema
```json
{
  "evaluationRunId": "guid",
  "options": {
    "priority": "high|normal|low",
    "timeoutMinutes": 5,
    "retryEnabled": true
  }
}
```

#### 5.1.2 Output Schema (HTTP 202 Response)
```json
{
  "evaluationRunId": "guid", 
  "status": "accepted",
  "message": "Evaluation run started successfully",
  "totalTestCases": 50000,
  "estimatedCompletionTime": "2024-11-07T15:30:00Z",
  "trackingUrl": "/api/status/{evaluationRunId}"
}
```

### 5.2 Status Tracking API Schema
```json
{
  "evaluationRunId": "guid",
  "status": "running|completed|failed|cancelled",
  "statusCode": 778760002,
  "progress": {
    "totalTestCases": 50000,
    "completedTestCases": 25000,
    "failedTestCases": 5,
    "successRate": 0.9998,
    "testTypeBreakdown": {
      "778760000": { "total": 20000, "completed": 10000, "failed": 2 },
      "778760001": { "total": 15000, "completed": 7500, "failed": 1 },
      "778760002": { "total": 15000, "completed": 7500, "failed": 2 }
    }
  },
  "summary": {
    "averageScore": 0.85,
    "passingThreshold": 0.7,
    "overallPassed": true
  },
  "startTime": "2024-11-07T14:00:00Z",
  "estimatedCompletionTime": "2024-11-07T15:30:00Z",
  "errors": []
}
```

### 5.3 LLM Judge Prompt Structure

The evaluation prompts are stored in Microsoft Dataverse EvaluatorType records for better management and versioning capabilities:

#### 5.3.0 Prompt Storage Architecture
- **Storage Location**: Microsoft Dataverse `mcicaps_evaluatortypes` table
- **Fields**: 
  - `mcicaps_name`: Evaluator type name (e.g., "Similarity", "Accuracy", "Relevancy")
  - `mcicaps_prompttemplate`: User prompt template text
  - `mcicaps_systemprompttemplate`: System prompt template text (optional)
- **Versioning**: Managed through Dataverse solution lifecycle
- **Updates**: Prompts can be updated through Dataverse without function redeployment
- **Fallback**: Function includes default embedded prompts if Dataverse template is unavailable

**Sample EvaluatorType Records**:
- Similarity (GUID reference)
- Accuracy (GUID reference)
- Relevancy (GUID reference)
- Custom domain-specific evaluators (fashion, healthcare, etc.)

#### 5.3.1 Similarity Test Evaluation
Stored in Dataverse EvaluatorType record with name "Similarity":
```
You are an AI response evaluator focusing on semantic similarity. Compare the agent's response to the expected response to determine how semantically similar they are.

**User Input**: {UserInput}
**Expected Response**: {ExpectedResponse}
**Actual Response**: {ActualResponse}
**Conversation ID**: {ConversationId}

**Evaluation Criteria**:
- Semantic meaning alignment between expected and actual responses
- Key information coverage and consistency
- Intent preservation and message equivalence
- Allowance for different phrasing that conveys the same meaning

**Scoring Guidelines**:
- 1.0: Responses are semantically identical or nearly identical
- 0.8-0.9: Very similar meaning with minor differences in phrasing
- 0.6-0.7: Similar core meaning but some differences in details or emphasis
- 0.4-0.5: Partially similar but notable differences in meaning or completeness
- 0.2-0.3: Limited similarity, significant differences in meaning
- 0.0-0.1: No meaningful semantic similarity

**Output Format** (JSON):
{
  "score": 0.0-1.0,
  "reasoning": "detailed explanation of semantic similarity assessment"
}
```

#### 5.3.2 Accuracy Test Evaluation
Stored in Dataverse EvaluatorType record with name "Accuracy":
```
You are an AI response evaluator focusing on factual accuracy. Evaluate whether the agent's response provides accurate information for the original user prompt.

**User Input**: {UserInput}
**Expected Response**: {ExpectedResponse} (reference for context)
**Actual Response**: {ActualResponse}
**Conversation ID**: {ConversationId}

**Evaluation Criteria**:
- Factual correctness of information provided
- Accuracy of data, numbers, dates, procedures, or instructions
- Absence of misleading or incorrect statements
- Completeness of accurate information needed to address the prompt

**Scoring Guidelines**:
- 1.0: All information is completely accurate and factually correct
- 0.8-0.9: Mostly accurate with very minor factual issues
- 0.6-0.7: Generally accurate but contains some factual errors or omissions
- 0.4-0.5: Mixed accuracy, significant factual issues present
- 0.2-0.3: Multiple factual errors, unreliable information
- 0.0-0.1: Highly inaccurate or completely wrong information

**Output Format** (JSON):
{
  "score": 0.0-1.0,
  "reasoning": "detailed explanation of accuracy assessment with specific factual issues noted"
}
```

#### 5.3.3 Relevancy Test Evaluation
Stored in Dataverse EvaluatorType record with name "Relevancy":
```
You are an AI response evaluator focusing on relevancy. Evaluate whether the agent's response is relevant and appropriately addresses the original user prompt.

**User Input**: {UserInput}
**Expected Response**: {ExpectedResponse} (reference for context)
**Actual Response**: {ActualResponse}
**Conversation ID**: {ConversationId}

**Evaluation Criteria**:
- Direct relevance to the user's question or request
- Appropriateness of the response scope and focus
- Addressing the core intent and needs expressed in the prompt
- Avoiding off-topic or tangential information

**Scoring Guidelines**:
- 1.0: Perfectly relevant, directly addresses all aspects of the prompt
- 0.8-0.9: Highly relevant with minor tangential elements
- 0.6-0.7: Generally relevant but may include some off-topic content
- 0.4-0.5: Partially relevant, misses some key aspects of the prompt
- 0.2-0.3: Limited relevance, mostly off-topic or inappropriate
- 0.0-0.1: Completely irrelevant or inappropriate response

**Output Format** (JSON):
{
  "score": 0.0-1.0,
  "reasoning": "detailed explanation of relevancy assessment"
}
```

## 6. Performance Requirements

### 6.1 Scalability
- Support up to 50,000 test cases per evaluation run
- Handle multiple concurrent evaluation runs (10+ simultaneous)
- Implement auto-scaling based on Service Bus queue depth
- Support burst processing for urgent evaluations

### 6.2 Performance Targets
- **Orchestrator Response**: < 30 seconds for 50,000 test cases (queue population)
- **Individual Test Case**: < 5 minutes per evaluation
- **Overall Throughput**: 1,000+ test cases per minute at peak
- **Queue Processing**: Near real-time processing with minimal latency
- **Availability**: 99.9% uptime
- **Error Rate**: < 0.1% for successful API calls

### 6.3 Service Bus Configuration
- **Queue Capacity**: 80GB message storage
- **Message TTL**: 7 days
- **Dead Letter Queue**: Enabled with retry policies
- **Concurrent Processing**: Up to 1,000 concurrent workers
- **Auto-scaling**: Event-driven based on queue depth

## 7. Error Handling & Resilience

### 7.1 Retry Logic
- **Service Bus**: Built-in retry with exponential backoff (max 3 attempts)
- **Dead Letter Queue**: Automatic handling of repeatedly failed messages
- **API Calls**: Custom retry logic for transient failures (Copilot Studio, OpenAI)
- **Circuit Breaker**: Pattern for external API protection

### 7.2 Fallback Strategies
- Continue processing remaining test cases if individual cases fail
- Poison message handling for malformed queue messages  
- Graceful degradation under high load conditions
- Automatic retry of failed test cases from dead letter queue

### 7.3 Monitoring & Alerting
- Queue depth monitoring and alerts
- Function failure rate tracking
- API response time monitoring
- Automatic escalation for critical failures

## 8. Monitoring & Observability

### 8.1 Logging Requirements
- Structured logging using ILogger
- Application Insights integration
- Custom telemetry for evaluation metrics

### 8.2 Key Metrics
- Evaluation run duration (orchestrator + total completion time)
- Queue processing rates and throughput
- Success/failure rates by test case and by evaluation run
- API response times (Copilot Studio, OpenAI, Dataverse)
- Resource utilization (CPU, memory, connections)
- Service Bus queue depth and processing lag

## 9. Security Considerations

### 9.1 Data Protection
- Encrypt sensitive data at rest and in transit
- Minimize data retention in function memory
- Audit trails for all evaluation activities

### 9.2 Access Controls
- **Principle of least privilege**: Function managed identity has only required permissions
- **RBAC-based access**: No shared keys or connection strings for Azure services
- **External API key rotation**: Only for non-Azure services (Copilot Studio)
- **Network security groups**: Function isolation with private endpoints where applicable
- **Audit trails**: All managed identity operations logged in Azure Activity Log

## 10. Development & Testing Strategy

### 10.1 Local Development
- Azure Functions Core Tools v4
- Local settings configuration
- Mock services for external dependencies

### 10.2 Testing Approach
- Unit tests for core evaluation logic
- Integration tests with Dataverse
- End-to-end testing with sample agents

## 11. Deployment Requirements

### 11.1 Infrastructure
- **Azure Functions Premium Plan**: For extended execution time and VNET integration
- **Azure Service Bus Standard**: For high-throughput message processing (2 queues)
- **Azure Storage**: For function app state and configuration
- **Application Insights**: For monitoring and telemetry
- **Key Vault**: For Dataverse OAuth credentials only
- **Managed Identity**: System-assigned identity for all Azure service authentication
- **RBAC Assignments**: Properly scoped role assignments for least privilege access
- **Azure OpenAI**: GPT-4o deployment with Managed Identity access

### 11.2 Scaling Configuration
- **Function App**: Auto-scale up to 1000 instances
- **Service Bus**: Premium tier with auto-scaling enabled
- **Connection Pools**: Optimized for high-concurrency scenarios
- **Resource Limits**: Configured for burst processing capabilities

### 11.2 CI/CD Pipeline
- Automated testing and validation
- Infrastructure as Code (Bicep/ARM)
- Environment-specific configuration

## 12. Success Criteria

### 12.1 Functional Success
- ✅ Successfully execute evaluation runs end-to-end for all test types
- ✅ Support both single-turn and multi-turn conversation evaluations
- ✅ Apply appropriate evaluation criteria based on EvaluatorType GUID references
- ✅ Store complete evaluation results in Dataverse with type-specific scoring
- ✅ Generate consistent scoring using LLM Judge with specialized prompts from Dataverse
- ✅ Handle errors gracefully without data loss across all test types
- ✅ Correctly update evaluation run status using proper Dataverse option set values
- ✅ Maintain conversation context across multi-turn conversations
- ✅ Provide real-time progress tracking with percentage and timestamps

### 12.2 Non-Functional Success
- ✅ Process 50,000 test cases within 1 hour
- ✅ Achieve 99.9%+ success rate for valid inputs
- ✅ Support 1000+ concurrent worker functions
- ✅ Maintain sub-second orchestrator response time
- ✅ Provide comprehensive logging and monitoring
- ✅ Handle peak loads with automatic scaling

## 13. Future Enhancements

### 13.1 Planned Features
- Custom domain-specific evaluator types (healthcare, finance, legal, etc.)
- Batch evaluation API for multiple runs
- Real-time evaluation progress tracking with WebSockets
- Advanced analytics and reporting by test type and domain
- Integration with Azure DevOps for CI/CD testing
- A/B testing for different evaluation prompt variations
- Migration option from Dataverse prompts to Azure Blob Storage for very high volume
- Custom evaluator type management UI
- Automated test case generation from conversation logs

### 13.2 Scaling Considerations
- **Azure Durable Functions**: For complex orchestration workflows
- **Event Grid Integration**: For real-time status notifications  
- **Multi-region Deployment**: For global availability and performance
- **Cosmos DB Integration**: For high-scale result storage and analytics
- **Azure Container Apps**: Alternative hosting for extreme scale scenarios

---

**Document Version**: 2.0  
**Last Updated**: November 12, 2025  
**Status**: Current - Reflects Implemented System