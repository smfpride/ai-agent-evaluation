# Test Plan: Configurable LLM Judge (Story 10)

**Feature**: Support for both Azure OpenAI and Microsoft Copilot Studio as LLM Judge implementations  
**Story**: `docs/stories/10-configurable-llm-judge.md`  
**Created**: November 13, 2025  
**Test Environment**: Staging (`func-aiestagingmbwc`)

---

## 1. Test Strategy

### 1.1 Testing Approach

This feature introduces a **factory pattern** to support multiple LLM judge implementations while maintaining the existing `ILLMJudgeService` interface. Testing must verify:

1. **Service selection logic** - Factory correctly instantiates the right service based on Judge Agent configuration
2. **Azure OpenAI path** - Existing functionality continues to work with refactored code
3. **Copilot Studio path** - New implementation correctly integrates with Direct Line API
4. **Model routing** - Optional model name correctly enables Copilot Studio topic routing
5. **Configuration validation** - Missing or invalid fields are caught with clear error messages
6. **Prompt handling** - Templates are correctly retrieved from Dataverse and formatted
7. **Response parsing** - JSON responses are correctly parsed from both judge types
8. **Error handling** - Transient failures, timeouts, and malformed responses are handled gracefully

### 1.2 Test Levels

| Level | Focus | Tools | Coverage Target |
|-------|-------|-------|-----------------|
| **Unit Tests** | Individual components, parsing logic, validation | xUnit, Moq | >80% code coverage |
| **Integration Tests** | End-to-end service interactions with real Azure/MCS | xUnit, Test Dataverse | Critical paths |
| **Manual Tests** | Complete evaluation runs with real agents | Azure Portal, Dataverse | User scenarios |

### 1.3 Test Data Requirements

**Dataverse Test Data** (to be created in staging):

1. **Judge Agents**:
   - Azure OpenAI judge with valid endpoint/deployment
   - Copilot Studio judge with Direct Line URL (no model name)
   - Copilot Studio judge with Direct Line URL and model name "gpt-4o"
   - Invalid judge with missing required fields

2. **Evaluator Types**:
   - Default evaluator (uses system prompt)
   - Similarity evaluator (references Azure OpenAI judge)
   - Accuracy evaluator (references Copilot Studio judge without model)
   - Custom evaluator (references Copilot Studio judge with model name)

3. **Test Cases**:
   - Single-turn tests for each evaluator type
   - Multi-turn tests using Copilot Studio judge

---

## 2. Unit Test Plan

Location: `backend/tests/AiAgentEvaluation.UnitTests/Services/`

### 2.1 LLMJudgeServiceFactory Tests

**File**: `LLMJudgeServiceFactoryTests.cs`

```csharp
public class LLMJudgeServiceFactoryTests
{
    [Fact]
    public void CreateService_WithAzureOpenAIType_ReturnsAzureOpenAIJudgeService()
    
    [Fact]
    public void CreateService_WithCopilotStudioType_ReturnsCopilotStudioJudgeService()
    
    [Fact]
    public void CreateService_WithInvalidType_ThrowsArgumentException()
    
    [Fact]
    public void CreateService_WithNullJudgeAgent_ThrowsArgumentNullException()
    
    [Theory]
    [InlineData(null, null)] // Missing AOAI fields
    [InlineData("", "")] // Empty AOAI fields
    public void CreateService_WithInvalidAzureOpenAIConfig_ThrowsArgumentException(
        string endpoint, string deployment)
    
    [Theory]
    [InlineData(null)] // Missing DirectLineUrl
    [InlineData("")] // Empty DirectLineUrl
    public void CreateService_WithInvalidCopilotStudioConfig_ThrowsArgumentException(
        string directLineUrl)
    
    [Fact]
    public void CreateService_LogsCreationWithModelName_WhenModelNameProvided()
    
    [Fact]
    public void CreateService_LogsCreationWithDefaultTopic_WhenModelNameEmpty()
}
```

### 2.2 AzureOpenAIJudgeService Tests

**File**: `AzureOpenAIJudgeServiceTests.cs`

```csharp
public class AzureOpenAIJudgeServiceTests
{
    [Fact]
    public async Task EvaluateAsync_WithValidRequest_ReturnsLLMJudgeResponse()
    
    [Fact]
    public async Task EvaluateAsync_WithEvaluatorTypeId_UsesDataversePrompt()
    
    [Fact]
    public async Task EvaluateAsync_WithNullEvaluatorTypeId_UsesDefaultPrompt()
    
    [Fact]
    public async Task EvaluateAsync_WithDataverseFailure_FallsBackToDefaultPrompt()
    
    [Fact]
    public async Task EvaluateAsync_ParsesJsonResponse_Correctly()
    
    [Fact]
    public async Task EvaluateAsync_ParsesJsonWithMarkdown_Correctly()
    
    [Fact]
    public async Task EvaluateAsync_WithRegexPattern_ExtractsScoreCorrectly()
    
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public async Task EvaluateAsync_WithVariousScores_ReturnsCorrectValue(double score)
    
    [Fact]
    public async Task EvaluateAsync_WithTransientFailure_RetriesAndSucceeds()
    
    [Fact]
    public async Task EvaluateAsync_WithPermanentFailure_ThrowsException()
    
    [Fact]
    public async Task EvaluateAsync_ReplacesPlaceholders_InPromptTemplate()
    
    [Fact]
    public void ParseLLMResponse_WithValidJson_ReturnsCorrectResponse()
    
    [Fact]
    public void ParseLLMResponse_WithMalformedJson_ThrowsJsonException()
    
    [Fact]
    public void ParseLLMResponseWithRegex_FindsScorePattern_ReturnsValue()
}
```

### 2.3 CopilotStudioJudgeService Tests

**File**: `CopilotStudioJudgeServiceTests.cs`

```csharp
public class CopilotStudioJudgeServiceTests
{
    [Fact]
    public async Task EvaluateAsync_WithValidRequest_ReturnsLLMJudgeResponse()
    
    [Fact]
    public async Task EvaluateAsync_WithModelName_InjectsModelPrefix()
    
    [Fact]
    public async Task EvaluateAsync_WithEmptyModelName_SendsPromptWithoutPrefix()
    
    [Fact]
    public async Task EvaluateAsync_WithNullModelName_SendsPromptWithoutPrefix()
    
    [Fact]
    public async Task EvaluateAsync_StartsConversation_WhenNoConversationId()
    
    [Fact]
    public async Task EvaluateAsync_ReusesConversation_WhenConversationIdProvided()
    
    [Fact]
    public async Task EvaluateAsync_WithEvaluatorTypeId_UsesDataversePrompt()
    
    [Fact]
    public async Task EvaluateAsync_WithDataverseFailure_FallsBackToDefaultPrompt()
    
    [Fact]
    public async Task EvaluateAsync_ParsesJsonResponse_Correctly()
    
    [Fact]
    public async Task EvaluateAsync_WithDirectLineError_ThrowsException()
    
    [Fact]
    public async Task EvaluateAsync_WithTransientFailure_RetriesAndSucceeds()
    
    [Fact]
    public async Task WrapPromptForJsonResponse_WithModelName_AddsModelPrefix()
    
    [Fact]
    public async Task WrapPromptForJsonResponse_WithoutModelName_OmitsModelPrefix()
    
    [Fact]
    public async Task BuildEvaluationPrompt_ReplacesAllPlaceholders()
    
    [Fact]
    public void ParseLLMResponse_WithValidJson_ReturnsCorrectResponse()
    
    [Fact]
    public void ParseLLMResponse_WithMarkdownCodeBlock_ExtractsJson()
}
```

### 2.4 JudgeAgent Model Tests

**File**: `JudgeAgentTests.cs`

```csharp
public class JudgeAgentTests
{
    [Fact]
    public void JudgeAgent_WithAllFields_ValidatesSuccessfully()
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JudgeAgent_WithInvalidJudgeType_ThrowsValidationException(string judgeType)
    
    [Fact]
    public void JudgeAgent_WithOptionalModelName_IsValid()
    
    [Fact]
    public void JudgeAgent_Serialization_PreservesAllFields()
}
```

### 2.5 DataverseService Tests (Judge Agent Methods)

**File**: `DataverseServiceTests.cs` (additions)

```csharp
public class DataverseServiceTests
{
    [Fact]
    public async Task GetJudgeAgentAsync_WithValidId_ReturnsJudgeAgent()
    
    [Fact]
    public async Task GetJudgeAgentAsync_WithInvalidId_ThrowsException()
    
    [Fact]
    public async Task GetJudgeAgentAsync_MapsAllFields_Correctly()
    
    [Fact]
    public async Task GetJudgeAgentAsync_WithNullModelName_ReturnsEmptyString()
    
    [Fact]
    public async Task GetJudgeAgentAsync_WithDataverseError_ThrowsException()
}
```

---

## 3. Integration Test Plan

Location: `backend/tests/AiAgentEvaluation.IntegrationTests/`

### 3.1 End-to-End Azure OpenAI Judge Tests

**File**: `AzureOpenAIJudgeIntegrationTests.cs`

**Prerequisites**:
- Valid Azure OpenAI endpoint in staging
- Test Judge Agent record in Dataverse (Azure OpenAI type)
- Test Evaluator Type with custom prompt

```csharp
[Fact]
public async Task EvaluateAsync_WithRealAzureOpenAI_ReturnsValidScore()
{
    // Arrange
    var judgeAgent = await _dataverseService.GetJudgeAgentAsync(testJudgeAgentId);
    var service = _factory.CreateService(judgeAgent);
    var request = new LLMJudgeRequest
    {
        UserInput = "What is your return policy?",
        ExpectedResponse = "We offer 30-day money-back guarantee.",
        ActualResponse = "You can return items within 30 days for a full refund."
    };
    
    // Act
    var result = await service.EvaluateAsync(request, testEvaluatorTypeId);
    
    // Assert
    Assert.InRange(result.Score, 0.0, 1.0);
    Assert.NotEmpty(result.Reasoning);
}

[Fact]
public async Task EvaluateAsync_WithDefaultPrompt_Works()

[Fact]
public async Task EvaluateAsync_WithCustomPrompt_Works()

[Fact]
public async Task EvaluateAsync_WithPlaceholders_SubstitutesCorrectly()
```

### 3.2 End-to-End Copilot Studio Judge Tests

**File**: `CopilotStudioJudgeIntegrationTests.cs`

**Prerequisites**:
- Deployed Copilot Studio judge agent
- Direct Line channel configured
- Test Judge Agent record in Dataverse (Copilot Studio type)
- Agent configured to return JSON responses

```csharp
[Fact]
public async Task EvaluateAsync_WithRealCopilotStudio_ReturnsValidScore()
{
    // Arrange
    var judgeAgent = await _dataverseService.GetJudgeAgentAsync(testMCSJudgeAgentId);
    var service = _factory.CreateService(judgeAgent);
    var request = new LLMJudgeRequest
    {
        UserInput = "What is your return policy?",
        ExpectedResponse = "We offer 30-day money-back guarantee.",
        ActualResponse = "You can return items within 30 days for a full refund."
    };
    
    // Act
    var result = await service.EvaluateAsync(request, testEvaluatorTypeId);
    
    // Assert
    Assert.InRange(result.Score, 0.0, 1.0);
    Assert.NotEmpty(result.Reasoning);
}

[Fact]
public async Task EvaluateAsync_WithModelName_RoutesToCorrectTopic()

[Fact]
public async Task EvaluateAsync_WithoutModelName_RoutesToDefaultTopic()

[Fact]
public async Task EvaluateAsync_WithConversationContext_MaintainsState()

[Fact]
public async Task EvaluateAsync_HandlesAgentTimeout_Gracefully()
```

### 3.3 Factory Integration Tests

**File**: `LLMJudgeServiceFactoryIntegrationTests.cs`

```csharp
[Fact]
public async Task CreateService_WithAzureOpenAIConfig_ConnectsSuccessfully()

[Fact]
public async Task CreateService_WithCopilotStudioConfig_ConnectsSuccessfully()

[Fact]
public async Task SwitchBetweenJudgeTypes_WithinSameRun_Works()
```

---

## 4. Manual Test Plan

### 4.1 Test Scenario 1: Azure OpenAI Judge (Existing Behavior)

**Objective**: Verify refactored Azure OpenAI path maintains existing functionality

**Prerequisites**:
- Staging environment deployed
- Judge Agent record created (Azure OpenAI type)
- Evaluator Type configured to use this judge
- Test Set with 3 test cases

**Steps**:
1. Create Evaluation Run in Dataverse with AgentToEval configured
2. Update Evaluator Type to reference Azure OpenAI Judge Agent
3. Trigger evaluation run via OrchestratorFunction
4. Monitor execution in Application Insights

**Expected Results**:
- ✅ All 3 test cases complete successfully
- ✅ Evaluation results saved to Dataverse with scores 0-1
- ✅ Reasoning field populated with detailed explanations
- ✅ Run status changes to Completed
- ✅ No errors in Application Insights

**Validation Queries**:
```sql
-- Check evaluation results
SELECT mcicaps_testcaseid, mcicaps_score, mcicaps_reasoning
FROM mcicaps_evaluationresults
WHERE mcicaps_evaluationrunid = '<run-id>'

-- Verify all completed
SELECT mcicaps_totaltestcases, mcicaps_completedtestcases, mcicaps_overallscore
FROM mcicaps_evaluationruns
WHERE mcicaps_evaluationrunid = '<run-id>'
```

### 4.2 Test Scenario 2: Copilot Studio Judge (No Model Name)

**Objective**: Verify Copilot Studio judge integration with default topic routing

**Prerequisites**:
- Copilot Studio judge agent deployed (see docs/stories/10-configurable-llm-judge.md section 8)
- Direct Line channel configured
- Judge Agent record created (Copilot Studio type, ModelName empty)
- Evaluator Type configured to use this judge

**Steps**:
1. Configure Copilot Studio agent to return JSON responses (score + reasoning)
2. Create Judge Agent record in Dataverse:
   ```
   JudgeType: "CopilotStudio"
   DirectLineUrl: "<your-direct-line-url>"
   ModelName: "" (empty)
   ```
3. Create Evaluator Type referencing this Judge Agent
4. Create Test Case using this Evaluator Type
5. Trigger single-turn evaluation run

**Expected Results**:
- ✅ Prompt sent to Copilot Studio WITHOUT "MODEL:" prefix
- ✅ Agent routes to default topic
- ✅ Response contains valid JSON with score and reasoning
- ✅ Score parsed correctly (0-1 range)
- ✅ Evaluation result saved to Dataverse

**Validation**:
- Check Application Insights for "Creating CopilotStudioJudgeService for Judge Agent {id} (default topic)" log
- Verify Direct Line conversation started
- Check no "MODEL:" prefix in sent prompt

### 4.3 Test Scenario 3: Copilot Studio Judge (With Model Name)

**Objective**: Verify topic routing via model name prefix

**Prerequisites**:
- Same Copilot Studio agent with multiple topics configured
- Topic with trigger phrase "MODEL: gpt-4o"
- Judge Agent record with model name specified

**Steps**:
1. Create Judge Agent record in Dataverse:
   ```
   JudgeType: "CopilotStudio"
   DirectLineUrl: "<your-direct-line-url>"
   ModelName: "gpt-4o"
   ```
2. Create Evaluator Type referencing this Judge Agent
3. Create Test Case using this Evaluator Type
4. Trigger single-turn evaluation run
5. Check Copilot Studio conversation history in Power Apps

**Expected Results**:
- ✅ Prompt sent with "MODEL: gpt-4o\n\n" prefix
- ✅ Agent routes to gpt-4o topic
- ✅ Response contains valid JSON
- ✅ Evaluation result saved correctly

**Validation**:
- Check Application Insights for "Creating CopilotStudioJudgeService for Judge Agent {id} with model: gpt-4o" log
- Verify "MODEL: gpt-4o" appears at start of prompt in Direct Line API call
- Check Copilot Studio analytics shows correct topic activated

### 4.4 Test Scenario 4: Multi-Turn with Copilot Studio Judge

**Objective**: Verify Copilot Studio judge works in multi-turn orchestrations

**Prerequisites**:
- Multi-turn test case created (3+ turns)
- Copilot Studio Judge Agent configured
- Evaluator Type using Copilot Studio judge

**Steps**:
1. Create multi-turn test case in Dataverse:
   ```
   TestCase: mcicaps_ismultiturn = true
   Turn 1: "What is your return policy?"
   Turn 2: "How long does processing take?"
   Turn 3: "Can I track my refund?"
   ```
2. Configure Evaluator Type to use Copilot Studio judge
3. Trigger evaluation run
4. Monitor Durable Functions orchestration in Azure Storage

**Expected Results**:
- ✅ Each turn evaluated individually via Copilot Studio
- ✅ Conversation context maintained across turns
- ✅ Aggregate result created with overall pass rate
- ✅ All turn results saved to Dataverse with mcicaps_isaggregate = false
- ✅ Aggregate result saved with mcicaps_isaggregate = true

**Validation Queries**:
```sql
-- Check individual turn results
SELECT mcicaps_turnnumber, mcicaps_score, mcicaps_isaggregate
FROM mcicaps_evaluationresults
WHERE mcicaps_testcaseid = '<test-case-id>'
ORDER BY mcicaps_turnnumber

-- Check aggregate result
SELECT mcicaps_score, mcicaps_reasoning, mcicaps_isaggregate
FROM mcicaps_evaluationresults
WHERE mcicaps_testcaseid = '<test-case-id>' 
  AND mcicaps_isaggregate = true
```

### 4.5 Test Scenario 5: Mixed Judge Types in Same Run

**Objective**: Verify different test cases can use different judge types within one evaluation run

**Prerequisites**:
- Azure OpenAI Judge Agent configured
- Copilot Studio Judge Agent configured
- Test Set with 6 test cases (3 using AOAI, 3 using MCS)

**Steps**:
1. Create Evaluator Types:
   - EvaluatorType A → Azure OpenAI Judge
   - EvaluatorType B → Copilot Studio Judge
2. Create Test Cases:
   - TestCase 1, 2, 3 → Use EvaluatorType A
   - TestCase 4, 5, 6 → Use EvaluatorType B
3. Add all 6 test cases to Test Set
4. Create Evaluation Run
5. Trigger run via OrchestratorFunction

**Expected Results**:
- ✅ Test cases 1-3 processed by Azure OpenAI service
- ✅ Test cases 4-6 processed by Copilot Studio service
- ✅ All 6 results saved correctly
- ✅ Run completes with status = Completed
- ✅ Overall score calculated correctly

**Validation**:
- Check Application Insights for both "AzureOpenAIJudgeService" and "CopilotStudioJudgeService" log entries
- Verify mcicaps_completedtestcases = 6
- Confirm results from both judge types present in Dataverse

### 4.6 Test Scenario 6: Invalid Configuration Handling

**Objective**: Verify clear error messages for misconfigured Judge Agents

**Test Cases**:

| Configuration Issue | Expected Behavior |
|---------------------|-------------------|
| JudgeAgent with JudgeType = "AzureOpenAI" but missing Endpoint | Factory throws ArgumentException with message "Azure OpenAI endpoint is required" |
| JudgeAgent with JudgeType = "AzureOpenAI" but missing DeploymentName | Factory throws ArgumentException with message "Azure OpenAI deployment name is required" |
| JudgeAgent with JudgeType = "CopilotStudio" but missing DirectLineUrl | Factory throws ArgumentException with message "Copilot Studio DirectLineUrl is required" |
| JudgeAgent with invalid JudgeType "Unknown" | Factory throws ArgumentException with message "Unsupported judge type: Unknown" |
| EvaluatorType references non-existent JudgeAgent GUID | DataverseService throws exception, test case moves to dead letter queue |

**Steps**:
1. Create invalid Judge Agent configurations in Dataverse
2. Create Evaluator Types referencing these invalid judges
3. Attempt to run evaluation
4. Verify error handling

**Expected Results**:
- ✅ Clear error messages logged to Application Insights
- ✅ Test cases with invalid configs move to dead letter queue
- ✅ Other test cases in run continue processing
- ✅ Run status shows Errored if all test cases fail

---

## 5. Performance Testing

### 5.1 Load Test: Concurrent Evaluations

**Objective**: Verify system handles multiple judge types under load

**Test Configuration**:
- 100 test cases (50 Azure OpenAI, 50 Copilot Studio)
- Single evaluation run
- Monitor throughput and latency

**Metrics to Collect**:
- Average evaluation time per test case
- Azure OpenAI API latency (P50, P95, P99)
- Copilot Studio Direct Line latency (P50, P95, P99)
- Service Bus queue depth
- Function execution count
- Error rate

**Success Criteria**:
- ✅ 95% of test cases complete within 30 seconds
- ✅ Error rate < 1%
- ✅ No throttling errors from Azure OpenAI
- ✅ No timeout errors from Copilot Studio

### 5.2 Retry Behavior Test

**Objective**: Verify retry logic handles transient failures

**Test Scenarios**:
1. Azure OpenAI returns 429 (rate limit) - should retry with exponential backoff
2. Copilot Studio times out - should retry up to 3 times
3. Network interruption - should retry with backoff

**Expected Results**:
- ✅ Transient failures automatically retried
- ✅ Successful after retry within 10 attempts
- ✅ Permanent failures move to dead letter queue
- ✅ Retry attempts logged to Application Insights

---

## 6. Regression Testing

### 6.1 Existing Features to Verify

After deploying configurable judge changes, verify these existing features still work:

| Feature | Test Method | Expected Result |
|---------|-------------|-----------------|
| Single-turn evaluations | Run existing test set | All pass with Azure OpenAI judge |
| Multi-turn evaluations | Run multi-turn test case | Durable orchestration completes |
| Monitor Function | Wait for run completion | Status updated to Completed |
| Default prompts | Evaluator with no custom prompt | System default prompt used |
| Custom prompts in Dataverse | Evaluator with custom EvaluatorType | Custom prompt retrieved and used |
| Placeholder replacement | Prompts with {UserInput} etc. | Placeholders correctly substituted |
| JSON parsing | Various response formats | Scores extracted correctly |
| Regex fallback | Non-JSON responses | Score extracted via regex |

### 6.2 Backward Compatibility

**Objective**: Verify existing Evaluator Types work without Judge Agent configuration

**Test**:
1. Create Evaluator Type WITHOUT mcicaps_judgeagentid field (legacy behavior)
2. Create Test Case using this Evaluator Type
3. Run evaluation

**Expected Results**:
- ✅ System falls back to default Azure OpenAI configuration from app settings
- ✅ Evaluation completes successfully
- ✅ Warning logged: "No Judge Agent configured for EvaluatorType {id}, using default Azure OpenAI"

---

## 7. Error Scenarios & Edge Cases

### 7.1 Error Handling Test Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| Copilot Studio returns non-JSON response | Regex parsing attempted, if fails → error logged, test moves to DLQ |
| Copilot Studio agent is offline | Timeout after 30s, retry 3 times, then DLQ |
| Azure OpenAI returns malformed JSON | Regex parsing attempted, score extracted if possible |
| Dataverse connection lost during evaluation | Exception caught, evaluation retried from Service Bus |
| Judge Agent deleted mid-run | GetJudgeAgentAsync throws, test case fails with clear error |
| Direct Line URL invalid | Connection failure on first request, logged and DLQ |
| Model name contains special characters | Escaped properly, sent as-is to Copilot Studio |
| Empty reasoning field in response | Valid if score present, reasoning = "" |
| Score outside 0-1 range | Validation error, logged and DLQ |

### 7.2 Edge Cases

| Edge Case | Expected Behavior |
|-----------|-------------------|
| Very long prompt (>10,000 chars) | Sent successfully, may hit token limits in LLM |
| Unicode characters in prompts | Properly encoded and sent |
| Null or empty ActualResponse | Placeholder replaced with empty string, evaluation proceeds |
| Multiple conversations to same Copilot Studio agent | Each evaluation gets unique conversationId |
| Judge Agent ModelName = whitespace only | Treated as empty (no prefix added) |
| Evaluator Type prompt with missing placeholders | Placeholders not replaced, sent as-is |

---

## 8. Test Data Setup Scripts

### 8.1 Create Test Judge Agents (PowerShell)

```powershell
# Azure OpenAI Judge Agent
$aoaiJudge = @{
    "mcicaps_name" = "Test Azure OpenAI Judge"
    "mcicaps_judgetype" = "AzureOpenAI"
    "mcicaps_azureopenaiendpoint" = "https://your-aoai.openai.azure.com/"
    "mcicaps_azureopenaimodeldeployment" = "gpt-4o"
    "mcicaps_judgeagentid" = [Guid]::NewGuid()
}

# Copilot Studio Judge Agent (No Model)
$mcsJudgeNoModel = @{
    "mcicaps_name" = "Test MCS Judge (Default Topic)"
    "mcicaps_judgetype" = "CopilotStudio"
    "mcicaps_directlineurl" = "https://directline.botframework.com/v3/directline"
    "mcicaps_modelname" = ""
    "mcicaps_judgeagentid" = [Guid]::NewGuid()
}

# Copilot Studio Judge Agent (With Model)
$mcsJudgeWithModel = @{
    "mcicaps_name" = "Test MCS Judge (GPT-4o Topic)"
    "mcicaps_judgetype" = "CopilotStudio"
    "mcicaps_directlineurl" = "https://directline.botframework.com/v3/directline"
    "mcicaps_modelname" = "gpt-4o"
    "mcicaps_judgeagentid" = [Guid]::NewGuid()
}

# Insert into Dataverse via Web API or Power Automate
```

### 8.2 Create Test Evaluator Types

```powershell
# Similarity Evaluator (Azure OpenAI)
$similarityEvaluator = @{
    "mcicaps_name" = "Similarity - Azure OpenAI"
    "mcicaps_testtype" = "Similarity"
    "mcicaps_judgeagentid@odata.bind" = "/mcicaps_judgeagents($($aoaiJudge.mcicaps_judgeagentid))"
    "mcicaps_prompttemplate" = "Evaluate semantic similarity..."
}

# Accuracy Evaluator (Copilot Studio)
$accuracyEvaluator = @{
    "mcicaps_name" = "Accuracy - Copilot Studio"
    "mcicaps_testtype" = "Accuracy"
    "mcicaps_judgeagentid@odata.bind" = "/mcicaps_judgeagents($($mcsJudgeNoModel.mcicaps_judgeagentid))"
    "mcicaps_prompttemplate" = "Evaluate factual accuracy..."
}
```

---

## 9. Test Execution Schedule

### Phase 1: Unit Tests (Development)
- **When**: During implementation of each component
- **Who**: Developer
- **Duration**: Continuous (part of build pipeline)
- **Exit Criteria**: >80% code coverage, all tests green

### Phase 2: Integration Tests (Pre-Staging)
- **When**: After all components implemented
- **Who**: Developer + QA
- **Duration**: 1 day
- **Exit Criteria**: All integration tests pass with real Azure/MCS services

### Phase 3: Manual Testing (Staging)
- **When**: After deployment to staging
- **Who**: QA + Product Owner
- **Duration**: 2-3 days
- **Exit Criteria**: All 6 manual test scenarios pass

### Phase 4: Performance Testing (Staging)
- **When**: After manual tests pass
- **Who**: Performance Engineer
- **Duration**: 1 day
- **Exit Criteria**: Meets performance success criteria

### Phase 5: Regression Testing (Staging)
- **When**: Before production deployment
- **Who**: QA
- **Duration**: 1 day
- **Exit Criteria**: All existing features still work

---

## 10. Test Environment Configuration

### 10.1 Staging Environment Requirements

**Azure Resources**:
- Function App: `func-aiestagingmbwc`
- Azure OpenAI: Deployed with `gpt-4o` model
- Copilot Studio: Judge agent published to staging environment
- Dataverse: Staging instance with test data
- Service Bus: `test-case-processing` queue
- Storage Account: For Durable Functions state

**Configuration**:
```json
{
  "LLMJudge__DefaultJudgeType": "AzureOpenAI",
  "AzureOpenAI__Endpoint": "https://your-aoai.openai.azure.com/",
  "AzureOpenAI__DeploymentName": "gpt-4o",
  "CopilotStudio__DirectLineUrl": "https://directline.botframework.com/v3/directline"
}
```

### 10.2 Test Data Cleanup

After testing, clean up test data:
```sql
-- Delete test evaluation results
DELETE FROM mcicaps_evaluationresults 
WHERE mcicaps_evaluationrunid IN (
  SELECT mcicaps_evaluationrunid 
  FROM mcicaps_evaluationruns 
  WHERE mcicaps_name LIKE 'TEST%'
)

-- Delete test runs
DELETE FROM mcicaps_evaluationruns 
WHERE mcicaps_name LIKE 'TEST%'

-- Delete test judge agents
DELETE FROM mcicaps_judgeagents 
WHERE mcicaps_name LIKE 'Test%'
```

---

## 11. Acceptance Criteria

Feature is considered **complete and ready for production** when:

- ✅ All unit tests pass with >80% code coverage
- ✅ All integration tests pass with real Azure/MCS services
- ✅ All 6 manual test scenarios pass successfully
- ✅ Performance tests meet success criteria (95% < 30s, error rate < 1%)
- ✅ All regression tests pass (existing features unaffected)
- ✅ Error scenarios handled gracefully with clear error messages
- ✅ Documentation updated (Architecture, PRD, Story 10)
- ✅ Deployment runbook created and validated
- ✅ Code review completed and approved
- ✅ Staging deployment validated by Product Owner

---

## 12. Known Limitations & Future Enhancements

**Current Limitations**:
- Copilot Studio agent must be manually configured to return JSON (not enforced by system)
- Model name routing relies on Copilot Studio topic triggers (not validated)
- No UI for creating/editing Judge Agent records (must use Power Apps or API)

**Future Test Coverage**:
- Automated UI tests for Judge Agent configuration
- Chaos engineering tests (random service failures)
- Cross-region failover testing
- Scale testing with 1000+ concurrent evaluations

---

## 13. Test Reporting

### 13.1 Test Results Template

```markdown
# Test Execution Report: Configurable LLM Judge
**Date**: [Date]
**Tester**: [Name]
**Environment**: Staging

## Summary
- Total Tests: X
- Passed: X
- Failed: X
- Blocked: X
- Pass Rate: X%

## Failed Tests
| Test ID | Test Name | Failure Reason | Bug ID |
|---------|-----------|----------------|--------|
| ...     | ...       | ...            | ...    |

## Performance Metrics
- Average evaluation time: Xs
- P95 latency: Xs
- Error rate: X%

## Recommendations
- [Any issues or concerns]
```

### 13.2 Bug Report Template

```markdown
# Bug Report
**Title**: [Brief description]
**Severity**: Critical / High / Medium / Low
**Environment**: Staging / Production
**Test Scenario**: [Reference test scenario number]

## Steps to Reproduce
1. ...
2. ...

## Expected Result
...

## Actual Result
...

## Logs / Screenshots
...

## Impact
[How this affects functionality]
```

---

## Contact & Support

**Test Plan Owner**: [Name]  
**Questions**: Refer to `docs/stories/10-configurable-llm-judge.md`  
**Issues**: File in GitHub Issues with label `testing`
