# Story 06: Multi-Turn Test Case Support

> **⚠️ IMPLEMENTATION UPDATE (November 19, 2025)**  
> Multi-turn support was initially implemented using Azure Durable Functions as documented below.  
> **Story 11 (Multi-Turn Service Bus Migration)** replaced the Durable Functions approach with a pure Service Bus queue architecture for improved reliability and simplicity.  
> See `docs/stories/11-multi-turn-service-bus-migration.md` for current implementation details.

## Overview
This document outlines the original implementation plan for adding multi-turn conversation support to the AI Agent Evaluation system using Durable Functions. Multi-turn test cases enable testing of conversational scenarios where the agent must maintain context across multiple user prompts.

## Use Case Example
```
Turn 1:
  User: "I would like to see blue shirts for men"
  Agent: "Opulent blue mens button down, cool sky mens t-shirt, Royal navy henley."
  
Turn 2:
  User: "I'm a size medium"
  Agent: "Opulent blue mens button down, Royal navy henley."
```

## Key Requirements (Original Durable Functions Implementation)
1. Each turn stored as a separate test case record for simplicity
2. All turns in a sequence share the same conversationId to maintain context
3. Individual evaluation results for each turn
4. Aggregate evaluation result for the overall multi-turn sequence
5. Continue execution even if earlier turns fail
6. Evaluate each turn independently using the test type defined at sequence level
7. No new Azure infrastructure required (can add functions if needed)

**Current Implementation (Story 11)**: Uses Service Bus queues (`multi-turn-agent-call-queue`) with stateless workers instead of Durable Functions orchestrations. See Story 11 for details.

---

## 1. Dataverse Schema Changes ✅ COMPLETED

### 1.1 Added Field: `mcicaps_testcases.mcicaps_ismultiturn` ✅
**Purpose:** Flag to identify multi-turn test cases

| Property | Value |
|----------|-------|
| **Display Name** | Is Multi-Turn |
| **Schema Name** | `mcicaps_ismultiturn` |
| **Data Type** | Two Options (Boolean) |
| **Default Value** | No (false) |
| **Required** | Yes |
| **Description** | Indicates if this test case has multiple turns |
| **Unique Constraint** | No |
| **Status** | ✅ Created in Dataverse |

### 1.2 Created Table: `mcicaps_testcaseturn` ✅
**Purpose:** Child entity to store individual turns for multi-turn test cases

| Property | Value |
|----------|-------|
| **Display Name** | Test Case Turn |
| **Plural Display Name** | Test Case Turns |
| **Schema Name** | `mcicaps_testcaseturn` |
| **Primary Name Field** | `mcicaps_name` (auto-generated with global sequence number) |
| **Description** | Individual turns within a multi-turn test case |
| **Ownership** | Organization-owned |
| **Status** | ✅ Created in Dataverse |

#### Fields for `mcicaps_testcaseturn` table (All ✅ Created):

**1.2.1 - Primary Name Field**
| Property | Value |
|----------|-------|
| **Display Name** | Name |
| **Schema Name** | `mcicaps_name` |
| **Data Type** | Text (Single Line) |
| **Max Length** | 100 |
| **Required** | Yes |
| **Auto Number Format** | `Turn-{SEQNUM:5}` (e.g., "Turn-00001", "Turn-00002") |
| **Description** | Auto-generated name using global sequence. Note: This is NOT the turn order within a test case - use mcicaps_sequence field for ordering. |
| **Status** | ✅ Created in Dataverse |

**1.2.2 - Test Case Lookup**
| Property | Value |
|----------|-------|
| **Display Name** | Test Case |
| **Schema Name** | `mcicaps_testcaseid` |
| **Data Type** | Lookup |
| **Target Entity** | `mcicaps_testcase` |
| **Required** | Yes |
| **Description** | Parent test case this turn belongs to |
| **Relationship Name** | `mcicaps_testcase_testcaseturns` |
| **Cascade Behavior** | Delete: Cascade, Assign: Cascade, Share: Cascade |
| **Status** | ✅ Created in Dataverse |

**1.2.3 - Sequence Number**
| Property | Value |
|----------|-------|
| **Display Name** | Sequence |
| **Schema Name** | `mcicaps_sequence` |
| **Data Type** | Whole Number |
| **Min Value** | 1 |
| **Max Value** | 100 |
| **Required** | Yes |
| **Description** | Order of execution within the test case (1-based). **THIS is the field used for sorting turns, NOT mcicaps_name.** |
| **Unique Constraint** | No (but should be unique within a test case - enforced at application level) |
| **Status** | ✅ Created in Dataverse |

**1.2.4 - Prompt Text**
| Property | Value |
|----------|-------|
| **Display Name** | Prompt Text |
| **Schema Name** | `mcicaps_prompttext` |
| **Data Type** | Text (Multiple Lines) |
| **Max Length** | 10000 |
| **Required** | Yes |
| **Description** | The user prompt/input for this turn |
| **Status** | ✅ Created in Dataverse |

**1.2.5 - Expected Response**
| Property | Value |
|----------|-------|
| **Display Name** | Expected Response |
| **Schema Name** | `mcicaps_expectedresponse` |
| **Data Type** | Text (Multiple Lines) |
| **Max Length** | 10000 |
| **Required** | No |
| **Description** | The expected agent response for this turn |
| **Status** | ✅ Created in Dataverse |

### 1.3 Added Field: `mcicaps_evaluationresults.mcicaps_testcaseturnid` ✅
**Purpose:** Links evaluation result to a specific turn (for multi-turn tests)

| Property | Value |
|----------|-------|
| **Display Name** | Test Case Turn |
| **Schema Name** | `mcicaps_testcaseturnid` |
| **Data Type** | Lookup |
| **Target Entity** | `mcicaps_testcaseturn` |
| **Required** | No |
| **Description** | The specific turn this result corresponds to. Null for single-turn tests or aggregate results. |
| **Relationship Name** | `mcicaps_testcaseturn_evaluationresults` |
| **Cascade Behavior** | Delete: RemoveLink, Assign: NoCascade, Share: NoCascade |
| **Unique Constraint** | No |
| **Status** | ✅ Created in Dataverse |

### 1.4 Added Field: `mcicaps_evaluationresults.mcicaps_conversationid` ✅
**Purpose:** Stores the Direct Line conversationId for this specific test run

| Property | Value |
|----------|-------|
| **Display Name** | Conversation ID |
| **Schema Name** | `mcicaps_conversationid` |
| **Data Type** | Text (Single Line) |
| **Max Length** | 200 |
| **Required** | No |
| **Description** | Direct Line conversation ID for this test execution. Shared across all turns in a multi-turn test case run. |
| **Unique Constraint** | No |
| **Status** | ✅ Created in Dataverse |

### 1.5 Added Field: `mcicaps_evaluationresults.mcicaps_isaggregate` ✅
**Purpose:** Distinguishes aggregate results from individual turn results

| Property | Value |
|----------|-------|
| **Display Name** | Is Aggregate Result |
| **Schema Name** | `mcicaps_isaggregate` |
| **Data Type** | Two Options (Boolean) |
| **Default Value** | No (false) |
| **Required** | Yes |
| **Description** | True for overall multi-turn aggregate result, false for individual turn results or single-turn tests |
| **Unique Constraint** | No |
| **Status** | ✅ Created in Dataverse |

---

## 2. Data Model Updates

### 2.1 TestCase Model Enhancement
**File:** `backend/AiAgentEvaluation/Models/TestCase.cs`

Add new property:
```csharp
/// <summary>
/// Indicates if this test case has multiple turns
/// </summary>
public bool IsMultiTurn { get; set; }

/// <summary>
/// Collection of turns for multi-turn test cases
/// </summary>
public List<TestCaseTurn>? Turns { get; set; }
```

### 2.2 New Model: TestCaseTurn
**File:** `backend/AiAgentEvaluation/Models/TestCaseTurn.cs`

```csharp
namespace AiAgentEvaluation.Models
{
    /// <summary>
    /// Represents a single turn within a multi-turn test case
    /// </summary>
    public class TestCaseTurn
    {
        /// <summary>
        /// Unique identifier for the turn
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Name of the turn (e.g., "Turn 1")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Parent test case ID
        /// </summary>
        public string TestCaseId { get; set; } = string.Empty;

        /// <summary>
        /// Order of execution within the test case (1-based)
        /// </summary>
        public int Sequence { get; set; }

        /// <summary>
        /// User prompt/input for this turn
        /// </summary>
        public string PromptText { get; set; } = string.Empty;

        /// <summary>
        /// Expected agent response for this turn
        /// </summary>
        public string? ExpectedResponse { get; set; }

        /// <summary>
        /// Navigation property to parent test case
        /// </summary>
        public TestCase? TestCase { get; set; }
    }
}
```

### 2.3 EvaluationResult Model Enhancement
**File:** `backend/AiAgentEvaluation/Models/EvaluationResult.cs`

Add new properties:
```csharp
/// <summary>
/// Lookup to specific turn (for multi-turn tests)
/// </summary>
public string? TestCaseTurnId { get; set; }

/// <summary>
/// Navigation property to the turn
/// </summary>
public TestCaseTurn? TestCaseTurn { get; set; }

/// <summary>
/// Direct Line conversation ID for this test execution
/// </summary>
public string? ConversationId { get; set; }

/// <summary>
/// Indicates if this is an aggregate result for the overall multi-turn test
/// </summary>
public bool IsAggregate { get; set; }
```

### 2.4 New Model: MultiTurnExecutionContext
**File:** `backend/AiAgentEvaluation/Models/MultiTurnExecutionContext.cs`

```csharp
namespace AiAgentEvaluation.Models
{
    /// <summary>
    /// Context for tracking multi-turn test case execution
    /// </summary>
    public class MultiTurnExecutionContext
    {
        /// <summary>
        /// The test case being executed
        /// </summary>
        public TestCase TestCase { get; set; } = null!;

        /// <summary>
        /// Direct Line conversation ID shared across all turns
        /// </summary>
        public string? ConversationId { get; set; }

        /// <summary>
        /// Ordered list of turns to execute
        /// </summary>
        public List<TestCaseTurn> Turns { get; set; } = new();

        /// <summary>
        /// Results for each turn (indexed by sequence number)
        /// </summary>
        public Dictionary<int, EvaluationResult> TurnResults { get; set; } = new();

        /// <summary>
        /// Test run ID this execution belongs to
        /// </summary>
        public string TestRunId { get; set; } = string.Empty;

        /// <summary>
        /// Total number of turns
        /// </summary>
        public int TotalTurns => Turns.Count;

        /// <summary>
        /// Number of turns completed
        /// </summary>
        public int CompletedTurns => TurnResults.Count;

        /// <summary>
        /// Number of passed turns
        /// </summary>
        public int PassedTurns => TurnResults.Values.Count(r => r.Passed);

        /// <summary>
        /// Number of failed turns
        /// </summary>
        public int FailedTurns => TurnResults.Values.Count(r => !r.Passed);

        /// <summary>
        /// Overall pass rate for the test case
        /// </summary>
        public double PassRate => TotalTurns > 0 ? (double)PassedTurns / TotalTurns : 0.0;
    }
}
```

---

## 3. Service Layer Updates

### 3.1 DataverseService Enhancements
**File:** `backend/AiAgentEvaluation/Services/DataverseService.cs`

#### 3.1.1 Update FetchTestCasesAsync
Modify query to fetch multi-turn flag:
```csharp
// Add to SELECT clause
mcicaps_ismultiturn
```

#### 3.1.2 New Method: GetTestCaseTurnsAsync
```csharp
/// <summary>
/// Fetches all turns for a given test case, ordered by sequence
/// </summary>
public async Task<List<TestCaseTurn>> GetTestCaseTurnsAsync(
    string testCaseId, 
    CancellationToken cancellationToken = default)
{
    // FetchXML query:
    // - Filter by mcicaps_testcaseid
    // - Order by mcicaps_sequence
    // - Select: id, name, sequence, prompttext, expectedresponse
}
```

#### 3.1.3 New Method: CreateTestCaseTurnAsync
```csharp
/// <summary>
/// Creates a new turn for a test case
/// </summary>
public async Task<string> CreateTestCaseTurnAsync(
    TestCaseTurn turn,
    CancellationToken cancellationToken = default)
{
    // POST to mcicaps_testcaseturns
    // Returns created turn ID
}
```

#### 3.1.4 Update SaveEvaluationResultAsync
Modify to include new fields when creating result:
```csharp
// Add to result payload
mcicaps_testcaseturnid (lookup - if turn result),
mcicaps_conversationid (text),
mcicaps_isaggregate (boolean)
```

#### 3.1.5 New Method: SaveAggregateEvaluationResultAsync
```csharp
/// <summary>
/// Saves an aggregate evaluation result for a multi-turn test case
/// </summary>
public async Task<string> SaveAggregateEvaluationResultAsync(
    MultiTurnExecutionContext context,
    CancellationToken cancellationToken = default)
{
    // Create aggregate result with:
    // - mcicaps_testcaseid = testCase.Id (lookup to parent)
    // - mcicaps_testcaseturnid = null
    // - mcicaps_isaggregate = true
    // - mcicaps_conversationid = context.ConversationId
    // - Overall metrics (pass rate, summary)
    // - Reference to test run
}
```

#### 3.1.6 New Method: GetConversationIdFromPreviousTurnAsync
```csharp
/// <summary>
/// Retrieves the conversation ID from a previous turn's result in the same test run
/// </summary>
public async Task<string?> GetConversationIdFromPreviousTurnAsync(
    string testCaseId,
    string testRunId,
    int currentTurnSequence,
    CancellationToken cancellationToken = default)
{
    // Query mcicaps_evaluationresults:
    // - Filter by testCaseId and testRunId
    // - Get result for turn sequence = currentTurnSequence - 1
    // - Return conversationId
    // Used to get conversationId from Turn 1 when executing Turn 2+
}
```

### 3.2 CopilotStudioService Enhancements
**File:** `backend/AiAgentEvaluation/Services/CopilotStudioService.cs`

The `SendMessageAsync` method already supports conversationId parameter - **no changes needed**:
```csharp
public async Task<CopilotDirectLineResponse> SendMessageAsync(
    string directLineSpeechUrl, 
    string userInput, 
    string? conversationId = null, 
    CancellationToken cancellationToken = default)
```

### 3.3 New Service: MultiTurnExecutionService
**File:** `backend/AiAgentEvaluation/Services/MultiTurnExecutionService.cs`

```csharp
namespace AiAgentEvaluation.Services
{
    /// <summary>
    /// Executes multi-turn test cases
    /// </summary>
    public interface IMultiTurnExecutionService
    {
        /// <summary>
        /// Executes a multi-turn test case
        /// </summary>
        Task<MultiTurnExecutionContext> ExecuteTestCaseAsync(
            TestCase testCase, 
            string testRunId,
            CancellationToken cancellationToken = default);
    }

    public class MultiTurnExecutionService : IMultiTurnExecutionService
    {
        private readonly IDataverseService _dataverseService;
        private readonly ICopilotStudioService _copilotStudioService;
        private readonly IEvaluationService _evaluationService;
        private readonly ILogger<MultiTurnExecutionService> _logger;

        public MultiTurnExecutionService(
            IDataverseService dataverseService,
            ICopilotStudioService copilotStudioService,
            IEvaluationService evaluationService,
            ILogger<MultiTurnExecutionService> logger)
        {
            _dataverseService = dataverseService;
            _copilotStudioService = copilotStudioService;
            _evaluationService = evaluationService;
            _logger = logger;
        }

        public async Task<MultiTurnExecutionContext> ExecuteTestCaseAsync(
            TestCase testCase, 
            string testRunId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Executing multi-turn test case {TestCaseId} with {TurnCount} turns",
                testCase.Id, testCase.Turns?.Count ?? 0);

            var context = new MultiTurnExecutionContext
            {
                TestCase = testCase,
                TestRunId = testRunId,
                Turns = testCase.Turns?.OrderBy(t => t.Sequence).ToList() ?? new()
            };

            // Execute each turn in sequence
            foreach (var turn in context.Turns)
            {
                try
                {
                    // 1. Get conversation ID (null for first turn, reuse for subsequent)
                    string? conversationId = context.ConversationId;
                    
                    if (turn.Sequence > 1 && string.IsNullOrEmpty(conversationId))
                    {
                        // Try to retrieve from previous turn's result
                        conversationId = await _dataverseService
                            .GetConversationIdFromPreviousTurnAsync(
                                testCase.Id, testRunId, turn.Sequence, cancellationToken);
                    }

                    // 2. Send message to Copilot Studio
                    var response = await _copilotStudioService.SendMessageAsync(
                        testCase.DirectLineEndpoint,
                        turn.PromptText,
                        conversationId,
                        cancellationToken);

                    // 3. Store conversation ID after first turn
                    if (turn.Sequence == 1 && !string.IsNullOrEmpty(response.ConversationId))
                    {
                        context.ConversationId = response.ConversationId;
                    }

                    // 4. Evaluate response
                    var evaluation = await _evaluationService.EvaluateResponseAsync(
                        turn.ExpectedResponse ?? string.Empty,
                        response.AgentResponse,
                        testCase.TestType,
                        cancellationToken);

                    // 5. Create evaluation result
                    var result = new EvaluationResult
                    {
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        TestRunId = testRunId,
                        ConversationId = context.ConversationId,
                        IsAggregate = false,
                        AgentResponse = response.AgentResponse,
                        ExpectedResponse = turn.ExpectedResponse,
                        Passed = evaluation.Passed,
                        Score = evaluation.Score,
                        ErrorMessage = evaluation.ErrorMessage,
                        ResponseTimeMs = response.ResponseTimeMs,
                        ExecutedAt = DateTime.UtcNow
                    };

                    // 6. Save turn result
                    result.Id = await _dataverseService.SaveEvaluationResultAsync(
                        result, cancellationToken);

                    context.TurnResults[turn.Sequence] = result;

                    _logger.LogInformation(
                        "Turn {Sequence} completed: {Status}",
                        turn.Sequence, result.Passed ? "PASSED" : "FAILED");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing turn {Sequence}", turn.Sequence);
                    
                    // Create failure result and continue
                    var failureResult = new EvaluationResult
                    {
                        TestCaseId = testCase.Id,
                        TestCaseTurnId = turn.Id,
                        TestRunId = testRunId,
                        ConversationId = context.ConversationId,
                        IsAggregate = false,
                        Passed = false,
                        ErrorMessage = $"Exception during turn execution: {ex.Message}",
                        ExecutedAt = DateTime.UtcNow
                    };

                    failureResult.Id = await _dataverseService.SaveEvaluationResultAsync(
                        failureResult, cancellationToken);

                    context.TurnResults[turn.Sequence] = failureResult;
                }
            }

            // 7. Save aggregate result
            await _dataverseService.SaveAggregateEvaluationResultAsync(
                context, cancellationToken);

            _logger.LogInformation(
                "Multi-turn test case {TestCaseId} completed: {PassRate:P0} pass rate",
                testCase.Id, context.PassRate);

            return context;
        }
    }
}
```

---

## 4. Function Updates

### 4.1 OrchestratorFunction Updates
**File:** `backend/AiAgentEvaluation/Functions/OrchestratorFunction.cs`

#### Changes:
1. After fetching test cases, check if each is multi-turn
2. For single-turn tests, queue individual worker activities (existing behavior)
3. For multi-turn tests, queue multi-turn worker activity
4. Load turns when needed (lazy load or eager load based on performance)

**Pseudocode:**
```csharp
public async Task<List<string>> RunOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var input = context.GetInput<OrchestratorInput>();
    
    // Fetch test cases
    var testCases = await context.CallActivityAsync<List<TestCase>>(
        nameof(FetchTestCasesActivity), input.TestRunId);

    var tasks = new List<Task<string>>();
    
    foreach (var testCase in testCases)
    {
        if (testCase.IsMultiTurn)
        {
            // Multi-turn test case
            tasks.Add(context.CallActivityAsync<string>(
                nameof(MultiTurnWorkerActivity), 
                new MultiTurnWorkerInput 
                { 
                    TestCaseId = testCase.Id, 
                    TestRunId = input.TestRunId 
                }));
        }
        else
        {
            // Single-turn test case (existing behavior)
            tasks.Add(context.CallActivityAsync<string>(
                nameof(WorkerActivity), testCase));
        }
    }
    
    // Execute all in parallel
    await Task.WhenAll(tasks);
    return tasks.Select(t => t.Result).ToList();
}
```

#### FetchTestCasesActivity Update:
```csharp
[FunctionName(nameof(FetchTestCasesActivity))]
public async Task<List<TestCase>> FetchTestCasesActivity(
    [ActivityTrigger] string testRunId,
    CancellationToken cancellationToken)
{
    var testCases = await _dataverseService.FetchTestCasesAsync(
        testRunId, cancellationToken);

    // For multi-turn test cases, load their turns
    foreach (var testCase in testCases.Where(tc => tc.IsMultiTurn))
    {
        testCase.Turns = await _dataverseService.GetTestCaseTurnsAsync(
            testCase.Id, cancellationToken);
    }

    return testCases;
}
```

### 4.2 New Function: MultiTurnWorkerFunction
**File:** `backend/AiAgentEvaluation/Functions/MultiTurnWorkerFunction.cs`

```csharp
namespace AiAgentEvaluation.Functions
{
    /// <summary>
    /// Executes multi-turn test cases
    /// </summary>
    public class MultiTurnWorkerFunction
    {
        private readonly IMultiTurnExecutionService _multiTurnService;
        private readonly IDataverseService _dataverseService;
        private readonly ILogger<MultiTurnWorkerFunction> _logger;

        public MultiTurnWorkerFunction(
            IMultiTurnExecutionService multiTurnService,
            IDataverseService dataverseService,
            ILogger<MultiTurnWorkerFunction> logger)
        {
            _multiTurnService = multiTurnService;
            _dataverseService = dataverseService;
            _logger = logger;
        }

        [FunctionName(nameof(MultiTurnWorkerActivity))]
        public async Task<string> MultiTurnWorkerActivity(
            [ActivityTrigger] MultiTurnWorkerInput input,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Executing multi-turn test case {TestCaseId} for test run {TestRunId}",
                input.TestCaseId, input.TestRunId);

            try
            {
                // Get test case with turns (may have been loaded by orchestrator)
                // If not loaded, fetch here
                var testCase = input.TestCase;
                if (testCase.Turns == null || !testCase.Turns.Any())
                {
                    testCase.Turns = await _dataverseService.GetTestCaseTurnsAsync(
                        testCase.Id, cancellationToken);
                }

                // Execute the test case
                var context = await _multiTurnService.ExecuteTestCaseAsync(
                    testCase,
                    input.TestRunId,
                    cancellationToken);

                _logger.LogInformation(
                    "Multi-turn test case {TestCaseId} completed: {Passed}/{Total} turns passed",
                    input.TestCaseId, context.PassedTurns, context.TotalTurns);

                return $"TestCase {testCase.Name}: {context.PassRate:P0} pass rate ({context.PassedTurns}/{context.TotalTurns})";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error executing multi-turn test case {TestCaseId}", 
                    input.TestCaseId);
                throw;
            }
        }
    }

    public class MultiTurnWorkerInput
    {
        public string TestCaseId { get; set; } = string.Empty;
        public TestCase TestCase { get; set; } = null!;
        public string TestRunId { get; set; } = string.Empty;
    }
}
```

### 4.3 WorkerFunction Updates (Optional)
**File:** `backend/AiAgentEvaluation/Functions/WorkerFunction.cs`

**No breaking changes needed** - existing worker continues to handle single-turn tests. If a test case happens to be marked as multi-turn but processed individually, it will work (just won't maintain conversation context).

### 4.4 MonitorFunction Updates
**File:** `backend/AiAgentEvaluation/Functions/MonitorFunction.cs`

#### Changes:
1. When reporting results, distinguish between:
   - Individual turn results
   - Aggregate sequence results
2. Add filtering to show only aggregate results for multi-turn sequences (optional)
3. Include sequence-level metrics in monitoring dashboard

**Example additions to monitoring payload:**
```csharp
public class MonitoringPayload
{
    // Existing fields...
    
    public int MultiTurnSequences { get; set; }
    public int TotalTurns { get; set; }
    public Dictionary<string, SequenceMetrics> SequenceResults { get; set; }
}

public class SequenceMetrics
{
    public string SequenceId { get; set; }
    public int TotalTurns { get; set; }
    public int PassedTurns { get; set; }
    public double PassRate { get; set; }
}
```

---

## 5. Configuration Updates

### 5.1 No Infrastructure Changes Required
The existing Azure Functions infrastructure supports this implementation:
- **Orchestrator Function**: Already supports parallel activity execution
- **Worker Functions**: Can scale horizontally to handle sequences
- **Service Bus**: Can handle multi-turn worker messages
- **Application Insights**: Will track multi-turn execution

### 5.2 New Configuration Settings (Optional)
**File:** `backend/AiAgentEvaluation/local.settings.json` and Azure App Settings

```json
{
  "MultiTurn:MaxTurnsPerSequence": "20",
  "MultiTurn:ConversationTimeoutSeconds": "600",
  "MultiTurn:ContinueOnTurnFailure": "true"
}
```

---

## 6. Execution Flow

### 6.1 Single-Turn Test (Existing Behavior)
```
Orchestrator
  └─> WorkerActivity (TestCase A - single turn)
  └─> WorkerActivity (TestCase B - single turn)
  └─> WorkerActivity (TestCase C - single turn)
```

### 6.2 Multi-Turn Test (New Behavior)
```
Orchestrator
  └─> MultiTurnWorkerActivity (TestCase D - multi-turn)
        ├─> Turn 1: Send message (conversationId = null)
        │     ├─> Store conversationId from response in result
        │     └─> Save Turn 1 result to Dataverse
        ├─> Turn 2: Send message (conversationId = from Turn 1 result)
        │     ├─> Context maintained
        │     └─> Save Turn 2 result to Dataverse
        └─> Turn 3: Send message (conversationId = from Turn 1 result)
              ├─> Context maintained
              ├─> Save Turn 3 result to Dataverse
              └─> Create and save aggregate result
```

### 6.3 Mixed Test Run
```
Orchestrator
  ├─> WorkerActivity (TestCase A - single turn)
  ├─> WorkerActivity (TestCase B - single turn)
  ├─> MultiTurnWorkerActivity (TestCase C - 3 turns)
  └─> MultiTurnWorkerActivity (TestCase D - 2 turns)
```

### 6.4 Data Structure Example

**Single-Turn Test:**
```
mcicaps_testcases (TestCase A)
  └─> mcicaps_evaluationresults (1 result)
        - testcaseid: TestCase A
        - testcaseturnid: null
        - isaggregate: false
        - conversationid: "conv-abc-123"
```

**Multi-Turn Test:**
```
mcicaps_testcases (TestCase C)
  ├─> mcicaps_testcaseturn
  │     ├─> Turn-00001 (sequence: 1, prompt: "I want blue shirts")
  │     ├─> Turn-00002 (sequence: 2, prompt: "Size medium")
  │     └─> Turn-00003 (sequence: 3, prompt: "Show availability")
  └─> mcicaps_evaluationresults
        ├─> Result 1 (testcaseturnid: Turn-00001, isaggregate: false, conversationid: "conv-xyz-789")
        ├─> Result 2 (testcaseturnid: Turn-00002, isaggregate: false, conversationid: "conv-xyz-789")
        ├─> Result 3 (testcaseturnid: Turn-00003, isaggregate: false, conversationid: "conv-xyz-789")
        └─> Aggregate (testcaseturnid: null, isaggregate: true, conversationid: "conv-xyz-789")
```

---

## 7. Conversation Context Management

### 7.1 Direct Line Conversation Flow
Based on Microsoft Copilot Studio documentation:

1. **First Turn:**
   - Call `SendMessageAsync` with `conversationId = null`
   - Direct Line creates new conversation
   - Response includes `conversationId` in metadata
   - Store conversationId in Dataverse for sequence

2. **Subsequent Turns:**
   - Retrieve conversationId from Dataverse (or from context)
   - Call `SendMessageAsync` with `conversationId = <stored_value>`
   - Direct Line maintains conversation context
   - Agent sees full conversation history

3. **Token Management:**
   - Existing `CopilotStudioService` handles token caching
   - Tokens are valid for 30 minutes (default)
   - Sequence execution should complete within token lifetime

### 7.2 Key Direct Line Concepts
From Microsoft documentation:
- **ConversationId**: Unique identifier for conversation thread
- **Token**: Authentication token (can be refreshed)
- **Watermark**: Position in activity stream (handled by SDK)

**No changes needed** - current service already implements this correctly.

---

## 8. Aggregate Result Calculation

### 8.1 Metrics to Calculate
For each multi-turn sequence:

1. **Per-Turn Metrics** (already captured):
   - Individual pass/fail status
   - Response time
   - Score (if applicable)
   - Error messages

2. **Aggregate Metrics** (new):
   - Overall pass rate: `PassedTurns / TotalTurns`
   - Average response time across turns
   - First turn success rate (critical indicator)
   - Consecutive failure count
   - Context maintenance score (custom metric)

### 8.2 Aggregate Result Structure
```csharp
public class AggregateEvaluationResult
{
    public string SequenceId { get; set; }
    public int TotalTurns { get; set; }
    public int PassedTurns { get; set; }
    public int FailedTurns { get; set; }
    public double PassRate { get; set; }
    public double AverageResponseTime { get; set; }
    public bool FirstTurnPassed { get; set; }
    public int MaxConsecutiveFailures { get; set; }
    public string Summary { get; set; } // Human-readable summary
    public List<string> TurnResultIds { get; set; } // Reference to individual results
}
```

---

## 9. Error Handling

### 9.1 Turn-Level Failures
**Strategy:** Continue execution even if a turn fails

```csharp
foreach (var turn in sequence.Turns)
{
    try
    {
        var result = await ExecuteTurnAsync(turn, conversationId);
        context.TurnResults[turn.TurnNumber] = result;
        
        if (!result.Passed)
        {
            _logger.LogWarning(
                "Turn {TurnNumber} failed in sequence {SequenceId}, continuing...",
                turn.TurnNumber, sequence.SequenceId);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error executing turn {TurnNumber}", turn.TurnNumber);
        
        // Create failure result
        context.TurnResults[turn.TurnNumber] = CreateFailureResult(turn, ex);
        
        // Continue to next turn
        continue;
    }
}
```

### 9.2 Sequence-Level Failures
**Critical failures that stop sequence:**
- Unable to fetch test cases
- Unable to establish initial conversation
- Service unavailable (after retries)
- Orchestration timeout

### 9.3 Conversation Token Expiry
```csharp
if (IsTokenExpired(conversationId))
{
    _logger.LogWarning("Conversation token expired, creating new conversation");
    conversationId = null; // Start fresh
}
```

---

## 10. Testing Strategy

### 10.1 Unit Tests
**New test files:**
- `MultiTurnExecutionServiceTests.cs`
- `MultiTurnWorkerFunctionTests.cs`
- `TestCaseTurnModelTests.cs`

**Test scenarios:**
1. Execute 2-turn test case successfully
2. Execute test case with failing turns (verify continuation)
3. Handle conversation token expiry
4. Calculate aggregate metrics correctly
5. Retrieve conversationId from previous turn's result
6. Handle missing turns gracefully

### 10.2 Integration Tests
**File:** `tests/AiAgentEvaluation.IntegrationTests/MultiTurnTests.cs`

**Test scenarios:**
1. End-to-end 3-turn conversation
2. Verify conversation context maintained across turns
3. Verify individual and aggregate results saved to Dataverse
4. Test parallel execution of multiple multi-turn test cases
5. Verify monitoring data includes multi-turn metrics
6. Test creating and retrieving test case turns from Dataverse

### 10.3 Manual Testing
**Test data setup in Dataverse:**

Create 2 multi-turn test cases:

**Test Case 1: Blue Shirts (2 turns)**
```
mcicaps_testcases:
  - mcicaps_name: "Blue Shirts Shopping"
  - mcicaps_ismultiturn: Yes
  - mcicaps_testtype: Similarity (or Accuracy)
  - mcicaps_directlineendpoint: "<your-endpoint>"

  mcicaps_testcaseturn (children):
    Record 1:
      - mcicaps_name: "Turn-00001" (auto-generated - do not set manually)
      - mcicaps_testcaseid: [lookup to test case]
      - mcicaps_sequence: 1  ← USE THIS FOR SORTING
      - mcicaps_prompttext: "I would like to see blue shirts for men"
      - mcicaps_expectedresponse: "Opulent blue mens button down, cool sky mens t-shirt, Royal navy henley"
    
    Record 2:
      - mcicaps_name: "Turn-00002" (auto-generated - do not set manually)
      - mcicaps_testcaseid: [lookup to test case]
      - mcicaps_sequence: 2  ← USE THIS FOR SORTING
      - mcicaps_prompttext: "I'm a size medium"
      - mcicaps_expectedresponse: "Opulent blue mens button down, Royal navy henley"
```

**Test Case 2: Order Status (3 turns)**
```
mcicaps_testcases:
  - mcicaps_name: "Order Status Inquiry"
  - mcicaps_ismultiturn: Yes
  - mcicaps_testtype: Accuracy
  - mcicaps_directlineendpoint: "<your-endpoint>"

  mcicaps_testcaseturn (children):
    Record 1:
      - mcicaps_name: "Turn-00003" (auto-generated - do not set manually)
      - mcicaps_testcaseid: [lookup to test case]
      - mcicaps_sequence: 1  ← USE THIS FOR SORTING
      - mcicaps_prompttext: "What's the status of my order?"
      - mcicaps_expectedresponse: "I can help with that. What's your order number?"
    
    Record 2:
      - mcicaps_name: "Turn-00004" (auto-generated - do not set manually)
      - mcicaps_testcaseid: [lookup to test case]
      - mcicaps_sequence: 2  ← USE THIS FOR SORTING
      - mcicaps_prompttext: "ORDER-12345"
      - mcicaps_expectedresponse: "Your order ORDER-12345 is currently in transit"
    
    Record 3:
      - mcicaps_name: "Turn-00005" (auto-generated - do not set manually)
      - mcicaps_testcaseid: [lookup to test case]
      - mcicaps_sequence: 3  ← USE THIS FOR SORTING
      - mcicaps_prompttext: "When will it arrive?"
      - mcicaps_expectedresponse: "Expected delivery is November 12, 2025"
```

---

## 11. Implementation Phases

### Phase 0: Dataverse Schema ✅ COMPLETED
**Tasks:**
1. ✅ Create `mcicaps_ismultiturn` field on `mcicaps_testcases`
2. ✅ Create new `mcicaps_testcaseturn` table with all fields
3. ✅ Add fields to `mcicaps_evaluationresults` (testcaseturnid, conversationid, isaggregate)
4. ✅ Establish relationships between tables

**Deliverables:**
- ✅ Dataverse schema complete and ready for use
- ✅ Schema documentation (this document)

---

### Phase 1: Data Models (Week 1) 🔨 READY TO START
**Tasks:**
1. Create `TestCaseTurn.cs` model
2. Update `TestCase.cs` model with `IsMultiTurn` and `Turns` properties
3. Update `EvaluationResult.cs` model with new fields
4. Create `MultiTurnExecutionContext.cs` model
5. Add unit tests for models

**Deliverables:**
- New `TestCaseTurn` model
- Updated `TestCase` and `EvaluationResult` models
- New `MultiTurnExecutionContext` model
- Model unit tests

---

### Phase 2: Service Layer (Week 2)
**Tasks:**
1. Update `DataverseService`:
   - Modify FetchXML queries
   - Add `GetTestCasesBySequenceIdAsync`
   - Add `UpdateTestCaseConversationIdAsync`
   - Update `SaveEvaluationResultAsync`
   - Add `SaveAggregateEvaluationResultAsync`
2. Create `MultiTurnOrchestrationService`
3. Add service unit tests
4. Integration tests for Dataverse operations

**Deliverables:**
- Updated `DataverseService`
- New `MultiTurnOrchestrationService`
- Service unit tests
- Integration tests

---

### Phase 3: Function Implementation (Week 3)
**Tasks:**
1. Create `MultiTurnWorkerFunction`
2. Update `OrchestratorFunction` to handle sequences
3. Update `MonitorFunction` for sequence metrics
4. Add function unit tests
5. Add dependency injection registrations

**Deliverables:**
- New `MultiTurnWorkerFunction`
- Updated orchestrator logic
- Updated monitoring
- Function tests

---

### Phase 4: Testing & Validation (Week 4)
**Tasks:**
1. Create integration tests
2. Manual testing with sample data
3. Performance testing (multiple sequences)
4. Update documentation
5. Create runbook for multi-turn scenarios

**Deliverables:**
- Comprehensive test suite
- Test data scripts
- Updated documentation
- Operations runbook

---

### Phase 5: Deployment & Monitoring (Week 5)
**Tasks:**
1. Deploy to staging environment
2. Validate with real Copilot Studio agent
3. Monitor execution metrics
4. Fine-tune error handling
5. Deploy to production

**Deliverables:**
- Deployed solution
- Monitoring dashboards
- Production validation report

---

## 12. Backward Compatibility

### 12.1 Existing Single-Turn Tests
**No breaking changes:**
- Existing test cases with `mcicaps_ismultiturn = false` (or null) continue to work
- `WorkerFunction` handles them as before
- No turns table records needed for single-turn tests
- EvaluationResults for single-turn tests:
  - `mcicaps_testcaseturnid` = null
  - `mcicaps_isaggregate` = false
  - `mcicaps_conversationid` = populated (single conversation)

### 12.2 Migration Path
**For users who want to convert existing single-turn tests to multi-turn:**

Manual process in Dataverse:
1. Update test case: Set `mcicaps_ismultiturn = true`
2. Create a turn record:
   - `mcicaps_testcaseid` = lookup to test case
   - `mcicaps_sequence` = 1
   - `mcicaps_prompttext` = copy from test case's prompt field
   - `mcicaps_expectedresponse` = copy from test case's expected response field
3. Add additional turns as needed

**Optional:** Provide PowerShell/API script for bulk migration (future enhancement)

---

## 13. Monitoring & Observability

### 13.1 Application Insights Metrics
**New custom metrics:**
- `MultiTurnSequence.Executed` - Counter
- `MultiTurnSequence.PassRate` - Gauge
- `MultiTurnSequence.TurnCount` - Distribution
- `MultiTurnSequence.Duration` - Timer

### 13.2 Log Correlation
Use `SequenceId` as correlation ID:
```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["SequenceId"] = sequenceId,
    ["TestRunId"] = testRunId
}))
{
    // All logs within scope include these properties
}
```

### 13.3 Dashboard Additions
**Monitor Function payload enhancements:**
- Total multi-turn test cases executed
- Average turns per multi-turn test case
- Multi-turn test case pass rate (aggregate results)
- Individual turn pass rate (across all multi-turn tests)
- Comparison: multi-turn vs single-turn success rates
- Conversation context success (did conversationId propagate correctly?)

---

## 14. Open Questions & Decisions

### 14.1 Conversation Timeout
**Question:** How long should we wait between turns?
**Recommendation:** Add configurable delay (default: 2 seconds) to simulate realistic user behavior

### 14.2 Maximum Turns
**Question:** Should we limit max turns per test case?
**Recommendation:** Set max to 20 turns (configurable) to prevent runaway test cases

### 14.3 Conversation Cleanup
**Question:** Should we explicitly close Direct Line conversations?
**Recommendation:** Direct Line conversations timeout automatically (24 hours), no explicit cleanup needed

### 14.4 Parallel Test Case Execution
**Question:** Should multi-turn test cases execute in parallel with each other?
**Recommendation:** Yes - each multi-turn test case uses its own conversation, so parallel execution is safe and faster

### 14.5 Expected Response per Turn
**Question:** Do all turns need expected responses?
**Recommendation:** Yes - each turn should have expected response for independent evaluation. Make `mcicaps_expectedresponse` required on turns table.

### 14.6 Single-Turn Test Case Fields
**Question:** Should single-turn test cases still have `mcicaps_prompttext` and `mcicaps_expectedresponse` fields?
**Recommendation:** Yes - keep existing fields for backward compatibility. For multi-turn tests, these fields can be ignored or populated with summary data.

### 14.7 ConversationId Retrieval Strategy
**Question:** How to get conversationId for turn 2+ if turn 1 result hasn't been written yet?
**Recommendation:** Pass conversationId through the execution context in-memory, and also persist to each result for audit trail

---

## 15. Success Criteria

### 15.1 Functional Requirements
- ✅ Multi-turn test cases execute with maintained conversation context
- ✅ Individual evaluation results created for each turn
- ✅ Aggregate evaluation results created for sequences
- ✅ Execution continues despite turn failures
- ✅ Existing single-turn tests unaffected

### 15.2 Non-Functional Requirements
- ✅ No additional Azure infrastructure required
- ✅ Performance: Multi-turn sequences complete within 2x single-turn duration
- ✅ Reliability: 99% success rate for sequence orchestration
- ✅ Observability: All sequence executions logged and traceable

---

## 16. References

### 16.1 Microsoft Documentation
- [Copilot Studio Direct Line API](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-connect-bot-to-custom-application)
- [Direct Line Conversation Management](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-connect-bot-to-azure-bot-service-channels#manage-conversation-sessions-with-your-copilot-studio-agent)
- [Bot Framework Direct Line API](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-concepts)

### 16.2 Related Documents
- [Story 01: Orchestrator Function](01-orchestrator-function.md)
- [Story 02: Worker Function](02-worker-function.md)
- [Story 03: Monitor Function](03-monitor-function.md)
- [PRD: Agent Evaluation Function](../prd/PRD-Agent-Evaluation-Function.md)

---

## 17. Summary of Schema Changes ✅ COMPLETED

### Tables Created/Modified:

1. **`mcicaps_testcases`** ✅ (existing table - added 1 field):
   - Added: `mcicaps_ismultiturn` (Boolean, default false)

2. **`mcicaps_testcaseturn`** ✅ (NEW table created):
   - `mcicaps_name` (Text, primary name field, auto-numbered: "Turn-{SEQNUM:5}")
   - `mcicaps_testcaseid` (Lookup to mcicaps_testcase, required, cascade delete)
   - `mcicaps_sequence` (Whole Number, 1-100, required) **← Use this for ordering, not name!**
   - `mcicaps_prompttext` (Multiple Lines, 10000 chars, required)
   - `mcicaps_expectedresponse` (Multiple Lines, 10000 chars, optional)

3. **`mcicaps_evaluationresults`** ✅ (existing table - added 3 fields):
   - Added: `mcicaps_testcaseturnid` (Lookup to mcicaps_testcaseturn, optional)
   - Added: `mcicaps_conversationid` (Text, 200 chars, optional)
   - Added: `mcicaps_isaggregate` (Boolean, default false, required)

### Relationships Established:
- `mcicaps_testcase` (1) → (N) `mcicaps_testcaseturn` (parent-child, cascade delete)
- `mcicaps_testcaseturn` (1) → (N) `mcicaps_evaluationresults` (lookup, no cascade)
- `mcicaps_testcase` (1) → (N) `mcicaps_evaluationresults` (existing relationship)

### Important Notes for Developers:
- **Sorting:** Always sort turns by `mcicaps_sequence` field, NOT by `mcicaps_name`
- **Auto-numbering:** The `mcicaps_name` field uses global sequence (Turn-00001, Turn-00002, etc.) which is NOT related to the turn order within a test case
- **Cascade Delete:** When a test case is deleted, all its turns are automatically deleted
- **Schema Complete:** All Dataverse schema changes have been completed and are ready for code implementation

---

## 18. Implementation Status & Next Steps

### Completed ✅
1. ~~Create Dataverse schema changes~~ - **DONE**
   - Added `mcicaps_ismultiturn` field to `mcicaps_testcases`
   - Created new `mcicaps_testcaseturn` table with all fields
   - Added `mcicaps_testcaseturnid`, `mcicaps_conversationid`, and `mcicaps_isaggregate` fields to `mcicaps_evaluationresults`
   - Established all required relationships

### Ready for Implementation 🔨
2. **Phase 1:** Implement Data Models (C# classes)
   - Create `TestCaseTurn.cs` model
   - Update `TestCase.cs` model with `IsMultiTurn` and `Turns` properties
   - Update `EvaluationResult.cs` model with new fields
   - Create `MultiTurnExecutionContext.cs` model

3. **Phase 2:** Implement Service Layer
   - Update `DataverseService.cs` with turn-related methods
   - Create `MultiTurnExecutionService.cs`
   - Add dependency injection registrations

4. **Phase 3:** Implement Functions
   - Create `MultiTurnWorkerFunction.cs`
   - Update `OrchestratorFunction.cs` to handle multi-turn test cases
   - Update `MonitorFunction.cs` for multi-turn metrics

5. **Phase 4:** Testing & Validation
   - Unit tests for new services and models
   - Integration tests for end-to-end multi-turn execution
   - Manual testing with sample multi-turn test cases

---

**Document Version:** 2.1  
**Created:** November 9, 2025  
**Last Updated:** November 9, 2025  
**Status:** Dataverse Schema Complete ✅ | Code Implementation Ready 🔨  
**Major Changes:**
- v2.0: Simplified structure - test cases as root, turns as child entity, conversationId in results
- v2.1: Dataverse schema changes completed, ready for code implementation
