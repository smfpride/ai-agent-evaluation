# AI Agent Evaluation System - Architecture Documentation

## Table of Contents
- [Overview](#overview)
- [Architecture Diagram](#architecture-diagram)
- [System Components](#system-components)
- [Data Flow](#data-flow)
- [Technology Stack](#technology-stack)
- [Security Architecture](#security-architecture)
- [Scalability & Performance](#scalability--performance)
- [Monitoring & Observability](#monitoring--observability)

---

## Overview

The AI Agent Evaluation System is a cloud-native, serverless solution built on Azure that automates the testing and evaluation of Copilot Studio agents. It uses an LLM-as-a-Judge approach with Azure OpenAI to assess agent responses against expected outcomes across three evaluation types: Similarity, Accuracy, and Relevancy.

### Key Capabilities
- **Automated Testing**: Execute test cases against Copilot Studio agents via Direct Line API
- **LLM-Based Evaluation**: Use Azure OpenAI (GPT-4o) to score responses with human-like judgment
- **Multi-Turn Conversations**: Support for complex conversation scenarios with context preservation via Durable Functions
- **Scalable Processing**: Handle up to 50,000 test cases per evaluation run
- **Comprehensive Results**: Store detailed results, reasoning, and metrics in Dataverse
- **Flexible Evaluation Types**: Extensible evaluator type system stored in Dataverse
- **Hybrid Execution**: Service Bus workers for single-turn, Durable Functions orchestration for multi-turn

---

## Architecture Diagram
<!-- ![](./images/ArchitectureDiagram.png) -->
```mermaid
flowchart TB
    subgraph subGraph0["External Systems"]
        CS["Copilot Studio Agent<br>Direct Line API"]
        DV[("Microsoft Dataverse<br>Test Data &amp; Results")]
        USER["Power Automate"]
    end
    subgraph subGraph1["HTTP Triggered"]
        ORCH["Orchestrator Function<br>StartEvaluationRun"]
        HEALTH["Health Check Function"]
    end
    subgraph subGraph2["Service Bus Triggered"]
        WORKER["Worker Function<br>ProcessTestCase<br>Single-Turn Only"]
    end
    subgraph subGraph3["Durable Functions (Multi-Turn)"]
        DORCH["Durable Orchestrator<br>MultiTurnTestCaseOrchestrator<br>Unlimited Runtime"]
        ACT1["Activity:<br>GetTestCaseWithTurns"]
        ACT2["Activity:<br>ExecuteSingleTurn"]
        ACT3["Activity:<br>CreateAggregateResult"]
    end
    subgraph subGraph4["Timer Triggered"]
        MONITOR["Monitor Function<br>TrackProgress<br>Update Status"]
    end
    subgraph subGraph5["Azure Function App"]
        subGraph1
        subGraph2
        subGraph3
        subGraph4
    end
    subgraph subGraph6["Azure Services"]
        SBUS["Azure Service Bus<br>1 Queue:<br>- test-case-processing<br>(single-turn only)"]
        AOAI["Azure OpenAI<br>GPT-4o Model<br>LLM Judge"]
        KV["Azure Key Vault<br>Secrets Management"]
        AI["Application Insights<br>Logging &amp; Metrics"]
        STORAGE["Azure Storage<br>Function State +<br>Durable State"]
    end
    subgraph subGraph7["Data Models"]
        TR["Test Run<br>mcicaps_evaluationruns"]
        TC["Test Case<br>mcicaps_testcases"]
        TURN["Test Case Turn<br>mcicaps_testcaseturn"]
        RES["Evaluation Result<br>mcicaps_evaluationresults"]
        EVAL["Evaluator Type<br>mcicaps_evaluatortypes"]
        AGENT["Agent To Eval<br>mcicaps_agenttoevals"]
    end
    USER -- 1: POST /api/orchestrator --> ORCH
    ORCH -- 2: Fetch Test Cases --> DV
    DV -- 3: Test Cases + Turns --> ORCH
    ORCH -- 4a: Queue Single-Turn --> SBUS
    ORCH -- 4b: Start Durable Orch<br>(Multi-Turn) --> DORCH
    SBUS -- 5a: Single-Turn Messages --> WORKER
    DORCH -- 5b: Call Activity --> ACT1
    ACT1 -- Fetch Data --> DV
    DORCH -- 5c: Call Activity --> ACT2
    ACT2 -- Execute Turn --> CS
    ACT2 -- Evaluate --> AOAI
    DORCH -- 5d: Call Activity --> ACT3
    ACT3 -- Save Aggregate --> DV
    WORKER -- 6: Execute Test --> CS
    CS -- 7a: Agent Response --> WORKER
    CS -- 7b: Turn Responses --> ACT2
    WORKER -- 8: Evaluate Response --> AOAI
    AOAI -- 9a: Score & Reasoning --> WORKER
    AOAI -- 9b: Turn Scores --> ACT2
    WORKER -- 10: Save Result --> DV
    MONITOR -- 11: Check Progress --> DV
    MONITOR -- 12: Update Run Status --> DV
    DORCH -.-> STORAGE
    STORAGE -.-> DORCH
  TR -- 1:N --> TC
  TR -- N:1 --> AGENT
  TR -- N:N --> EVAL
  RES -- N:1 --> EVAL
  TC -- 1:N --> TURN & RES
  TURN -- 1:N --> RES
    CS:::external
    DV:::external
    ORCH:::function
    HEALTH:::function
    WORKER:::function
    DORCH:::durable
    ACT1:::durable
    ACT2:::durable
    ACT3:::durable
    MONITOR:::function
    SBUS:::azureService
    AOAI:::azureService
    KV:::azureService
    AI:::azureService
    STORAGE:::azureService
    TR:::data
    TC:::data
    TURN:::data
    RES:::data
    EVAL:::data
    AGENT:::data
    classDef azureService fill:#0078D4,stroke:#004578,stroke-width:2px,color:#fff
    classDef function fill:#7FBA00,stroke:#5A8700,stroke-width:2px,color:#000
    classDef durable fill:#9B59B6,stroke:#6C3483,stroke-width:2px,color:#fff
    classDef external fill:#F25022,stroke:#A73513,stroke-width:2px,color:#fff
    classDef data fill:#FFB900,stroke:#B38500,stroke-width:2px,color:#000
```

---

## System Components

### 1. Azure Functions (Compute Layer)

#### 1.1 Orchestrator Function
**Trigger**: HTTP POST  
**Runtime**: .NET 8 Isolated  
**Purpose**: Initiates evaluation runs and coordinates test execution

**Responsibilities**:
- Receive evaluation run requests with run ID
- Fetch test cases from Dataverse (including multi-turn tests)
- Update evaluation run status to "Running"
- Queue test case messages to Service Bus
- Return HTTP 202 Accepted with tracking information

**Key Features**:
- Handles both single-turn and multi-turn test cases
- Supports up to 50,000 test cases per run
- Fast response time (< 30 seconds for large runs)
- Idempotent operation support

#### 1.2 Worker Function (Single-Turn)
**Trigger**: Service Bus Queue (`test-case-processing`)  
**Runtime**: .NET 8 Isolated  
**Purpose**: Execute individual single-turn test cases

**Responsibilities**:
- Receive test case message from queue
- Send prompt to Copilot Studio via Direct Line API
- Collect agent response
- Use `LLMJudgeServiceFactory` to get appropriate judge (Azure OpenAI or Copilot Studio)
- Evaluate response using selected judge
- Save evaluation result to Dataverse

**Key Features**:
- Stateless execution for horizontal scaling
- Retry logic for transient failures
- Type-specific evaluation (Similarity, Accuracy, Relevancy)
- Dynamic judge selection via factory pattern
- Response time tracking

#### 1.3 Monitor Function
**Trigger**: Timer (every 1 minute)  
**Runtime**: .NET 8 Isolated  
**Purpose**: Track evaluation run progress and update completion status

**Responsibilities**:
- Query active evaluation runs (status = "Running")
- Check completion based on test case count vs results count
- Calculate overall metrics (average score, pass/fail rate)
- Update run status to "Completed" or "Errored"
- Detect stalls (no progress for 30+ minutes or runtime > 120 minutes)
- Handle timeout scenarios

**Key Features**:
- Automatic progress tracking
- No user intervention required
- Handles both single-turn and multi-turn result aggregation

#### 1.4 Durable Functions Orchestrator (Multi-Turn Execution)
**Trigger**: Started by Orchestrator Function  
**Runtime**: .NET 8 Isolated (Durable Functions)  
**Purpose**: Long-running orchestration for multi-turn test cases

**Responsibilities**:
- Execute multi-turn conversations with unlimited execution time
- Call activities sequentially for turn execution
- Maintain conversation state and context
- Create aggregate results after all turns complete
- Support pause/resume and retry logic

**Key Features**:
- No 5-minute timeout limitation
- Supports very long conversations (10+ turns)
- Fault-tolerant with automatic state management
- Primary multi-turn execution path

**Activities**:
- `GetTestCaseWithTurns`: Fetch test case and turns from Dataverse
- `ExecuteSingleTurn`: Execute one turn with conversation context
- `CreateAggregateResult`: Generate final multi-turn evaluation result

**Flow**:
1. Orchestrator Function calls `durableClient.ScheduleNewOrchestrationInstanceAsync` for multi-turn cases
2. Durable Orchestrator retrieves test case with all turns
3. For each turn sequentially:
   - Execute turn with conversation context
   - Update conversation history
4. After all turns complete, create aggregate result in Dataverse

**Implementation Note**: This is the PRIMARY execution path for all multi-turn test cases. The orchestrator does NOT use Service Bus queuing for multi-turn execution.

#### 1.5 Health Check Function
**Trigger**: HTTP GET  
**Runtime**: .NET 8 Isolated  
**Purpose**: Provide system health status

**Responsibilities**:
- Check connectivity to all dependencies
- Verify Managed Identity authentication
- Return detailed health report

---

### 2. Azure Service Bus (Message Queue)

**Purpose**: Decouple orchestration from execution and enable massive scaling for single-turn test cases

**Note**: Multi-turn test cases use Durable Functions orchestration instead of Service Bus queuing.

#### Queue Configuration

##### 2.1 test-case-processing (Single-Turn Queue)
- **Max Size**: 5 GB
- **Message TTL**: 14 days
- **Dead Letter**: Enabled
- **Max Delivery Count**: 10
- **Lock Duration**: 5 minutes
- **Session Support**: No
- **Status**: Active - used by Worker Function

##### 2.2 multi-turn-test-case-processing (Legacy Queue)
- **Max Size**: 5 GB
- **Message TTL**: 14 days
- **Dead Letter**: Enabled
- **Max Delivery Count**: 10
- **Lock Duration**: 5 minutes
- **Session Support**: No
- **Status**: UNUSED - Created by infrastructure but no messages are sent to this queue. Multi-turn execution uses Durable Functions orchestration instead.

#### Message Schema

**Single-Turn Message**:
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "testCaseName": "string",
  "prompt": "string",
  "expectedResponse": "string",
  "testType": 778760000,
  "retryCount": 0,
  "enqueuedAt": "2025-11-10T10:00:00Z"
}
```

**Multi-Turn Message**:
```json
{
  "evaluationRunId": "guid",
  "testCaseId": "guid",
  "testCaseName": "string",
  "isMultiTurn": true,
  "testType": 778760000,
  "retryCount": 0,
  "enqueuedAt": "2025-11-10T10:00:00Z"
}
```

---

### 3. LLM Judge (Configurable Backend)

**Purpose**: Evaluate agent responses using configurable judge backends (Azure OpenAI or Copilot Studio)

#### Architecture Pattern: Factory Pattern

The system uses `LLMJudgeServiceFactory` to dynamically select the appropriate judge implementation based on Dataverse configuration:

```
EvaluatorType → JudgeAgent → Factory → AzureOpenAIJudgeService | CopilotStudioJudgeService
```

**Factory Behavior**:
1. Fetches `EvaluatorType` from Dataverse using evaluator type ID
2. Retrieves linked `JudgeAgent` via `mcicaps_judgeagentid` lookup
3. Validates configuration based on judge type
4. Creates appropriate service instance:
   - `AzureOpenAIJudgeService` for JudgeType = 778760000
   - `CopilotStudioJudgeService` for JudgeType = 778760001
5. Falls back to default Azure OpenAI if no Judge Agent configured

#### Azure OpenAI Judge

**Configuration**:
- **Models**: Supports multiple deployments (gpt-4o, gpt-4-turbo, gpt-35-turbo, etc.)
- **Default Deployment**: gpt-4o (2024-08-06) - Standard (150 TPM capacity)
- **Authentication**: Managed Identity (Cognitive Services OpenAI User role)
- **Dynamic Selection**: Per-judge-agent endpoint and deployment configuration

**Features**:
- On-demand `ChatClient` creation with judge-specific endpoint
- Supports multiple Azure OpenAI instances
- JSON response parsing with structured evaluation format
- Retry logic with exponential backoff

#### Copilot Studio Judge

**Configuration**:
- **Integration**: Direct Line API
- **Authentication**: Token-based via Direct Line endpoint
- **Optional Model Routing**: Prefix injection for multi-model agents

**Features**:
- Optional MODEL prefix for topic routing: `MODEL: gpt-4o\n\n{prompt}`
- JSON response parsing with markdown code fence removal
- Regex fallback for non-JSON responses
- Stateless execution (new conversation per evaluation)
- Supports domain-specific judge agents

**Model Routing** (Optional):
If `mcicaps_modelname` is specified in Judge Agent:
- System prefixes all prompts with `MODEL: {modelname}\n\n`
- Copilot Studio agent routes to appropriate topic/model
- Enables single agent to support multiple models
- If NULL, no prefix added (agent uses default topic)

**Benefits**:
- Cost optimization (Azure OpenAI vs Power Platform licensing)
- Domain-specific evaluations (specialized Copilot Studio agents)
- Flexible model selection per evaluator type
- Centralized judge configuration in Dataverse

#### Evaluation Types

Evaluation types are now stored in Dataverse `mcicaps_evaluatortypes` table with:
- **Name**: Display name (e.g., "Similarity", "Accuracy", "Relevancy")
- **Prompt Template**: User prompt with placeholders ({UserInput}, {ExpectedResponse}, {ActualResponse}, {ConversationId})
- **System Prompt Template**: Optional system-level guidance
- **Deployment Name**: Azure OpenAI deployment name (optional - uses system default if not specified)
- **GUID Reference**: Used in test cases and messages to identify evaluator type

##### Built-In Evaluator Types

**Similarity**
**Criteria**: Semantic meaning alignment between expected and actual responses
- Evaluates if responses convey the same meaning
- Allows different phrasing
- Focuses on information equivalence

**Accuracy**
**Criteria**: Factual correctness of information
- Validates data accuracy (numbers, dates, facts)
- Checks for misleading statements
- Ensures completeness of information

**Relevancy**
**Criteria**: Response appropriateness to the prompt
- Measures how well response addresses the question
- Detects off-topic content
- Evaluates scope and focus

##### Custom Evaluator Types

The system supports custom domain-specific evaluators (e.g., fashion, healthcare, finance) by creating new EvaluatorType records in Dataverse with specialized prompts.

#### Judge Output Format
```json
{
  "score": 0.85,
  "reasoning": "The agent response correctly addresses..."
}
```

**Note**: The `passed` and `evaluation` fields are calculated/generated by the worker function based on the score and passing threshold.

---

### 4. Microsoft Dataverse (Data Layer)

**Purpose**: Store test definitions, evaluation results, and run metadata

#### Data Schema

##### 4.1 mcicaps_evaluationruns (Test Run)
**Purpose**: Represents an evaluation run
```
Primary Key: mcicaps_evaluationrunsid (GUID)
Fields:
  - mcicaps_name (Text): Run name
  - mcicaps_testset (Lookup): Associated test set
  - mcicaps_agenttoevalid (Lookup): Associated agent configuration
  - mcicaps_passingthreshold (Decimal): Pass threshold (0-1)
  - mcicaps_overallpass (Boolean): Overall pass status
  - mcicaps_overallscore (Decimal): Average score
  - mcicaps_progresspercentage (Decimal): Real-time progress (0-100)
  - mcicaps_starttime (DateTime): Run start timestamp
  - mcicaps_endtime (DateTime): Run end timestamp
  - mcicaps_lastprogressupdate (DateTime): Last progress update time
  - mcicaps_totaltestcases (Integer): Total test cases
  - mcicaps_completedtestcases (Integer): Completed test cases
  - mcicaps_failedtestcases (Integer): Failed test cases
  - statuscode (OptionSet): 
      1 = Created
      778760001 = Execute/Run
      778760002 = Running
      778760003 = Errored
      778760004 = Completed
```

##### 4.2 mcicaps_agenttoevals (Agent Configuration)
**Purpose**: Centralized agent configuration
```
Primary Key: mcicaps_agenttoevalid (GUID)
Fields:
  - mcicaps_name (Text): Agent name
  - mcicaps_directlinespeechurl (Text): Direct Line endpoint URL
  - mcicaps_description (Text): Agent description
```

##### 4.3 mcicaps_testcases (Test Case)
**Purpose**: Test case definitions
```
Primary Key: mcicaps_testcasesid (GUID)
Fields:
  - mcicaps_name (Text): Test case name
  - mcicaps_prompt (Text): User prompt (UserInput)
  - mcicaps_expectedresponse (Text): Expected agent response
  - mcicaps_testset (Lookup): Parent test set
  - (Removed) mcicaps_evaluatortypeid (Lookup): Evaluator Type (moved to run-level N:N association)
  - mcicaps_ismultiturn (Boolean): Multi-turn flag
  - mcicaps_conversationid (Text): Conversation identifier
```

##### 4.4 mcicaps_judgeagents (Judge Agent Configuration) - NEW
**Purpose**: Configurable LLM Judge backends (Azure OpenAI or Copilot Studio)
```
Primary Key: mcicaps_judgeagentid (GUID)
Fields:
  - mcicaps_name (Text): Judge agent name (e.g., "GPT-4o Judge", "Finance Domain Judge")
  - mcicaps_description (Text): Purpose and configuration notes
  - mcicaps_judgetype (Choice/OptionSet): Judge backend type
    - 778760000: Azure OpenAI
    - 778760001: Copilot Studio
  
  Azure OpenAI Fields (required when mcicaps_judgetype = 778760000):
  - mcicaps_aoaiendpoint (Text): Azure OpenAI endpoint URL
  - mcicaps_aoaideploymentname (Text): Deployment name (e.g., gpt-4o, gpt-4-turbo)
  
  Copilot Studio Fields (required when mcicaps_judgetype = 778760001):
  - mcicaps_directlineurl (Text): Direct Line token endpoint URL
  - mcicaps_modelname (Text, Optional): Model name for topic routing (e.g., "gpt-4o", "gpt-4-turbo")
      * If specified, system prefixes prompts with "MODEL: {modelname}\n\n"
      * If NULL/empty, no prefix added (agent uses default topic)
      * Enables multi-model Copilot Studio agents with topic-based routing

Notes:
  - Reusable across multiple evaluator types
  - Enables centralized judge configuration
  - Supports cost optimization and domain-specific evaluations
```

##### 4.5 mcicaps_evaluatortypes (Evaluator Type Configuration)
**Purpose**: Evaluation type definitions and prompt templates
```
Primary Key: mcicaps_evaluatortypeid (GUID)
Fields:
  - mcicaps_name (Text): Evaluator name (e.g., "Similarity", "Accuracy", "Relevancy")
  - mcicaps_description (Text): Purpose and criteria description
  - mcicaps_evaluatorprompt (Text): LLM Judge prompt template with placeholders
  - mcicaps_judgeagentid (Lookup): Reference to Judge Agent configuration (replaces mcicaps_deploymentname)
  - mcicaps_requiresexpectedresponse (Boolean): If true and expected response missing, evaluation result marked Not Applicable

Migration Notes:
  - mcicaps_deploymentname field removed (replaced by Judge Agent lookup)
  - If mcicaps_judgeagentid is NULL, factory falls back to system default Azure OpenAI from AppSettings
  - Fields not in use: mcicaps_systemprompttemplate, mcicaps_isdomainspecific
```

##### 4.6 mcicaps_evaluationruns_mcicaps_evaluato (Run ↔ EvaluatorType Join) - NEW
**Purpose**: N:N relationship table defining evaluator types selected for a specific evaluation run (multi-evaluator support)
```
Relationship Name: EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType
Relationship Table: mcicaps_evaluationruns_mcicaps_evaluato (auto-generated by Dataverse with schema prefix, truncated at 50 chars)
Primary Key: mcicaps_evaluationruns_mcicaps_evaluatoid (GUID)
Fields:
  - mcicaps_evaluationrunid (Lookup): Evaluation run
  - mcicaps_evaluatortypeid (Lookup): Evaluator type
  - createdon (DateTime)
Notes:
  - Order is arbitrary (no weighting yet)
  - Orchestrator enumerates evaluator types from this join when scheduling
```

##### 4.7 mcicaps_testcaseturn (Test Case Turn)
**Purpose**: Individual turns within multi-turn test cases
```
Primary Key: mcicaps_testcaseturnid (GUID)
Fields:
  - mcicaps_name (Text): Auto-generated (e.g., "Turn-00001")
  - mcicaps_testcaseid (Lookup): Parent test case
  - mcicaps_sequence (Integer): Turn order (1-based)
  - mcicaps_prompttext (Text): User prompt for this turn
  - mcicaps_expectedresponse (Text): Expected response for this turn

Relationship: N:1 with mcicaps_testcases (cascade delete)
```

##### 4.8 mcicaps_evaluationresults (Evaluation Result)
**Purpose**: Individual test execution results
```
Primary Key: mcicaps_evaluationresultsid (GUID, auto-generated)
Fields:
  - mcicaps_testcase (Lookup): Test case reference
  - mcicaps_testcaseturnid (Lookup): Specific turn (multi-turn only)
  - mcicaps_evaluation (Lookup): Evaluation run reference
  - mcicaps_actualresponse (Text): Agent's actual response
  - mcicaps_score (Decimal): Evaluation score (0-1)
  - mcicaps_passed (Boolean): Pass/fail status
  - mcicaps_reasoning (Text): Detailed judge reasoning
  - mcicaps_conversationid (Text): Direct Line conversation ID
  - mcicaps_isaggregate (Boolean): Aggregate result flag
  - mcicaps_evaluatortypeid (Lookup): Evaluator type used for this result (required for all results including aggregates)
  - mcicaps_executiontime (Integer): Execution time in milliseconds
  - mcicaps_errormessage (Text): Error details if failed
  - statuscode (Choice/OptionSet): Result lifecycle status
    - 1: Pending (placeholder created, not started)
    - 778760001: Running (reserved for future use)
    - 778760002: Completed (successfully finished with score)
  - 778760003: Errored (finished with error)
  - 778760004: Not Applicable (evaluator requires expected response but none provided; evaluation skipped, score set to 0.0)
```

**Orchestration Idempotency Pattern** (Story 08):
- **Multi-Turn:** Before starting Durable Functions orchestration, create placeholder aggregate result with `statuscode = 1` (Pending)
- **Tracking:** The auto-generated `mcicaps_evaluationresultsid` serves as the orchestration tracker
- **Idempotency:** Query for existing aggregate result before orchestration start prevents duplicates
- **Completion:** Orchestration updates placeholder result to `statuscode = 778760002` or `778760003` with final scores
- **Monitor:** Only counts results with `statuscode = 778760002` as completed, preventing premature run completion

#### Data Relationships
```
mcicaps_evaluationruns (1) ←→ (N) mcicaps_evaluationresults
mcicaps_evaluationruns (N) ←→ (1) mcicaps_agenttoevals
mcicaps_testcases (1) ←→ (N) mcicaps_evaluationresults
mcicaps_evaluationruns (N) ←→ (N) mcicaps_evaluatortypes (via mcicaps_evaluationruns_mcicaps_evaluato)
mcicaps_evaluationresults (N) ←→ (1) mcicaps_evaluatortypes
mcicaps_testcases (1) ←→ (N) mcicaps_testcaseturn
mcicaps_testcaseturn (1) ←→ (N) mcicaps_evaluationresults
```

#### Multi-Evaluator Architecture (Story 09)

**Overview**: The system supports N:N relationships between evaluation runs and evaluator types, enabling a single test case to be evaluated with multiple evaluators (Similarity, Accuracy, Relevancy, etc.) in a single run.

**Key Design Principles**:
1. **Test Cases are Evaluator-Agnostic**: No evaluator type stored on test case - selection happens at run level
2. **Optimized API Calls**: Copilot Studio called once per test case, response evaluated by all evaluators
3. **Concurrent Evaluation**: All evaluators run in parallel using `Task.WhenAll` for maximum performance
4. **NA Handling**: Evaluators requiring expected responses skip execution when data missing

**Architecture Flow**:
```
1. Orchestrator fetches evaluator types from N:N join (GetEvaluatorTypesForRunAsync)
2. For each test case:
   - Create 1 message/orchestration with all evaluator IDs (not N separate messages)
   - Worker/Activity calls Copilot Studio once
   - Worker/Activity evaluates concurrently with all evaluators using Task.WhenAll
   - Each evaluator creates its own result with evaluatorTypeId
```

**Efficiency Gains**:
- **Copilot Studio API Calls**: Reduced by (N-1)/N
  - 3 evaluators: 67% fewer calls
  - 5 evaluators: 80% fewer calls  
  - 10 evaluators: 90% fewer calls
- **Evaluation Time**: Reduced by concurrent execution
  - 3 evaluators @ 2s each: 6s → 2s (67% faster)
  - 5 evaluators @ 2s each: 10s → 2s (80% faster)

**Progress Tracking Formula**:
```
totalUnits = Σ(single-turn: testCount × evaluatorCount)
           + Σ(multi-turn: (turnCount × evaluatorCount) + evaluatorCount for aggregates)
```

**Not Applicable (NA) Handling**:
- EvaluatorType field: `mcicaps_requiresexpectedresponse` (boolean)
- If true and expected response missing → create NA result immediately
- NA results: StatusCode 778760004, Score 0.0, no API calls made
- NA results excluded from aggregate calculations
- Counts as "completed" for progress tracking

---

### 5. Supporting Azure Services

#### 5.1 Azure Key Vault
**Purpose**: Secure storage for external service credentials

**Stored Secrets**:
- `DataverseConnectionString`: ClientSecret connection string for Dataverse (AuthType=ClientSecret;Url=...;ClientId=...;ClientSecret=...)

**Note**: All Azure service connections use Managed Identity (no secrets needed). Only Dataverse requires a connection string as it does not support Managed Identity authentication.

#### 5.2 Application Insights
**Purpose**: Centralized logging, monitoring, and diagnostics

**Collected Data**:
- Function execution traces
- Custom metrics (test case counts, scores, durations)
- Exception tracking
- Performance counters
- Dependency calls (Dataverse, OpenAI, Service Bus)

**Key Metrics**:
- `TestCase.Executed` (count)
- `TestCase.PassRate` (percentage)
- `EvaluationRun.Duration` (milliseconds)
- `ServiceBus.QueueDepth` (count)

#### 5.3 Azure Storage
**Purpose**: Function app state and configuration

**Contents**:
- Function app binaries
- Host configuration
- Durable Functions state (for multi-turn orchestrations)
- Shared state (if needed)

---

## Data Flow

### Single-Turn Test Case Execution (Multi-Evaluator with Concurrent Evaluation)
<!-- ![](./images/SingleTurnTestCaseExecution.png) -->
```mermaid
sequenceDiagram
    participant User
    participant Orchestrator
    participant Dataverse
    participant ServiceBus
    participant Worker
    participant CopilotStudio
    participant OpenAI
    participant Monitor

    User->>Orchestrator: POST /api/orchestrator<br/>{evaluationRunId}
    Orchestrator->>Dataverse: Fetch Evaluator Types (N:N join)
    Dataverse-->>Orchestrator: EvaluatorTypes [Similarity, Accuracy, Relevancy]
    Orchestrator->>Dataverse: Fetch Test Cases
    Dataverse-->>Orchestrator: Test Cases (single-turn)
    Orchestrator->>Dataverse: Update Status = Running
    
    loop For each test case
        Orchestrator->>ServiceBus: Queue 1 Message<br/>(with all evaluator IDs)
    end
    
    Orchestrator-->>User: 202 Accepted<br/>{tracking info}

    ServiceBus->>Worker: Dequeue Message
    Worker->>CopilotStudio: Send Prompt ONCE via Direct Line
    CopilotStudio-->>Worker: Agent Response + conversationId
    
    Note over Worker: Concurrent Evaluation with Task.WhenAll
    par Evaluate with Similarity
        Worker->>OpenAI: Evaluate (Similarity prompt)
        OpenAI-->>Worker: Score + Reasoning
        Worker->>Dataverse: Save Result 1 (evaluatorTypeId=Similarity)
    and Evaluate with Accuracy  
        Worker->>OpenAI: Evaluate (Accuracy prompt)
        OpenAI-->>Worker: Score + Reasoning
        Worker->>Dataverse: Save Result 2 (evaluatorTypeId=Accuracy)
    and Evaluate with Relevancy
        Worker->>OpenAI: Evaluate (Relevancy prompt)
        OpenAI-->>Worker: Score + Reasoning
        Worker->>Dataverse: Save Result 3 (evaluatorTypeId=Relevancy)
    end
    
    Note over Monitor: Timer fires every 1 min
    Monitor->>Dataverse: Check Progress
    Monitor->>Dataverse: Update Status = Completed<br/>Calculate Metrics
```

### Multi-Turn Test Case Execution (Service Bus Worker)
<!-- ![](./images/Multi-TurnTestCaseExecution.png) -->
```mermaid
sequenceDiagram
    participant Orchestrator
    participant Dataverse
    participant ServiceBus
    participant MTWorker as Multi-Turn Worker
    participant CopilotStudio
    participant OpenAI

    Orchestrator->>Dataverse: Fetch Test Cases<br/>(IsMultiTurn = true)
    Dataverse-->>Orchestrator: Multi-Turn Test Cases
    Orchestrator->>Dataverse: Fetch Turns<br/>(ordered by sequence)
    Dataverse-->>Orchestrator: Turns
    Orchestrator->>ServiceBus: Queue Multi-Turn Message

    ServiceBus->>MTWorker: Dequeue Message
    
    Note over MTWorker: Turn 1 Execution
    MTWorker->>CopilotStudio: Send Turn 1 Prompt<br/>(conversationId = null)
    CopilotStudio-->>MTWorker: Response + conversationId
    MTWorker->>OpenAI: Evaluate Turn 1
    OpenAI-->>MTWorker: Score + Reasoning
    MTWorker->>Dataverse: Save Turn 1 Result<br/>(conversationId stored)
    
    Note over MTWorker: Turn 2 Execution
    MTWorker->>CopilotStudio: Send Turn 2 Prompt<br/>(conversationId = from Turn 1)
    CopilotStudio-->>MTWorker: Response (context-aware)
    MTWorker->>OpenAI: Evaluate Turn 2
    OpenAI-->>MTWorker: Score + Reasoning
    MTWorker->>Dataverse: Save Turn 2 Result
    
    Note over MTWorker: Turn 3 Execution
    MTWorker->>CopilotStudio: Send Turn 3 Prompt<br/>(conversationId = from Turn 1)
    CopilotStudio-->>MTWorker: Response (full context)
    MTWorker->>OpenAI: Evaluate Turn 3
    OpenAI-->>MTWorker: Score + Reasoning
    MTWorker->>Dataverse: Save Turn 3 Result
    
    Note over MTWorker: Aggregate Result
    MTWorker->>MTWorker: Calculate Pass Rate<br/>& Overall Metrics
    MTWorker->>Dataverse: Save Aggregate Result<br/>(IsAggregate = true)
```

### Multi-Turn Test Case Execution (Durable Functions with Multi-Evaluator & Concurrent Evaluation)
<!-- ![](./images/DurableFunctionsMultiTurn.png) -->
```mermaid
sequenceDiagram
    participant Orchestrator
    participant Dataverse
    participant DurableClient as Durable Client
    participant DurableOrch as Durable Orchestrator
    participant GetTurns as Activity: GetTestCaseWithTurns
    participant ExecTurn as Activity: ExecuteSingleTurn
    participant CreateAgg as Activity: CreateAggregateResult
    participant Storage as Azure Storage
    participant CopilotStudio
    participant OpenAI

    Orchestrator->>Dataverse: Fetch Evaluator Types (N:N join)
    Dataverse-->>Orchestrator: EvaluatorTypes [Similarity, Relevancy]
    Orchestrator->>Dataverse: Fetch Test Cases<br/>(IsMultiTurn = true)
    Dataverse-->>Orchestrator: Multi-Turn Test Cases
    Orchestrator->>DurableClient: ScheduleNewOrchestrationInstanceAsync<br/>(1 orchestration per test case,<br/>with all evaluator IDs)
    
    Note over DurableOrch: Orchestration Started<br/>(evaluatorTypeIds=[Sim, Rel])
    DurableOrch->>Storage: Save Orchestration State
    
    DurableOrch->>GetTurns: CallActivityAsync<br/>(GetTestCaseWithTurns)
    GetTurns->>Dataverse: Fetch Test Case + Turns
    Dataverse-->>GetTurns: Test Case with Turns
    GetTurns-->>DurableOrch: TestCase Object
    
    DurableOrch->>Storage: Update State
    
    Note over DurableOrch: Loop through each turn
    
    loop For each turn (1 to N)
        DurableOrch->>ExecTurn: CallActivityAsync<br/>(ExecuteSingleTurn with all evaluator IDs)
        ExecTurn->>CopilotStudio: Send Turn Prompt ONCE<br/>(with conversationId from Turn 1)
        CopilotStudio-->>ExecTurn: Agent Response
        
        Note over ExecTurn: Concurrent Evaluation with Task.WhenAll
        par Evaluate with Similarity
            ExecTurn->>OpenAI: Evaluate (Similarity)
            OpenAI-->>ExecTurn: Score + Reasoning
            ExecTurn->>Dataverse: Save Turn Result 1
        and Evaluate with Relevancy
            ExecTurn->>OpenAI: Evaluate (Relevancy)
            OpenAI-->>ExecTurn: Score + Reasoning
            ExecTurn->>Dataverse: Save Turn Result 2
        end
        
        ExecTurn-->>DurableOrch: List<TurnExecutionResult>
        DurableOrch->>Storage: Update State with Turn Results
        
        Note over DurableOrch: Optional 2-second delay<br/>between turns
        DurableOrch->>DurableOrch: CreateTimer (2 seconds)
    end
    
    Note over DurableOrch: All turns completed<br/>Create aggregates for each evaluator
    
    loop For each evaluator type
        DurableOrch->>CreateAgg: CallActivityAsync<br/>(CreateAggregateResult per evaluator)
        CreateAgg->>CreateAgg: Calculate Pass Rate<br/>& Overall Metrics<br/>(filter NA results)
        CreateAgg->>Dataverse: Save Aggregate Result<br/>(IsAggregate = true, evaluatorTypeId)
        CreateAgg-->>DurableOrch: Aggregate Created
    end
    
    DurableOrch->>Dataverse: Increment Completed Count<br/>(by evaluator count)
    DurableOrch->>Storage: Save Final State
    
    Note over DurableOrch: Orchestration Complete<br/>(Can run for hours)
```

**Key Advantages of Durable Functions Multi-Evaluator Approach**:
- **Unlimited Runtime**: No 5-minute Service Bus message lock limitation
- **State Management**: Automatic checkpointing and replay
- **Resilience**: Automatic retries and failure handling
- **Debugging**: Full execution history in Azure Storage
- **Long Conversations**: Ideal for 10+ turn conversations
- **Efficiency**: 1 Copilot call per turn (not N), concurrent evaluation
- **Cost Savings**: 67-90% fewer Copilot Studio API calls
- **Performance**: 67-80% faster evaluation through parallelization

### Azure OpenAI Deployment Name Resolution
<!-- ![](./images/DeploymentNameResolution.png) -->
```mermaid
sequenceDiagram
    participant Worker as Worker/Activity
    participant LLMJudge as LLM Judge Service
    participant Dataverse
    participant AppSettings
    participant OpenAI as Azure OpenAI

    Worker->>LLMJudge: EvaluateResponseAsync<br/>(EvaluatorTypeId)
    
    Note over LLMJudge: Retrieve evaluator configuration
    LLMJudge->>Dataverse: GetEvaluatorTypeAsync<br/>(EvaluatorTypeId)
    Dataverse-->>LLMJudge: EvaluatorType<br/>(Prompt + DeploymentName)
    
    alt Evaluator has custom deployment
        LLMJudge->>LLMJudge: deploymentName = evaluatorType.DeploymentName<br/>(e.g., "gpt-35-turbo")
        Note over LLMJudge: Log: "Using gpt-35-turbo (from evaluator)"
    else Evaluator deployment is NULL/empty
        LLMJudge->>AppSettings: Get default deployment
        AppSettings-->>LLMJudge: DeploymentName = "gpt-4o"
        Note over LLMJudge: Log: "Using gpt-4o (system default)"
    end
    
    LLMJudge->>OpenAI: _openAIClient.GetChatClient<br/>(deploymentName)
    Note over LLMJudge: Create ChatClient on-demand<br/>for specific deployment
    
    LLMJudge->>OpenAI: chatClient.CompleteChatAsync<br/>(evaluation prompt)
    OpenAI-->>LLMJudge: Score + Reasoning
    
    LLMJudge-->>Worker: LLMJudgeResponse<br/>(score, reasoning, success)
```

**Deployment Resolution Logic**:
1. Worker/Activity calls `EvaluateResponseAsync` with `EvaluatorTypeId`
2. Service fetches evaluator configuration from Dataverse
3. Check if `mcicaps_deploymentname` field has a value:
   - **If yes**: Use evaluator-specific deployment (e.g., `gpt-35-turbo`)
   - **If no/NULL**: Fall back to system default from `AppSettings.AzureOpenAI.DeploymentName`
4. Create `ChatClient` on-demand for the resolved deployment
5. Log deployment source for observability (evaluator vs system default)
6. Execute evaluation using appropriate model

**Benefits**:
- Cost optimization (cheaper models for simple evaluations)
- Performance tuning (match model to task complexity)
- Backward compatibility (existing evaluators use system default)
- Observable (logs show which deployment is used)

---

## Technology Stack

### Runtime & Languages
- **.NET 8**: Function runtime (isolated process model)
- **C#**: Primary programming language
- **Azure Functions v4**: Serverless compute platform
- **Azure Durable Functions**: Long-running orchestrations and stateful workflows

### Azure Services
- **Azure Functions**: Compute (Premium or Consumption Plan)
- **Azure Service Bus**: Message queue (Standard tier, 1 active queue for single-turn)
- **Azure Durable Functions**: Multi-turn orchestration (requires Azure Storage for state)
- **Azure OpenAI**: LLM Judge (GPT-4o)
- **Microsoft Dataverse**: Data storage and prompt templates
- **Azure Key Vault**: Secrets management (Dataverse ClientSecret only)
- **Application Insights**: Monitoring and telemetry
- **Azure Storage**: Function state and Durable Functions orchestration state

### External Integrations
- **Copilot Studio Direct Line API**: Agent communication
- **Microsoft Dataverse Web API**: Data operations

### Development Tools
- **Visual Studio / VS Code**: IDE
- **Azure Functions Core Tools**: Local development
- **Bicep**: Infrastructure as Code
- **xUnit**: Unit testing
- **Moq**: Mocking framework

---

## Security Architecture

### Authentication & Authorization

#### Managed Identity (Primary Method)
All Azure-to-Azure connections use Managed Identity:
<!-- ![](./images/ManagedIdentity.png) -->
```mermaid
graph LR
    FA[Function App<br/>Managed Identity]
    
    FA -->|Cognitive Services<br/>OpenAI User| AOAI[Azure OpenAI]
    FA -->|Service Bus<br/>Data Owner| SB[Service Bus]
    FA -->|Storage Blob<br/>Data Contributor| STOR[Azure Storage]
    FA -->|Key Vault<br/>Secrets User| KV[Key Vault]
    
    style FA fill:#7FBA00,stroke:#5A8700,color:#000
    style AOAI fill:#0078D4,stroke:#004578,color:#fff
    style SB fill:#0078D4,stroke:#004578,color:#fff
    style STOR fill:#0078D4,stroke:#004578,color:#fff
    style KV fill:#0078D4,stroke:#004578,color:#fff
```

#### RBAC Role Assignments
| Service | Role | Scope | Purpose |
|---------|------|-------|---------|
| Azure OpenAI | Cognitive Services OpenAI User | Resource | Generate completions |
| Service Bus | Azure Service Bus Data Owner | Namespace | Send/receive messages |
| Azure Storage | Storage Blob Data Contributor | Account | Read/write blobs |
| Azure Storage | Storage File Data Privileged Contributor | Account | Function state |
| Key Vault | Key Vault Secrets User | Vault | Read secrets |
| Dataverse | Application User | Environment | CRUD operations |

#### External Service Credentials
Only non-Azure services require secrets in Key Vault:
- **Dataverse**: ClientSecret connection string (ClientId + ClientSecret)
- **Copilot Studio**: Direct Line token endpoint (preferred) or secret

### Data Protection
- **Encryption at Rest**: All Azure Storage and Service Bus data
- **Encryption in Transit**: TLS 1.2+ for all connections
- **Secret Management**: Key Vault with soft delete and purge protection
- **Audit Logging**: Azure Activity Log for all Managed Identity operations

### Network Security
- **HTTPS Only**: All function endpoints
- **Function Authentication**: Function-level keys for HTTP endpoints
- **Private Endpoints**: Optional for enhanced isolation

---

## Scalability & Performance

### Horizontal Scaling

#### Function Scaling Behavior
| Function | Trigger | Scaling Strategy | Max Instances |
|----------|---------|------------------|---------------|
| Orchestrator | HTTP | On-demand | 200 |
| Worker (Single-Turn) | Service Bus | Event-driven (queue depth) | 1000 |
| Durable Orchestrator (Multi-Turn) | Durable Functions | Automatic (orchestration instances) | Unlimited |
| Monitor | Timer | Single instance | 1 |

#### Service Bus Scaling
- **Auto-scaling**: Enabled based on queue depth (single-turn only)
- **Concurrent Workers**: Up to 1000 for single-turn queue
- **Message Throughput**: 1000+ messages/minute
- **Queue Capacity**: 5 GB per queue

#### Durable Functions Scaling
- **Concurrent Orchestrations**: Unlimited multi-turn conversations
- **State Management**: Automatic via Azure Storage
- **Long-Running Support**: No timeout limitations

### Performance Targets
| Metric | Target | Actual |
|--------|--------|--------|
| Orchestrator Response | < 30 seconds | ~10-15 seconds |
| Single Test Case | < 5 minutes | ~2-3 minutes |
| Overall Throughput | 1000+ tests/min | TBD |
| Evaluation Run (50K tests) | < 10 hours | TBD |

### Optimization Strategies

1. **Queue-Based Processing**: Decouple orchestration from execution
2. **Parallel Execution**: Process test cases concurrently across two dedicated queues
3. **Connection Pooling**: Reuse HTTP connections
4. **Prompt Caching**: Cache EvaluatorType prompts to reduce Dataverse calls
5. **Batch Operations**: Batch Dataverse updates where possible
6. **Atomic Updates**: Use atomic increment operations for progress tracking
7. **Durable Functions**: Alternative execution path for very long conversations

---

## Monitoring & Observability

### Application Insights Integration

#### Custom Metrics
```csharp
// Example telemetry
telemetry.TrackMetric("TestCase.Executed", 1, 
    properties: new Dictionary<string, string>
    {
        { "TestType", "Similarity" },
        { "Passed", "true" }
    });

telemetry.TrackDependency("Copilot Studio", "SendMessage", 
    startTime, duration, success);
```

#### Key Dashboards
1. **Evaluation Run Dashboard**
   - Total runs, success rate, average duration
   - Test case distribution by type
   - Pass/fail trends over time

2. **Worker Function Dashboard**
   - Queue depth over time
   - Worker execution duration
   - Success/failure rates
   - Exception frequency

3. **Multi-Turn Dashboard**
   - Conversation context success rate
   - Per-turn pass rates
   - Aggregate vs individual results

### Alerting Rules

| Alert | Condition | Severity | Action |
|-------|-----------|----------|--------|
| High Queue Depth | > 10,000 messages for 10 min | Warning | Scale review |
| Worker Failures | > 5% failure rate | Critical | Investigation |
| OpenAI Rate Limit | HTTP 429 responses | Warning | Throttle control |
| Dataverse Connection | Connection failures | Critical | Immediate action |
| Evaluation Run Timeout | > 2 hours | Warning | Manual review |
| Stalled Evaluation | No progress for 30+ min | Warning | Check workers |

### Logging Strategy

#### Log Levels
- **Information**: Normal execution flow
- **Warning**: Recoverable issues (retries, degradation)
- **Error**: Unrecoverable failures
- **Critical**: System-wide failures

#### Structured Logging
```csharp
logger.LogInformation(
    "Test case {TestCaseId} completed: {Status}, Score: {Score}",
    testCaseId, passed ? "PASSED" : "FAILED", score);
```

#### Correlation
Use `OperationId` and `ParentId` for distributed tracing:
- Orchestrator → Service Bus → Worker → OpenAI

---

## Deployment Architecture

### Environment Strategy
<!-- ![](./images/EnvironmentStrategy.png) -->
```mermaid
graph TB
    subgraph "Staging"
        STG_FUNC[Function App - Staging]
        STG_SB[Service Bus - Staging]
        STG_AOAI[Azure OpenAI - Shared]
    end
    
    DV_STG[(Dataverse - Staging)]
    
    STG_FUNC -.-> DV_STG
    
    style STG_FUNC fill:#FFB900
```

### Infrastructure as Code

**Bicep Template**: `backend/infrastructure/main.bicep`

Provisions:
- Function App with Premium Plan (supports Durable Functions)
- Service Bus namespace with 1 queue (test-case-processing for single-turn)
- Azure OpenAI with GPT-4o deployment
- Key Vault
- Application Insights
- Storage Account (required for Durable Functions state management)
- All RBAC role assignments

**Note**: Multi-turn execution uses Durable Functions orchestration and does not require Service Bus queuing.

**Deployment Command**:
```bash
az deployment group create \
  --resource-group rg-ai-eval-prod \
  --template-file main.bicep \
  --parameters main.parameters.prod.json
```

---

## Future Enhancements

### Planned Features
1. **Real-Time Progress Tracking**: WebSocket-based live updates
2. **A/B Testing**: Compare multiple agent versions
3. **Custom Judge Models**: Support for different LLM judges
4. **Batch API**: Execute multiple evaluation runs
5. **Advanced Analytics**: Machine learning insights on results
6. **Multi-Region Deployment**: Global load distribution

### Scalability Improvements
1. **Event Grid Integration**: Event-driven notifications
2. **Cosmos DB**: High-scale result storage alternative
3. **Azure Container Apps**: Extreme scale scenarios
4. **Durable Functions Expansion**: Enhanced orchestration patterns for complex workflows

**Note**: Azure Durable Functions is already implemented for multi-turn conversations as an alternative to Service Bus workers.

---

## Appendix

### API Endpoints

#### Orchestrator Endpoint
```http
POST /api/orchestrator
Content-Type: application/json

{
  "evaluationRunId": "guid",
  "options": {
    "priority": "normal",
    "timeoutMinutes": 5
  }
}

Response: 202 Accepted
{
  "evaluationRunId": "guid",
  "status": "accepted",
  "totalTestCases": 50000,
  "estimatedCompletionTime": "2025-11-10T15:30:00Z",
  "trackingUrl": "/api/status/{evaluationRunId}"
}
```

#### Health Check Endpoint
```http
GET /api/health

Response: 200 OK
{
  "status": "healthy",
  "services": {
    "dataverse": "connected",
    "serviceBus": "connected",
    "azureOpenAI": "connected",
    "keyVault": "connected"
  },
  "timestamp": "2025-11-10T10:00:00Z"
}
```

### Configuration Reference

**Application Settings (Azure Function App)**:
```json
{
  "ServiceBus__Namespace": "sbus-aiedev1234.servicebus.windows.net",
  "ServiceBus__QueueName": "test-case-processing",
  "ServiceBusNamespace__fullyQualifiedNamespace": "sbus-aiedev1234.servicebus.windows.net",
  "AzureOpenAIEndpoint": "https://openai-aiedev1234.openai.azure.com/",
  "KeyVaultUrl": "https://kv-aiedev1234.vault.azure.net/",
  "DataverseConnectionString": "@Microsoft.KeyVault(VaultName=kv-aiedev1234;SecretName=DataverseConnectionString)",
  "DataverseSchemaPrefix": "mcicaps",
  "DefaultEvaluationTimeoutMinutes": "5",
  "MaxConcurrentTestCases": "100",
  "CopilotStudio__TimeoutSeconds": "60",
  "CopilotStudio__BotResponseTimeoutSeconds": "60",
  "CopilotStudio__MaxRetryAttempts": "3",
  "AzureOpenAI__MaxRetryAttempts": "5",
  "AzureOpenAI__TimeoutSeconds": "60"
}
```

**Dataverse Schema Prefix Configuration**:

The `DataverseSchemaPrefix` setting allows customization of the Dataverse table and field prefix:

- **Default Value**: `mcicaps` (if not configured)
- **Automatic Underscore**: The system automatically appends `_` if not present
- **Usage Examples**:
  - Set `DataverseSchemaPrefix=mcicaps` → Tables/fields use `mcicaps_`
  - Set `DataverseSchemaPrefix=custom` → Tables/fields use `custom_`
  - Set `DataverseSchemaPrefix=mcicaps_` → Tables/fields use `mcicaps_` (underscore not duplicated)
- **Where Used**: All Dataverse table names (`evaluationruns`, `testcases`, etc.) and field names in `DataverseService`
- **Backward Compatibility**: Defaults to `mcicaps` to maintain compatibility with existing deployments

### Troubleshooting Guide

| Issue | Possible Cause | Resolution |
|-------|---------------|------------|
| Orchestrator timeout | Too many test cases | Increase timeout or batch requests |
| Worker failures | OpenAI rate limits | Increase deployment capacity or retry logic |
| Missing results | Monitor not running | Check timer trigger configuration |
| Authentication errors | Managed Identity not configured | Verify RBAC assignments |
| Dataverse errors | ClientSecret connection string invalid | Update Key Vault secret |
| Queue backlog | Insufficient workers | Check scaling configuration |
| Stalled evaluation | Worker stuck or crashed | Check Application Insights logs |
| Multi-turn context loss | ConversationId not preserved | Verify turn execution logic |

---

**Document Version**: 2.0  
**Created**: November 10, 2025  
**Last Updated**: November 12, 2025  
**Status**: Current - Reflects Implemented System

**Related Documents**:
- [Product Requirements Document](prd/PRD-Agent-Evaluation-Function.md)
- [Multi-Turn Support Story](stories/06-multi-turn-support.md)
- [Deployment Checklist](deployment/DEPLOYMENT_CHECKLIST.md)
- [Security Hardening](security/security-hardening-checklist.md)
