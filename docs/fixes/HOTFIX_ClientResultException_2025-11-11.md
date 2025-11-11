# HOTFIX: ClientResultException Rate Limit Handling

**Date:** November 11, 2025  
**Time:** 19:57 UTC  
**Severity:** CRITICAL  
**Environment:** Staging (func-aiestagingmbwc)

## Issue Summary

After initial deployment of rate limit fixes at 19:22 UTC, the system continued to experience HTTP 429 rate limit errors from Azure OpenAI **without any retry attempts**.

### Root Cause

The Azure OpenAI SDK v2.1.0 throws `System.ClientModel.ClientResultException` for rate limit errors, **not** `Azure.RequestFailedException` as initially assumed. The retry policy was catching `RequestFailedException` but not `ClientResultException`, causing all rate limit errors to bypass the retry logic entirely.

### Evidence from Logs

Application Insights query showed:
- **Exception Type:** `System.ClientModel.ClientResultException`
- **Message Format:** `"HTTP 429 (429)\r\n\r\nRate limit is exceeded. Try again in X seconds."`
- **Stack Trace:** Exception was reaching `RetryPolicy.ExecuteAsync` but **not being caught** as retriable
- **No Retry Attempts:** Zero retry log entries found for these 429 errors

## Changes Implemented

### File: `backend/AiAgentEvaluation/Utils/RetryPolicy.cs`

#### 1. Added Using Statement
```csharp
using System.ClientModel;
```

#### 2. Updated Exception Matching
Added `ClientResultException` to the retriable exception switch:
```csharp
// System.ClientModel exceptions (from Azure OpenAI SDK v2.1.0+)
ClientResultException clientEx => IsRetriableClientException(clientEx),
```

#### 3. Added ClientResultException Handler
```csharp
private static bool IsRetriableClientException(ClientResultException ex)
{
    // Handle ClientResultException from Azure.AI.OpenAI SDK v2.1.0+
    // This is the new exception type used by the Azure OpenAI SDK
    return ex.Status == 429 ||  // Rate limit
           ex.Status == 503 ||  // Service unavailable
           ex.Status == 502 ||  // Bad Gateway
           ex.Status == 504;    // Gateway Timeout
}
```

#### 4. Enhanced Retry-After Extraction
Added message parsing for `ClientResultException` since it doesn't expose response headers:
```csharp
// Extract Retry-After from ClientResultException (Azure OpenAI SDK v2.1.0+)
// The message format is: "HTTP 429 (429)\r\n\r\nRate limit is exceeded. Try again in X seconds."
if (ex is ClientResultException clientEx)
{
    try
    {
        var message = clientEx.Message;
        var match = Regex.Match(message, @"Try again in (\d+) seconds?", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
        {
            _logger.LogInformation("Extracted Retry-After from ClientResultException message: {Seconds} seconds", seconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }
    catch (Exception extractEx)
    {
        _logger.LogDebug(extractEx, "Failed to extract Retry-After from ClientResultException message, using exponential backoff");
    }
}
```

## Deployment Timeline

| Time (UTC) | Event | Status |
|------------|-------|--------|
| 19:22 | Initial rate limit fix deployment | ❌ Incomplete |
| 19:30-19:50 | Evaluation running, 16+ rate limit errors observed | ❌ No retries |
| 19:52 | Issue investigation started | 🔍 |
| 19:53 | Root cause identified via Application Insights | ✅ |
| 19:55 | Hotfix implemented | ✅ |
| 19:57 | Hotfix deployed | ✅ |

## Expected Behavior After Hotfix

### Before Hotfix
```
[Exception] ClientResultException: HTTP 429 - Rate limit exceeded. Try again in 4 seconds.
[Error] Test case failed immediately (no retries)
```

### After Hotfix
```
[Warning] Attempt 1 failed: HTTP 429 - Rate limit exceeded
[Info] Extracted Retry-After from ClientResultException message: 4 seconds
[Info] Retrying EvaluateResponse (attempt 2/5) after 4000ms delay
[Success] Retry succeeded
```

## Verification Steps

### 1. Monitor for Retry Attempts
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Extracted Retry-After from ClientResultException"
| project timestamp, message
| order by timestamp desc
```

### 2. Check 429 Error Reduction
```kusto
exceptions
| where timestamp > ago(2h)
| where type == "System.ClientModel.ClientResultException"
| summarize count() by bin(timestamp, 5m)
| render timechart
```

### 3. Verify Retry Success Rate
```kusto
traces
| where timestamp > ago(1h)
| where message contains "Retrying" and message contains "EvaluateResponse"
| extend Attempt = extract(@"attempt (\d+)/(\d+)", 1, message)
| summarize RetryCount=count() by Attempt
```

## Testing Recommendations

### Immediate (Next Hour)
- ✅ Hotfix deployed
- ⏳ Monitor Application Insights for retry log entries
- ⏳ Verify rate limit errors trigger retries (look for "Extracted Retry-After" logs)
- ⏳ Check if error rate decreases

### Short-term (Next 24 Hours)
- ⏳ Run evaluation with same test set that produced original errors
- ⏳ Compare error rates: Before (9 failures) vs After (expected 0-2)
- ⏳ Verify Retry-After delays are being honored
- ⏳ Monitor circuit breaker state

## Why the Initial Fix Missed This

1. **SDK Version Mismatch:** Azure OpenAI SDK v2.1.0 uses newer `System.ClientModel` package
2. **Documentation Gap:** SDK migration guide didn't clearly specify the new exception type
3. **Testing Limitation:** Unit tests mocked the service, didn't catch the actual SDK exception type
4. **Rapid Deployment:** Initial fix deployed before production testing completed

## Lessons Learned

1. **Always test against actual SDK behavior** in non-production environment first
2. **Check SDK release notes** for breaking changes in exception types
3. **Use Application Insights** for rapid root cause analysis
4. **Add integration tests** that use real SDK instances (not mocks)

## Rollback (If Needed)

If this hotfix causes issues:
```bash
# Revert to previous deployment
# Contact: steven.pride@microsoft.com
```

## Related Documents

- Initial Fix: `docs/fixes/DEPLOYMENT_RECORD_2025-11-11.md`
- Design Doc: `docs/fixes/rate-limit-and-copilot-studio-reliability-improvements.md`

## Sign-off

**Hotfix By:** GitHub Copilot  
**Severity:** Critical  
**Status:** ✅ Deployed  
**Monitoring:** Active  
**Next Review:** 1 hour (20:57 UTC)

---

## Post-Deployment Update (To be filled after monitoring)

**Time:** _TBD_  
**Status:** _TBD_  
**Retry Attempts Observed:** _TBD_  
**Error Rate Change:** _TBD_  
**Production Ready:** _TBD_
