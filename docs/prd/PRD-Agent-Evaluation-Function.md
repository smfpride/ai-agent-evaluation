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
2. **Azure Service Bus**: Message queue for test case distribution and scaling
3. **Worker Functions**: Service Bus-triggered evaluation processors
4. **Dataverse Integration**: Data source and result storage
5. **Copilot Studio Agent**: Target system under test via Direct Line API
6. **Azure OpenAI**: LLM Judge for response evaluation
7. **Result Storage & Aggregation**: Evaluation outcomes and run completion tracking

### 2.3 Function App Architecture
- **HTTP Orchestrator Function**: `StartEvaluationRun` - Initiates evaluation runs
- **Service Bus Worker Function**: `ProcessTestCase` - Executes individual test evaluations
- **Timer Function**: `MonitorEvaluationRuns` - Tracks completion and updates final status

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

#### 3.1.2 Worker Function Workflow (`ProcessTestCase`)
1. Receive test case message from Service Bus queue
2. Parse test case type (`778760000` = Similarity, `778760001` = Accuracy, `778760002` = Relevancy)
3. Load appropriate LLM Judge prompt template from Azure Blob Storage based on test type
4. Execute test case against Copilot Studio agent via Direct Line API
5. Evaluate agent response using Azure OpenAI Judge with type-specific criteria
6. Store individual evaluation result in Dataverse
7. Update evaluation run progress counter
8. Check if all test cases are completed and update overall status if needed

#### 3.1.3 Monitoring Workflow (`MonitorEvaluationRuns`)
1. Timer-triggered function (every 1 minute)
2. Query evaluation runs with status `778760002` (Running)
3. Check completion status based on test case count vs results count
4. Calculate overall metrics (average score, pass/fail)
5. Update evaluation run status to `778760004` (Completed) or `778760003` (Errored) as appropriate

### 3.2 Data Schema Integration

#### 3.2.1 Input Tables
- **mcicaps_evaluationruns**: Source evaluation run configuration
  - Primary Key: `mcicaps_evaluationrunsid`
  - Key Fields: `mcicaps_testset`, `mcicaps_passingthreshold`, `mcicaps_overallpass`, `mcicaps_overallscore`, `statuscode`
  - Status Codes: `1` (Created), `778760001` (Execute/Run), `778760002` (Running), `778760003` (Errored), `778760004` (Completed)
  
- **mcicaps_testcases**: Test case definitions
  - Primary Key: `mcicaps_testcasesid`
  - Key Fields: `mcicaps_prompt`, `mcicaps_expectedresponse`, `mcicaps_testset`, `mcicaps_testtype`
  - Test Types: `778760000` (Similarity), `778760001` (Accuracy), `778760002` (Relevancy)

#### 3.2.2 Output Tables
- **mcicaps_evaluationresults**: Individual test results
  - Key Fields: `mcicaps_actualresponse`, `mcicaps_score`, `mcicaps_passed`, `mcicaps_evaluation`, `mcicaps_reasoning`, `mcicaps_testcase`

#### 3.2.3 Message Queue Schema
- **Service Bus Message**: Test case execution request
  ```json
  {
    "evaluationRunId": "guid",
    "testCaseId": "guid",
    "testCaseName": "string",
    "prompt": "string", 
    "expectedResponse": "string",
    "testType": 778760000,
    "retryCount": 0,
    "enqueuedAt": "datetime"
  }
  ```
  
  **Test Type Values**:
  - `778760000`: Similarity
  - `778760001`: Accuracy  
  - `778760002`: Relevancy

### 3.3 API Integrations

#### 3.3.1 Azure Service Bus Integration
- Message queuing for test case distribution
- Dead letter queue handling for failed messages
- Batch processing capabilities
- Automatic scaling based on queue depth

#### 3.3.2 Copilot Studio Direct Line API
- Establish conversation sessions
- Send prompts and receive responses
- Handle conversation state management
- Implement retry logic for API failures

#### 3.3.3 Azure OpenAI Judge Integration
- Type-specific evaluation prompts loaded from Azure Blob Storage (778760000=Similarity, 778760001=Accuracy, 778760002=Relevancy)
- Structured prompt templates for consistent evaluation
- Score normalization (0-1 scale) with type-specific criteria
- Reasoning capture for transparency and debugging
- Rate limiting and error handling

#### 3.3.4 Azure Blob Storage Integration
- Centralized storage for evaluation prompt templates
- Version-controlled prompt management
- Easy updates without function redeployment
- Caching strategy for frequently accessed prompts

## 4. Technical Requirements

### 4.1 Azure Function Specifications

#### 4.1.1 Orchestrator Function (`StartEvaluationRun`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: HTTP (POST method)
- **Authentication**: Function-level authentication
- **Timeout**: 5 minutes (quick orchestration only)
- **Response**: HTTP 202 Accepted

#### 4.1.2 Worker Functions (`ProcessTestCase`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: Service Bus Queue
- **Concurrency**: Up to 1000 concurrent instances
- **Timeout**: 5 minutes per test case
- **Scaling**: Event-driven auto-scaling

#### 4.1.3 Monitor Function (`MonitorEvaluationRuns`)
- **Runtime**: .NET 8 (isolated process model)
- **Trigger**: Timer (every 1 minute)
- **Timeout**: 2 minutes
- **Host Version**: v4 for all functions

### 4.2 Authentication & Security
- **Managed Identity**: Primary authentication method for ALL Azure services (no connection strings)
- **Dataverse**: Managed identity with proper application user setup
- **Azure OpenAI**: Managed identity with Cognitive Services OpenAI User role
- **Service Bus**: Managed identity with Service Bus Data Sender/Receiver roles
- **Azure Blob Storage**: Managed identity with Storage Blob Data Contributor role
- **Key Vault**: Managed identity with Key Vault Secrets User role (for non-Azure service secrets only)
- **Application Insights**: Managed identity with Monitoring Metrics Publisher role

### 4.3 Configuration Management
```json
{
  "ManagedIdentityClientId": null,
  "DataverseEnvironmentUrl": "https://yourorg.crm.dynamics.com/",
  "CopilotStudioDirectLineSecret": "Key Vault Reference",
  "AzureOpenAIEndpoint": "https://your-openai.openai.azure.com/",
  "AzureOpenAIDeployment": "gpt-4o",
  "JudgePromptTemplates": {
    "StorageType": "BlobStorage",
    "StorageAccountName": "yourstorageaccount",
    "ContainerName": "evaluation-prompts",
    "PromptFiles": {
      "778760000": "similarity-judge-prompt.txt",
      "778760001": "accuracy-judge-prompt.txt", 
      "778760002": "relevancy-judge-prompt.txt"
    }
  },
  "ServiceBusNamespace": "yournamespace.servicebus.windows.net",
  "TestCaseQueueName": "evaluation-testcases",
  "MaxConcurrentWorkers": 1000,
  "DefaultTimeoutMinutes": 5,
  "MonitoringIntervalMinutes": 1,
  "StatusCodes": {
    "Created": 1,
    "ExecuteRun": 778760001,
    "Running": 778760002,
    "Errored": 778760003,
    "Completed": 778760004
  }
}
```

**Note**: All Azure service connections use managed identity. Only the Copilot Studio Direct Line secret requires Key Vault storage as it's an external service credential.

### 4.4 Managed Identity RBAC Requirements

The Function App's managed identity must be assigned the following roles:

#### 4.4.1 Azure OpenAI Service
- **Role**: `Cognitive Services OpenAI User`
- **Scope**: Azure OpenAI resource
- **Purpose**: Generate completions using the judge model

#### 4.4.2 Service Bus
- **Roles**: 
  - `Azure Service Bus Data Sender` (for enqueueing messages)
  - `Azure Service Bus Data Receiver` (for processing queue messages)
- **Scope**: Service Bus namespace
- **Purpose**: Send and receive test case messages

#### 4.4.3 Blob Storage
- **Role**: `Storage Blob Data Contributor`
- **Scope**: Storage account or specific containers
- **Purpose**: Read evaluation prompt templates

#### 4.4.4 Key Vault
- **Role**: `Key Vault Secrets User`
- **Scope**: Key Vault instance
- **Purpose**: Read Copilot Studio Direct Line secret

#### 4.4.5 Dataverse
- **Setup**: Application user with appropriate security roles
- **Roles**: 
  - Read access to `mcicaps_evaluationruns` and `mcicaps_testcases`
  - Create/Update access to `mcicaps_evaluationresults`
  - Update access to `mcicaps_evaluationruns` (for status updates)
- **Purpose**: Query test data and store evaluation results

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

The evaluation prompts are stored in Azure Blob Storage for better management and versioning capabilities:

#### 5.3.0 Prompt Storage Architecture
- **Storage Location**: Azure Blob Storage container `evaluation-prompts`
- **File Naming**: `{testType}-judge-prompt.txt` (e.g., `778760000-judge-prompt.txt`)
- **Versioning**: Blob versioning enabled for prompt history and rollback
- **Caching**: Function app caches prompts in memory with configurable TTL
- **Updates**: Prompts can be updated without function redeployment

**Blob Structure**:
```
evaluation-prompts/
├── 778760000-judge-prompt.txt    (Similarity evaluation)
├── 778760001-judge-prompt.txt    (Accuracy evaluation)
├── 778760002-judge-prompt.txt    (Relevancy evaluation)
└── metadata.json                 (Prompt versions and metadata)
```

#### 5.3.1 Similarity Test Evaluation (Test Type: 778760000)
```
You are an AI response evaluator focusing on semantic similarity. Compare the agent's response to the expected response to determine how semantically similar they are.

**Test Case**: {testCaseName}
**Test Type**: Similarity
**User Prompt**: {userPrompt}
**Expected Response**: {expectedResponse}
**Actual Response**: {actualResponse}

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
  "passed": boolean,
  "reasoning": "detailed explanation of semantic similarity assessment",
  "evaluation": "brief summary of similarity analysis"
}
```

#### 5.3.2 Accuracy Test Evaluation (Test Type: 778760001)
```
You are an AI response evaluator focusing on factual accuracy. Evaluate whether the agent's response provides accurate information for the original user prompt.

**Test Case**: {testCaseName}
**Test Type**: Accuracy
**User Prompt**: {userPrompt}
**Expected Response**: {expectedResponse} (reference for context)
**Actual Response**: {actualResponse}

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
  "passed": boolean,
  "reasoning": "detailed explanation of accuracy assessment with specific factual issues noted",
  "evaluation": "brief summary of accuracy analysis"
}
```

#### 5.3.3 Relevancy Test Evaluation (Test Type: 778760002)
```
You are an AI response evaluator focusing on relevancy. Evaluate whether the agent's response is relevant and appropriately addresses the original user prompt.

**Test Case**: {testCaseName}
**Test Type**: Relevancy
**User Prompt**: {userPrompt}
**Expected Response**: {expectedResponse} (reference for context)
**Actual Response**: {actualResponse}

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
  "passed": boolean,
  "reasoning": "detailed explanation of relevancy assessment",
  "evaluation": "brief summary of relevancy analysis"
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
- **Azure Service Bus Premium**: For high-throughput message processing
- **Azure Blob Storage**: For storing evaluation prompt templates and versioning
- **Application Insights**: For monitoring and telemetry
- **Key Vault**: For external service secrets only (Copilot Studio)
- **Managed Identity**: System-assigned identity for all Azure service authentication
- **RBAC Assignments**: Properly scoped role assignments for least privilege access

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
- ✅ Successfully execute evaluation runs end-to-end for all test types (778760000, 778760001, 778760002)
- ✅ Apply appropriate evaluation criteria based on test case type numerical values
- ✅ Store complete evaluation results in Dataverse with type-specific scoring
- ✅ Generate consistent scoring using LLM Judge with specialized prompts
- ✅ Handle errors gracefully without data loss across all test types
- ✅ Correctly update evaluation run status using proper Dataverse option set values

### 12.2 Non-Functional Success
- ✅ Process 50,000 test cases within 1 hour
- ✅ Achieve 99.9%+ success rate for valid inputs
- ✅ Support 1000+ concurrent worker functions
- ✅ Maintain sub-second orchestrator response time
- ✅ Provide comprehensive logging and monitoring
- ✅ Handle peak loads with automatic scaling

## 13. Future Enhancements

### 13.1 Planned Features
- Batch evaluation API for multiple runs
- Custom judge model configurations per test type
- Real-time evaluation progress tracking with type breakdown
- Advanced analytics and reporting by test type
- Integration with Azure DevOps for CI/CD testing
- A/B testing for different evaluation prompt variations
- Prompt template versioning and rollback capabilities
- Dynamic prompt template updates via management API

### 13.2 Scaling Considerations
- **Azure Durable Functions**: For complex orchestration workflows
- **Event Grid Integration**: For real-time status notifications  
- **Multi-region Deployment**: For global availability and performance
- **Cosmos DB Integration**: For high-scale result storage and analytics
- **Azure Container Apps**: Alternative hosting for extreme scale scenarios

---

**Document Version**: 1.0  
**Last Updated**: November 7, 2024  
**Status**: Draft - Pending Review