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
2. **Azure Service Bus**: Message queue for single-turn test case distribution and scaling
3. **Worker Function**: Service Bus-triggered evaluation processor for single-turn tests
4. **Durable Functions**: Long-running orchestrators for multi-turn conversations (PRIMARY multi-turn execution path)
5. **Dataverse Integration**: Data source and result storage
6. **Copilot Studio Agent**: Target system under test via Direct Line API
7. **Azure OpenAI**: LLM Judge for response evaluation (Managed Identity authentication)
8. **Result Storage & Aggregation**: Evaluation outcomes and run completion tracking

### 2.3 Function App Architecture
- **HTTP Orchestrator Function**: `StartEvaluationRun` - Initiates evaluation runs
- **Service Bus Worker Function**: `ProcessTestCase` - Executes individual single-turn test evaluations
- **Durable Functions Orchestrator**: `MultiTurnTestCaseOrchestrator` - Long-running orchestration for multi-turn tests
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
5. Fetch evaluator types selected for the run (N:N join)
6. Compute total evaluation units = Σ(single-turn: evaluatorCount) + Σ(multi-turn: (turnCount * evaluatorCount) + evaluatorCount for aggregates)
7. For each test case × evaluator type:
  - If evaluator requires expected response and none provided → create Not Applicable result (statuscode 778760004, score 0.0) immediately
  - Else queue single-turn message or start durable orchestration (multi-turn)
8. Update run counters (total/completed for NA short-circuits) and enqueue remaining messages
9. Return HTTP 202 Accepted with operation tracking information

#### 3.1.2 Worker Function Workflow (`ProcessTestCase` - Single-Turn with Multi-Evaluator & Concurrent Evaluation)
1. Receive test case message from Service Bus queue (`test-case-processing`)
2. Parse test case including **List<Guid> EvaluatorTypeIds** (multiple evaluators per message)
3. Execute test case against Copilot Studio agent **ONCE** via Direct Line API
4. **Concurrently evaluate** agent response with all evaluators using `Task.WhenAll`:
   - For each evaluator type:
     - Load LLM Judge prompt template from Dataverse EvaluatorType record
     - Evaluate using Azure OpenAI Judge (Managed Identity) with type-specific criteria
     - Calculate pass/fail based on passing threshold
     - Store individual evaluation result in Dataverse with evaluatorTypeId
     - Atomically increment evaluation run progress counter
5. Handle errors per evaluator with exponential backoff
6. All evaluations complete in parallel (2s for 3 evaluators instead of 6s sequential)

#### 3.1.3 Durable Functions Orchestrator Workflow (`MultiTurnTestCaseOrchestrator` with Multi-Evaluator & Concurrent Evaluation)
1. Started by Orchestrator Function for all multi-turn test cases (1 orchestration per test case)
2. Uses Durable Functions for unlimited execution time (no 5-minute limit)
3. Receives **List<Guid> EvaluatorTypeIds** and **Dictionary<Guid, Guid> AggregateResultIds** as input
4. Calls activities sequentially:
   - `GetTestCaseWithTurns`: Retrieve test case and turns from Dataverse
   - `ExecuteSingleTurn`: Execute each turn **ONCE** and **concurrently evaluate** with all evaluators
   - `CreateAggregateResult`: Generate final multi-turn result **for each evaluator**
5. Maintains state and conversation context across activities
6. Supports pause/resume and retry logic
7. Handles conversations of any length (1 turn to 100+ turns)

**Execution Flow**:
1. Fetch test case and all associated turns (ordered by sequence) from Dataverse
2. Execute turns sequentially while maintaining conversation context:
   - Turn 1: Create new conversation (conversationId = null)
   - Subsequent turns: Use conversationId from Turn 1 to maintain context
   - **Per turn**: Call Copilot Studio **ONCE**, then **concurrently evaluate** with all evaluators using `Task.WhenAll`
3. Store per-turn evaluation results in Dataverse (N results per turn for N evaluators)
4. Create **separate aggregate results** for each evaluator type
5. Filter Not Applicable (NA) results from aggregate calculations
6. Atomically increment evaluation run progress counter (by evaluator count)
7. Continue execution even if individual turns or evaluators fail (resilient design)

**Efficiency Gains**:
- **Copilot Studio calls**: 1 per turn (was: N per turn for N evaluators)
- **Evaluation time**: Concurrent (2s for 3 evaluators vs 6s sequential)
- **Example**: 5-turn test with 3 evaluators: 5 Copilot calls (was: 15)

#### 3.1.4 Monitoring Workflow (`MonitorEvaluationRuns`)
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
  - Key Fields: `mcicaps_prompt` (UserInput), `mcicaps_expectedresponse`, `mcicaps_testset`, `mcicaps_ismultiturn`, `mcicaps_conversationid`  
    (Evaluator type moved to run-level selection via join table)
  - Multi-Turn Support: `mcicaps_ismultiturn` flag indicates conversation test cases

- **mcicaps_testcaseturns**: Multi-turn conversation turns (NEW)
  - Primary Key: `mcicaps_testcaseturnid`
  - Key Fields: `mcicaps_testcaseid` (parent test case), `mcicaps_sequence` (turn order), `mcicaps_prompttext` (user input for this turn), `mcicaps_expectedresponse` (expected response for this turn)
  - Relationship: N:1 with mcicaps_testcases

- **mcicaps_judgeagents**: Judge agent configuration (NEW - Story 11)
  - Primary Key: `mcicaps_judgeagentid` (GUID)
  - Key Fields:
    - `mcicaps_name` (e.g., "GPT-4o Judge", "Finance Domain Judge")
    - `mcicaps_description`
    - `mcicaps_judgetype` (Choice: 778760000 = Azure OpenAI, 778760001 = Copilot Studio)
    - `mcicaps_aoaiendpoint` (Azure OpenAI endpoint URL, required for Azure OpenAI judges)
    - `mcicaps_aoaideploymentname` (deployment name like gpt-4o, required for Azure OpenAI judges)
    - `mcicaps_directlineurl` (Direct Line token endpoint, required for Copilot Studio judges)
    - `mcicaps_modelname` (optional, for Copilot Studio topic routing)
  - **Purpose**: Configurable judge backend supporting both Azure OpenAI and Copilot Studio
  - **Reusability**: Single judge agent can be used by multiple evaluator types
  - **Model Routing** (Copilot Studio): If `mcicaps_modelname` is specified, system prefixes prompts with `MODEL: {modelname}\n\n` for topic-based routing

- **mcicaps_evaluatortypes**: Evaluation type definitions (NEW)
  - Primary Key: `mcicaps_evaluatortypeid` (GUID)
  - Key Fields: 
    - `mcicaps_name` (e.g., "Similarity", "Accuracy", "Relevancy")
    - `mcicaps_description`
    - `mcicaps_evaluatorprompt` (LLM Judge prompt text)
    - `mcicaps_judgeagentid` (lookup to Judge Agent - replaces mcicaps_deploymentname)
    - `mcicaps_requiresexpectedresponse` (Boolean)
  - **Judge Selection**: References Judge Agent via lookup field. Factory pattern selects appropriate judge service (Azure OpenAI or Copilot Studio) based on Judge Agent configuration. If NULL, falls back to system default Azure OpenAI from AppSettings.
  - Purpose: Stores evaluation criteria and prompts in Dataverse (replaces Azure Blob Storage approach)
  - Note: Fields not in use: `mcicaps_systemprompttemplate`, `mcicaps_isdomainspecific`

#### 3.2.2 Output Tables
- **mcicaps_evaluationresults**: Individual test results
  - Primary Key: `mcicaps_evaluationresultsid` (auto-generated GUID)
  - Key Fields: `mcicaps_actualresponse`, `mcicaps_score`, `mcicaps_passed`, `mcicaps_evaluation` (lookup to evaluation run), `mcicaps_reasoning`, `mcicaps_testcase`, `mcicaps_testcaseturnid` (for multi-turn results), `mcicaps_conversationid`, `mcicaps_executiontime`, `mcicaps_isaggregate`, `mcicaps_evaluatortypeid`, `mcicaps_errormessage`, `statuscode`
  - Multi-Turn Support: Individual turn results linked via `mcicaps_testcaseturnid`, aggregate results marked with `mcicaps_isaggregate = true`
  - **Status Code Lifecycle Management** (as of Story 08):
    - `1` (Pending): Placeholder result created, execution not started (multi-turn orchestrations only)
    - `778760001` (Running): Reserved for future use (execution in progress tracking)
    - `778760002` (Completed): Execution completed successfully with score
    - `778760003` (Errored): Execution completed with error
  - `778760004` (Not Applicable): Skipped because evaluator requires expected response but none provided (score stored as 0.0 and excluded from aggregates)
  - **Orchestration Idempotency**: The auto-generated `mcicaps_evaluationresultsid` serves as the orchestration tracker for multi-turn tests. Placeholder aggregate results are created with `statuscode = 1` before orchestration starts, then updated to `778760002` or `778760003` upon completion. This prevents duplicate orchestrations and enables Monitor function to count only truly completed results.

#### 3.2.3 Message Queue Schema

**Single-Turn Service Bus Message** (`test-case-processing` queue):
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "directLineUrl": "string",
  "userInput": "string", 
  "expectedResponse": "string",
  "evaluatorTypeIds": ["guid1", "guid2", "guid3"], // Multiple evaluators per message (N:N relationship)
  "conversationId": "string (optional)",
  "passingThreshold": 0.7,
  "retryCount": 0,
  "maxRetries": 3
}
```

**Multi-Turn Durable Functions Input**:
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "directLineUrl": "string",
  "evaluatorTypeIds": ["guid1", "guid2", "guid3"], // Multiple evaluators per orchestration
  "aggregateResultIds": {  // Pre-created aggregate result IDs mapped to evaluators
    "guid1": "aggregateGuid1",
    "guid2": "aggregateGuid2",
    "guid3": "aggregateGuid3"
  },
  "passingThreshold": 0.7
}
```

**Note**: Multi-turn test cases do NOT use Service Bus queuing. They are executed via Durable Functions orchestration, which receives the parameters above directly from the Orchestrator Function. One orchestration handles all evaluators for a test case via concurrent evaluation.

### 3.3 API Integrations

#### 3.3.1 Azure Service Bus Integration
- Message queuing for single-turn test case distribution only
- **Single active queue**: `test-case-processing` (single-turn)
- Dead letter queue handling for failed messages
- Batch processing capabilities
- Automatic scaling based on queue depth
- Managed Identity authentication (no connection strings)

**Note**: The `multi-turn-test-case-processing` queue exists in infrastructure but is unused. Multi-turn execution uses Durable Functions orchestration instead.

#### 3.3.2 Copilot Studio Direct Line API
- Establish conversation sessions
- Send prompts and receive responses
- Handle conversation state management
- Implement retry logic for API failures

#### 3.3.3 LLM Judge Integration (Configurable Backend)

**Architecture**: Factory pattern with dynamic judge selection based on Dataverse configuration

**Supported Judge Types**:
1. **Azure OpenAI Judge** (JudgeType = 778760000)
   - Evaluation prompts loaded from Dataverse EvaluatorType records
   - Judge-specific endpoint and deployment name from Judge Agent record
   - On-demand ChatClient creation using judge configuration
   - Structured prompt templates for consistent evaluation
   - Score normalization (0-1 scale) with type-specific criteria
   - Reasoning capture for transparency and debugging
   - Rate limiting and error handling with exponential backoff
   - **Managed Identity authentication** (Cognitive Services OpenAI User role)
   - Supports multiple Azure OpenAI instances via different Judge Agents

2. **Copilot Studio Judge** (JudgeType = 778760001)
   - Integration via Direct Line API
   - Optional MODEL prefix injection for multi-model agent routing
   - JSON response parsing with markdown cleanup
   - Regex fallback for non-JSON responses
   - Stateless execution (new conversation per evaluation)
   - Domain-specific evaluation capabilities
   - Token-based authentication via Direct Line endpoint

**Factory Pattern**:
- `LLMJudgeServiceFactory` fetches Judge Agent via EvaluatorType lookup
- Validates configuration based on judge type
- Creates appropriate service instance (Azure OpenAI or Copilot Studio)
- Falls back to system default Azure OpenAI when Judge Agent is NULL
- Supports health checks with null evaluatorTypeId parameter

**Benefits**:
- **Cost Optimization**: Choose between Azure OpenAI token pricing or Power Platform licensing
- **Flexibility**: Use specialized Copilot Studio agents for domain evaluations
- **Centralized Configuration**: Judge agents reusable across evaluator types
- **No Code Changes**: Reconfigure judges in Dataverse without redeployment

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
- **Dataverse**: ClientSecret connection string with ClientId + ClientSecret (Managed Identity not supported by Dataverse SDK)
- **Azure OpenAI**: Managed identity with Cognitive Services OpenAI User role
- **Service Bus**: Managed identity with Service Bus Data Owner role
- **Azure Blob Storage**: Managed identity with Storage Blob Data Contributor role (if used)
- **Key Vault**: Managed identity with Key Vault Secrets User role (for Dataverse ClientSecret credentials only)
- **Application Insights**: Managed identity with Monitoring Metrics Publisher role

### 4.3 Configuration Management
```json
{
  "DataverseConnectionString": "@Microsoft.KeyVault(VaultName=kv-name;SecretName=DataverseConnectionString)",
  "DataverseSchemaPrefix": "mcicaps",
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

**Note**: All Azure service connections use managed identity. Only Dataverse requires ClientSecret connection string stored in Key Vault as it does not support Managed Identity authentication.

**Dataverse Schema Prefix**: The `DataverseSchemaPrefix` setting (default: `mcicaps`) allows customization of table and field prefixes. The system automatically appends an underscore if not present. For example, setting `DataverseSchemaPrefix=custom` will use `custom_` for all table and field names in Dataverse operations.

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
- **Purpose**: Read Dataverse ClientSecret connection string (only external service credential)

#### 4.4.5 Dataverse
- **Setup**: ClientSecret Application User with ClientId and ClientSecret
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
  - `mcicaps_evaluatorprompt`: Evaluator prompt template text
- **Versioning**: Managed through Dataverse solution lifecycle
- **Updates**: Prompts can be updated through Dataverse without function redeployment
- **Fallback**: Function includes default embedded prompts if Dataverse template is unavailable
- **Note**: Fields not in use: `mcicaps_systemprompttemplate`, `mcicaps_isdomainspecific`

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

### 5.4 Multi-Evaluator Efficiency Architecture (Story 09 - Implemented)

**Status**: ✅ IMPLEMENTED (2025-11-14)

#### 5.4.1 Architecture Overview

The system implements N:N relationships between evaluation runs and evaluator types with two critical optimizations:
1. **Single Copilot Studio API call** per test case/turn (not N calls for N evaluators)
2. **Concurrent evaluation** using `Task.WhenAll` for all evaluators in parallel

**Before (Inefficient)**:
```
For 1 test case with 3 evaluators:
- Create 3 separate Service Bus messages
- Each message calls Copilot Studio independently
- Each message evaluates sequentially
Result: 3 Copilot calls + 6 seconds evaluation time (2s × 3)
```

**After (Optimized)**:
```
For 1 test case with 3 evaluators:
- Create 1 Service Bus message with all evaluator IDs
- Message calls Copilot Studio once
- Message evaluates concurrently with all evaluators (Task.WhenAll)
Result: 1 Copilot call + 2 seconds evaluation time (parallel)
```

#### 5.4.2 Cost Savings

**Copilot Studio API Calls Reduction**:
| Evaluators | Calls Before | Calls After | Savings |
|------------|--------------|-------------|---------|
| 3          | 3            | 1           | 67%     |
| 5          | 5            | 1           | 80%     |
| 10         | 10           | 1           | 90%     |

**Formula**: Savings = ((N-1)/N × 100)%

#### 5.4.3 Performance Improvements

**Evaluation Time Reduction** (concurrent vs sequential):
| Evaluators | Time Before (sequential) | Time After (concurrent) | Speedup |
|------------|-------------------------|------------------------|---------|
| 3 @ 2s each| 6 seconds              | 2 seconds              | 3x      |
| 5 @ 2s each| 10 seconds             | 2 seconds              | 5x      |
| 10 @ 2s each| 20 seconds            | 2 seconds              | 10x     |

#### 5.4.4 Implementation Details

**Single-Turn Worker Function**:
```csharp
// 1. Call Copilot Studio ONCE
var actualResponse = await _copilotService.SendMessageAsync(prompt, conversationId);

// 2. Evaluate concurrently with all evaluators
var evaluationTasks = evaluatorTypeIds.Select(async evaluatorTypeId =>
{
    var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId);
    var judgeService = await _judgeFactory.CreateJudgeAsync(evaluatorTypeId);
    var result = await judgeService.EvaluateResponseAsync(actualResponse, expectedResponse, ...);
    await _dataverseService.CreateEvaluationResultAsync(...);
    await _dataverseService.IncrementCompletedTestCasesAsync(...);
}).ToList();

// 3. Wait for all evaluations to complete
await Task.WhenAll(evaluationTasks);
```

**Multi-Turn Durable Activity**:
```csharp
// ExecuteSingleTurn activity
// 1. Call Copilot Studio ONCE per turn
var actualResponse = await _copilotService.SendMessageAsync(turn.PromptText, conversationId);

// 2. Evaluate concurrently with all evaluators
var turnResults = await Task.WhenAll(
    evaluatorTypeIds.Select(async evaluatorTypeId =>
    {
        // Same concurrent pattern as single-turn
        var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId);
        var judgeService = await _judgeFactory.CreateJudgeAsync(evaluatorTypeId);
        var result = await judgeService.EvaluateResponseAsync(...);
        await _dataverseService.CreateEvaluationResultAsync(...);
        return new TurnExecutionResult { ... };
    })
);

return turnResults.ToList(); // List<TurnExecutionResult>
```

#### 5.4.5 Progress Tracking Formula

**Total Evaluation Units Calculation**:
```
totalUnits = Σ(single-turn tests: testCount × evaluatorCount)
           + Σ(multi-turn tests: (turnCount × evaluatorCount) + evaluatorCount)
```

**Example**:
- 100 single-turn tests × 3 evaluators = 300 units
- 10 multi-turn tests (5 turns each) × 3 evaluators = 10 × [(5 × 3) + 3] = 180 units
- **Total**: 480 evaluation units

#### 5.4.6 Not Applicable (NA) Handling

**Purpose**: Skip evaluation when evaluator requires expected response but none provided.

**Configuration**: `mcicaps_requiresexpectedresponse` field on EvaluatorType
- **Accuracy**: `true` (requires ground truth)
- **Similarity**: `false` (can compare to conversation history)
- **Relevancy**: `false` (judges contextual fit)

**Short-Circuit Logic**:
```csharp
if (evaluator.RequiresExpectedResponse && string.IsNullOrWhiteSpace(expectedResponse))
{
    // Create NA result immediately (statuscode 778760004, score 0.0)
    // No Copilot Studio or LLM Judge API calls
    // Counts as "completed" for progress tracking
    // Excluded from aggregate score calculations
}
```

**Benefits**:
- Saves LLM tokens (no judge API calls for NA)
- Faster completion (no waiting for unnecessary evaluations)
- Clear distinction from failures (statuscode 778760004 vs 778760003)

#### 5.4.7 Aggregate Calculation with NA Filtering

**Multi-Turn Aggregate Logic**:
1. Collect all turn results for an evaluator
2. Filter out NA results (statuscode 778760004)
3. Calculate average of remaining scored turns
4. If all turns are NA → mark aggregate as NA

**Example**:
```
Turn 1: Score 0.8 (Completed)
Turn 2: Score 0.0, StatusCode 778760004 (NA) - EXCLUDED
Turn 3: Score 0.9 (Completed)
Turn 4: Score 0.0, StatusCode 778760004 (NA) - EXCLUDED  
Turn 5: Score 1.0 (Completed)

Aggregate Score: (0.8 + 0.9 + 1.0) / 3 = 0.9
(NA turns excluded from average)
```

#### 5.4.8 Data Model Changes

**Message Schema Update**:
```json
{
  "evaluatorTypeIds": ["guid1", "guid2", "guid3"]  // Was: single evaluatorTypeId
}
```

**Orchestrator Input Update**:
```json
{
  "evaluatorTypeIds": ["guid1", "guid2", "guid3"],
  "aggregateResultIds": {
    "guid1": "aggGuid1",
    "guid2": "aggGuid2",
    "guid3": "aggGuid3"
  }
}
```

**Result Schema Addition**:
```json
{
  "evaluatorTypeId": "guid",  // NEW: Links result to specific evaluator
  "statusCode": 778760004     // NEW: 778760004 = Not Applicable
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
- **Azure Functions Premium Plan**: For extended execution time, VNET integration, and Durable Functions support
- **Azure Service Bus Standard**: For high-throughput single-turn message processing (1 active queue)
- **Azure Storage**: For function app state, configuration, and Durable Functions orchestration state
- **Application Insights**: For monitoring and telemetry
- **Key Vault**: For Dataverse ClientSecret credentials only
- **Managed Identity**: System-assigned identity for all Azure service authentication
- **RBAC Assignments**: Properly scoped role assignments for least privilege access
- **Azure OpenAI**: GPT-4o deployment with Managed Identity access

**Note**: Infrastructure provisions a `multi-turn-test-case-processing` queue, but it is not actively used. Multi-turn execution uses Durable Functions orchestration.

### 11.2 Scaling Configuration
- **Function App**: Auto-scale up to 1000 instances (single-turn workers) plus unlimited Durable Functions orchestrations
- **Service Bus**: Standard tier with auto-scaling for single-turn queue only
- **Connection Pools**: Optimized for high-concurrency scenarios
- **Resource Limits**: Configured for burst processing capabilities

### 11.2 CI/CD Pipeline
- Automated testing and validation
- Infrastructure as Code (Bicep/ARM)
- Environment-specific configuration

## 12. Success Criteria (Multi-Evaluator Updated)

### 12.1 Functional Success
- ✅ Successfully execute evaluation runs end-to-end across all selected evaluator types
- ✅ Support both single-turn and multi-turn conversation evaluations
- ✅ Apply appropriate evaluation criteria based on evaluator types selected at run level (N:N join)
- ✅ Store complete evaluation results in Dataverse with evaluator type lookup and type-specific scoring
- ✅ Generate consistent scoring using LLM Judge with specialized prompts from Dataverse
- ✅ **Support per-evaluator Azure OpenAI deployment name configuration for cost/performance optimization**
- ✅ **Fall back to system default deployment when evaluator doesn't specify custom deployment**
- ✅ Handle errors gracefully without data loss across all test types
- ✅ Correctly update evaluation run status; Not Applicable results counted toward completion without affecting pass/fail statistics
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

**Document Version**: 2.2  
**Last Updated**: November 13, 2025  
**Status**: Current - Reflects Implemented System with Dynamic Deployment Names