# Progress-Based Timeout Implementation Plan

**Date:** November 26, 2025  
**Status:** Planning Phase  
**Priority:** Medium  
**Complexity:** Low-Medium

## Overview

Convert the Monitor Function timeout mechanism from a hybrid approach (total runtime + progress-based) to a pure progress-based timeout system. This change makes evaluation runs more flexible by allowing them to run indefinitely as long as they continue making progress, while quickly detecting and terminating truly stalled evaluations.

## Current State

### Existing Timeout Mechanisms

The `MonitorFunction.ProcessEvaluationRunAsync` currently implements **two independent timeout checks**:

1. **Total Runtime Timeout** (lines ~90-104)
   - Uses: `EvaluationRun.StartTime`
   - Threshold: `MaxRunningTimeMinutes` (120 minutes default)
   - Logic: `DateTime.UtcNow - StartTime > 120 minutes` → mark as Errored
   - **Problem**: Kills evaluations making steady progress if total time > 2 hours

2. **No Progress Timeout** (lines ~107-149)
   - Uses: `EvaluationRun.LastProgressUpdate`
   - Threshold: `MaxNoProgressMinutes` (30 minutes, hard-coded constant)
   - Logic: `DateTime.UtcNow - LastProgressUpdate > 30 minutes` → mark as Errored
   - Fallback: Uses `StartTime` if `LastProgressUpdate` is null
   - **Status**: Already implemented and working correctly

### Configuration

**Current Settings** (`AppSettings.EvaluationSettings`):
```csharp
public int MaxRunningTimeMinutes { get; set; } = 120;
```

**Hard-coded Constant** (`MonitorFunction.cs`):
```csharp
private const int MaxNoProgressMinutes = 30;
```

## Problems with Current Implementation

### 1. Conflicting Timeout Philosophies
- Total runtime timeout assumes evaluations should complete in fixed time
- Progress timeout assumes evaluations can run indefinitely if making progress
- Having both creates confusion and unpredictable behavior

### 2. False Positive Timeouts
**Scenario:** Large evaluation with 10,000 test cases
- Starts at 10:00 AM
- Makes steady progress every 5 minutes
- At 12:01 PM (121 minutes), still has 2,000 test cases remaining
- **Result**: Marked as Errored despite healthy progress
- **Expected**: Should continue running until completion

### 3. Hard-coded Values
- `MaxNoProgressMinutes = 30` cannot be adjusted without code changes
- Different workloads may need different thresholds
- No runtime configurability

### 4. Architectural Misalignment
- System tracks progress via `LastProgressUpdate` field
- Progress percentage calculated and updated continuously
- But overall timeout ignores progress data
- Contradicts the progress-tracking architecture

## Proposed Solution

### Changes Summary

| Component | Current | Proposed |
|-----------|---------|----------|
| **Total Runtime Check** | Uses `StartTime`, 120 min limit | **REMOVE** |
| **Progress Timeout** | Hard-coded 30 min | **Make configurable** |
| **Configuration** | `MaxRunningTimeMinutes` | Rename to `MaxNoProgressMinutes` |
| **Behavior** | Kill after 2 hours regardless | Allow indefinite run with progress |

### Implementation Approach

**Philosophy:** "An evaluation can run as long as needed, but must show continuous progress"

**New Logic:**
1. Check if `LastProgressUpdate` is null
   - If null, use `StartTime` as fallback (evaluation hasn't started processing)
2. Calculate time since last progress: `DateTime.UtcNow - LastProgressUpdate`
3. If time exceeds configurable threshold → mark as Errored
4. Otherwise, let evaluation continue

**Benefits:**
- ✅ Evaluations can run for hours if making progress
- ✅ Stalled evaluations detected quickly (30 min default)
- ✅ Simpler logic (one timeout mechanism)
- ✅ Configurable threshold for different workloads
- ✅ Aligns with progress-tracking architecture

## Implementation Steps

### Step 1: Update Configuration Model

**File:** `backend/AiAgentEvaluation/Models/AppSettings.cs`

**Change:**
```csharp
// BEFORE
public int MaxRunningTimeMinutes { get; set; } = 120;

// AFTER
/// <summary>
/// Maximum time without progress before marking evaluation as stalled (minutes)
/// If an evaluation shows no progress updates for this duration, it's marked as Errored
/// Default: 30 minutes
/// Override via Azure app setting: Evaluation__MaxNoProgressMinutes
/// </summary>
public int MaxNoProgressMinutes { get; set; } = 30;
```

**Rationale:** 
- Clear naming that reflects progress-based detection
- XML documentation explains behavior
- Default 30 minutes matches current hard-coded value
- Configuration path documented for deployment

### Step 2: Update MonitorFunction

**File:** `backend/AiAgentEvaluation/Functions/MonitorFunction.cs`

#### 2A: Remove Hard-coded Constant

```csharp
// REMOVE THIS LINE
private const int MaxNoProgressMinutes = 30;
```

#### 2B: Remove Total Runtime Check

```csharp
// REMOVE THIS ENTIRE BLOCK (lines ~90-104)
// Check if evaluation has stalled (running longer than max time)
if (evaluationRun.StartTime.HasValue)
{
    var runningTime = DateTime.UtcNow - evaluationRun.StartTime.Value;
    var maxRunningTime = _settings.Evaluation.MaxRunningTimeMinutes;
    if (runningTime.TotalMinutes > maxRunningTime)
    {
        _logger.LogWarning("Evaluation run {EvaluationRunId} has been running for {Minutes} minutes (max: {MaxMinutes}), marking as errored",
            evaluationRun.Id, runningTime.TotalMinutes, maxRunningTime);
        
        await _dataverseService.UpdateEvaluationRunCompletionAsync(
            evaluationRun.Id,
            EvaluationRunStatus.Errored,
            null,
            false,
            DateTime.UtcNow,
            cancellationToken);
        return;
    }
}
```

#### 2C: Update Progress-Based Check

```csharp
// UPDATE THIS BLOCK to use configuration instead of constant
// Check if evaluation has made no progress for extended period
if (evaluationRun.LastProgressUpdate.HasValue)
{
    var timeSinceProgress = DateTime.UtcNow - evaluationRun.LastProgressUpdate.Value;
    var maxNoProgressMinutes = _settings.Evaluation.MaxNoProgressMinutes; // CHANGED: Use config
    if (timeSinceProgress.TotalMinutes > maxNoProgressMinutes)
    {
        _logger.LogWarning(
            "Evaluation run {EvaluationRunId} has made no progress for {Minutes} minutes (threshold: {MaxMinutes}), marking as errored",
            evaluationRun.Id, timeSinceProgress.TotalMinutes, maxNoProgressMinutes); // CHANGED: Updated message
        
        await _dataverseService.UpdateEvaluationRunCompletionAsync(
            evaluationRun.Id,
            EvaluationRunStatus.Errored,
            null,
            false,
            DateTime.UtcNow,
            cancellationToken);
        return;
    }
}
else if (evaluationRun.StartTime.HasValue)
{
    // If no progress update yet, use start time as baseline
    var timeSinceStart = DateTime.UtcNow - evaluationRun.StartTime.Value;
    var maxNoProgressMinutes = _settings.Evaluation.MaxNoProgressMinutes; // CHANGED: Use config
    if (timeSinceStart.TotalMinutes > maxNoProgressMinutes)
    {
        _logger.LogWarning(
            "Evaluation run {EvaluationRunId} has had no progress since start ({Minutes} minutes, threshold: {MaxMinutes}), marking as errored",
            evaluationRun.Id, timeSinceStart.TotalMinutes, maxNoProgressMinutes); // CHANGED: Updated message
        
        await _dataverseService.UpdateEvaluationRunCompletionAsync(
            evaluationRun.Id,
            EvaluationRunStatus.Errored,
            null,
            false,
            DateTime.UtcNow,
            cancellationToken);
        return;
    }
}
```

**Key Changes:**
- Use `_settings.Evaluation.MaxNoProgressMinutes` instead of constant
- Updated log messages to clarify progress-based detection
- Include threshold value in logs for troubleshooting

### Step 3: Update Configuration Files

#### 3A: Local Settings Example

**File:** `backend/AiAgentEvaluation/local.settings.example.json`

```json
{
  "Values": {
    // ... existing settings ...
    
    "Evaluation__MaxNoProgressMinutes": "30",
    // Note: MaxRunningTimeMinutes has been removed - evaluations now use only progress-based timeout
    
    // ... rest of settings ...
  }
}
```

#### 3B: Azure App Settings (Post-Deployment)

Update Function App configuration:
```powershell
# Remove old setting (or leave for backward compatibility during transition)
az functionapp config appsettings delete --name func-aiestagingmbwc --resource-group rg-agent-eval-staging --setting-names "Evaluation__MaxRunningTimeMinutes"

# Add new setting
az functionapp config appsettings set --name func-aiestagingmbwc --resource-group rg-agent-eval-staging --settings "Evaluation__MaxNoProgressMinutes=30"
```

### Step 4: Update Unit Tests

**File:** `backend/tests/AiAgentEvaluation.UnitTests/MonitorFunctionTests.cs`

#### Add New Test: Long Running but Progressing

```csharp
[Fact]
public async Task MonitorEvaluationRuns_LongRunningWithProgress_DoesNotTimeout()
{
    // Arrange - Evaluation running for 3 hours but made progress 5 minutes ago
    var evaluationRunId = Guid.NewGuid();
    var evaluationRun = new EvaluationRun
    {
        Id = evaluationRunId,
        StatusCode = EvaluationRunStatus.Running,
        TotalTestCases = 10000,
        CompletedTestCases = 8000,
        StartTime = DateTime.UtcNow.AddHours(-3), // Started 3 hours ago
        LastProgressUpdate = DateTime.UtcNow.AddMinutes(-5) // Progress 5 minutes ago
    };

    _mockDataverseService
        .Setup(x => x.GetRunningEvaluationRunsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EvaluationRun> { evaluationRun });

    // Act
    await _monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);

    // Assert - Should NOT be marked as errored (still in progress)
    _mockDataverseService.Verify(
        x => x.UpdateEvaluationRunCompletionAsync(
            evaluationRunId,
            EvaluationRunStatus.Errored,
            It.IsAny<decimal?>(),
            It.IsAny<bool>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Never,
        "Evaluation with recent progress should not timeout regardless of total runtime");
}
```

#### Update Existing Test: Stalled Detection

```csharp
[Fact]
public async Task MonitorEvaluationRuns_NoProgressForThreshold_MarksAsErrored()
{
    // Arrange - No progress for 35 minutes (threshold is 30)
    var evaluationRunId = Guid.NewGuid();
    var evaluationRun = new EvaluationRun
    {
        Id = evaluationRunId,
        StatusCode = EvaluationRunStatus.Running,
        TotalTestCases = 100,
        CompletedTestCases = 50,
        StartTime = DateTime.UtcNow.AddMinutes(-40),
        LastProgressUpdate = DateTime.UtcNow.AddMinutes(-35) // No progress for 35 minutes
    };

    _mockDataverseService
        .Setup(x => x.GetRunningEvaluationRunsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EvaluationRun> { evaluationRun });

    // Act
    await _monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);

    // Assert - Should be marked as errored due to lack of progress
    _mockDataverseService.Verify(
        x => x.UpdateEvaluationRunCompletionAsync(
            evaluationRunId,
            EvaluationRunStatus.Errored,
            null,
            false,
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Once,
        "Evaluation with no progress beyond threshold should be marked as errored");
}
```

#### Add Test: Configurable Threshold

```csharp
[Theory]
[InlineData(30)] // Default
[InlineData(60)] // Extended
[InlineData(15)] // Shortened
public async Task MonitorEvaluationRuns_RespectsConfigurableThreshold(int thresholdMinutes)
{
    // Arrange - Configure threshold
    var options = Options.Create(new AppSettings
    {
        Evaluation = new EvaluationSettings
        {
            MaxNoProgressMinutes = thresholdMinutes
        }
    });
    
    var monitorFunction = new MonitorFunction(_mockLogger.Object, _mockDataverseService.Object, _mockServiceBusService.Object, options);
    
    var evaluationRun = new EvaluationRun
    {
        Id = Guid.NewGuid(),
        StatusCode = EvaluationRunStatus.Running,
        TotalTestCases = 100,
        StartTime = DateTime.UtcNow.AddMinutes(-(thresholdMinutes + 5)),
        LastProgressUpdate = DateTime.UtcNow.AddMinutes(-(thresholdMinutes + 5))
    };

    _mockDataverseService
        .Setup(x => x.GetRunningEvaluationRunsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<EvaluationRun> { evaluationRun });

    // Act
    await monitorFunction.MonitorEvaluationRuns(timerInfo, cancellationToken);

    // Assert - Should timeout when exceeding configured threshold
    _mockDataverseService.Verify(
        x => x.UpdateEvaluationRunCompletionAsync(
            It.IsAny<Guid>(),
            EvaluationRunStatus.Errored,
            It.IsAny<decimal?>(),
            It.IsAny<bool>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

### Step 5: Update Documentation

See separate section below for comprehensive documentation updates.

## Testing Strategy

### Unit Tests (Required)
- ✅ Long-running evaluation with progress does not timeout
- ✅ Stalled evaluation (no progress) times out correctly
- ✅ Configurable threshold value respected
- ✅ Fallback to StartTime when LastProgressUpdate is null
- ✅ Log messages include threshold values

### Integration Tests (Recommended)
1. **Large Evaluation Run**
   - Start evaluation with 1000+ test cases
   - Monitor for > 2 hours
   - Verify it completes successfully if making progress

2. **Stalled Evaluation Detection**
   - Start evaluation
   - Disable worker function processing
   - Verify timeout after configured threshold

3. **Configuration Override**
   - Deploy with custom `MaxNoProgressMinutes` value
   - Verify timeout behavior matches configuration

### Manual Testing Scenarios
1. Start evaluation in staging environment
2. Monitor Application Insights for timeout warnings
3. Verify log messages show progress-based detection
4. Test configuration changes without redeployment

## Deployment Plan

### Pre-Deployment Checklist
- [ ] All unit tests passing
- [ ] Integration tests validated (if applicable)
- [ ] Documentation updated (see below)
- [ ] Configuration changes reviewed
- [ ] Rollback plan documented

### Deployment Steps

#### 1. Update Code
```powershell
cd backend/AiAgentEvaluation
# Make code changes per implementation steps above
```

#### 2. Run Tests
```powershell
dotnet build --configuration Release
dotnet test ../tests/AiAgentEvaluation.UnitTests/AiAgentEvaluation.UnitTests.csproj
```

#### 3. Update Configuration (Before Deployment)
```powershell
# Add new setting to Azure Function App
az functionapp config appsettings set `
  --name func-aiestagingmbwc `
  --resource-group rg-agent-eval-staging `
  --settings "Evaluation__MaxNoProgressMinutes=30"
```

#### 4. Deploy Function App
```powershell
# Follow standard deployment process from .github/copilot-instructions.md
dotnet publish --configuration Release --runtime win-x64 --self-contained false --output .\publish
cd publish
Compress-Archive -Path * -DestinationPath ..\deploy.zip -Force
cd ..
az functionapp deployment source config-zip --resource-group rg-agent-eval-staging --name func-aiestagingmbwc --src deploy.zip
```

#### 5. Verify Deployment
```powershell
# Check health endpoint
curl https://func-aiestagingmbwc.azurewebsites.net/api/health

# Monitor Application Insights for timeout behavior
# Query for: traces | where message contains "no progress"
```

#### 6. Cleanup Old Configuration (Optional)
```powershell
# After verifying new behavior works
az functionapp config appsettings delete `
  --name func-aiestagingmbwc `
  --resource-group rg-agent-eval-staging `
  --setting-names "Evaluation__MaxRunningTimeMinutes"
```

## Rollback Plan

If issues arise after deployment:

### Option 1: Quick Config Rollback
```powershell
# Increase threshold to effectively disable timeout
az functionapp config appsettings set `
  --name func-aiestagingmbwc `
  --resource-group rg-agent-eval-staging `
  --settings "Evaluation__MaxNoProgressMinutes=1440" # 24 hours
```

### Option 2: Code Rollback
1. Revert code changes in MonitorFunction.cs
2. Restore `MaxRunningTimeMinutes` setting
3. Redeploy previous version
4. Restore Azure configuration

## Monitoring & Observability

### Application Insights Queries

#### Monitor Progress-Based Timeouts
```kusto
traces
| where timestamp > ago(24h)
| where message contains "no progress"
| project timestamp, message, customDimensions.EvaluationRunId, customDimensions.Minutes
| order by timestamp desc
```

#### Track Evaluation Durations
```kusto
traces
| where timestamp > ago(7d)
| where message contains "completed successfully"
| extend Duration = tostring(customDimensions.Duration)
| summarize 
    AvgDuration = avg(todouble(Duration)),
    MaxDuration = max(todouble(Duration)),
    Count = count()
| project AvgDuration, MaxDuration, Count
```

#### Identify Long-Running Evaluations
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Processing evaluation run"
| extend 
    EvalId = tostring(customDimensions.EvaluationRunId),
    Completed = toint(customDimensions.CompletedTestCases),
    Total = toint(customDimensions.TotalTestCases)
| summarize 
    LastSeen = max(timestamp),
    Progress = max(Completed) * 100.0 / max(Total)
    by EvalId
| where (now() - LastSeen) > 30m // Not seen in 30 minutes
| project EvalId, LastSeen, Progress, MinutesSinceLastSeen = (now() - LastSeen) / 1m
| order by MinutesSinceLastSeen desc
```

### Alerts (Recommended)

Create Application Insights alert for frequent timeouts:
```kusto
traces
| where timestamp > ago(1h)
| where message contains "no progress"
| summarize TimeoutCount = count()
| where TimeoutCount > 5 // Alert if > 5 timeouts per hour
```

## Risk Assessment

### Low Risk
- ✅ Progress tracking already implemented and tested
- ✅ Fallback logic prevents false positives
- ✅ Configuration-driven, easy to adjust
- ✅ Simpler code with fewer edge cases
- ✅ No database schema changes required

### Potential Issues & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Long evaluations never timeout | Low | Medium | Default 30-min threshold catches stalls |
| Configuration not loaded | Low | High | Validate config in health check |
| False positives on slow progress | Low | Low | Threshold adjustable via config |
| Breaking change for existing runs | Low | Low | Applies only to new monitoring cycles |

## Success Criteria

### Functional
- ✅ Evaluations making progress are not terminated
- ✅ Stalled evaluations detected within threshold period
- ✅ Configuration value properly applied
- ✅ Log messages clearly indicate progress-based timeout

### Performance
- ✅ No degradation in monitor function execution time
- ✅ Same frequency of monitoring (every 1 minute)

### Operational
- ✅ Fewer false-positive timeouts
- ✅ Clear logs for troubleshooting
- ✅ Configuration adjustable without redeployment

## Future Enhancements

### Adaptive Thresholds
- Adjust timeout based on evaluation size
- Smaller evaluations: 15-minute threshold
- Larger evaluations: 60-minute threshold

### Progress Rate Detection
- Track rate of progress (test cases per minute)
- Timeout if rate drops below minimum threshold
- Useful for detecting degraded performance

### Proactive Alerts
- Notification when evaluation approaches timeout
- "Evaluation XYZ has made no progress for 25 minutes (threshold: 30 minutes)"
- Allows intervention before termination

## References

### Related Documentation
- `docs/ARCHITECTURE.md` - Monitor Function architecture
- `docs/prd/PRD-Agent-Evaluation-Function.md` - Monitoring requirements
- `docs/stories/03-monitor-function.md` - Monitor function stories
- `docs/implementation/IMPLEMENTATION_SUMMARY.md` - Previous implementations

### Related Code Files
- `backend/AiAgentEvaluation/Functions/MonitorFunction.cs`
- `backend/AiAgentEvaluation/Models/AppSettings.cs`
- `backend/AiAgentEvaluation/Models/EvaluationRun.cs`
- `backend/AiAgentEvaluation/Services/DataverseService.cs`

### Related Issues/Stories
- Story 03: Monitor Function implementation
- Story 08: Progress tracking enhancement
- Gap Analysis: Stall detection improvements

---

**Document Version:** 1.0  
**Last Updated:** November 26, 2025  
**Author:** Planning Agent  
**Status:** Ready for Implementation
