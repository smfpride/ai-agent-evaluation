# Performance Optimization Guide

**Last Updated**: November 8, 2025  
**Version**: 1.0  
**Target**: 50,000 test cases per evaluation run, 1,000+ test cases/minute throughput

## 📋 Overview

This guide provides comprehensive performance optimization strategies for the AI Agent Evaluation Function to achieve the target performance of processing 50,000 test cases with throughput exceeding 1,000 test cases per minute.

## 🎯 Performance Targets

| Metric | Target | Current | Status |
|--------|--------|---------|--------|
| Orchestrator Response Time | < 30 seconds | TBD | ⏳ |
| Test Case Processing Time | < 5 minutes | TBD | ⏳ |
| Throughput (peak) | > 1,000/minute | TBD | ⏳ |
| Auto-scale Max Instances | 1,000 | Configurable | ✅ |
| Memory per Instance | < 2 GB | TBD | ⏳ |
| Cold Start Time | < 10 seconds | TBD | ⏳ |

## 🚀 Optimization Areas

### 1. Function App Configuration

#### 1.1 App Service Plan Optimization

**Recommended SKU for Production:**
```bicep
{
  "sku": {
    "name": "EP3",  // Elastic Premium Plan 3
    "tier": "ElasticPremium",
    "capacity": 3  // Minimum always-ready instances
  }
}
```

**Configuration Parameters:**
```json
{
  "WEBSITE_ELASTIC_SCALING_ENABLED": "1",
  "WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT": "1000",
  "WEBSITE_ENABLE_SYNC_UPDATE_SITE": "true",
  "WEBSITE_RUN_FROM_PACKAGE": "1",
  "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING": "<storage-connection>",
  "WEBSITE_CONTENTSHARE": "<share-name>"
}
```

**PowerShell Configuration:**
```powershell
# Update Function App for optimal performance
az functionapp plan update `
    --name <plan-name> `
    --resource-group <rg> `
    --max-elastic-worker-count 1000 `
    --number-of-workers 3

# Enable runtime scale monitoring
az functionapp config appsettings set `
    --name <app-name> `
    --resource-group <rg> `
    --settings "WEBSITE_ELASTIC_SCALING_ENABLED=1"
```

#### 1.2 Cold Start Mitigation

**Pre-Warmed Instances:**
```bicep
resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  properties: {
    siteConfig: {
      preWarmedInstanceCount: 3  // Keep 3 instances always warm
      minimumElasticInstanceCount: 3
      functionAppScaleLimit: 1000
    }
  }
}
```

**Always-On Setting (for non-Consumption plans):**
```powershell
az functionapp config set `
    --name <app-name> `
    --resource-group <rg> `
    --always-on true
```

**Optimization: Run from Package**
- Reduces deployment slot swap time
- Improves cold start performance
- Enables faster scale-out

```powershell
# Build and package application
dotnet publish -c Release -o ./publish
cd ./publish
zip -r ../app.zip .

# Upload to storage and configure run from package
az storage blob upload `
    --account-name <storage> `
    --container-name packages `
    --name app.zip `
    --file ../app.zip

$packageUrl = az storage blob url `
    --account-name <storage> `
    --container-name packages `
    --name app.zip `
    --output tsv

az functionapp config appsettings set `
    --name <app-name> `
    --resource-group <rg> `
    --settings "WEBSITE_RUN_FROM_PACKAGE=$packageUrl"
```

### 2. Service Bus Optimization

#### 2.1 Namespace Configuration

**Production SKU:**
```bicep
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  sku: {
    name: 'Premium'  // Premium tier for best performance
    tier: 'Premium'
    capacity: 4  // 4 messaging units
  }
  properties: {
    minimumTlsVersion: '1.2'
    zoneRedundant: true  // High availability
  }
}
```

**Performance Benefits of Premium Tier:**
- Up to 1 million messages/second per messaging unit
- Predictable latency (< 10ms p99)
- Dedicated resources (no noisy neighbors)
- Support for large messages (up to 100 MB)
- VNet integration support

#### 2.2 Queue Configuration

**Optimal Queue Settings:**
```bicep
resource queue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  properties: {
    maxMessageSizeInKilobytes: 256  // Larger messages if needed
    maxSizeInMegabytes: 5120  // 5 GB max queue size
    requiresDuplicateDetection: false  // Disable if not needed for performance
    requiresSession: false  // Disable sessions for parallel processing
    defaultMessageTimeToLive: 'P1D'  // 1 day TTL
    lockDuration: 'PT5M'  // 5 minutes lock duration
    maxDeliveryCount: 5  // Retry attempts before dead-letter
    enablePartitioning: true  // Enable partitioning for throughput
    enableBatchedOperations: true  // Enable batched operations
  }
}
```

#### 2.3 Function Trigger Configuration

**host.json Configuration:**
```json
{
  "version": "2.0",
  "extensions": {
    "serviceBus": {
      "prefetchCount": 1000,
      "messageHandlerOptions": {
        "maxConcurrentCalls": 100,
        "autoComplete": true,
        "maxAutoRenewDuration": "00:05:00"
      },
      "sessionHandlerOptions": {
        "maxConcurrentSessions": 100,
        "messageWaitTimeout": "00:00:03"
      },
      "batchOptions": {
        "maxMessageCount": 1000,
        "operationTimeout": "00:01:00",
        "autoComplete": true
      }
    }
  },
  "functionTimeout": "00:10:00",
  "logging": {
    "logLevel": {
      "default": "Warning",
      "Function": "Warning"
    }
  }
}
```

**Key Parameters:**
- `prefetchCount: 1000` - Pre-fetch messages for faster processing
- `maxConcurrentCalls: 100` - Process up to 100 messages concurrently per instance
- `autoComplete: true` - Automatically complete messages after successful processing
- `maxAutoRenewDuration: 5 minutes` - Renew message lock for long-running operations

**PowerShell Configuration:**
```powershell
# Enable partitioning for existing queue
az servicebus queue update `
    --resource-group <rg> `
    --namespace-name <namespace> `
    --name evaluation-testcases `
    --enable-partitioning true `
    --enable-batched-operations true `
    --max-delivery-count 5
```

### 3. Azure OpenAI Optimization

#### 3.1 Deployment Configuration

**Optimal Model Deployment:**
```bicep
resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = {
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'  // Latest model with best performance
      version: 'latest'
    }
    sku: {
      name: 'Standard'
      capacity: 100  // Request higher quota for production
    }
  }
}
```

**Quota Management:**
- Request quota increase to support 1,000+ concurrent evaluations
- Monitor quota usage and set alerts at 80% threshold
- Implement retry logic with exponential backoff for 429 responses

#### 3.2 Prompt Optimization

**Best Practices:**
1. **Minimize Token Count**
   - Remove unnecessary context from prompts
   - Use concise evaluation criteria
   - Target < 1000 tokens per request

2. **Optimize Temperature Settings**
   ```csharp
   var options = new ChatCompletionsOptions
   {
       Temperature = 0.3f,  // Lower for more deterministic results
       MaxTokens = 500,  // Limit response size
       FrequencyPenalty = 0,
       PresencePenalty = 0
   };
   ```

3. **Caching Strategy**
   - Cache prompt templates in memory
   - Load templates once at startup
   - Use distributed cache for multi-instance scenarios

#### 3.3 Connection Management

**HttpClient Configuration:**
```csharp
services.AddHttpClient("AzureOpenAI")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 100,
        EnableMultipleHttp2Connections = true
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
```

### 4. Dataverse Optimization

#### 4.1 Query Optimization

**Best Practices:**
1. **Use $select to Limit Fields**
   ```csharp
   var query = "mcicaps_evaluationruns?" +
               "$select=mcicaps_evaluationrunid,mcicaps_status,mcicaps_testcasecount" +
               "&$filter=mcicaps_evaluationrunid eq {id}";
   ```

2. **Implement Paging for Large Result Sets**
   ```csharp
   var query = "mcicaps_testcases?" +
               "$select=mcicaps_testcaseid,mcicaps_input,mcicaps_expectedoutput" +
               "&$filter=_mcicaps_evaluationrun_value eq {runId}" +
               "&$top=100";  // Process in batches
   ```

3. **Use Batch Operations**
   ```csharp
   // Batch create evaluation results
   var batchRequests = results.Select(r => new CreateEvaluationResultRequest
   {
       // ... properties
   }).ToList();
   
   await _dataverseService.BatchCreateResultsAsync(batchRequests);
   ```

#### 4.2 Connection Management

**HttpClient Configuration:**
```csharp
services.AddHttpClient("Dataverse")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 50
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

#### 4.3 API Rate Limit Management

**Implement Token Bucket Pattern:**
```csharp
public class DataverseRateLimiter
{
    private static readonly SemaphoreSlim _semaphore = new(50, 50); // 50 concurrent requests
    
    public async Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> operation)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

### 5. Blob Storage Optimization

#### 5.1 Storage Account Configuration

**Optimal Configuration:**
```bicep
resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  sku: {
    name: 'Standard_ZRS'  // Zone-redundant for HA, or 'Premium_LRS' for ultra-low latency
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
    supportsHttpsTrafficOnly: true
  }
}
```

#### 5.2 Caching Strategy

**In-Memory Cache for Prompt Templates:**
```csharp
public class CachedBlobStorageService : IBlobStorageService
{
    private readonly IBlobStorageService _innerService;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    
    public async Task<string> GetPromptTemplateAsync(string templateName, CancellationToken ct = default)
    {
        var cacheKey = $"prompt-template:{templateName}";
        
        if (_cache.TryGetValue(cacheKey, out string cachedTemplate))
        {
            return cachedTemplate;
        }
        
        var template = await _innerService.GetPromptTemplateAsync(templateName, ct);
        _cache.Set(cacheKey, template, CacheDuration);
        
        return template;
    }
}
```

**Register in Program.cs:**
```csharp
services.AddMemoryCache(options =>
{
    options.SizeLimit = 100; // Limit cache size
});

services.Decorate<IBlobStorageService, CachedBlobStorageService>();
```

### 6. Application Code Optimization

#### 6.1 Async/Await Best Practices

**Avoid Blocking Calls:**
```csharp
// ❌ BAD - Blocks thread
var result = _service.GetDataAsync().Result;

// ✅ GOOD - Async all the way
var result = await _service.GetDataAsync();
```

**Use ConfigureAwait(false) for Library Code:**
```csharp
// In service implementations
var data = await _httpClient.GetAsync(url).ConfigureAwait(false);
```

#### 6.2 Parallel Processing

**Process Multiple Test Cases in Parallel (with throttling):**
```csharp
public async Task ProcessTestCasesBatchAsync(List<TestCase> testCases)
{
    var semaphore = new SemaphoreSlim(10); // Max 10 concurrent
    
    var tasks = testCases.Select(async testCase =>
    {
        await semaphore.WaitAsync();
        try
        {
            await ProcessTestCaseAsync(testCase);
        }
        finally
        {
            semaphore.Release();
        }
    });
    
    await Task.WhenAll(tasks);
}
```

#### 6.3 Object Pooling

**Use ArrayPool for Large Buffers:**
```csharp
var buffer = ArrayPool<byte>.Shared.Rent(minimumSize);
try
{
    // Use buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

**Use ObjectPool for Expensive Objects:**
```csharp
services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
services.AddSingleton(serviceProvider =>
{
    var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
    return provider.Create(new DefaultPooledObjectPolicy<StringBuilder>());
});
```

#### 6.4 Memory Management

**Dispose Resources Properly:**
```csharp
await using var stream = await _blobClient.DownloadStreamingAsync();
using var reader = new StreamReader(stream);
return await reader.ReadToEndAsync();
```

**Avoid Large Object Heap (LOH) Allocations:**
- Keep objects < 85KB to avoid LOH
- Use ArrayPool for large buffers
- Stream large data instead of loading into memory

### 7. Monitoring & Profiling

#### 7.1 Application Insights Performance Monitoring

**Custom Performance Metrics:**
```csharp
public async Task<EvaluationResult> EvaluateTestCaseAsync(TestCase testCase)
{
    var stopwatch = Stopwatch.StartNew();
    
    try
    {
        var result = await _evaluator.EvaluateAsync(testCase);
        
        _telemetryClient.TrackMetric(
            "TestCaseProcessingTime",
            stopwatch.ElapsedMilliseconds,
            new Dictionary<string, string>
            {
                ["TestType"] = testCase.TestType.ToString()
            });
        
        return result;
    }
    finally
    {
        stopwatch.Stop();
    }
}
```

#### 7.2 Profiling Tools

**Use Application Insights Profiler:**
```powershell
# Enable profiler
az webapp config set `
    --name <app-name> `
    --resource-group <rg> `
    --settings "APPINSIGHTS_PROFILERFEATURE_VERSION=1.0.0"
```

**Use dotnet-counters for Real-Time Metrics:**
```powershell
# Install tool
dotnet tool install --global dotnet-counters

# Monitor live metrics
dotnet-counters monitor --process-id <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting
```

### 8. Load Testing

#### 8.1 Azure Load Testing Configuration

**Test Configuration:**
```yaml
# loadtest.yaml
version: v0.1
testName: EvaluationLoadTest
testPlan: loadtest.jmx
engineInstances: 10
failureCriteria:
  - avg(response_time_ms) > 5000
  - percentage(error) > 5
  - percentage(requests) < 1000 per minute
env:
  - name: FUNCTION_URL
    value: https://<app-name>.azurewebsites.net
  - name: FUNCTION_KEY
    value: ${{ secrets.FUNCTION_KEY }}
```

**Run Load Test:**
```powershell
# Create load test
az load test create `
    --name evaluation-load-test `
    --resource-group <rg> `
    --load-test-config-file loadtest.yaml

# Run test
az load test-run create `
    --test-id evaluation-load-test `
    --load-test-resource <load-test-resource> `
    --resource-group <rg>
```

#### 8.2 Load Test Scenarios

**Scenario 1: Gradual Ramp-Up**
- Start: 10 test cases/minute
- Ramp up: Add 100/minute every 5 minutes
- Peak: 1,500 test cases/minute
- Duration: 60 minutes

**Scenario 2: Sustained Peak Load**
- Load: 1,000 test cases/minute
- Duration: 120 minutes
- Validate: No degradation over time

**Scenario 3: Spike Test**
- Baseline: 100 test cases/minute
- Spike: 2,000 test cases/minute for 5 minutes
- Validate: System recovers automatically

### 9. Cost Optimization

#### 9.1 Right-Sizing Resources

**Monitor and Adjust:**
```powershell
# Check actual usage
az monitor metrics list `
    --resource <function-app-id> `
    --metric "FunctionExecutionCount" `
    --start-time 2025-11-01T00:00:00Z `
    --end-time 2025-11-08T00:00:00Z `
    --interval PT1H

# Adjust based on usage patterns
az functionapp plan update `
    --name <plan-name> `
    --resource-group <rg> `
    --number-of-workers 2  # Reduce if over-provisioned
```

#### 9.2 Cost-Performance Trade-offs

| Component | Configuration | Cost | Performance | Recommendation |
|-----------|--------------|------|-------------|----------------|
| Function App | EP1 | $ | ⭐⭐⭐ | Dev/Test |
| Function App | EP2 | $$ | ⭐⭐⭐⭐ | Staging |
| Function App | EP3 | $$$ | ⭐⭐⭐⭐⭐ | Production |
| Service Bus | Standard | $ | ⭐⭐⭐ | <100K messages/day |
| Service Bus | Premium | $$$$ | ⭐⭐⭐⭐⭐ | >100K messages/day |
| Storage | Standard LRS | $ | ⭐⭐⭐⭐ | Most scenarios |
| Storage | Premium LRS | $$$ | ⭐⭐⭐⭐⭐ | Ultra-low latency |

## ✅ Performance Checklist

### Before Production Deployment
- [ ] Load test with 50K test cases completed
- [ ] All performance targets met or exceeded
- [ ] Auto-scaling tested and validated
- [ ] Cold start time < 10 seconds
- [ ] Memory usage optimized (< 2 GB per instance)
- [ ] All performance metrics tracked in Application Insights
- [ ] Cost projections validated against budget

### Post-Production Monitoring
- [ ] Daily review of performance dashboards
- [ ] Weekly analysis of slow operations
- [ ] Monthly capacity planning review
- [ ] Quarterly load testing
- [ ] Continuous optimization based on actual usage patterns

## 📊 Performance Baseline

After implementing optimizations, document baseline metrics:

| Metric | Baseline Value | Date | Notes |
|--------|---------------|------|-------|
| Avg processing time | ___ ms | ___ | Under normal load |
| P95 processing time | ___ ms | ___ | 95th percentile |
| P99 processing time | ___ ms | ___ | 99th percentile |
| Max throughput | ___ /min | ___ | Peak observed |
| Avg instance count | ___ | ___ | Typical load |
| Max instance count | ___ | ___ | Peak load |
| Avg memory usage | ___ GB | ___ | Per instance |
| Cold start time | ___ sec | ___ | From cold to ready |

---

**Document Owner**: Performance Engineering Team  
**Review Frequency**: After major changes or quarterly  
**Last Performance Review**: November 8, 2025  
**Next Performance Review**: February 8, 2026
