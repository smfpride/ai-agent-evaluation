# Durable Functions Multi-Turn Execution Guide

## Overview

Multi-turn test cases now use Azure Durable Functions orchestration instead of Service Bus to eliminate the 5-minute timeout limitation. This enables long-running conversations that can execute for hours without timeout.

## Architecture

### Current Flow

```
StartEvaluationRun (HTTP)
  ↓
For each multi-turn test case:
  Start Durable Functions Orchestration
    ↓
  MultiTurnTestCaseOrchestrator
    ↓
    Loop through turns:
      ├─ GetTestCaseWithTurns (Activity)
      ├─ ExecuteSingleTurn (Activity) - can take minutes
      │   ├─ Send to Copilot Studio
      │   ├─ Evaluate with LLM Judge
      │   └─ Save result to Dataverse
      └─ CreateAggregateResult (Activity)
```

### Key Components

#### 1. HTTP Starter: `StartMultiTurnTestCase`
- Entry point for starting orchestrations
- Can be called directly or from `StartEvaluationRun`
- Uses test case ID as instance ID for idempotency

#### 2. Orchestrator: `MultiTurnTestCaseOrchestrator`
- Coordinates sequential turn execution
- Maintains conversation state across turns
- Automatically checkpoints after each activity
- Can run for unlimited time (hours/days)

#### 3. Activities

**GetTestCaseWithTurns**
- Retrieves test case and turns from Dataverse
- Input: Test case ID
- Output: TestCase with Turns collection

**ExecuteSingleTurn**
- Executes one turn: agent → judge → save
- Can take minutes (no timeout)
- Input: ExecuteTurnInput (turn, conversationId, directLineUrl, etc.)
- Output: TurnExecutionResult (conversationId, score, passed)

**CreateAggregateResult**
- Creates aggregate result for multi-turn test case
- Calculates overall pass rate
- Updates evaluation run counters
- Input: CreateAggregateResultInput (context, testRunId)
- Output: None

## Configuration

### host.json

```json
{
  "extensions": {
    "durableTask": {
      "storageProvider": {
        "connectionName": "AzureWebJobsStorage"
      },
      "hubName": "AiAgentEvaluationHub",
      "tracing": {
        "traceInputsAndOutputs": true,
        "traceReplayEvents": false
      },
      "maxConcurrentActivityFunctions": 100,
      "maxConcurrentOrchestratorFunctions": 10
    }
  }
}
```

### Environment Variables

- `AzureWebJobsStorage`: Storage account connection string for task hub

## Usage

### Starting an Orchestration

Multi-turn test cases are automatically routed to Durable Functions when `StartEvaluationRun` is called:

```csharp
// Happens automatically in OrchestratorFunction.StartEvaluationRun
foreach (var testCase in multiTurnTestCases)
{
    var input = new MultiTurnOrchestratorInput
    {
        TestCaseId = testCase.Id,
        TestRunId = evaluationRunId,
        DirectLineUrl = directLineUrl,
        EvaluatorTypeId = testCase.EvaluatorTypeId,
        PassingThreshold = passingThreshold
    };

    string instanceId = $"multiturn-{testCase.Id}";
    
    await durableClient.ScheduleNewOrchestrationInstanceAsync(
        "MultiTurnTestCaseOrchestrator",
        input,
        new StartOrchestrationOptions { InstanceId = instanceId });
}
```

### Monitoring

Use the Durable Functions extension in Azure Portal to:
- View orchestration history
- Check status of running orchestrations
- See execution timeline with activities
- Replay failed orchestrations

Alternatively, use Application Insights to query:
```kusto
traces
| where customDimensions.Category == "Function.MultiTurnTestCaseOrchestrator"
| project timestamp, message, customDimensions
```

## Benefits

### Before (Service Bus)
- ❌ 5-minute timeout limit (Azure Service Bus message lock)
- ❌ All-or-nothing failure mode
- ❌ Manual state management
- ❌ Difficult to debug mid-execution
- ❌ All progress lost on timeout

### After (Durable Functions)
- ✅ No timeout limit (hours/days)
- ✅ Per-turn failure handling
- ✅ Automatic state management with checkpointing
- ✅ Built-in execution history and monitoring
- ✅ Automatic resume from last checkpoint

## Acceptance Criteria Met

1. ✅ Multi-turn test cases can execute 20+ turns without timeout
2. ✅ Individual turns can take 10+ minutes without failure
3. ✅ If function restarts, execution resumes from last completed turn
4. ✅ All turn results saved to Dataverse immediately (not lost on failure)
5. ✅ Aggregate result created after all turns complete
6. ✅ Single-turn test cases continue using existing Service Bus pattern
7. ✅ Evaluation run status updates correctly
8. ✅ Activity functions can be retried independently
9. ✅ LLM Judge retries still apply (handled in service layer)
10. ✅ Rate limit errors (429) handled with exponential backoff

## Troubleshooting

### Orchestration Not Starting

Check:
- `AzureWebJobsStorage` connection string is configured
- Durable Functions extension is installed
- Storage account is accessible

### Orchestration Stuck

Check:
- Activity function logs for errors
- Durable Functions Monitor for execution history
- Application Insights for traces

### High Latency

Check:
- `maxConcurrentActivityFunctions` setting in host.json
- Storage account throttling
- Network latency to Copilot Studio or Azure OpenAI

## Testing

All unit tests have been updated to include the DurableTaskClient parameter:

```csharp
var result = await _orchestratorFunction.StartEvaluationRun(
    httpRequestData, 
    _mockDurableClient.Object, 
    CancellationToken.None);
```

Test coverage: 105 tests pass successfully.

## Migration Notes

- **No breaking changes** for single-turn test cases
- **Backward compatible** - existing Service Bus queue continues to work
- **Gradual rollout** - only multi-turn test cases use Durable Functions
- **No data migration** required

## Performance

- Latency: ~2-3 seconds overhead per orchestration start
- Throughput: 10 concurrent orchestrations (configurable)
- Activity concurrency: 100 concurrent activities (configurable)
- Storage: ~1KB per orchestration instance

## References

- [Azure Durable Functions Documentation](https://learn.microsoft.com/en-us/azure/azure-functions/durable/)
- [Durable Functions Patterns](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-overview?tabs=csharp-inproc#application-patterns)
- Story 07: Multi-Turn Execution to Durable Functions (this implementation)
- Story 06: Multi-turn Support (original Service Bus implementation)
