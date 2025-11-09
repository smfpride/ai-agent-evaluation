# Production Monitoring Configuration

## Application Insights Alerts

### Critical Alerts (P1 - Immediate Response)

#### Function App Down
```json
{
  "name": "FunctionApp-Down",
  "description": "Function app is not responding",
  "severity": 0,
  "enabled": true,
  "evaluationFrequency": "PT1M",
  "windowSize": "PT5M",
  "criteria": {
    "allOf": [
      {
        "metricName": "Http5xx",
        "dimensions": [],
        "operator": "GreaterThan",
        "threshold": 10,
        "timeAggregation": "Total"
      }
    ]
  },
  "actions": [
    {
      "actionGroupId": "/subscriptions/{subscription-id}/resourceGroups/{rg}/providers/microsoft.insights/actionGroups/critical-alerts"
    }
  ]
}
```

#### High Failure Rate
```json
{
  "name": "HighFailureRate",
  "description": "More than 95% of evaluation runs are failing",
  "severity": 0,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "query": "customMetrics | where name == 'EvaluationRunFailureRate' | where value > 95",
        "timeAggregation": "Average",
        "operator": "GreaterThan",
        "threshold": 0
      }
    ]
  }
}
```

#### Managed Identity Authentication Failures
```json
{
  "name": "ManagedIdentityAuthFailure",
  "description": "Managed identity authentication is failing",
  "severity": 0,
  "enabled": true,
  "evaluationFrequency": "PT1M",
  "windowSize": "PT5M",
  "criteria": {
    "allOf": [
      {
        "query": "traces | where message contains 'Authentication failed' and message contains 'DefaultAzureCredential' | summarize count()",
        "timeAggregation": "Total",
        "operator": "GreaterThan",
        "threshold": 5
      }
    ]
  }
}
```

#### Azure OpenAI Quota Exhausted
```json
{
  "name": "OpenAIQuotaExhausted",
  "description": "Azure OpenAI quota has been exhausted",
  "severity": 0,
  "enabled": true,
  "evaluationFrequency": "PT1M",
  "windowSize": "PT5M",
  "criteria": {
    "allOf": [
      {
        "query": "exceptions | where customDimensions.StatusCode == '429' and customDimensions.Service == 'AzureOpenAI' | summarize count()",
        "timeAggregation": "Total",
        "operator": "GreaterThan",
        "threshold": 10
      }
    ]
  }
}
```

#### Service Bus Dead Letter Queue Threshold
```json
{
  "name": "ServiceBusDeadLetterThreshold",
  "description": "Too many messages in dead letter queue",
  "severity": 0,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "metricName": "DeadletteredMessages",
        "metricNamespace": "Microsoft.ServiceBus/namespaces",
        "operator": "GreaterThan",
        "threshold": 100,
        "timeAggregation": "Average"
      }
    ]
  }
}
```

### Warning Alerts (P2 - Investigation within 1 hour)

#### Elevated Error Rate
```json
{
  "name": "ElevatedErrorRate",
  "description": "Error rate is above 10%",
  "severity": 2,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "query": "requests | summarize FailureRate = 100.0 * countif(success == false) / count() | where FailureRate > 10",
        "timeAggregation": "Average",
        "operator": "GreaterThan",
        "threshold": 0
      }
    ]
  }
}
```

#### Performance Degradation
```json
{
  "name": "PerformanceDegradation",
  "description": "Test case processing time exceeds 5 minutes",
  "severity": 2,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "query": "customMetrics | where name == 'TestCaseProcessingTime' | where value > 300000 | summarize count()",
        "timeAggregation": "Total",
        "operator": "GreaterThan",
        "threshold": 10
      }
    ]
  }
}
```

#### Service Bus Backlog Building
```json
{
  "name": "ServiceBusBacklog",
  "description": "Service Bus queue has significant backlog",
  "severity": 2,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "metricName": "ActiveMessages",
        "metricNamespace": "Microsoft.ServiceBus/namespaces",
        "operator": "GreaterThan",
        "threshold": 5000,
        "timeAggregation": "Average"
      }
    ]
  }
}
```

#### High P95 Latency
```json
{
  "name": "HighP95Latency",
  "description": "P95 latency is elevated",
  "severity": 2,
  "enabled": true,
  "evaluationFrequency": "PT5M",
  "windowSize": "PT15M",
  "criteria": {
    "allOf": [
      {
        "query": "requests | summarize percentile(duration, 95) | where percentile_duration_95 > 30000",
        "timeAggregation": "Average",
        "operator": "GreaterThan",
        "threshold": 0
      }
    ]
  }
}
```

### Informational Alerts (P3 - Daily Review)

#### Daily Evaluation Summary
```json
{
  "name": "DailyEvaluationSummary",
  "description": "Daily summary of evaluation runs",
  "severity": 3,
  "enabled": true,
  "evaluationFrequency": "P1D",
  "windowSize": "P1D",
  "criteria": {
    "allOf": [
      {
        "query": "customEvents | where name == 'EvaluationRunCompleted' | summarize TotalRuns=count(), FailedRuns=countif(customDimensions.Status == 'Failed'), AvgTestCases=avg(toint(customDimensions.TestCaseCount))",
        "timeAggregation": "Total",
        "operator": "GreaterThan",
        "threshold": 0
      }
    ]
  }
}
```

#### Cost Anomaly Detection
```json
{
  "name": "CostAnomaly",
  "description": "Unusual cost increase detected",
  "severity": 3,
  "enabled": true,
  "evaluationFrequency": "P1D",
  "windowSize": "P1D",
  "criteria": {
    "allOf": [
      {
        "query": "// Cost tracking query - integrate with Azure Cost Management",
        "timeAggregation": "Total",
        "operator": "GreaterThan",
        "threshold": 1000
      }
    ]
  }
}
```

## Application Insights Dashboards

### Operations Dashboard

```json
{
  "name": "AI-Agent-Evaluation-Operations",
  "tiles": [
    {
      "title": "Function Execution Rate",
      "query": "requests | summarize count() by bin(timestamp, 5m), name",
      "visualization": "timechart"
    },
    {
      "title": "Error Rate by Function",
      "query": "requests | summarize ErrorRate = 100.0 * countif(success == false) / count() by name",
      "visualization": "barchart"
    },
    {
      "title": "Service Bus Queue Depth",
      "query": "customMetrics | where name == 'ServiceBusQueueDepth' | project timestamp, value",
      "visualization": "timechart"
    },
    {
      "title": "Active Evaluation Runs",
      "query": "customMetrics | where name == 'ActiveEvaluationRuns' | project timestamp, value",
      "visualization": "timechart"
    },
    {
      "title": "Function Instance Count",
      "query": "performanceCounters | where name == 'Instance Count' | project timestamp, value",
      "visualization": "timechart"
    },
    {
      "title": "Recent Errors",
      "query": "exceptions | order by timestamp desc | take 10 | project timestamp, type, outerMessage",
      "visualization": "table"
    }
  ]
}
```

### Performance Dashboard

```json
{
  "name": "AI-Agent-Evaluation-Performance",
  "tiles": [
    {
      "title": "Test Case Processing Time (Avg)",
      "query": "customMetrics | where name == 'TestCaseProcessingTime' | summarize avg(value) by bin(timestamp, 5m)",
      "visualization": "timechart"
    },
    {
      "title": "Throughput (Test Cases per Minute)",
      "query": "customMetrics | where name == 'TestCasesProcessed' | summarize sum(value) by bin(timestamp, 1m)",
      "visualization": "timechart"
    },
    {
      "title": "Auto-Scale Instance Count",
      "query": "performanceCounters | where name == 'Instance Count' | project timestamp, value",
      "visualization": "timechart"
    },
    {
      "title": "Latency Percentiles",
      "query": "requests | summarize P50=percentile(duration, 50), P95=percentile(duration, 95), P99=percentile(duration, 99) by bin(timestamp, 5m)",
      "visualization": "timechart"
    },
    {
      "title": "Azure OpenAI Response Time",
      "query": "dependencies | where target contains 'openai' | summarize avg(duration) by bin(timestamp, 5m)",
      "visualization": "timechart"
    },
    {
      "title": "Service Bus Processing Latency",
      "query": "customMetrics | where name == 'ServiceBusProcessingLatency' | summarize avg(value) by bin(timestamp, 5m)",
      "visualization": "timechart"
    }
  ]
}
```

### Cost Dashboard

```json
{
  "name": "AI-Agent-Evaluation-Cost",
  "tiles": [
    {
      "title": "Daily Cost Trend",
      "query": "// Integrate with Azure Cost Management API",
      "visualization": "timechart"
    },
    {
      "title": "Cost per Evaluation Run",
      "query": "customMetrics | where name == 'CostPerEvaluationRun' | summarize avg(value) by bin(timestamp, 1d)",
      "visualization": "timechart"
    },
    {
      "title": "Cost per Test Case",
      "query": "customMetrics | where name == 'CostPerTestCase' | summarize avg(value) by bin(timestamp, 1d)",
      "visualization": "timechart"
    },
    {
      "title": "Resource Utilization",
      "query": "performanceCounters | where name in ('CPU', 'Memory') | summarize avg(value) by name, bin(timestamp, 5m)",
      "visualization": "timechart"
    },
    {
      "title": "Azure OpenAI Token Usage",
      "query": "customMetrics | where name == 'OpenAITokensUsed' | summarize sum(value) by bin(timestamp, 1h)",
      "visualization": "timechart"
    }
  ]
}
```

## Custom Metrics to Track

### Business Metrics
- `EvaluationRunsStarted` - Counter of evaluation runs initiated
- `EvaluationRunsCompleted` - Counter of evaluation runs completed
- `EvaluationRunsFailed` - Counter of evaluation runs that failed
- `TestCasesProcessed` - Counter of test cases evaluated
- `TestCasesFailed` - Counter of test case evaluations that failed
- `AverageEvaluationScore` - Average score across all evaluations
- `CostPerEvaluationRun` - Estimated cost per evaluation run
- `CostPerTestCase` - Estimated cost per test case

### Performance Metrics
- `TestCaseProcessingTime` - Time to process a single test case (ms)
- `OrchestratorResponseTime` - Orchestrator function response time (ms)
- `ServiceBusProcessingLatency` - Time from message enqueue to process start (ms)
- `DataverseQueryTime` - Time for Dataverse queries (ms)
- `AzureOpenAIResponseTime` - Time for Azure OpenAI evaluation calls (ms)
- `ServiceBusQueueDepth` - Current number of messages in queue
- `ActiveEvaluationRuns` - Number of evaluation runs currently processing

### Reliability Metrics
- `EvaluationRunFailureRate` - Percentage of evaluation runs failing
- `TestCaseFailureRate` - Percentage of test case evaluations failing
- `RetryCount` - Number of retries performed
- `CircuitBreakerTrips` - Number of circuit breaker activations
- `DeadLetterMessageCount` - Messages moved to dead letter queue

## Kusto Query Library

### Common Queries

#### Find Slow Evaluation Runs
```kusto
customMetrics
| where name == "EvaluationRunDuration"
| where value > 3600000 // Over 1 hour
| project timestamp, EvaluationRunId=tostring(customDimensions.EvaluationRunId), DurationMinutes=value/60000
| order by DurationMinutes desc
```

#### Identify Error Patterns
```kusto
exceptions
| summarize Count=count() by Type=type, Message=outerMessage
| order by Count desc
| take 20
```

#### Trace Evaluation Run End-to-End
```kusto
let runId = "your-evaluation-run-id";
union
  (requests | where customDimensions.EvaluationRunId == runId),
  (traces | where customDimensions.EvaluationRunId == runId),
  (exceptions | where customDimensions.EvaluationRunId == runId),
  (dependencies | where customDimensions.EvaluationRunId == runId)
| order by timestamp asc
| project timestamp, itemType, name, message, duration
```

#### Monitor Service Bus Health
```kusto
customMetrics
| where name in ("ServiceBusQueueDepth", "ServiceBusProcessingLatency", "DeadLetterMessageCount")
| summarize avg(value) by name, bin(timestamp, 5m)
| render timechart
```

#### Calculate Success Rate by Test Type
```kusto
customEvents
| where name == "TestCaseCompleted"
| extend TestType = tostring(customDimensions.TestType), Success = tobool(customDimensions.Success)
| summarize SuccessRate = 100.0 * countif(Success) / count() by TestType
```

## Deployment Script

### PowerShell Script to Configure Monitoring

```powershell
# configure-monitoring.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$true)]
    [string]$AppInsightsName,
    
    [Parameter(Mandatory=$true)]
    [string]$ActionGroupName
)

Write-Host "Configuring monitoring for $AppInsightsName..." -ForegroundColor Cyan

# Get Application Insights resource
$appInsights = Get-AzApplicationInsights -ResourceGroupName $ResourceGroupName -Name $AppInsightsName

if (-not $appInsights) {
    Write-Error "Application Insights resource not found"
    exit 1
}

# Create action group for critical alerts
Write-Host "Creating action group for critical alerts..." -ForegroundColor Yellow
$actionGroup = Get-AzActionGroup -ResourceGroupName $ResourceGroupName -Name $ActionGroupName -ErrorAction SilentlyContinue

if (-not $actionGroup) {
    $emailReceiver = New-AzActionGroupReceiver -Name "OpsTeam" -EmailReceiver -EmailAddress "ops@example.com"
    $actionGroup = Set-AzActionGroup -ResourceGroupName $ResourceGroupName -Name $ActionGroupName -ShortName "CritAlerts" -Receiver $emailReceiver
    Write-Host "Action group created successfully" -ForegroundColor Green
} else {
    Write-Host "Action group already exists" -ForegroundColor Yellow
}

# Create metric alerts
Write-Host "Creating metric alerts..." -ForegroundColor Yellow

# High Failure Rate Alert
$condition = New-AzMetricAlertRuleV2Criteria -MetricName "Http5xx" -TimeAggregation Total -Operator GreaterThan -Threshold 10
Add-AzMetricAlertRuleV2 `
    -Name "HighFailureRate" `
    -ResourceGroupName $ResourceGroupName `
    -TargetResourceId $appInsights.Id `
    -Condition $condition `
    -WindowSize (New-TimeSpan -Minutes 5) `
    -Frequency (New-TimeSpan -Minutes 1) `
    -Severity 0 `
    -ActionGroupId $actionGroup.Id `
    -ErrorAction SilentlyContinue

Write-Host "Monitoring configuration completed successfully!" -ForegroundColor Green
```

## Usage Instructions

### Setup Monitoring

```powershell
# Configure monitoring after deployment
cd c:\dev\ai-agent-evaluation\backend\infrastructure
.\configure-monitoring.ps1 -ResourceGroupName "rg-agent-eval-prod" -AppInsightsName "appi-agent-eval-prod" -ActionGroupName "ag-critical-alerts"
```

### View Dashboards

1. Navigate to Azure Portal
2. Go to Application Insights resource
3. Select "Dashboards" from left menu
4. Import dashboard JSON files
5. Pin to Azure Dashboard for easy access

### Test Alerts

```powershell
# Test critical alert by triggering failures
# This should only be done in non-production environments
Invoke-RestMethod -Uri "https://your-function-app.azurewebsites.net/api/health" -Method Get
```

---

**Last Updated**: November 8, 2025  
**Review Frequency**: Monthly or after significant changes  
**Owner**: Operations Team
