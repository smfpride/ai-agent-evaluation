# Test Plan: Orchestration Idempotency and Status Code Management

**Story Reference:** [Story 08: Orchestration Idempotency and Status Tracking](../stories/08-orchestration-idempotency-status-tracking.md)  
**Created:** November 12, 2025  
**Status:** Planning

---

## Test Objectives

1. Verify orchestration idempotency prevents duplicate test executions
2. Validate StatusCode lifecycle management across all result types
3. Ensure Monitor function correctly interprets result status codes
4. Test edge cases and failure recovery scenarios

---

## Test Categories

### 1. Unit Tests - OrchestratorFunction

#### 1.1 Placeholder Creation
```csharp
[Fact]
public async Task StartEvaluationRun_MultiTurn_CreatesPlaceholderWithPendingStatus()
{
    // Arrange
    var multiTurnTestCase = CreateTestCase(isMultiTurn: true);
    _mockDataverseService
        .Setup(x => x.GetAggregateResultForTestCaseAsync(It.IsAny<Guid>(), multiTurnTestCase.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync((EvaluationResult?)null); // No existing result
    
    // Act
    var response = await _orchestratorFunction.StartEvaluationRun(request, durableClient, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => 
            r.IsAggregate == true &&
            r.StatusCode == 1 && // Pending
            r.TestCaseId == multiTurnTestCase.Id),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

#### 1.2 Idempotency - Existing Completed Result
```csharp
[Fact]
public async Task StartEvaluationRun_ExistingCompletedResult_SkipsOrchestration()
{
    // Arrange
    var existingResult = new EvaluationResult 
    { 
        Id = Guid.NewGuid(),
        StatusCode = 778760002, // Completed
        IsAggregate = true
    };
    
    _mockDataverseService
        .Setup(x => x.GetAggregateResultForTestCaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(existingResult);
    
    // Act
    await _orchestratorFunction.StartEvaluationRun(request, durableClient, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.IsAny<CreateEvaluationResultRequest>(),
        It.IsAny<CancellationToken>()), Times.Never);
    
    _mockDurableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
        It.IsAny<string>(),
        It.IsAny<object>(),
        It.IsAny<StartOrchestrationOptions>()), Times.Never);
}
```

#### 1.3 Retry After Failure
```csharp
[Fact]
public async Task StartEvaluationRun_ExistingFailedResult_AllowsRetry()
{
    // Arrange
    var failedResult = new EvaluationResult 
    { 
        Id = Guid.NewGuid(),
        StatusCode = 778760003, // Failed
        IsAggregate = true
    };
    
    _mockDataverseService
        .Setup(x => x.GetAggregateResultForTestCaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(failedResult);
    
    // Act
    await _orchestratorFunction.StartEvaluationRun(request, durableClient, cancellationToken);
    
    // Assert - Should create new placeholder and start orchestration
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => r.StatusCode == 1),
        It.IsAny<CancellationToken>()), Times.Once);
    
    _mockDurableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
        It.IsAny<string>(),
        It.IsAny<object>(),
        It.IsAny<StartOrchestrationOptions>()), Times.Once);
}
```

#### 1.4 Pending Result - Idempotency
```csharp
[Fact]
public async Task StartEvaluationRun_ExistingPendingResult_SkipsOrchestration()
{
    // Arrange
    var pendingResult = new EvaluationResult 
    { 
        Id = Guid.NewGuid(),
        StatusCode = 1, // Pending
        IsAggregate = true
    };
    
    _mockDataverseService
        .Setup(x => x.GetAggregateResultForTestCaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(pendingResult);
    
    // Act
    await _orchestratorFunction.StartEvaluationRun(request, durableClient, cancellationToken);
    
    // Assert - Should skip (orchestration already running or queued)
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.IsAny<CreateEvaluationResultRequest>(),
        It.IsAny<CancellationToken>()), Times.Never);
}
```

#### 1.5 AggregateResultId Passed to Orchestrator
```csharp
[Fact]
public async Task StartEvaluationRun_MultiTurn_PassesAggregateResultIdToOrchestrator()
{
    // Arrange
    var resultId = Guid.NewGuid();
    _mockDataverseService
        .Setup(x => x.CreateEvaluationResultAsync(It.IsAny<CreateEvaluationResultRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(resultId);
    
    // Act
    await _orchestratorFunction.StartEvaluationRun(request, durableClient, cancellationToken);
    
    // Assert
    _mockDurableClient.Verify(x => x.ScheduleNewOrchestrationInstanceAsync(
        "MultiTurnTestCaseOrchestrator",
        It.Is<MultiTurnOrchestratorInput>(input => input.AggregateResultId == resultId),
        It.IsAny<StartOrchestrationOptions>()), Times.Once);
}
```

---

### 2. Unit Tests - WorkerFunction (Single-Turn)

#### 2.1 Completed Status on Success
```csharp
[Fact]
public async Task ProcessTestCase_Success_CreatesResultWithCompletedStatus()
{
    // Arrange
    var message = CreateTestCaseMessage();
    _mockLLMJudgeService
        .Setup(x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LLMJudgeResponse { Success = true, Score = 0.85m });
    
    // Act
    await _workerFunction.ProcessTestCase(message, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => r.StatusCode == 778760002), // Completed
        It.IsAny<CancellationToken>()), Times.Once);
}
```

#### 2.2 Failed Status on Error
```csharp
[Fact]
public async Task ProcessTestCase_CopilotStudioError_CreatesResultWithFailedStatus()
{
    // Arrange
    var message = CreateTestCaseMessage();
    _mockCopilotStudioService
        .Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CopilotStudioResponse { Success = false, ErrorMessage = "Connection timeout" });
    
    // Act
    await _workerFunction.ProcessTestCase(message, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => 
            r.StatusCode == 778760003 && // Failed
            r.ErrorMessage == "Connection timeout"),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

---

### 3. Unit Tests - MultiTurnDurableActivities

#### 3.1 ExecuteSingleTurn - Completed Status
```csharp
[Fact]
public async Task ExecuteSingleTurn_Success_CreatesResultWithCompletedStatus()
{
    // Arrange
    var input = CreateExecuteTurnInput();
    _mockLLMJudgeService
        .Setup(x => x.EvaluateResponseAsync(It.IsAny<LLMJudgeRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LLMJudgeResponse { Success = true, Score = 0.9m });
    
    // Act
    var result = await _activities.ExecuteSingleTurn(input, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.CreateEvaluationResultAsync(
        It.Is<CreateEvaluationResultRequest>(r => 
            r.StatusCode == 778760002 && // Completed
            r.IsAggregate == false), // Turn-level result
        It.IsAny<CancellationToken>()), Times.Once);
}
```

#### 3.2 CreateAggregateResult - Updates Placeholder
```csharp
[Fact]
public async Task CreateAggregateResult_Success_UpdatesPlaceholderWithCompletedStatus()
{
    // Arrange
    var context = CreateMultiTurnContext(passedTurns: 3, totalTurns: 3);
    var aggregateResultId = Guid.NewGuid();
    var input = new CreateAggregateResultInput
    {
        Context = context,
        TestRunId = Guid.NewGuid(),
        AggregateResultId = aggregateResultId
    };
    
    // Act
    await _activities.CreateAggregateResult(input, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
        aggregateResultId,
        It.IsAny<decimal>(),
        true, // Passed
        It.IsAny<string>(),
        It.IsAny<int>(),
        778760002, // Completed
        It.IsAny<CancellationToken>()), Times.Once);
}
```

#### 3.3 CreateAggregateResult - Failed Status
```csharp
[Fact]
public async Task CreateAggregateResult_BelowThreshold_UpdatesWithFailedStatus()
{
    // Arrange
    var context = CreateMultiTurnContext(passedTurns: 1, totalTurns: 3);
    var input = new CreateAggregateResultInput
    {
        Context = context,
        TestRunId = Guid.NewGuid(),
        AggregateResultId = Guid.NewGuid()
    };
    
    // Act
    await _activities.CreateAggregateResult(input, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.UpdateAggregateResultAsync(
        It.IsAny<Guid>(),
        It.IsAny<decimal>(),
        false, // Failed
        It.IsAny<string>(),
        It.IsAny<int>(),
        778760003, // Failed
        It.IsAny<CancellationToken>()), Times.Once);
}
```

---

### 4. Unit Tests - MonitorFunction

#### 4.1 Count Only Completed Results
```csharp
[Fact]
public async Task MonitorEvaluationRuns_MixedStatusCodes_CountsOnlyCompleted()
{
    // Arrange
    var evaluationRun = CreateEvaluationRun(totalTestCases: 10);
    var results = new List<EvaluationResult>
    {
        new() { IsAggregate = true, StatusCode = 778760002 }, // Completed
        new() { IsAggregate = true, StatusCode = 778760002 }, // Completed
        new() { IsAggregate = true, StatusCode = 1 }, // Pending
        new() { IsAggregate = true, StatusCode = 778760003 }, // Errored
        // Additional turn-level results (should be filtered out)
        new() { IsAggregate = false, TestCaseTurnId = Guid.NewGuid() }
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluationResultsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(results);
    
    // Act
    await _monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);
    
    // Assert
    _mockDataverseService.Verify(x => x.UpdateEvaluationRunProgressAsync(
        evaluationRun.Id,
        2, // completedCount (only StatusCode = 778760002)
        1, // failedCount (StatusCode = 778760003)
        It.IsAny<CancellationToken>()), Times.Once);
}
```

#### 4.2 Pending Results Prevent Completion
```csharp
[Fact]
public async Task MonitorEvaluationRuns_PendingResults_DoesNotMarkComplete()
{
    // Arrange
    var evaluationRun = CreateEvaluationRun(totalTestCases: 3);
    var results = new List<EvaluationResult>
    {
        new() { IsAggregate = true, StatusCode = 778760002, Score = 0.8m }, // Completed
        new() { IsAggregate = true, StatusCode = 778760002, Score = 0.7m }, // Completed
        new() { IsAggregate = true, StatusCode = 1 }  // Pending (blocks completion)
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluationResultsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(results);
    
    // Act
    await _monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);
    
    // Assert - Should NOT mark as completed
    _mockDataverseService.Verify(x => x.UpdateEvaluationRunCompletionAsync(
        It.IsAny<Guid>(),
        EvaluationRunStatus.Completed,
        It.IsAny<decimal?>(),
        It.IsAny<bool?>(),
        It.IsAny<DateTime>(),
        It.IsAny<CancellationToken>()), Times.Never);
}
```

#### 4.3 Stuck Orchestration Detection
```csharp
[Fact]
public async Task MonitorEvaluationRuns_PendingOver30Min_LogsWarning()
{
    // Arrange
    var evaluationRun = CreateEvaluationRun(totalTestCases: 1);
    var stuckResult = new EvaluationResult
    {
        IsAggregate = true,
        StatusCode = 1, // Pending
        CreatedOn = DateTime.UtcNow.AddMinutes(-35) // Created 35 minutes ago
    };
    
    _mockDataverseService
        .Setup(x => x.GetEvaluationResultsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EvaluationResult> { stuckResult });
    
    // Act
    await _monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);
    
    // Assert - Should log warning about stuck orchestration
    _mockLogger.Verify(
        x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("stuck") || v.ToString().Contains("pending")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()),
        Times.AtLeastOnce);
}
```

---

### 5. Integration Tests

#### 5.1 End-to-End Multi-Turn Idempotency
```csharp
[Fact]
public async Task EndToEnd_MultiTurnTestCase_IdempotentExecution()
{
    // Arrange - Create evaluation run with multi-turn test case
    var evaluationRunId = await CreateEvaluationRunInDataverse();
    var testCaseId = await CreateMultiTurnTestCaseInDataverse(evaluationRunId);
    
    // Act - Start evaluation run TWICE
    var response1 = await CallStartEvaluationRun(evaluationRunId);
    await Task.Delay(500); // Small delay
    var response2 = await CallStartEvaluationRun(evaluationRunId);
    
    // Assert - Only ONE orchestration started
    var results = await GetAggregateResultsFromDataverse(evaluationRunId, testCaseId);
    Assert.Single(results);
    Assert.Equal(1, results[0].StatusCode); // Pending
    
    // Wait for orchestration to complete
    await WaitForOrchestrationCompletion(timeout: TimeSpan.FromMinutes(5));
    
    // Assert - Result updated to Completed
    var finalResults = await GetAggregateResultsFromDataverse(evaluationRunId, testCaseId);
    Assert.Single(finalResults);
    Assert.Equal(778760002, finalResults[0].StatusCode); // Completed
    Assert.NotNull(finalResults[0].Score);
}
```

#### 5.2 Placeholder Creation Failure Recovery
```csharp
[Fact]
public async Task Integration_PlaceholderCreationFails_SkipsOrchestration()
{
    // Arrange - Simulate Dataverse failure
    var evaluationRunId = await CreateEvaluationRunInDataverse();
    await CreateMultiTurnTestCaseInDataverse(evaluationRunId);
    
    // Inject failure for CreateEvaluationResultAsync
    InjectDataverseFailure("CreateEvaluationResult");
    
    // Act
    var response = await CallStartEvaluationRun(evaluationRunId);
    
    // Assert - Function should handle gracefully
    Assert.Equal(202, response.StatusCode); // Accepted
    
    // Verify no orphaned orchestrations started
    var orchestrations = await GetRunningOrchestrations();
    Assert.Empty(orchestrations.Where(o => o.Name == "MultiTurnTestCaseOrchestrator"));
}
```

#### 5.3 Monitor Counts Only Completed
```csharp
[Fact]
public async Task Integration_MonitorFunction_CountsOnlyCompleted()
{
    // Arrange - Create evaluation run with mixed results
    var evaluationRunId = await CreateEvaluationRunInDataverse();
    await CreateResultInDataverse(evaluationRunId, statusCode: 778760002, isAggregate: true); // Completed
    await CreateResultInDataverse(evaluationRunId, statusCode: 1, isAggregate: true); // Pending
    await CreateResultInDataverse(evaluationRunId, statusCode: 778760002, isAggregate: true); // Completed
    
    // Act - Trigger monitor function
    await TriggerMonitorFunction();
    
    // Assert - Evaluation run should show 2/3 completed (pending not counted)
    var evaluationRun = await GetEvaluationRunFromDataverse(evaluationRunId);
    Assert.Equal(2, evaluationRun.CompletedTestCases);
    Assert.Equal(778760002, evaluationRun.StatusCode); // Still Running (not all completed)
}
```

---

### 6. Manual Test Scenarios

#### 6.1 Happy Path - Multi-Turn Idempotency
**Steps:**
1. Create evaluation run with 1 multi-turn test case (3 turns)
2. Call `POST /api/evaluations/start` with evaluation run ID
3. Immediately call `POST /api/evaluations/start` again (duplicate request)
4. Query Dataverse for aggregate results with `isaggregate = true`

**Expected Results:**
- Only ONE aggregate result created with `statuscode = 1` (Pending)
- Only ONE orchestration instance started
- Logs show: "Test case already has aggregate result, skipping"
- After completion: Result updated to `statuscode = 778760002` with score

#### 6.2 Retry After Failure
**Steps:**
1. Start evaluation run with multi-turn test case
2. Kill function app mid-execution (simulate crash)
3. Verify placeholder result exists with `statuscode = 1` (Pending)
4. Manually update result to `statuscode = 778760003` (Errored)
5. Restart evaluation run

**Expected Results:**
- New placeholder created (old one has Errored status)
- New orchestration started
- Completion succeeds with new result marked Completed

#### 6.3 Stuck Orchestration Detection
**Steps:**
1. Start multi-turn test case
2. Placeholder created with `statuscode = 1` (Pending)
3. Disable Copilot Studio endpoint (simulate stall)
4. Wait 35 minutes
5. Check monitor function logs

**Expected Results:**
- Monitor logs warning about pending result >30 minutes
- Evaluation run status remains Running
- No false-positive completion

#### 6.4 Monitor Completion Accuracy
**Steps:**
1. Create evaluation run with 5 test cases (3 single-turn, 2 multi-turn)
2. Start evaluation run
3. Monitor Dataverse results table in real-time

**Expected Timeline:**
- T+0s: 2 placeholder aggregate results created (Pending)
- T+30s: 3 single-turn results created (Completed)
- T+60s: Monitor check - should show 3/5 completed
- T+120s: Multi-turn orchestrations finish - 2 aggregates updated (Completed)
- T+150s: Monitor check - should show 5/5 completed
- T+150s: Evaluation run marked Completed

---

## Test Data Setup

### Dataverse StatusCode Option Set Configuration

Before running tests, ensure StatusCode field has the following options:

```
Entity: mcicaps_evaluationresults
Field: statuscode
Options:
  - 1: Pending
  - 778760001: Running (reserved)
  - 778760002: Completed
  - 778760003: Errored
```

### Sample Test Cases

**Single-Turn:**
```json
{
  "mcicaps_testcaseid": "guid",
  "mcicaps_ismultiturn": false,
  "mcicaps_userinput": "What is your name?",
  "mcicaps_expectedresponse": "I am a helpful assistant."
}
```

**Multi-Turn:**
```json
{
  "mcicaps_testcaseid": "guid",
  "mcicaps_ismultiturn": true,
  "turns": [
    {"sequence": 1, "prompttext": "Hello", "expectedresponse": "Hi there!"},
    {"sequence": 2, "prompttext": "How are you?", "expectedresponse": "I'm doing well!"},
    {"sequence": 3, "prompttext": "Goodbye", "expectedresponse": "Goodbye!"}
  ]
}
```

---

## Success Criteria

### Unit Tests
- ✅ All OrchestratorFunction tests pass (5/5)
- ✅ All WorkerFunction tests pass (2/2)
- ✅ All MultiTurnDurableActivities tests pass (3/3)
- ✅ All MonitorFunction tests pass (3/3)

### Integration Tests
- ✅ End-to-end idempotency test passes
- ✅ Failure recovery test passes
- ✅ Monitor accuracy test passes

### Manual Testing
- ✅ No duplicate orchestrations in 10 test runs
- ✅ Stuck orchestration detected within 5 minutes
- ✅ Monitor completion timing accurate within 2 minutes

### Performance
- ✅ GetAggregateResultForTestCaseAsync query <500ms p95
- ✅ UpdateAggregateResultAsync <300ms p95
- ✅ No regression in overall evaluation run duration

---

## Test Execution Checklist

- [ ] Unit tests written for all modified functions
- [ ] Integration tests cover happy path and failure scenarios
- [ ] Manual test scenarios documented and executed
- [ ] Dataverse StatusCode options configured in test environment
- [ ] Test data scripts created for reproducibility
- [ ] Performance benchmarks recorded
- [ ] All tests passing in dev environment
- [ ] All tests passing in staging environment
- [ ] Documentation updated with test results

---

## Known Limitations & Future Tests

1. **Orchestration Instance ID Tracking:** Current design uses EvaluationResultId. Future: Add dedicated OrchestrationInstanceId field for direct Durable Functions API queries.

2. **Automatic Stuck Orchestration Recovery:** Monitor currently only logs warnings. Future: Auto-mark failed or trigger retry.

3. **Multi-Evaluator Support:** StatusCode tracking works, but idempotency check doesn't include EvaluatorTypeId. Future: Extend idempotency key.

4. **Status Transition Validation:** No enforcement of valid state transitions (e.g., Pending → Running → Completed). Future: Add state machine validation.

---

## References

- [Story 08: Orchestration Idempotency](../stories/08-orchestration-idempotency-status-tracking.md)
- [PRD: Evaluation Results Schema](../prd/PRD-Agent-Evaluation-Function.md#322-output-tables)
- [Architecture: Idempotency Pattern](../ARCHITECTURE.md#46-mcicaps_evaluationresults-evaluation-result)
- [Test Summary](./00-test-summary.md)
