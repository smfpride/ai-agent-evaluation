# Multi-Turn Service Bus Queue Issue - Resolution Summary

## Issue Description
**Error:** `ServiceBusException: The messaging entity 'multi-turn-test-case-processing' could not be found`

**Root Cause:** The multi-turn worker function was configured to listen to a queue named `multi-turn-test-case-processing`, but this queue was not created in the Azure infrastructure.

## Resolution

### 1. Infrastructure Updates ✅

**Updated File:** `backend/infrastructure/main.bicep`

Added a second Service Bus queue for multi-turn test case processing:

```bicep
// Create Service Bus Queue for multi-turn test cases
resource multiTurnTestCaseProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'multi-turn-test-case-processing'
  properties: {
    maxSizeInMegabytes: 5120
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    enablePartitioning: false
    requiresSession: false
  }
}
```

**Deployed:** Queue created directly using Azure CLI:
```bash
az servicebus queue create \
  --resource-group rg-agent-eval-staging \
  --namespace-name sbus-aiestagingmbwc \
  --name multi-turn-test-case-processing \
  --max-size 5120 \
  --default-message-time-to-live P14D \
  --enable-dead-lettering-on-message-expiration true \
  --lock-duration PT5M \
  --max-delivery-count 10
```

### 2. Service Layer Updates ✅

**Updated File:** `backend/AiAgentEvaluation/Services/IServiceBusService.cs`

Added interface methods for multi-turn message sending:
```csharp
Task SendMultiTurnTestCaseMessageAsync(ProcessMultiTurnTestCaseMessage message, CancellationToken cancellationToken = default);
Task SendBatchMultiTurnTestCaseMessagesAsync(IEnumerable<ProcessMultiTurnTestCaseMessage> messages, CancellationToken cancellationToken = default);
```

**Updated File:** `backend/AiAgentEvaluation/Services/ServiceBusService.cs`

- Added `_multiTurnSender` field to manage the separate queue sender
- Implemented `SendMultiTurnTestCaseMessageAsync` method
- Implemented `SendBatchMultiTurnTestCaseMessagesAsync` method
- Updated constructor to create sender for multi-turn queue
- Updated `Dispose` method to dispose multi-turn sender

### 3. Orchestrator Updates ✅

**Updated File:** `backend/AiAgentEvaluation/Functions/OrchestratorFunction.cs`

Changed from logging multi-turn messages to actually sending them:

**Before:**
```csharp
if (multiTurnMessages.Any())
{
    _logger.LogInformation("Multi-turn test cases detected: {Count} (queue implementation pending)", multiTurnMessages.Count);
    // TODO: Implement SendBatchMultiTurnTestCaseMessagesAsync
}
```

**After:**
```csharp
if (multiTurnMessages.Any())
{
    await _serviceBusService.SendBatchMultiTurnTestCaseMessagesAsync(multiTurnMessages, cancellationToken);
    _logger.LogInformation("Sent {Count} multi-turn test case messages", multiTurnMessages.Count);
}
```

### 4. Deployment ✅

1. ✅ Built solution in Release mode
2. ✅ Created multi-turn queue in Service Bus namespace
3. ✅ Deployed updated function app to staging
4. ✅ Verified both queues exist:
   - `test-case-processing` (single-turn)
   - `multi-turn-test-case-processing` (multi-turn)
5. ✅ Verified function app is healthy

## Verification

### Queue Status
```bash
$ az servicebus queue list --resource-group rg-agent-eval-staging \
  --namespace-name sbus-aiestagingmbwc --query "[].name" --output table

Result
-------------------------------
multi-turn-test-case-processing
test-case-processing
```

### Function App Status
```bash
$ curl https://func-aiestagingmbwc.azurewebsites.net/api/health/basic

{"status":"Healthy","timestamp":"2025-11-10T03:13:24.3144332Z","version":"1.0.0","message":"Function app is running"}
```

### Deployed Functions
- ✅ `ProcessTestCase` - Single-turn test case processing (listens to `test-case-processing`)
- ✅ `ProcessMultiTurnTestCase` - Multi-turn test case processing (listens to `multi-turn-test-case-processing`)
- ✅ `StartEvaluationRun` - Orchestrator to initiate evaluation runs
- ✅ Health check endpoints
- ✅ Monitor function

## Architecture

### Message Flow

```
Orchestrator Function
├─> Separates single-turn and multi-turn test cases
├─> Single-turn messages → test-case-processing queue
│   └─> ProcessTestCase function receives and processes
└─> Multi-turn messages → multi-turn-test-case-processing queue
    └─> ProcessMultiTurnTestCase function receives and processes
```

### Why Separate Queues?

1. **Independent Scaling**: Multi-turn test cases may take longer and can scale independently
2. **Monitoring**: Easier to monitor single vs multi-turn processing separately
3. **Retry Logic**: Different retry strategies for different test types
4. **Message Routing**: Clear separation of concerns in the architecture

## Testing Next Steps

Now that the infrastructure is fixed, you can:

1. **Create Test Data** - Follow `docs/deployment/multi-turn-test-data-setup.md`:
   - Create multi-turn test cases in Dataverse
   - Add turns with proper sequence numbers
   - Configure Direct Line endpoints

2. **Run Test Evaluation**:
   ```bash
   curl -X POST https://func-aiestagingmbwc.azurewebsites.net/api/evaluations/start \
     -H "Content-Type: application/json" \
     -d '{"testRunId": "<your-test-run-id>"}'
   ```

3. **Monitor Execution** - Check Application Insights:
   ```kusto
   traces
   | where message contains "multi-turn" or message contains "MultiTurn"
   | where timestamp > ago(30m)
   | order by timestamp desc
   ```

4. **Verify Results** - Query Dataverse:
   - Individual turn results (isaggregate = false)
   - Aggregate results (isaggregate = true)
   - Verify conversationId is consistent across turns

## Files Changed

### Infrastructure
- ✅ `backend/infrastructure/main.bicep` - Added multi-turn queue definition
- ✅ `backend/infrastructure/main.parameters.staging.json` - Removed invalid `appName` parameter

### Code
- ✅ `backend/AiAgentEvaluation/Services/IServiceBusService.cs` - Added interface methods
- ✅ `backend/AiAgentEvaluation/Services/ServiceBusService.cs` - Implemented multi-turn sending
- ✅ `backend/AiAgentEvaluation/Functions/OrchestratorFunction.cs` - Enabled multi-turn message sending

### Documentation
- ✅ `docs/deployment/multi-turn-test-data-setup.md` - Test data creation guide
- ✅ `docs/deployment/multi-turn-staging-validation.md` - Validation checklist

## Status

✅ **Issue Resolved** - The Service Bus queue error is fixed and the multi-turn functionality is fully deployed to staging.

**Ready for:** Test data creation and validation with real Copilot Studio agent.

---

**Date:** November 9, 2025  
**Environment:** Staging (rg-agent-eval-staging)  
**Deployment:** Successful
