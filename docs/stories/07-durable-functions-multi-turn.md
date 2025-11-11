# Story 07: Migrate Multi-Turn Execution to Durable Functions

**Status:** Approved  
**Priority:** High  
**Estimated Effort:** 3-5 days  
**Dependencies:** None  
**Related Issues:** Multi-turn test cases timing out after 5 minutes (Service Bus message lock limit)

## Problem Statement

Multi-turn test cases are failing to complete when execution exceeds 5 minutes:

### Current Issues
1. **Service Bus Standard tier has a 5-minute maximum message lock duration**
2. **Multi-turn conversations can take 10+ minutes:**
   - 10 turns × 60 seconds per turn = 10 minutes
   - Agent response times are unpredictable (30 seconds to 5+ minutes)
   - LLM Judge evaluation adds 10-30 seconds per turn
   - Rate limit retries can add significant time

3. **Observed Failures:**
   - 11 out of production test runs: conversations stopped mid-execution
   - Example: 10-turn conversation completed only 2 turns before timeout
   - Message lock expires → Function execution terminates → Remaining turns never execute

4. **Current Architecture Limitations:**
   - All turns processed in single Service Bus message
   - Cannot extend lock beyond 5 minutes (Azure limit)
   - Message lock renewal is complex and unreliable
   - All-or-nothing failure mode (lose all progress on timeout)

## Proposed Solution: Azure Durable Functions

Migrate multi-turn test case execution to **Durable Functions** orchestrator pattern, which is designed for long-running, stateful workflows.

### Why Durable Functions?

| Feature | Service Bus (Current) | Durable Functions (Proposed) |
|---------|----------------------|------------------------------|
| Max Execution Time | 5 minutes (hard limit) | Hours/days (no limit) |
| State Management | Manual (Dataverse) | Automatic (checkpointing) |
| Failure Handling | All-or-nothing | Per-activity retry |
| Resume After Failure | Manual reprocessing | Automatic replay |
| Monitoring | Application Insights | Built-in execution history |
| Variable Wait Times | Limited by timeout | Native support |

### Architecture Changes

#### Current Architecture
```
StartEvaluationRun (HTTP)
  ↓
Send message to Service Bus queue
  ↓
MultiTurnWorkerFunction (Service Bus trigger)
  ↓
Loop: Execute all turns in sequence
  - SendToAgent
  - EvaluateWithLLM  
  - SaveResult
  ↓
CreateAggregateResult
```

**Problem:** If any turn takes too long, entire execution times out.

#### Proposed Architecture
```
StartEvaluationRun (HTTP)
  ↓
Start Orchestrator per multi-turn test case
  ↓
Orchestrator Function (durable)
  ↓
  Loop through turns:
    ├─ Activity: GetNextTurn
    ├─ Activity: SendMessageToAgent (can take minutes)
    ├─ Activity: WaitForAgentResponse (can take minutes)
    ├─ Activity: EvaluateResponseWithLLM (can retry on 429)
    ├─ Activity: SaveTurnResult
    └─ Check: More turns? → Continue loop
  ↓
Activity: CreateAggregateResult
  ↓
Update evaluation run status
```

**Benefits:**
- Each activity can take as long as needed
- Automatic checkpointing after each activity
- If orchestrator is interrupted, it replays from last checkpoint
- Built-in retry policies per activity
- No message lock limitations

## Acceptance Criteria

### Functional Requirements
1. ✅ Multi-turn test cases can execute 20+ turns without timeout
2. ✅ Individual turns can take 10+ minutes without failure
3. ✅ If function restarts, execution resumes from last completed turn
4. ✅ All turn results saved to Dataverse immediately (not lost on failure)
5. ✅ Aggregate result created after all turns complete
6. ✅ Single-turn test cases continue using existing Service Bus pattern
7. ✅ Evaluation run status updates correctly
8. ✅ Failed turns are retried according to retry policy
9. ✅ Circuit breaker pattern still applies to LLM Judge calls
10. ✅ Rate limit errors (429) are retried with exponential backoff

### Non-Functional Requirements
1. ✅ Orchestrator execution visible in Azure Portal (Durable Functions Monitor)
2. ✅ Application Insights tracks each turn as separate operation
3. ✅ Execution history retained for debugging
4. ✅ Performance: No slower than current implementation for short conversations
5. ✅ Scalability: 100+ concurrent multi-turn executions
6. ✅ Cost: Minimal increase (durable functions use storage for state)

## Technical Design

### 1. New Durable Functions

#### Orchestrator: `MultiTurnTestCaseOrchestrator`
```csharp
[FunctionName("MultiTurnTestCaseOrchestrator")]
public async Task<MultiTurnExecutionContext> RunOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext context)
{
    var input = context.GetInput<MultiTurnOrchestratorInput>();
    var results = new MultiTurnExecutionContext();
    
    // Get test case with turns
    var testCase = await context.CallActivityAsync<TestCase>(
        "GetTestCaseWithTurns", input.TestCaseId);
    
    string? conversationId = null;
    
    // Execute each turn sequentially
    foreach (var turn in testCase.Turns.OrderBy(t => t.Sequence))
    {
        var turnInput = new ExecuteTurnInput
        {
            Turn = turn,
            ConversationId = conversationId,
            DirectLineUrl = input.DirectLineUrl,
            EvaluatorTypeId = input.EvaluatorTypeId,
            PassingThreshold = input.PassingThreshold
        };
        
        // Execute turn (can take minutes - no timeout)
        var turnResult = await context.CallActivityAsync<TurnExecutionResult>(
            "ExecuteSingleTurn", turnInput);
        
        // Store conversation ID from first turn
        if (turn.Sequence == 1)
            conversationId = turnResult.ConversationId;
        
        results.TurnResults[turn.Sequence] = turnResult;
        
        // Optional: Add delay between turns if needed
        if (turn.Sequence < testCase.Turns.Count)
            await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(3), CancellationToken.None);
    }
    
    // Create aggregate result
    await context.CallActivityAsync("CreateAggregateResult", 
        new { Context = results, TestRunId = input.TestRunId });
    
    return results;
}
```

#### Activity: `ExecuteSingleTurn`
```csharp
[FunctionName("ExecuteSingleTurn")]
public async Task<TurnExecutionResult> ExecuteSingleTurn(
    [ActivityTrigger] ExecuteTurnInput input,
    ILogger log)
{
    // 1. Send to agent (can take minutes)
    var copilotResponse = await _copilotStudioService.SendMessageAsync(
        input.DirectLineUrl, 
        input.Turn.PromptText, 
        input.ConversationId);
    
    if (!copilotResponse.Success)
        return CreateFailureResult(input, copilotResponse.ErrorMessage);
    
    // 2. Evaluate with LLM (can take minutes, has retry logic)
    var judgeResponse = await _llmJudgeService.EvaluateResponseAsync(
        new LLMJudgeRequest
        {
            EvaluatorTypeId = input.EvaluatorTypeId,
            UserInput = input.Turn.PromptText,
            ExpectedResponse = input.Turn.ExpectedResponse,
            ActualResponse = copilotResponse.Response,
            ConversationId = copilotResponse.ConversationId
        });
    
    if (!judgeResponse.Success)
        return CreateFailureResult(input, judgeResponse.ErrorMessage);
    
    // 3. Save to Dataverse immediately
    var result = await _dataverseService.CreateEvaluationResultAsync(
        new CreateEvaluationResultRequest
        {
            TestCaseId = input.Turn.TestCaseId,
            TestCaseTurnId = input.Turn.Id,
            ConversationId = copilotResponse.ConversationId,
            ActualResponse = copilotResponse.Response,
            Score = judgeResponse.Score,
            Passed = judgeResponse.Score >= input.PassingThreshold,
            Reasoning = judgeResponse.Reasoning,
            ExecutionTimeMs = copilotResponse.ExecutionTimeMs
        });
    
    return new TurnExecutionResult
    {
        ConversationId = copilotResponse.ConversationId,
        Success = true,
        Score = judgeResponse.Score,
        Passed = judgeResponse.Score >= input.PassingThreshold
    };
}
```

#### HTTP Starter: `StartMultiTurnTestCase`
```csharp
[FunctionName("StartMultiTurnTestCase")]
public async Task<IActionResult> StartMultiTurnTestCase(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
    [DurableClient] IDurableOrchestrationClient starter,
    ILogger log)
{
    var input = await JsonSerializer.DeserializeAsync<MultiTurnOrchestratorInput>(req.Body);
    
    string instanceId = await starter.StartNewAsync(
        "MultiTurnTestCaseOrchestrator", input);
    
    log.LogInformation("Started orchestration with ID = '{instanceId}'", instanceId);
    
    return starter.CreateCheckStatusResponse(req, instanceId);
}
```

### 2. Modified `StartEvaluationRun` Function

Update orchestrator function to:
- Keep Service Bus for single-turn test cases
- Start Durable Function orchestrator for multi-turn test cases

```csharp
foreach (var testCase in testCases)
{
    if (testCase.IsMultiTurn && testCase.Turns?.Count > 1)
    {
        // Start durable function orchestrator
        var input = new MultiTurnOrchestratorInput
        {
            TestCaseId = testCase.Id,
            TestRunId = evaluationRunId,
            DirectLineUrl = directLineUrl,
            EvaluatorTypeId = testCase.EvaluatorTypeId,
            PassingThreshold = passingThreshold
        };
        
        await durableClient.StartNewAsync(
            "MultiTurnTestCaseOrchestrator", 
            testCase.Id.ToString(), 
            input);
    }
    else
    {
        // Use existing Service Bus for single-turn
        await _serviceBusService.SendTestCaseMessageAsync(message);
    }
}
```

### 3. State Management

Durable Functions handles state automatically via Azure Storage:
- **History table:** Execution events and checkpoints
- **Instances table:** Active orchestration instances
- **Task hub:** Coordination between orchestrator and activities

No manual state management needed!

### 4. Monitoring & Observability

- **Durable Functions Monitor Extension:** Visual workflow execution
- **Application Insights:** Operation tracking per turn
- **Custom logging:** Turn-level progress tracking
- **Status API:** Check orchestration status programmatically

```csharp
// Check status
var status = await durableClient.GetStatusAsync(instanceId);
// status.RuntimeStatus: Running, Completed, Failed, Terminated
// status.Output: Final result
// status.History: All activity executions
```

## Implementation Plan

### Phase 1: Setup & Infrastructure (1 day)
- [ ] Add `Microsoft.Azure.WebJobs.Extensions.DurableTask` NuGet package
- [ ] Configure Durable Functions in `Program.cs`
- [ ] Update Bicep to add storage account connection for Durable Functions
- [ ] Use existing storage account for task hub

### Phase 2: Core Implementation (2 days)
- [ ] Implement `MultiTurnTestCaseOrchestrator` function
- [ ] Implement `ExecuteSingleTurn` activity function
- [ ] Implement `GetTestCaseWithTurns` activity function
- [ ] Implement `CreateAggregateResult` activity function
- [ ] Update `StartEvaluationRun` to route multi-turn to orchestrator
- [ ] Add retry policies for activities

### Phase 3: Migration & Testing (1 day)
- [ ] Test 2-turn conversation
- [ ] Test 10-turn conversation
- [ ] Test 20-turn conversation
- [ ] Test agent timeout scenario (5+ minute turn)
- [ ] Test LLM rate limit scenario
- [ ] Test orchestrator restart/resume
- [ ] Verify all results saved to Dataverse
- [ ] Compare performance with current implementation

### Phase 4: Deployment & Monitoring (1 day)
- [ ] Deploy to staging environment
- [ ] Run production test suite
- [ ] Verify no regressions for single-turn test cases
- [ ] Monitor execution times and success rates
- [ ] Update documentation
- [ ] Deploy to production

## Testing Strategy

### Unit Tests
- [ ] Test orchestrator with mock activities
- [ ] Test turn execution activity
- [ ] Test aggregate result creation
- [ ] Test failure handling per turn

### Integration Tests
- [ ] Test full multi-turn flow (end-to-end)
- [ ] Test orchestrator restart mid-execution
- [ ] Test with real Copilot Studio agent
- [ ] Test with real LLM Judge
- [ ] Test concurrent orchestrations (10+)

### Load Tests
- [ ] 100 concurrent 5-turn conversations
- [ ] 50 concurrent 10-turn conversations
- [ ] 20 concurrent 20-turn conversations
- [ ] Verify no timeouts under load

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Learning curve for Durable Functions | Medium | Use existing Azure samples, documentation |
| Increased storage costs | Low | Task hub storage is minimal (~KB per execution) |
| Debugging complexity | Medium | Use Durable Functions Monitor, detailed logging |
| Migration breaks existing flows | High | Keep Service Bus for single-turn, test thoroughly |
| Orchestrator replay bugs | Medium | Follow Durable Functions deterministic code rules |

## Success Metrics

### Before (Current State)
- ❌ Multi-turn completion rate: ~60% (11 out of test runs failed)
- ❌ Max turns completed: Limited by 5-minute timeout
- ❌ Agent response time limit: ~4 minutes max
- ❌ Recovery from timeout: Manual reprocessing

### After (Target State)
- ✅ Multi-turn completion rate: 95%+ (only genuine failures)
- ✅ Max turns completed: Unlimited (tested to 20+)
- ✅ Agent response time limit: No limit
- ✅ Recovery from timeout: Automatic resume

## Future Enhancements

1. **Parallel Turn Execution:** For independent turns (non-conversational)
2. **Human-in-the-loop:** Wait for manual approval between turns
3. **Conditional Branching:** Different paths based on turn results
4. **External Events:** Wait for async agent completion webhooks
5. **Sub-orchestrators:** Complex multi-agent scenarios

## References

- [Azure Durable Functions Documentation](https://learn.microsoft.com/en-us/azure/azure-functions/durable/)
- [Durable Functions Patterns](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview?tabs=csharp-inproc#application-patterns)
- [Service Bus vs Durable Functions](https://learn.microsoft.com/en-us/azure/architecture/guide/technology-choices/messaging)
- Story 06: Multi-turn Support (original implementation)
- HOTFIX: ClientResultException Rate Limit Handling

## Approval

**Product Owner:** _Pending_  
**Tech Lead:** _Pending_  
**Date:** _Pending_
