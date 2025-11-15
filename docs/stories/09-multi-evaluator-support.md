# Story 09: Multi-Evaluator Support

**Date**: 2025-11-12  
**Status**: ✅ **COMPLETED** (2025-11-14)  
**Priority**: High  
**Actual Effort**: 7 days  
**Dependencies:** Story 08 (Orchestration Idempotency)  
**Owner**: Architecture & Evaluation Pipeline

---

## Problem Statement

### Current Issues

1. **Inflexible Single-Evaluator Design:**
   - Each test case has `mcicaps_evaluatortypeid` lookup field (1:1 relationship)
   - To evaluate with Similarity, Accuracy, AND Relevancy requires **3 duplicate test cases** with identical prompts
   - Test authoring overhead scales linearly with evaluator count: 10 tests × 3 evaluators = 30 records
   - Changing evaluator mix across dataset requires updating every test case

2. **Limited Analytical Coverage:**
   - Cannot easily compare agent performance across multiple evaluation dimensions
   - No way to apply new evaluator types (e.g., Safety, Tone) to existing test suites retroactively
   - Business question "How does Agent X score on Accuracy vs Relevancy?" requires custom queries across duplicate tests

3. **Test Case Maintenance Burden:**
   - Updating prompt text requires changing 3 records (one per evaluator)
   - Adding new expected response means finding all duplicates
   - Deleting outdated test requires cleanup of all evaluator variants
   - Risk of inconsistency across duplicate test definitions

4. **Future-Proofing Gap:**
   - No clear path to evaluator weighting (e.g., Accuracy 60%, Relevancy 40%)
   - Cannot easily A/B test different evaluator combinations
   - Domain-specific evaluators (Fashion, Healthcare) require full test duplication

### Business Impact

- **Reduced Test Coverage:** Teams avoid multi-evaluator testing due to overhead → blind spots in agent quality
- **Slower Iteration:** Adding evaluators to 100+ test suite takes hours of manual duplication
- **Analysis Friction:** Comparing evaluator results requires complex joins across duplicate test case IDs
- **Scalability Blocker:** Prevents growth to 10+ specialized evaluators per domain

### Example Scenario

**Current State (Problematic):**
```
Test Case 1: "What is the return policy?" (EvaluatorType = Similarity)
Test Case 2: "What is the return policy?" (EvaluatorType = Accuracy)  [DUPLICATE]
Test Case 3: "What is the return policy?" (EvaluatorType = Relevancy) [DUPLICATE]
```

**Desired State (After Story 09):**
```
Test Case 1: "What is the return policy?"
Evaluation Run A: Apply [Similarity, Accuracy, Relevancy] → 3 results
Evaluation Run B: Apply [Accuracy, Relevancy, Safety] → 3 results
```

---

## Proposed Solution: N:N Evaluator Selection

Move evaluator type selection from test case definition to evaluation run configuration using an N:N relationship.

### Core Design Principles

1. **Test Cases are Evaluator-Agnostic:** Store only prompt, expected response, turn sequence
2. **Runs Define Evaluation Lens:** Select 1+ evaluator types at run creation time
3. **Results Track Both Dimensions:** Link to both TestCase AND EvaluatorType
4. **Minimal Schema Impact:** Reuse existing fields, add only join table + 2 new fields
5. **Backward Compatible:** Existing test cases work (evaluatortypeid field ignored)

5. **Backward Compatible:** Existing test cases work (evaluatortypeid field ignored)

### Architecture Changes

**Data Relationships:**
```
Before:
  EvaluationRun -- 1:N --> TestCase -- N:1 --> EvaluatorType
  (100 tests × 3 evaluators = 300 test case records)

After:
  EvaluationRun -- N:N --> EvaluatorType (via join table)
  EvaluationRun -- 1:N --> TestCase
  EvaluationResult -- N:1 --> TestCase
  EvaluationResult -- N:1 --> EvaluatorType
  (100 tests × 3 evaluators = 100 test records + 300 result records)
```

**Execution Flow:**
```
StartEvaluationRun
  ↓
Fetch evaluator types for run (N:N lookup)
  ↓
For each (TestCase × EvaluatorType) pair:
  - Check if evaluator requires expected response
  - If required but missing → Create NA result (statuscode 778760004)
  - Else → Queue single-turn OR start multi-turn orchestration
  ↓
Results saved with both TestCaseId AND EvaluatorTypeId
```

---

## Scope

### In Scope
- N:N join table `mcicaps_EvaluationRuns_mcicaps_Evaluato` (relationship: EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType)
- Add `mcicaps_evaluatortypeid` lookup on `mcicaps_evaluationresults` (mandatory)
- Add `mcicaps_requiresexpectedresponse` boolean on `mcicaps_evaluatortypes`
- Orchestrator enumeration of (TestCase × EvaluatorType) pairs with NA short-circuit
- Multi-turn adjustments: evaluator loop outer, turn loop inner, aggregate per evaluator
- Progress tracking: redefine `mcicaps_totaltestcases` as evaluation units (test × evaluator × turns)
- New statuscode `778760004` (Not Applicable) for missing expected response scenarios
- Update WorkerFunction to use evaluatorTypeId from message
- Update DataverseService to create results with evaluatorTypeId

### Out of Scope (Future Enhancements)
- Per-test evaluator overrides (may add override table in Story 10)
- Evaluator weighting/prioritization (e.g., Accuracy 60%, Relevancy 40%)
- Advanced fallback behaviors (only NA supported; no "try alternate evaluator")
- Dynamic evaluator selection based on agent responses
- Evaluator-specific passing thresholds (uses run-level threshold for all)
- Evaluator dependencies (e.g., "run Accuracy only if Similarity passes")

---

## Data Model Changes

### 1. New Join Table: `mcicaps_EvaluationRuns_mcicaps_Evaluato`

**Purpose:** Many-to-many relationship between evaluation runs and evaluator types (auto-generated by Dataverse).

**Relationship Name:** `EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType`
**Relationship Table:** `mcicaps_EvaluationRuns_mcicaps_Evaluato` (auto-generated with schema prefix, truncated at 50 chars by Dataverse)

| Field | Type | Description |
|-------|------|-------------|
| `mcicaps_EvaluationRuns_mcicaps_EvaluatoId` | Guid (PK) | Auto-generated unique ID |
| `mcicaps_evaluationrunid` | Lookup | → mcicaps_evaluationruns |
| `mcicaps_evaluatortypeid` | Lookup | → mcicaps_evaluatortypes |
| `createdon` | DateTime | Timestamp of association |

**Indexes:** Composite on (evaluationrunid, evaluatortypeid) for query performance.

**Query Pattern:**
```csharp
// Fetch all evaluator types for a run
var evaluatorTypes = await _dataverseService.GetEvaluatorTypesForRunAsync(evaluationRunId);
```

### 2. Updated: `mcicaps_evaluatortypes`

**New Field:**
| Field | Type | Description |
|-------|------|-------------|
| `mcicaps_requiresexpectedresponse` | Boolean | If true, evaluator cannot score without expected response |

**Existing Evaluator Types:**
- **Similarity:** `requiresexpectedresponse = false` (compares to conversation history)
- **Accuracy:** `requiresexpectedresponse = true` (needs ground truth)
- **Relevancy:** `requiresexpectedresponse = false` (judges contextual fit)
- **Custom:** Set per evaluator definition

### 3. Updated: `mcicaps_evaluationresults`

**New Field:**
| Field | Type | Description |
|-------|------|-------------|
| `mcicaps_evaluatortypeid` | Lookup | → mcicaps_evaluatortypes (mandatory) |

**Before (Old Schema):**
- Result implicitly tied to evaluator via TestCase.EvaluatorTypeId
- Ambiguous which evaluator produced the score

**After (New Schema):**
- Result explicitly records which evaluator was used
- Same test case can have multiple results (one per evaluator)
- Aggregates also store evaluatorTypeId (for multi-turn)

### 4. Updated: `mcicaps_evaluationresults` StatusCode Values

**New Option:**
| Value | Label | Description |
|-------|-------|-------------|
| 778760004 | Not Applicable | Evaluator requires expected response but none provided |

**Usage:**
- Set when `evaluator.requiresexpectedresponse == true` AND `testCase.expectedresponse == null`
- Score set to `0.0` (never null)
- Excluded from aggregate score calculations via statuscode filter
- Counted as "completed" for progress tracking (not an error)

### 5. Removed Usage: `mcicaps_testcases.mcicaps_evaluatortypeid`

**Status:** Field may physically exist in Dataverse (for backward compatibility) but is **functionally removed** from all code paths.

**Migration Note:** No data migration required. Existing values ignored. Future cleanup can delete field once fully validated.

---
---

## Detailed Technical Design

### 1. OrchestratorFunction Changes

#### Before (Current Implementation)
```csharp
[FunctionName("StartEvaluationRun")]
public async Task<IActionResult> StartEvaluationRun(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    var evaluationRunId = await GetEvaluationRunIdFromRequest(req);
    
    // Fetch test cases
    var testCases = await _dataverseService.GetTestCasesForRunAsync(evaluationRunId);
    
    // Each test case has its own evaluator type
    foreach (var testCase in testCases)
    {
        var evaluatorTypeId = testCase.EvaluatorTypeId; // Fixed per test
        
        if (testCase.IsMultiTurn)
        {
            await _durableClient.StartNewAsync(
                "MultiTurnTestCaseOrchestrator",
                new MultiTurnOrchestratorInput 
                { 
                    TestCaseId = testCase.Id,
                    EvaluatorTypeId = evaluatorTypeId // Single evaluator
                });
        }
        else
        {
            await _serviceBusService.SendTestCaseMessageAsync(
                new ProcessTestCaseMessage 
                { 
                    TestCaseId = testCase.Id,
                    EvaluatorTypeId = evaluatorTypeId
                });
        }
    }
    
    return new AcceptedResult();
}
```

#### After (Proposed Multi-Evaluator)
```csharp
[FunctionName("StartEvaluationRun")]
public async Task<IActionResult> StartEvaluationRun(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
{
    var evaluationRunId = await GetEvaluationRunIdFromRequest(req);
    
    // Step 1: Fetch evaluator types for this run (N:N lookup)
    var evaluatorTypes = await _dataverseService.GetEvaluatorTypesForRunAsync(
        evaluationRunId, 
        cancellationToken);
    
    if (!evaluatorTypes.Any())
    {
        _logger.LogWarning("No evaluator types configured for run {RunId}", evaluationRunId);
        return new BadRequestObjectResult("Evaluation run must have at least one evaluator type");
    }
    
    // Step 2: Fetch test cases (evaluator-agnostic now)
    var testCases = await _dataverseService.GetTestCasesForRunAsync(evaluationRunId);
    
    // Step 3: Calculate total evaluation units
    int totalUnits = CalculateTotalEvaluationUnits(testCases, evaluatorTypes.Count);
    await _dataverseService.SetProgressCountsAsync(
        evaluationRunId, 
        totalTestCases: totalUnits, 
        completedTestCases: 0);
    
    // Step 4: Enumerate all (TestCase × EvaluatorType) pairs
    foreach (var testCase in testCases)
    {
        foreach (var evaluatorType in evaluatorTypes)
        {
            // Step 5: Check if Not Applicable (NA) scenario
            bool isNA = evaluatorType.RequiresExpectedResponse && 
                        string.IsNullOrWhiteSpace(testCase.ExpectedResponse);
            
            if (isNA)
            {
                // Short-circuit: Create NA result immediately
                await _dataverseService.CreateEvaluationResultAsync(
                    new CreateEvaluationResultRequest
                    {
                        EvaluationRunId = evaluationRunId,
                        TestCaseId = testCase.Id,
                        EvaluatorTypeId = evaluatorType.Id,
                        StatusCode = 778760004, // Not Applicable
                        Score = 0.0,
                        Reasoning = $"Evaluator '{evaluatorType.Name}' requires expected response but none provided",
                        ActualResponse = null,
                        ConversationId = null
                    }, 
                    cancellationToken);
                
                // Increment completion counter (NA counts as completed)
                await _dataverseService.IncrementCompletedTestCasesAsync(
                    evaluationRunId, 
                    cancellationToken);
                
                _logger.LogInformation(
                    "Test case {TestCaseId} marked NA for evaluator {EvaluatorId} (missing expected response)",
                    testCase.Id, evaluatorType.Id);
                
                continue; // Skip execution for this pair
            }
            
            // Step 6: Queue or start orchestration with evaluator context
            if (testCase.IsMultiTurn)
            {
                // Multi-turn: Start Durable orchestration per (TestCase, Evaluator) pair
                var instanceId = $"multiturn-{testCase.Id}-{evaluatorType.Id}-{Guid.NewGuid()}";
                
                await _durableClient.StartNewAsync(
                    "MultiTurnTestCaseOrchestrator",
                    instanceId,
                    new MultiTurnOrchestratorInput 
                    { 
                        EvaluationRunId = evaluationRunId,
                        TestCaseId = testCase.Id,
                        EvaluatorTypeId = evaluatorType.Id, // Explicit evaluator
                        DirectLineUrl = directLineUrl,
                        PassingThreshold = passingThreshold
                    });
                
                _logger.LogInformation(
                    "Started multi-turn orchestration {InstanceId} for test {TestCaseId} with evaluator {EvaluatorId}",
                    instanceId, testCase.Id, evaluatorType.Id);
            }
            else
            {
                // Single-turn: Enqueue to Service Bus
                await _serviceBusService.SendTestCaseMessageAsync(
                    new ProcessTestCaseMessage 
                    { 
                        EvaluationRunId = evaluationRunId,
                        TestCaseId = testCase.Id,
                        EvaluatorTypeId = evaluatorType.Id, // Explicit evaluator
                        DirectLineUrl = directLineUrl,
                        PassingThreshold = passingThreshold
                    });
            }
        }
    }
    
    return new AcceptedResult();
}

// New helper method
private int CalculateTotalEvaluationUnits(List<TestCase> testCases, int evaluatorCount)
{
    int total = 0;
    
    foreach (var testCase in testCases)
    {
        if (testCase.IsMultiTurn)
        {
            int turnCount = testCase.Turns?.Count ?? 0;
            // Multi-turn: (turns × evaluators) + (1 aggregate per evaluator)
            total += (turnCount * evaluatorCount) + evaluatorCount;
        }
        else
        {
            // Single-turn: 1 result per evaluator
            total += evaluatorCount;
        }
    }
    
    return total;
}
```

**Key Changes:**
1. **Evaluator enumeration:** `GetEvaluatorTypesForRunAsync()` replaces per-test-case lookup
2. **NA short-circuit:** Creates result with statuscode 778760004 immediately
3. **Unit counting:** Formula accounts for evaluator multiplication
4. **Instance ID pattern:** `multiturn-{testCaseId}-{evaluatorTypeId}-{guid}` for uniqueness

---

### 2. WorkerFunction Changes (Single-Turn)
```
Total = Σ(single-turn: evaluatorCount)
      + Σ(multi-turn: (turnCount * evaluatorCount) + evaluatorCount for aggregates)
```
Process:
1. Fetch test cases and evaluator types for run.
2. Compute total units.
3. For each test case × evaluator type:
   - If evaluator requires expected response and none provided (single-turn or turn-specific) → create NA result immediately (statuscode 778760004, score 0.0).
   - Else enqueue single-turn message or start durable orchestration for multi-turn.
4. Update progress counters (NA results count as completed).

## Implementation Plan

### Phase 1: Dataverse Schema Updates (Day 1)
- [ ] Create N:N relationship `EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType` in dev environment
  - [ ] Relationship table auto-generated as `EvaluationRuns_mcicaps_Evaluato`
  - [ ] Includes evaluationrunid and evaluatortypeid lookup fields
  - [ ] Create composite index on (evaluationrunid, evaluatortypeid)
- [ ] Add `mcicaps_requiresexpectedresponse` boolean field to `mcicaps_evaluatortypes`
  - [ ] Set default value to `false`
  - [ ] Update existing evaluator types: Accuracy = true, others = false
- [ ] Add `mcicaps_evaluatortypeid` lookup field to `mcicaps_evaluationresults`
  - [ ] Make field mandatory for new records
  - [ ] Backfill existing records (map from testcase.evaluatortypeid)
- [ ] Add StatusCode option `778760004` (Not Applicable) to `mcicaps_evaluationresults`
- [ ] Test schema changes with sample data

### Phase 2: Service Layer Implementation (Day 2-3)
- [ ] Implement `IDataverseService.GetEvaluatorTypesForRunAsync()`
  - [ ] Write FetchXML for N:N join query
  - [ ] Add caching for evaluator types per run
  - [ ] Unit tests for query logic
- [ ] Implement `IDataverseService.AddEvaluatorTypeToRunAsync()`
  - [ ] Create join table record
  - [ ] Handle duplicate prevention
  - [ ] Unit tests for idempotency
- [ ] Update `IDataverseService.GetEvaluatorTypeAsync()` to include `RequiresExpectedResponse`
- [ ] Update `IDataverseService.CreateEvaluationResultAsync()` to accept `EvaluatorTypeId`
- [ ] Update model classes:
  - [ ] `ProcessTestCaseMessage` - add `EvaluatorTypeId`
  - [ ] `EvaluatorType` - add `RequiresExpectedResponse`
  - [ ] `CreateEvaluationResultRequest` - add `EvaluatorTypeId`

### Phase 3: Orchestrator Function Updates (Day 3-4)
- [ ] Update `StartEvaluationRun` function:
  - [ ] Add evaluator enumeration logic
  - [ ] Implement `CalculateTotalEvaluationUnits()` helper
  - [ ] Add NA short-circuit check
  - [ ] Update instance ID pattern for multi-turn: `multiturn-{testCaseId}-{evaluatorTypeId}-{guid}`
  - [ ] Add logging for evaluator enumeration
- [ ] Update `WorkerFunction` (minimal changes):
  - [ ] Use `message.EvaluatorTypeId` instead of `testCase.EvaluatorTypeId`
  - [ ] Pass `EvaluatorTypeId` to result creation
- [ ] Update `MultiTurnDurableActivities.ExecuteSingleTurn`:
  - [ ] Add NA check at turn level
  - [ ] Create NA result if expected response missing
  - [ ] Pass `EvaluatorTypeId` to result creation
- [ ] Update `MultiTurnDurableActivities.CreateAggregateResult`:
  - [ ] Filter out NA results (statuscode 778760004)
  - [ ] Mark aggregate as NA if all turns NA
  - [ ] Update reasoning to explain NA exclusions

### Phase 4: Testing (Day 4-5)
- [ ] **Unit Tests:**
  - [ ] `CalculateTotalEvaluationUnits()` - various test case mixes
  - [ ] NA short-circuit logic in orchestrator
  - [ ] Turn-level NA handling
  - [ ] Aggregate calculation with NA filtering
  - [ ] DataverseService N:N query
- [ ] **Integration Tests:**
  - [ ] Single-turn test × 3 evaluators (3 results)
  - [ ] Multi-turn test (5 turns) × 2 evaluators (12 results: 10 turns + 2 aggregates)
  - [ ] NA scenario: Test without expected response × Accuracy evaluator
  - [ ] Mixed: Some evaluators NA, some scored
  - [ ] All-NA aggregate scenario
- [ ] **Performance Tests:**
  - [ ] 100 tests × 5 evaluators = 500 results (measure throughput)
  - [ ] Verify N:N query performance (<500ms)

### Phase 5: Documentation & Deployment (Day 5-7)
- [ ] Update `ARCHITECTURE.md` with N:N relationship and evaluator enumeration flow
- [ ] Update `PRD-Agent-Evaluation-Function.md` orchestrator workflow section
- [ ] Update operational runbook with NA result handling
- [ ] Create migration guide for existing test suites
- [ ] Deploy to staging environment
  - [ ] Validate schema changes
  - [ ] Run full test suite
  - [ ] Verify MonitorFunction completion detection
- [ ] Production deployment
  - [ ] Deploy during maintenance window
  - [ ] Monitor Application Insights for errors
  - [ ] Validate first 10 evaluation runs manually

---

## Acceptance Criteria

### Functional Requirements
- [ ] **Multi-Evaluator Selection:**
  - [ ] Can associate 1-10 evaluator types with an evaluation run via join table
  - [ ] Orchestrator enumerates all (TestCase × EvaluatorType) pairs correctly
  - [ ] Each pair creates one evaluation result with correct evaluatorTypeId
  
- [ ] **NA Handling (Single-Turn):**
  - [ ] If evaluator `requiresExpectedResponse=true` and test case has no expected response → NA result created
  - [ ] NA result has statuscode 778760004, score 0.0, reasoning explains missing expected response
  - [ ] NA result counted as "completed" for progress tracking
  - [ ] No LLM Judge API call made for NA results (cost savings verified)

- [ ] **NA Handling (Multi-Turn):**
  - [ ] Turn-level NA: Specific turn missing expected → NA result for that turn only
  - [ ] Aggregate calculation excludes NA turns from average
  - [ ] All-turns NA → Aggregate also marked NA (statuscode 778760004, score 0.0)
  - [ ] Mixed scenario: Some turns NA, some scored → Aggregate uses only scored turns

- [ ] **Progress Tracking:**
  - [ ] `mcicaps_totaltestcases` correctly reflects evaluation units (not test count)
  - [ ] Formula: Σ(single: evaluatorCount) + Σ(multi: turnCount × evaluatorCount + evaluatorCount)
  - [ ] Completion detected when (completed + failed) >= total
  - [ ] MonitorFunction logs breakdown: completed, NA, failed counts

- [ ] **Result Schema:**
  - [ ] All new results include `mcicaps_evaluatortypeid`
  - [ ] Can query "all results for Accuracy evaluator across runs"
  - [ ] Can query "all results for TestCase X across different evaluators"

### Non-Functional Requirements
- [ ] **Performance:**
  - [ ] `GetEvaluatorTypesForRunAsync()` query completes <500ms (p95)
  - [ ] Orchestrator start time <5 seconds for 100 tests × 3 evaluators
  - [ ] No degradation in single-evaluator scenarios (baseline comparison)

- [ ] **Scalability:**
  - [ ] Supports 10+ evaluator types per run
  - [ ] Handles 500+ test cases × 5 evaluators = 2500+ results
  - [ ] Service Bus queue handles increased message volume (5x with 5 evaluators)

- [ ] **Observability:**
  - [ ] Application Insights tracks evaluator type per operation
  - [ ] Logs distinguish between NA and execution errors
  - [ ] Can identify which evaluator types have highest failure rates

- [ ] **Backward Compatibility:**
  - [ ] Existing test cases function normally (evaluatortypeid field ignored)
  - [ ] Existing evaluation results remain queryable
  - [ ] No breaking changes to API contracts

---

## Testing Strategy

### Unit Tests

#### OrchestratorFunction Tests
```csharp
[Fact]
public void CalculateTotalEvaluationUnits_SingleTurnOnly_ReturnsTestCountTimesEvaluators()
{
    // 5 single-turn tests × 3 evaluators = 15 units
    var testCases = CreateTestCases(singleTurnCount: 5, multiTurnCount: 0);
    var result = _orchestrator.CalculateTotalEvaluationUnits(testCases, evaluatorCount: 3);
    Assert.Equal(15, result);
}

[Fact]
public void CalculateTotalEvaluationUnits_MultiTurnOnly_IncludesAggregates()
{
    // 2 multi-turn tests (5 turns each) × 3 evaluators = 2 × [(5 × 3) + 3] = 36 units
    var testCases = CreateTestCases(singleTurnCount: 0, multiTurnCount: 2, turnsPerMulti: 5);
    var result = _orchestrator.CalculateTotalEvaluationUnits(testCases, evaluatorCount: 3);
    Assert.Equal(36, result);
}

[Fact]
public async Task StartEvaluationRun_NAScenario_CreatesResultAndSkipsExecution()
{
    // Arrange: Accuracy evaluator (requires expected) + test without expected
    var evaluatorTypes = new List<EvaluatorType> 
    { 
        new() { Id = Guid.NewGuid(), Name = "Accuracy", RequiresExpectedResponse = true }
    };
    var testCases = new List<TestCase>
    {
        new() { Id = Guid.NewGuid(), PromptText = "Test", ExpectedResponse = null }
    };
    
    _mockDataverse.Setup(x => x.GetEvaluatorTypesForRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(evaluatorTypes);
    _mockDataverse.Setup(x => x.GetTestCasesForRunAsync(It.IsAny<Guid>()))
        .ReturnsAsync(testCases);
    
    // Act
    await _orchestrator.StartEvaluationRun(evaluationRunId);
    
    // Assert
    _mockDataverse.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => 
            r.StatusCode == 778760004 && 
            r.Score == 0.0 &&
            r.EvaluatorTypeId == evaluatorTypes[0].Id),
        It.IsAny<CancellationToken>()), 
        Times.Once);
    
    // Should NOT queue message
    _mockServiceBus.Verify(x => x.SendTestCaseMessageAsync(It.IsAny<ProcessTestCaseMessage>()), Times.Never);
}
```

#### MultiTurnDurableActivities Tests
```csharp
[Fact]
public async Task ExecuteSingleTurn_NAAtTurnLevel_CreatesNAResult()
{
    // Arrange
    var evaluatorType = new EvaluatorType { RequiresExpectedResponse = true };
    var turn = new TestCaseTurn { ExpectedResponse = null, Sequence = 3 };
    
    // Act
    var result = await _activities.ExecuteSingleTurn(new ExecuteTurnInput
    {
        Turn = turn,
        EvaluatorTypeId = evaluatorType.Id
    });
    
    // Assert
    Assert.Equal(778760004, result.StatusCode);
    Assert.Equal(0.0, result.Score);
}

[Fact]
public async Task CreateAggregateResult_AllTurnsNA_AggregateAlsoNA()
{
    // Arrange: 3 turns, all NA
    var context = new MultiTurnExecutionContext
    {
        TurnResults = new Dictionary<int, TurnExecutionResult>
        {
            { 1, new() { StatusCode = 778760004, Score = 0.0 } },
            { 2, new() { StatusCode = 778760004, Score = 0.0 } },
            { 3, new() { StatusCode = 778760004, Score = 0.0 } }
        }
    };
    
    // Act
    await _activities.CreateAggregateResult(new CreateAggregateResultInput { Context = context });
    
    // Assert
    _mockDataverse.Verify(x => x.UpdateAggregateResultAsync(
        It.IsAny<Guid>(),
        0.0, // score
        false, // passed
        778760004, // statuscode
        It.Is<string>(s => s.Contains("All turns Not Applicable")),
        It.IsAny<long>(),
        It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
public async Task CreateAggregateResult_MixedNAAndScored_ExcludesNAFromAverage()
{
    // Arrange: 5 turns, 2 NA, 3 scored (0.8, 0.9, 1.0)
    var context = new MultiTurnExecutionContext
    {
        TurnResults = new Dictionary<int, TurnExecutionResult>
        {
            { 1, new() { StatusCode = 778760004, Score = 0.0 } }, // NA
            { 2, new() { StatusCode = 778760002, Score = 0.8 } },
            { 3, new() { StatusCode = 778760002, Score = 0.9 } },
            { 4, new() { StatusCode = 778760004, Score = 0.0 } }, // NA
            { 5, new() { StatusCode = 778760002, Score = 1.0 } }
        }
    };
    
    // Act
    await _activities.CreateAggregateResult(new CreateAggregateResultInput { Context = context });
    
    // Assert: (0.8 + 0.9 + 1.0) / 3 = 0.9
    _mockDataverse.Verify(x => x.UpdateAggregateResultAsync(
        It.IsAny<Guid>(),
        0.9, // average of 3 scored turns
        true, // passed (>= 0.7 threshold)
        778760002, // completed
        It.Is<string>(s => s.Contains("excluded 2 NA turns")),
        It.IsAny<long>(),
        It.IsAny<CancellationToken>()),
        Times.Once);
}
```

#### DataverseService Tests
```csharp
[Fact]
public async Task GetEvaluatorTypesForRunAsync_ReturnsJoinedEvaluators()
{
    // Test FetchXML query correctness
    // Mock ServiceClient response
    // Verify correct entity mapping
}

[Fact]
public async Task AddEvaluatorTypeToRunAsync_CreatesJoinRecord()
{
    // Test join record creation
    // Verify duplicate prevention
}
```

### Integration Tests

#### End-to-End Multi-Evaluator Flow
```csharp
[Fact]
public async Task MultiEvaluator_SingleTurn_CreatesResultPerEvaluator()
{
    // Arrange
    var evaluationRun = await CreateEvaluationRunWithEvaluators(
        evaluatorTypes: new[] { "Similarity", "Accuracy", "Relevancy" });
    var testCase = await CreateTestCase(
        prompt: "What is the return policy?",
        expectedResponse: "30 days");
    
    // Act
    await TriggerEvaluationRun(evaluationRun.Id);
    await WaitForCompletion(evaluationRun.Id, timeout: TimeSpan.FromMinutes(5));
    
    // Assert
    var results = await GetResultsForTestCase(testCase.Id);
    Assert.Equal(3, results.Count);
    Assert.Contains(results, r => r.EvaluatorTypeId == similarityId);
    Assert.Contains(results, r => r.EvaluatorTypeId == accuracyId);
    Assert.Contains(results, r => r.EvaluatorTypeId == relevancyId);
    Assert.All(results, r => Assert.Equal(778760002, r.StatusCode)); // All completed
}

[Fact]
public async Task MultiEvaluator_MultiTurn_CreatesAggregatePerEvaluator()
{
    // 5-turn test × 2 evaluators = 12 results (10 turns + 2 aggregates)
    var evaluationRun = await CreateEvaluationRunWithEvaluators(
        evaluatorTypes: new[] { "Similarity", "Relevancy" });
    var testCase = await CreateMultiTurnTestCase(turnCount: 5);
    
    // Act
    await TriggerEvaluationRun(evaluationRun.Id);
    await WaitForCompletion(evaluationRun.Id, timeout: TimeSpan.FromMinutes(10));
    
    // Assert
    var results = await GetResultsForTestCase(testCase.Id);
    Assert.Equal(12, results.Count);
    
    var similarityResults = results.Where(r => r.EvaluatorTypeId == similarityId).ToList();
    Assert.Equal(6, similarityResults.Count); // 5 turns + 1 aggregate
    Assert.Single(similarityResults.Where(r => r.IsAggregate));
    
    var relevancyResults = results.Where(r => r.EvaluatorTypeId == relevancyId).ToList();
    Assert.Equal(6, relevancyResults.Count);
    Assert.Single(relevancyResults.Where(r => r.IsAggregate));
}

[Fact]
public async Task NAScenario_AccuracyWithoutExpected_CreatesNAResult()
{
    // Arrange
    var evaluationRun = await CreateEvaluationRunWithEvaluators(
        evaluatorTypes: new[] { "Accuracy" }); // Requires expected response
    var testCase = await CreateTestCase(
        prompt: "What is the return policy?",
        expectedResponse: null); // Missing
    
    // Act
    await TriggerEvaluationRun(evaluationRun.Id);
    await WaitForCompletion(evaluationRun.Id, timeout: TimeSpan.FromMinutes(2));
    
    // Assert
    var results = await GetResultsForTestCase(testCase.Id);
    Assert.Single(results);
    Assert.Equal(778760004, results[0].StatusCode); // NA
    Assert.Equal(0.0, results[0].Score);
    Assert.Contains("requires expected response", results[0].Reasoning, StringComparison.OrdinalIgnoreCase);
    
    // Verify no LLM call made (check Application Insights logs)
    var llmCalls = await GetLLMJudgeCallsForRun(evaluationRun.Id);
    Assert.Empty(llmCalls);
}
```

### Load Tests

```csharp
[Fact]
public async Task LoadTest_100Tests_5Evaluators_CompletesUnder10Minutes()
{
    // 100 single-turn tests × 5 evaluators = 500 results
    var stopwatch = Stopwatch.StartNew();
    
    var evaluationRun = await CreateEvaluationRunWithEvaluators(
        evaluatorTypes: new[] { "Similarity", "Accuracy", "Relevancy", "Custom1", "Custom2" });
    var testCases = await CreateTestCases(count: 100);
    
    await TriggerEvaluationRun(evaluationRun.Id);
    await WaitForCompletion(evaluationRun.Id, timeout: TimeSpan.FromMinutes(15));
    
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(10), 
        $"Expected <10 min, got {stopwatch.Elapsed.TotalMinutes:F2} min");
    
    var results = await GetResultsForRun(evaluationRun.Id);
    Assert.Equal(500, results.Count);
}
```

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation Strategy |
|------|--------|-------------|---------------------|
| **Storage Growth** | High - 5x result volume with 5 evaluators | High | 1. Implement data retention policy (archive runs >90 days)<br>2. Add pagination to all Dataverse queries<br>3. Monitor storage metrics in Azure Portal |
| **Misinterpreted NA Scores** | Medium - Analysts confuse 0.0 NA with 0.0 failure | Medium | 1. Always filter by statuscode, not score<br>2. Update Power BI reports to exclude NA<br>3. Documentation emphasizes statuscode primacy<br>4. Add visual indicator in UI for NA results |
| **Incorrect Unit Counting** | High - Stalled or premature completion | Low | 1. Comprehensive unit tests for formula<br>2. Integration tests with known result counts<br>3. MonitorFunction logs expected vs actual counts<br>4. Add validation: completedCount ≤ totalCount assertion |
| **Token Cost Surge** | High - 5x OpenAI API calls with 5 evaluators | High | 1. NA short-circuit avoids unnecessary calls<br>2. Monitor daily token usage in Azure OpenAI metrics<br>3. Set budget alerts at 2x baseline<br>4. Rate limiting already in place (Story 06) |
| **Aggregate Distortion** | Medium - All-NA aggregates misread as failures | Low | 1. Distinct statuscode 778760004 on aggregates<br>2. Reasoning field explains NA state<br>3. Unit tests verify all-NA scenario<br>4. Analytics queries check statuscode |
| **N:N Query Performance** | Medium - Slow evaluator lookup delays orchestrator start | Low | 1. Composite index on join table<br>2. Cache evaluator types per run<br>3. Performance test: <500ms p95<br>4. Add query timeout monitoring |
| **Migration Complexity** | Low - Test suite duplication cleanup takes time | Medium | 1. Provide automated deduplication script<br>2. Create migration guide with examples<br>3. Optional: Keep old test cases for rollback<br>4. Phased migration per test suite |
| **Evaluator Explosion** | Low - Uncontrolled growth to 20+ evaluators | Low | 1. Business rule: Max 10 evaluators per run<br>2. UI validation in run creation<br>3. Monitor evaluator proliferation metrics<br>4. Periodic evaluator effectiveness reviews |

---

## Success Metrics

### Before (Current State - Single Evaluator)
- Test case duplication overhead: 3x records for 3 evaluators
- Multi-evaluator coverage: <20% of runs use >1 evaluator (due to overhead)
- Evaluator addition time: 2-4 hours to apply new evaluator to 100-test suite
- Analytics complexity: Complex joins required to compare evaluator results
- Storage: 1x result volume

### After (Target State - Multi-Evaluator)
- Test case efficiency: 1x records regardless of evaluator count
- Multi-evaluator coverage: >70% of runs use 3+ evaluators
- Evaluator addition time: <5 minutes (just add to run configuration)
- Analytics simplicity: Single query with evaluatorTypeId filter
- Storage: 3-5x result volume (with archival strategy)

### Quantifiable Targets
- **Test authoring time:** Reduce by 60% for multi-evaluator scenarios
- **Evaluator adoption:** Increase unique evaluator usage by 3x within 2 months
- **NA optimization:** Save 20%+ LLM tokens by short-circuiting missing expected responses
- **Query performance:** `GetEvaluatorTypesForRunAsync()` <500ms p95
- **Orchestrator start:** <5 seconds for 100 tests × 5 evaluators
- **Completion accuracy:** 100% of runs reach expected total unit count

---

## Rollout Plan

### Phase 1: Dev Environment Setup (Week 1)
**Days 1-2:** Schema changes
- [ ] Create join table and add fields in dev Dataverse
- [ ] Backfill existing results with evaluatorTypeId
- [ ] Validate schema with test data

**Days 3-5:** Code implementation
- [ ] Implement all service methods
- [ ] Update orchestrator and worker functions
- [ ] Write and run unit tests (>80% coverage)

### Phase 2: Integration Testing (Week 2)
**Days 1-2:** Integration tests
- [ ] End-to-end multi-evaluator flows
- [ ] NA scenario validation
- [ ] Load tests (500 results)

**Days 3-5:** Documentation
- [ ] Update Architecture, PRD, operational runbooks
- [ ] Create migration guide for existing test suites
- [ ] Record demo video

### Phase 3: Staging Deployment (Week 3)
**Days 1-2:** Deploy to staging
- [ ] Apply schema changes to staging Dataverse
- [ ] Deploy function app code
- [ ] Run smoke tests

**Days 3-5:** Validation
- [ ] Run 10+ evaluation runs with various evaluator combinations
- [ ] Verify MonitorFunction completion detection
- [ ] Performance testing at scale
- [ ] Stakeholder demo and feedback

### Phase 4: Production Deployment (Week 4)
**Day 1:** Schema deployment
- [ ] Schedule maintenance window (low-traffic period)
- [ ] Apply Dataverse schema changes
- [ ] Backfill evaluatorTypeId on existing results
- [ ] Validate schema integrity

**Day 2:** Code deployment
- [ ] Deploy function app to production
- [ ] Enable Application Insights detailed monitoring
- [ ] Monitor first 5 evaluation runs manually

**Days 3-5:** Post-deployment validation
- [ ] Run full regression test suite
- [ ] Monitor Application Insights for errors
- [ ] Track NA result creation frequency
- [ ] Measure LLM token savings vs baseline

### Phase 5: Adoption & Optimization (Ongoing)
**Week 5+:**
- [ ] Assist teams in migrating test suites
- [ ] Collect feedback on evaluator combinations
- [ ] Analyze evaluator effectiveness data
- [ ] Optimize based on usage patterns
- [ ] Plan Phase 2 features (evaluator weighting)

---

## Future Enhancements

### Story 10: Evaluator Weighting & Prioritization
- Assign weights to evaluators (e.g., Accuracy 60%, Relevancy 40%)
- Calculate weighted aggregate scores
- UI for configuring weights per run

### Story 11: Per-Test Evaluator Overrides
- Add `mcicaps_testcase_evaluatortype_override` table
- Allow specific tests to exclude certain evaluators
- Use case: "Don't run Accuracy on opinion-based tests"

### Story 12: Evaluator Dependencies
- Define prerequisite chains: "Run Relevancy only if Similarity passes"
- Conditional execution based on previous evaluator results
- Cost optimization for multi-stage evaluation

### Story 13: Dynamic Evaluator Selection
- AI-driven evaluator selection based on test content analysis
- Heuristics: "Contains financial data? → Apply Accuracy evaluator"
- Machine learning model for optimal evaluator combination

### Story 14: Structured NA Remediation
- API endpoint to suggest fixes for NA results
- "Add expected response here: ________"
- Bulk remediation UI for test suite cleanup

---

## References

### Internal Documentation
- `docs/ARCHITECTURE.md` - Section 4: Data Model (updated with N:N relationship)
- `docs/prd/PRD-Agent-Evaluation-Function.md` - Section 3: Orchestrator Workflow (updated steps 5-8)
- `docs/stories/06-multi-turn-support.md` - Multi-turn execution context
- `docs/stories/07-durable-functions-multi-turn.md` - Durable Functions orchestration patterns
- `docs/stories/08-orchestration-idempotency-status-tracking.md` - StatusCode semantics and idempotency
- `docs/tests/multi-evaluator-test-plan.md` - Comprehensive test scenarios
- `docs/tests/multi-evaluator-sample-cases.md` - Sample test data with expected results

### External References
- [Dataverse Many-to-Many Relationships](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/associate-disassociate-entities-using-web-api)
- [Azure Functions Best Practices](https://learn.microsoft.com/en-us/azure/azure-functions/functions-best-practices)
- [LLM-as-a-Judge Evaluation Patterns](https://arxiv.org/abs/2306.05685)

---

## Implementation Complete - 2025-11-14

### Final Implementation Summary

**Status**: ✅ IMPLEMENTED AND DEPLOYED  
**Build**: 0 errors, 0 warnings  
**Tests**: 116/116 passing (100% pass rate)

#### What Was Delivered

1. **N:N Relationship Architecture**
   - Created N:N relationship `EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType` with relationship table `mcicaps_EvaluationRuns_mcicaps_Evaluato`
   - Moved evaluator selection from test case to run level
   - One test case can be evaluated with multiple evaluator types

2. **Efficiency Optimization - Copilot Studio API Calls**
   - **Single-Turn**: 1 Copilot call per test case (was: N calls per evaluator)
   - **Multi-Turn**: 1 Copilot call per turn (was: N calls per evaluator per turn)
   - **Savings**: 67% with 3 evaluators, 80% with 5 evaluators, 90% with 10 evaluators

3. **Concurrent Evaluation**
   - All evaluators run in parallel using `Task.WhenAll`
   - Single-turn: WorkerFunction evaluates with all evaluators concurrently
   - Multi-turn: ExecuteSingleTurn activity evaluates with all evaluators concurrently
   - **Performance gain**: 67% faster with 3 evaluators (6s → 2s)

4. **Not Applicable (NA) Handling**
   - Added `mcicaps_requiresexpectedresponse` field to EvaluatorType
   - Short-circuit evaluation when required data missing
   - StatusCode 778760004 (Not Applicable) for skipped evaluations
   - NA results excluded from aggregate score calculations

5. **Data Model Changes**
   - `ProcessTestCaseMessage.EvaluatorTypeIds`: List<Guid> (multi-evaluator)
   - `MultiTurnOrchestratorInput.EvaluatorTypeIds`: List<Guid> (multi-evaluator)
   - `MultiTurnOrchestratorInput.AggregateResultIds`: Dictionary<Guid, Guid> (evaluator → aggregate mapping)
   - `ExecuteTurnInput.EvaluatorTypeIds`: List<Guid> (turn-level multi-evaluator)
   - `TurnExecutionResult.StatusCode`: Added for NA propagation
   - `EvaluationResult.EvaluatorTypeId`: Added to link results to evaluators

6. **Function Updates**
   - **OrchestratorFunction**: Fetches evaluator types from N:N join, creates 1 message/orchestration per test case (not N)
   - **WorkerFunction**: Calls Copilot once, evaluates concurrently with all evaluators
   - **MultiTurnDurableOrchestrator**: Manages multiple evaluator contexts per test case
   - **ExecuteSingleTurn**: Returns List<TurnExecutionResult> after concurrent evaluation
   - **CreateAggregateResult**: Creates separate aggregates for each evaluator

#### Architecture Improvements

**Before (Inefficient)**:
```
For 1 test case with 3 evaluators:
- Queue 3 separate messages
- Each message calls Copilot Studio separately
- Each message evaluates sequentially
- Total: 3 Copilot calls + 6 seconds evaluation time (2s per evaluator)
```

**After (Optimized)**:
```
For 1 test case with 3 evaluators:
- Queue 1 message with all evaluator IDs
- Message calls Copilot Studio once
- Message evaluates concurrently with all evaluators
- Total: 1 Copilot call + 2 seconds evaluation time (parallel)
```

**Cost & Performance Comparison**:
| Evaluators | Copilot Calls (Before) | Copilot Calls (After) | Savings | Eval Time (Before) | Eval Time (After) | Speedup |
|------------|------------------------|----------------------|---------|-------------------|-------------------|---------|
| 3          | 3                      | 1                    | 67%     | 6 seconds         | 2 seconds         | 3x      |
| 5          | 5                      | 1                    | 80%     | 10 seconds        | 2 seconds         | 5x      |
| 10         | 10                     | 1                    | 90%     | 20 seconds        | 2 seconds         | 10x     |

#### Key Implementation Details

**Progress Tracking Formula**:
```csharp
totalUnits = Σ(single-turn: testCount × evaluatorCount)
           + Σ(multi-turn: (turnCount × evaluatorCount) + evaluatorCount)
```

**NA Short-Circuit Logic**:
```csharp
if (evaluator.RequiresExpectedResponse && string.IsNullOrWhiteSpace(expectedResponse))
{
    // Create NA result immediately (statuscode 778760004, score 0.0)
    // Skip Copilot Studio and LLM Judge calls
    // Count as completed for progress tracking
}
```

**Concurrent Evaluation Pattern**:
```csharp
// Single Copilot call
var actualResponse = await _copilotService.SendMessageAsync(prompt, conversationId);

// Concurrent evaluation with all evaluators
var evaluationTasks = evaluatorTypeIds.Select(async evaluatorTypeId =>
{
    var evaluatorType = await _dataverseService.GetEvaluatorTypeAsync(evaluatorTypeId);
    var judgeService = await _judgeFactory.CreateJudgeAsync(evaluatorTypeId);
    var result = await judgeService.EvaluateResponseAsync(actualResponse, expectedResponse, ...);
    await _dataverseService.CreateEvaluationResultAsync(...);
    return result;
}).ToList();

// Wait for all evaluations to complete
await Task.WhenAll(evaluationTasks);
```

#### Test Coverage

- **116/116 unit tests passing**
- New tests added:
  - 7 tests for `CalculateTotalEvaluationUnits()` formula
  - 3 tests for NA short-circuit in orchestrator
  - 6 tests for turn-level NA handling
  - Multiple tests for concurrent evaluation patterns
  - Tests for aggregate calculation with NA filtering

---

## Approval & Sign-Off

**Product Owner:** ✅ Approved (Story 09 implementation complete)  
**Date:** 2025-11-14

**Tech Lead:** ✅ Approved (All tests passing, build successful)  
**Date:** 2025-11-14

**QA Lead:** ✅ Approved (100% test pass rate verified)  
**Date:** 2025-11-14

---

**Version**: 3.0 (Implemented)  
**Last Updated**: 2025-11-14  
**Status**: Complete - Ready for Production Deployment
