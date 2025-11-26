# Azure Function Configuration Analysis
**Date:** November 20, 2025  
**Version:** 1.0.45  
**Environment:** func-aiestagingmbwc (Staging)

## Executive Summary

This analysis compares Azure Function app settings with code expectations to identify unused settings, missing settings, and configuration mismatches.

### Key Findings:
- ❌ **3 settings NOT used in code** (can be removed)
- ⚠️ **3 settings checked incorrectly** (wrong variable names in health checks)
- ✅ **13 settings properly configured and used**
- 🆕 **1 newly added setting** (Evaluation__MaxRunningTimeMinutes)

---

## Settings Analysis

### ❌ UNUSED SETTINGS (Can be Removed)

#### 1. `MaxConcurrentTestCases` = 100
- **Status:** Provisioned by Bicep, set in Azure, but NOT defined in `AppSettings.cs` or used anywhere in code
- **Location:** Line 297 in `main.bicep`
- **Impact:** None - can be safely removed
- **Reason:** Leftover from original design. Actual concurrency controlled by:
  - Service Bus multiple instances
  - `CopilotStudio__MaxConcurrentRequests` (set to 2)
  - Durable Functions `maxConcurrentActivityFunctions: 100` in `host.json`
- **Recommendation:** **Remove from Bicep and Azure**

#### 2. `ServiceBusNamespace__fullyQualifiedNamespace` = sbus-aiestagingmbwc.servicebus.windows.net
- **Status:** Set in Azure but NOT read by code
- **Code uses:** `ServiceBus__Namespace` (double underscore format)
- **Impact:** None - this is a duplicate/alternate format not used
- **Recommendation:** **Remove from Azure** (keep `ServiceBus__Namespace`)

#### 3. `DefaultEvaluationTimeoutMinutes` = 5
- **Status:** Set in Azure, read in `Program.cs` line 70, but only used during startup configuration
- **Code uses:** `Evaluation__DefaultTimeoutMinutes` (nested format) for actual timeout logic
- **Mapping:** 
  ```csharp
  // Program.cs line 70-71
  if (int.TryParse(builder.Configuration["DefaultEvaluationTimeoutMinutes"], out int timeout))
      options.Evaluation.DefaultTimeoutMinutes = timeout;
  ```
- **Impact:** Low - setting is read but could be standardized
- **Recommendation:** **Migrate to `Evaluation__DefaultTimeoutMinutes`** for consistency

---

### ⚠️ INCORRECT CONFIGURATION CHECKS

The `HealthCheckFunction.cs` (lines 227-229) checks for environment variables that **don't exist**:

```csharp
hasDataverseConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DataverseBaseUrl")),
hasServiceBusConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ServiceBusNamespace")),
hasOpenAIConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AzureOpenAIEndpoint"))
```

#### Issues:
1. **`DataverseBaseUrl`** ❌ - Should be `DataverseConnectionString`
2. **`ServiceBusNamespace`** ❌ - Should be `ServiceBus__Namespace` 
3. **`AzureOpenAIEndpoint`** ✅ - This one is correct (flat format used in Program.cs)

**Recommendation:** Fix HealthCheckFunction.cs to check correct variable names

---

### ✅ PROPERLY CONFIGURED SETTINGS

| Azure Setting | AppSettings Property | Used By | Status |
|--------------|---------------------|---------|--------|
| `DataverseConnectionString` | `Dataverse.ConnectionString` | DataverseService | ✅ Used |
| `AzureOpenAIEndpoint` | `AzureOpenAI.Endpoint` | LLMJudgeServices | ✅ Used |
| `KeyVaultUrl` | `KeyVault.Url` | Program.cs startup | ✅ Used |
| `ServiceBus__Namespace` | `ServiceBus.Namespace` | ServiceBusService | ✅ Used |
| `CopilotStudio__TimeoutSeconds` | `CopilotStudio.TimeoutSeconds` | CopilotStudioService | ✅ Used |
| `CopilotStudio__BotResponseTimeoutSeconds` | `CopilotStudio.BotResponseTimeoutSeconds` | CopilotStudioService | ✅ Used |
| `CopilotStudio__MaxRetryAttempts` | `CopilotStudio.MaxRetryAttempts` | CopilotStudioService | ✅ Used |
| `CopilotStudio__MaxRequestsPerSecond` | `CopilotStudio.MaxRequestsPerSecond` | RateLimiter | ✅ Used |
| `CopilotStudio__BurstCapacity` | `CopilotStudio.BurstCapacity` | RateLimiter | ✅ Used |
| `CopilotStudio__MaxConcurrentRequests` | `CopilotStudio.MaxConcurrentRequests` | RateLimiter | ✅ Used |
| `AzureOpenAI__MaxRetryAttempts` | `AzureOpenAI.MaxRetryAttempts` | AzureOpenAIJudgeService | ✅ Used |
| `AzureOpenAI__TimeoutSeconds` | `AzureOpenAI.TimeoutSeconds` | AzureOpenAIJudgeService | ✅ Used |
| `Evaluation__MaxRetryAttempts` | `Evaluation.MaxRetryAttempts` | Multiple services | ✅ Used |
| `Evaluation__MaxRunningTimeMinutes` | `Evaluation.MaxRunningTimeMinutes` | MonitorFunction | 🆕 **Newly added** |

---

## AppSettings.cs Properties NOT Set in Azure

These have default values in code but could be overridden if needed:

### DataverseSettings
- ❌ `Dataverse__SchemaPrefix` (default: "mcicaps")
- ❌ `Dataverse__TimeoutSeconds` (default: 30)
- ❌ `Dataverse__MaxRetryAttempts` (default: 3)

### CopilotStudioSettings
- ❌ `CopilotStudio__DefaultBotId` (default: empty - Direct Line URLs from Dataverse)
- ⚠️ `CopilotStudio__RequestDelayMs` (DEPRECATED - use MaxRequestsPerSecond instead)

### AzureOpenAISettings
- ❌ `AzureOpenAI__ApiKey` (default: empty - using Managed Identity)
- ❌ `AzureOpenAI__DeploymentName` (default: "gpt-4o")
- ❌ `AzureOpenAI__ApiVersion` (default: "2024-02-01")
- ❌ `AzureOpenAI__MaxTokens` (default: 4000)
- ❌ `AzureOpenAI__Temperature` (default: 0.0)

### ServiceBusSettings
- ❌ `ServiceBus__ConnectionString` (default: empty - using Managed Identity)
- ❌ `ServiceBus__QueueName` (default: "test-case-processing")
- ❌ `ServiceBus__AutoCompleteTimeoutMinutes` (default: 5)
- ❌ `ServiceBus__MaxRetryAttempts` (default: 3)
- ❌ `ServiceBus__RetryDelaySeconds` (default: 30)
- ❌ `ServiceBus__MultiTurnAutoCompleteTimeoutMinutes` (default: 15)

### EvaluationSettings
- ❌ `Evaluation__RetryDelaySeconds` (default: 30)
- ❌ `Evaluation__EnableDetailedLogging` (default: true)
- ❌ `Evaluation__MinScore` (default: 0.0)
- ❌ `Evaluation__MaxScore` (default: 10.0)

### KeyVaultSettings
- ❌ `KeyVault__Enabled` (default: true)

**Note:** These defaults are generally fine. Only override if you need different values.

---

## Configuration Mapping Patterns

### Flat Format (Legacy)
Used in `Program.cs` for backward compatibility:
```
AzureOpenAIEndpoint → AzureOpenAI.Endpoint
DataverseConnectionString → Dataverse.ConnectionString
KeyVaultUrl → KeyVault.Url
DefaultEvaluationTimeoutMinutes → Evaluation.DefaultTimeoutMinutes
```

### Nested Format (Standard)
Standard ASP.NET Core configuration binding:
```
ServiceBus__Namespace → ServiceBus.Namespace
CopilotStudio__TimeoutSeconds → CopilotStudio.TimeoutSeconds
Evaluation__MaxRetryAttempts → Evaluation.MaxRetryAttempts
```

**Recommendation:** Migrate all settings to nested format for consistency.

---

## Recommended Actions

### Immediate (Remove Unused)
1. ✅ **Remove `MaxConcurrentTestCases`** from:
   - Bicep: `main.bicep` line 297
   - Azure: Delete app setting
   
2. ✅ **Remove `ServiceBusNamespace__fullyQualifiedNamespace`** from:
   - Azure: Delete app setting (keep `ServiceBus__Namespace`)

### Short Term (Standardize)
3. ⚠️ **Migrate `DefaultEvaluationTimeoutMinutes`** to nested format:
   - Change Azure setting to: `Evaluation__DefaultTimeoutMinutes`
   - Update `Program.cs` line 70 to read from nested format
   - Update Bicep template

4. ⚠️ **Fix health check variable names** in `HealthCheckFunction.cs` lines 227-229:
   ```csharp
   hasDataverseConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DataverseConnectionString")),
   hasServiceBusConfig = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ServiceBus__Namespace")),
   ```

### Long Term (Optimization)
5. 📋 **Consider migrating all flat format settings** to nested format:
   - `AzureOpenAIEndpoint` → `AzureOpenAI__Endpoint`
   - `KeyVaultUrl` → `KeyVault__Url`
   - Update `Program.cs` mapping logic
   - Update Bicep templates

---

## Current Azure Settings (Staging)

### App Settings Summary
Total settings: 22 (excluding system settings like WEBSITE_*, FUNCTIONS_*)

**By Category:**
- Azure OpenAI: 3 settings (1 flat + 2 nested)
- Copilot Studio: 6 settings (all nested)
- Dataverse: 1 setting (flat)
- Evaluation: 2 settings (1 flat + 1 nested)
- Service Bus: 2 settings (1 nested + 1 duplicate)
- Key Vault: 1 setting (flat)
- Unused: 1 setting (MaxConcurrentTestCases)

### Size Optimization
Current configuration is lean. No bloat detected except for the 3 unused settings identified above.

---

## Configuration Best Practices

1. ✅ **Use nested format** (`Section__Property`) for new settings
2. ✅ **Use Managed Identity** over connection strings (Azure OpenAI, Service Bus, Storage)
3. ✅ **Store secrets in Key Vault** with `@Microsoft.KeyVault()` references
4. ✅ **Set defaults in AppSettings.cs** to avoid missing config errors
5. ⚠️ **Validate environment variables** match what code actually reads
6. ⚠️ **Remove unused settings** to reduce configuration drift

---

## Testing Recommendations

After removing unused settings:
1. Run health check: `curl https://func-aiestagingmbwc.azurewebsites.net/api/health`
2. Verify all services initialize without warnings
3. Run a test evaluation to confirm functionality
4. Check Application Insights for any configuration errors

---

## Appendix: Configuration Flow

```
Azure App Settings (Environment Variables)
    ↓
Program.cs (reads and maps to AppSettings)
    ↓
IOptions<AppSettings> (injected into services)
    ↓
Service constructors (use _settings.Section.Property)
```

**Key Point:** Even though `builder.Services.Configure<AppSettings>(builder.Configuration)` on line 41 of Program.cs binds automatically, the manual mapping in lines 51-72 is needed for flat format settings that don't match the nested structure.
