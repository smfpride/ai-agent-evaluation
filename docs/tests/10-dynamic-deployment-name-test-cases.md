# Test Case Documentation: Dynamic Azure OpenAI Deployment Names

**Feature**: Per-Evaluator Azure OpenAI Deployment Name Configuration  
**Date Created**: November 13, 2025  
**Status**: Ready for Implementation  
**Related Story**: [Story 10: Dynamic Deployment Names](../stories/10-dynamic-deployment-names.md)  
**Related Implementation**: [Implementation Guide](../implementation/dynamic-deployment-names-implementation-guide.md)

---

## Test Overview

This document defines test cases for validating the dynamic Azure OpenAI deployment name feature, which allows each evaluator type to specify its own model deployment (e.g., GPT-4o, GPT-4-turbo, GPT-3.5-turbo) for cost and performance optimization.

### Testing Scope
- ✅ Deployment name resolution logic (evaluator vs system default)
- ✅ Backward compatibility with existing evaluators
- ✅ Integration with single-turn evaluations
- ✅ Integration with multi-turn evaluations
- ✅ Error handling and fallback scenarios
- ✅ Logging and observability
- ✅ Dataverse schema compatibility

---

## Unit Test Cases

### TC-DD-U01: Evaluator with Custom Deployment Name
**Objective**: Verify that evaluator with custom deployment name uses that deployment

**Preconditions**:
- EvaluatorType exists in Dataverse with `mcicaps_deploymentname = "gpt-35-turbo"`
- System default deployment is "gpt-4o"

**Test Steps**:
1. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
2. Verify method returns tuple `(promptTemplate, deploymentName)`

**Expected Results**:
- ✅ `deploymentName` equals "gpt-35-turbo" (from evaluator)
- ✅ `promptTemplate` matches evaluator's custom prompt
- ✅ Dataverse service called once to retrieve evaluator type
- ✅ Log message contains: "Using gpt-35-turbo (from evaluator)"

**Priority**: HIGH  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U02: Evaluator Without Deployment Name (NULL)
**Objective**: Verify fallback to system default when evaluator has NULL deployment name

**Preconditions**:
- EvaluatorType exists with `mcicaps_deploymentname = NULL`
- System default deployment is "gpt-4o" (from AppSettings)

**Test Steps**:
1. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
2. Verify returned deployment name

**Expected Results**:
- ✅ `deploymentName` equals "gpt-4o" (system default)
- ✅ `promptTemplate` matches evaluator's custom prompt
- ✅ Log message contains: "Using gpt-4o (system default)"

**Priority**: HIGH  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U03: Evaluator with Empty String Deployment Name
**Objective**: Verify fallback to system default when deployment name is empty string

**Preconditions**:
- EvaluatorType exists with `mcicaps_deploymentname = ""`
- System default deployment is "gpt-4o"

**Test Steps**:
1. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
2. Verify deployment name resolution

**Expected Results**:
- ✅ `deploymentName` equals "gpt-4o" (system default)
- ✅ Empty string treated as NULL/unspecified

**Priority**: MEDIUM  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U04: Evaluator with Whitespace-Only Deployment Name
**Objective**: Verify fallback when deployment name contains only whitespace

**Preconditions**:
- EvaluatorType exists with `mcicaps_deploymentname = "   "`
- System default deployment is "gpt-4o"

**Test Steps**:
1. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
2. Verify deployment name resolution

**Expected Results**:
- ✅ `deploymentName` equals "gpt-4o" (system default)
- ✅ Whitespace-only string treated as empty

**Priority**: LOW  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U05: No Evaluator Type (NULL EvaluatorTypeId)
**Objective**: Verify behavior when no evaluator type is specified

**Preconditions**:
- No evaluator type ID provided
- System default deployment is "gpt-4o"

**Test Steps**:
1. Call `GetPromptTemplateAndDeploymentAsync(null)`
2. Verify both prompt and deployment are defaults

**Expected Results**:
- ✅ `promptTemplate` equals default embedded prompt
- ✅ `deploymentName` equals "gpt-4o" (system default)
- ✅ No Dataverse call made

**Priority**: MEDIUM  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U06: Dataverse Failure During Retrieval
**Objective**: Verify fallback to defaults when Dataverse fails

**Preconditions**:
- Dataverse service throws exception when retrieving evaluator type

**Test Steps**:
1. Mock Dataverse to throw exception
2. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
3. Verify graceful fallback

**Expected Results**:
- ✅ `promptTemplate` equals default embedded prompt
- ✅ `deploymentName` equals "gpt-4o" (system default)
- ✅ Exception logged but not propagated
- ✅ No evaluation failure

**Priority**: HIGH  
**Test Type**: Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U07: Various Valid Deployment Names
**Objective**: Verify system accepts various deployment name formats

**Test Data**:
| Deployment Name | Expected Behavior |
|-----------------|-------------------|
| "gpt-4o" | Uses gpt-4o |
| "gpt-4-turbo" | Uses gpt-4-turbo |
| "gpt-35-turbo" | Uses gpt-35-turbo |
| "custom-model-v2" | Uses custom-model-v2 |
| "deployment-with-numbers-123" | Uses deployment-with-numbers-123 |

**Test Steps**:
1. For each deployment name, create evaluator type
2. Call `GetPromptTemplateAndDeploymentAsync(evaluatorTypeId)`
3. Verify exact deployment name returned

**Expected Results**:
- ✅ All valid deployment names accepted without modification
- ✅ Case-sensitive matching preserved

**Priority**: MEDIUM  
**Test Type**: Parameterized Unit Test  
**Component**: LLMJudgeService

---

### TC-DD-U08: ChatClient Created On-Demand
**Objective**: Verify ChatClient instances are created per request, not singleton

**Preconditions**:
- AzureOpenAIClient initialized at service startup

**Test Steps**:
1. Verify constructor stores `_openAIClient` (base client)
2. Verify constructor does NOT pre-create `_chatClient`
3. Verify `EvaluateResponseAsync` calls `_openAIClient.GetChatClient(deploymentName)`

**Expected Results**:
- ✅ Constructor initializes base AzureOpenAIClient only
- ✅ No singleton ChatClient stored in private field
- ✅ ChatClient created on-demand per evaluation
- ✅ Deployment name parameter passed to GetChatClient()

**Priority**: HIGH  
**Test Type**: Unit Test (Code Review)  
**Component**: LLMJudgeService Constructor

---

### TC-DD-U09: Health Check Uses Default Deployment
**Objective**: Verify health check uses system default deployment

**Preconditions**:
- System default deployment is "gpt-4o"
- Health check endpoint called

**Test Steps**:
1. Call `CheckHealthAsync()`
2. Verify ChatClient creation

**Expected Results**:
- ✅ Health check creates ChatClient with default deployment
- ✅ Uses `_settings.AzureOpenAI.DeploymentName`
- ✅ Does NOT query Dataverse for evaluator types

**Priority**: MEDIUM  
**Test Type**: Unit Test  
**Component**: LLMJudgeService.CheckHealthAsync

---

### TC-DD-U10: Backward Compatibility - GetPromptTemplateAsync
**Objective**: Verify legacy method still works for backward compatibility

**Preconditions**:
- Legacy code calls `GetPromptTemplateAsync(evaluatorTypeId)`
- Evaluator has custom deployment name

**Test Steps**:
1. Call `GetPromptTemplateAsync(evaluatorTypeId)` (old method)
2. Verify prompt returned

**Expected Results**:
- ✅ Method returns prompt template only (not tuple)
- ✅ Deployment name resolved internally but not returned
- ✅ No breaking changes to existing callers

**Priority**: HIGH  
**Test Type**: Unit Test  
**Component**: LLMJudgeService.GetPromptTemplateAsync

---

## Integration Test Cases

### TC-DD-I01: Single-Turn Evaluation with Custom Deployment
**Objective**: Verify single-turn evaluation uses evaluator's custom deployment

**Preconditions**:
- Evaluator "Budget Classifier" with `mcicaps_deploymentname = "gpt-35-turbo"`
- Test case queued to Service Bus
- Azure OpenAI has "gpt-35-turbo" deployment accessible

**Test Steps**:
1. Queue test case message with evaluator type ID
2. Worker function processes message
3. Monitor Application Insights logs

**Expected Results**:
- ✅ Evaluation completes successfully
- ✅ Application Insights log shows: "Using gpt-35-turbo (from evaluator)"
- ✅ Azure OpenAI metrics show call to "gpt-35-turbo" deployment
- ✅ Result saved to Dataverse with evaluator type ID

**Priority**: HIGH  
**Test Type**: Integration Test  
**Environment**: Staging

---

### TC-DD-I02: Multi-Turn Evaluation with Custom Deployment
**Objective**: Verify multi-turn evaluation uses correct deployment across all turns

**Preconditions**:
- Multi-turn test case with 3 turns
- Evaluator "Advanced Similarity" with `mcicaps_deploymentname = "gpt-4o"`
- Durable orchestration started

**Test Steps**:
1. Start multi-turn orchestration
2. Execute all 3 turns
3. Review Application Insights traces

**Expected Results**:
- ✅ Turn 1 uses "gpt-4o" deployment
- ✅ Turn 2 uses "gpt-4o" deployment
- ✅ Turn 3 uses "gpt-4o" deployment
- ✅ Aggregate result calculation succeeds
- ✅ All logs show consistent deployment name

**Priority**: HIGH  
**Test Type**: Integration Test  
**Environment**: Staging

---

### TC-DD-I03: Mixed Evaluators in Single Run
**Objective**: Verify evaluation run with multiple evaluators uses correct deployment per evaluator

**Preconditions**:
- Evaluation run with 2 evaluators:
  - Evaluator A: `mcicaps_deploymentname = "gpt-4o"`
  - Evaluator B: `mcicaps_deploymentname = "gpt-35-turbo"`
- Single test case evaluated by both evaluators

**Test Steps**:
1. Start evaluation run
2. Monitor execution logs
3. Review results in Dataverse

**Expected Results**:
- ✅ Evaluator A results show "Using gpt-4o (from evaluator)"
- ✅ Evaluator B results show "Using gpt-35-turbo (from evaluator)"
- ✅ Both evaluations complete successfully
- ✅ Two results created with correct evaluator type IDs

**Priority**: HIGH  
**Test Type**: Integration Test  
**Environment**: Staging

---

### TC-DD-I04: Evaluator Without Deployment Falls Back to Default
**Objective**: Verify existing evaluator without deployment name continues working

**Preconditions**:
- "Legacy Accuracy" evaluator with `mcicaps_deploymentname = NULL`
- System default is "gpt-4o"
- Test case assigned to this evaluator

**Test Steps**:
1. Execute evaluation with legacy evaluator
2. Monitor logs and results

**Expected Results**:
- ✅ Evaluation completes successfully
- ✅ Log shows: "Using gpt-4o (system default)"
- ✅ Azure OpenAI metrics show call to "gpt-4o"
- ✅ No errors or warnings

**Priority**: HIGH (Backward Compatibility)  
**Test Type**: Integration Test  
**Environment**: Staging

---

### TC-DD-I05: Non-Existent Deployment Name
**Objective**: Verify error handling when deployment name doesn't exist in Azure OpenAI

**Preconditions**:
- Evaluator with `mcicaps_deploymentname = "non-existent-model"`
- Azure OpenAI does NOT have this deployment

**Test Steps**:
1. Execute evaluation with invalid deployment
2. Monitor error handling

**Expected Results**:
- ✅ Azure OpenAI SDK throws exception
- ✅ Retry logic attempts retries (exponential backoff)
- ✅ After max retries, error logged
- ✅ Result marked as errored in Dataverse
- ✅ Evaluation run continues (doesn't crash entire run)

**Priority**: HIGH  
**Test Type**: Negative Test  
**Environment**: Staging

---

### TC-DD-I06: Cost Tracking by Deployment
**Objective**: Verify Azure OpenAI cost metrics track usage by deployment

**Preconditions**:
- 100 test cases with "gpt-35-turbo" (cheap)
- 100 test cases with "gpt-4o" (expensive)
- Azure Cost Management enabled

**Test Steps**:
1. Execute both evaluations
2. Wait 24 hours for cost data propagation
3. Review Azure Cost Management

**Expected Results**:
- ✅ Costs separated by deployment name
- ✅ "gpt-35-turbo" shows lower cost per request
- ✅ "gpt-4o" shows higher cost per request
- ✅ Total cost matches expected (not all using expensive model)

**Priority**: MEDIUM  
**Test Type**: Integration Test (Cost Validation)  
**Environment**: Production (after rollout)

---

## Manual Test Cases

### TC-DD-M01: Create Evaluator with Deployment Name in Dataverse
**Objective**: Verify Dataverse UI supports new deployment name field

**Test Steps**:
1. Navigate to Dataverse → Evaluator Types
2. Create new evaluator type
3. Locate "Deployment Name" field
4. Enter "gpt-4-turbo"
5. Save record

**Expected Results**:
- ✅ "Deployment Name" field visible in form
- ✅ Field accepts text input (max 100 characters)
- ✅ Field is optional (can be left blank)
- ✅ Record saves successfully

**Priority**: MEDIUM  
**Test Type**: Manual UI Test  
**Environment**: Staging

---

### TC-DD-M02: Update Existing Evaluator with Deployment Name
**Objective**: Verify existing evaluators can be updated with deployment names

**Preconditions**:
- Existing "Similarity" evaluator without deployment name

**Test Steps**:
1. Open existing evaluator in Dataverse
2. Add deployment name "gpt-4o"
3. Save
4. Execute evaluation using this evaluator
5. Verify logs

**Expected Results**:
- ✅ Field updates successfully
- ✅ No data migration required
- ✅ Next evaluation uses new deployment
- ✅ Previous evaluation results unchanged

**Priority**: MEDIUM  
**Test Type**: Manual Test  
**Environment**: Staging

---

### TC-DD-M03: Remove Deployment Name from Evaluator
**Objective**: Verify removing deployment name reverts to system default

**Preconditions**:
- Evaluator with `mcicaps_deploymentname = "gpt-35-turbo"`

**Test Steps**:
1. Open evaluator in Dataverse
2. Clear deployment name field (set to NULL)
3. Save
4. Execute evaluation
5. Check logs

**Expected Results**:
- ✅ Field clears successfully
- ✅ Next evaluation uses system default "gpt-4o"
- ✅ Log shows: "Using gpt-4o (system default)"

**Priority**: LOW  
**Test Type**: Manual Test  
**Environment**: Staging

---

### TC-DD-M04: Application Insights Deployment Logging
**Objective**: Verify deployment name appears in Application Insights logs

**Test Steps**:
1. Execute evaluation with custom deployment "gpt-4-turbo"
2. Open Application Insights
3. Query traces: `traces | where message contains "Using"`
4. Review log entries

**Expected Results**:
- ✅ Log entry contains: "Using gpt-4-turbo (from evaluator)"
- ✅ EvaluatorTypeId included in structured logging
- ✅ Deployment source indicated (evaluator vs default)
- ✅ Easy to filter by deployment name

**Priority**: HIGH (Observability)  
**Test Type**: Manual Test  
**Environment**: Staging

---

## Performance Test Cases

### TC-DD-P01: ChatClient Creation Performance
**Objective**: Verify on-demand ChatClient creation has negligible performance impact

**Test Steps**:
1. Measure evaluation time with singleton ChatClient (baseline)
2. Measure evaluation time with on-demand ChatClient (new approach)
3. Compare latencies

**Expected Results**:
- ✅ On-demand creation adds <10ms per request
- ✅ Overall evaluation time unchanged (within 5%)
- ✅ No memory leaks from repeated client creation

**Priority**: MEDIUM  
**Test Type**: Performance Test  
**Environment**: Staging

---

### TC-DD-P02: High-Volume Mixed Deployments
**Objective**: Verify system handles high volume with multiple deployments

**Preconditions**:
- 10,000 test cases
- 5 different evaluators with different deployments

**Test Steps**:
1. Execute large evaluation run
2. Monitor throughput and error rates
3. Verify all deployments used correctly

**Expected Results**:
- ✅ All 10,000 evaluations complete successfully
- ✅ Throughput unchanged from baseline
- ✅ No deployment name conflicts
- ✅ Correct deployment used per evaluator

**Priority**: MEDIUM  
**Test Type**: Performance Test  
**Environment**: Staging

---

## Security Test Cases

### TC-DD-S01: Managed Identity Access to All Deployments
**Objective**: Verify Managed Identity has access to all specified deployments

**Preconditions**:
- Function App Managed Identity enabled
- Multiple deployments exist in Azure OpenAI

**Test Steps**:
1. Create evaluators for each deployment
2. Execute test evaluations
3. Verify authentication succeeds

**Expected Results**:
- ✅ Managed Identity authenticated successfully for all deployments
- ✅ No authentication errors
- ✅ Role assignment (Cognitive Services OpenAI User) applies to all deployments

**Priority**: HIGH  
**Test Type**: Security Test  
**Environment**: Staging

---

### TC-DD-S02: Invalid Deployment Name Injection
**Objective**: Verify system handles malicious deployment names safely

**Test Data**:
| Deployment Name | Expected Behavior |
|-----------------|-------------------|
| `"; DROP TABLE--` | Treated as literal string, fails validation |
| `<script>alert()</script>` | No XSS, treated as string |
| `../../../etc/passwd` | No path traversal |
| Very long string (1000+ chars) | Truncated or rejected by Dataverse field limit |

**Test Steps**:
1. Attempt to create evaluator with malicious deployment name
2. Monitor error handling

**Expected Results**:
- ✅ No code injection possible
- ✅ Azure OpenAI SDK safely handles invalid names
- ✅ Errors logged without exposing sensitive data

**Priority**: MEDIUM  
**Test Type**: Security Test  
**Environment**: Staging

---

## Regression Test Cases

### TC-DD-R01: Existing Evaluations Still Work
**Objective**: Verify all existing evaluations work after deployment name feature added

**Preconditions**:
- System deployed with new code
- Existing evaluators have NULL deployment names

**Test Steps**:
1. Execute existing evaluation runs (unchanged)
2. Verify all complete successfully

**Expected Results**:
- ✅ All existing evaluations pass
- ✅ No breaking changes
- ✅ Results match previous baseline

**Priority**: CRITICAL  
**Test Type**: Regression Test  
**Environment**: Staging → Production

---

### TC-DD-R02: Worker Function Still Processes Single-Turn
**Objective**: Verify WorkerFunction unchanged and functional

**Test Steps**:
1. Queue single-turn test case
2. Verify worker processes it
3. Check deployment name resolution happens internally

**Expected Results**:
- ✅ Worker function code unchanged (no edits needed)
- ✅ Deployment resolution handled by LLMJudgeService
- ✅ Message schema unchanged

**Priority**: HIGH  
**Test Type**: Regression Test  
**Environment**: Staging

---

### TC-DD-R03: Multi-Turn Orchestrations Still Work
**Objective**: Verify multi-turn orchestrations unchanged and functional

**Test Steps**:
1. Start multi-turn Durable orchestration
2. Verify all activities execute
3. Check deployment name resolution per turn

**Expected Results**:
- ✅ Orchestrator code unchanged
- ✅ Activities code unchanged
- ✅ Deployment resolution automatic

**Priority**: HIGH  
**Test Type**: Regression Test  
**Environment**: Staging

---

## Acceptance Test Checklist

Use this checklist to verify feature is ready for production:

### Functional Tests
- [ ] TC-DD-U01: Custom deployment name works
- [ ] TC-DD-U02: NULL deployment falls back to default
- [ ] TC-DD-I01: Single-turn uses correct deployment
- [ ] TC-DD-I02: Multi-turn uses correct deployment
- [ ] TC-DD-I03: Mixed evaluators use correct deployments
- [ ] TC-DD-I04: Backward compatibility verified

### Non-Functional Tests
- [ ] TC-DD-P01: Performance impact negligible
- [ ] TC-DD-S01: Security (Managed Identity) works
- [ ] TC-DD-M04: Observability (logs) working

### Regression Tests
- [ ] TC-DD-R01: Existing evaluations unaffected
- [ ] TC-DD-R02: Worker function still works
- [ ] TC-DD-R03: Multi-turn still works

### Documentation
- [ ] Architecture diagram updated
- [ ] PRD updated
- [ ] Story created with acceptance criteria
- [ ] Implementation guide complete
- [ ] Test cases documented (this file)

---

## Test Data Setup

### Sample Evaluator Types for Testing

#### 1. Budget Evaluator (Cost Optimization)
```
Name: Budget Classifier
Deployment Name: gpt-35-turbo
Prompt: "Classify the response as yes/no/unsure"
Expected Use: Simple classifications
Expected Cost: ~50% cheaper than GPT-4o
```

#### 2. Advanced Evaluator (Performance)
```
Name: Advanced Similarity
Deployment Name: gpt-4o
Prompt: "Evaluate semantic similarity with deep reasoning"
Expected Use: Complex semantic analysis
Expected Cost: Premium pricing
```

#### 3. Balanced Evaluator (Middle Ground)
```
Name: Balanced Accuracy
Deployment Name: gpt-4-turbo
Prompt: "Check factual accuracy with moderate depth"
Expected Use: General purpose accuracy checks
Expected Cost: Mid-tier pricing
```

#### 4. Legacy Evaluator (Backward Compatibility)
```
Name: Legacy Relevancy
Deployment Name: NULL
Prompt: "Evaluate response relevancy"
Expected Use: Testing fallback to system default
Expected Behavior: Uses system default (gpt-4o)
```

---

## Test Execution Schedule

### Phase 1: Unit Tests (Day 1)
- Run all TC-DD-U* tests
- Verify code coverage >80%
- Fix any failures

### Phase 2: Integration Tests (Day 2-3)
- Deploy to staging
- Run TC-DD-I* tests
- Verify Application Insights logs
- Test cost tracking setup

### Phase 3: Manual Tests (Day 3-4)
- Dataverse UI testing
- Observability validation
- Cost analysis setup

### Phase 4: Performance & Security (Day 4-5)
- Load testing
- Security validation
- Managed Identity verification

### Phase 5: Regression (Day 5-6)
- Run full regression suite
- Verify backward compatibility
- Production readiness review

### Phase 6: Production Deployment (Day 7)
- Deploy to production
- Monitor for 24 hours
- Validate cost savings
- Document lessons learned

---

## Success Criteria

The dynamic deployment name feature is considered **PASS** if:

1. ✅ All HIGH priority test cases pass
2. ✅ Performance impact <5% of baseline
3. ✅ Backward compatibility maintained (existing evaluators work)
4. ✅ Application Insights logs show deployment names
5. ✅ Cost tracking enabled by deployment
6. ✅ No security vulnerabilities
7. ✅ Documentation complete and accurate

---

## Known Limitations & Future Enhancements

### Current Limitations
- No validation that deployment exists before evaluation starts
- No automatic cost optimization recommendations
- No A/B testing capability for comparing deployments

### Future Test Cases (Post-V1)
- **Deployment Validation**: Test pre-flight check for deployment existence
- **Cost Analytics**: Test dashboard showing cost by evaluator/deployment
- **Smart Selection**: Test ML-based deployment recommendation
- **Rate Limit Handling**: Test automatic fallback to cheaper deployment during throttling

---

**Test Documentation Version**: 1.0  
**Last Updated**: November 13, 2025  
**Status**: Ready for Test Execution  
**Owner**: QA Team
