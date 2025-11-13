# Story 10: Dynamic Azure OpenAI Deployment Names per Evaluator Type

**Status**: Ready for Implementation  
**Date**: November 13, 2025  
**Story Points**: 5  
**Priority**: Medium  

---

## User Story

**As a** system administrator or evaluation designer  
**I want** each evaluator type to optionally specify its own Azure OpenAI deployment name  
**So that** I can optimize costs and performance by using different models (GPT-4o, GPT-4-turbo, GPT-3.5-turbo) based on evaluation complexity  

---

## Business Context

### Problem
Currently, all evaluators use the same Azure OpenAI deployment configured at the system level (`AppSettings.AzureOpenAI.DeploymentName`). This prevents:
- **Cost Optimization**: Simple evaluations like "Yes/No" classification could use cheaper models (GPT-3.5-turbo) instead of expensive models (GPT-4o)
- **Performance Tuning**: Complex semantic similarity evaluations benefit from advanced reasoning models, while simple checks don't need them
- **Flexibility**: No way to experiment with different models per evaluator without changing system configuration

### Solution
Enable per-evaluator Azure OpenAI deployment name configuration stored in Dataverse (`mcicaps_deploymentname` field). The system will:
1. Check if evaluator specifies a deployment name
2. Use evaluator-specific deployment if provided
3. Fall back to system default if not specified
4. Log which deployment is being used for observability

### Business Value
- **Cost Savings**: Potential 50-70% cost reduction for simple evaluations by using GPT-3.5-turbo
- **Performance**: Match model capabilities to evaluation requirements
- **Flexibility**: Experiment with different models per evaluator without system-wide changes
- **Backward Compatibility**: Existing evaluators continue working without modification

---

## Technical Requirements

### Architecture Changes

#### Before
- Single `ChatClient` initialized at LLMJudgeService startup
- All evaluators use same deployment from `AppSettings.AzureOpenAI.DeploymentName`
- No per-evaluator model selection capability

#### After
- `AzureOpenAIClient` cached at service level (reusable across deployments)
- `ChatClient` created on-demand per evaluation request
- Deployment name resolved from evaluator type or system default
- Logging shows deployment source (evaluator vs system default)

### Data Model Changes

#### Dataverse Schema
**Table**: `mcicaps_evaluatortypes`

**New Field**:
- **Name**: `mcicaps_deploymentname`
- **Display Name**: Deployment Name
- **Type**: Single Line of Text
- **Max Length**: 100
- **Required**: No (Optional)
- **Description**: Azure OpenAI deployment name to use for this evaluator (e.g., gpt-4o, gpt-4-turbo, gpt-35-turbo). Leave blank to use system default.

**Migration Strategy**:
- ✅ Backward compatible - existing evaluators have NULL → use system default
- ✅ No data migration required
- ✅ Existing evaluations continue working unchanged

#### EvaluatorType Model (.NET)
```csharp
[JsonPropertyName("mcicaps_deploymentname")]
public string? DeploymentName { get; set; }
```

### Service Layer Changes

#### LLMJudgeService Refactoring
**Before**:
```csharp
private readonly ChatClient _chatClient; // Single client for all evaluations
```

**After**:
```csharp
private readonly AzureOpenAIClient _openAIClient; // Base client, creates ChatClients on-demand
```

**Key Methods**:
1. `GetPromptTemplateAndDeploymentAsync()` - Returns both prompt template and deployment name
2. `EvaluateResponseAsync()` - Creates ChatClient using resolved deployment name
3. `CheckHealthAsync()` - Uses default deployment for health check

**Fallback Logic**:
```csharp
var deploymentName = !string.IsNullOrWhiteSpace(evaluatorType.DeploymentName)
    ? evaluatorType.DeploymentName
    : _settings.AzureOpenAI.DeploymentName;
```

### Configuration Changes

#### AppSettings Documentation
```csharp
/// <summary>
/// Default Azure OpenAI deployment name (e.g., gpt-4o, gpt-4-turbo, gpt-35-turbo)
/// Used when evaluator type does not specify a deployment name
/// Individual evaluators can override by setting mcicaps_deploymentname in Dataverse
/// </summary>
public string DeploymentName { get; set; } = "gpt-4o";
```

#### local.settings.example.json Updates
Added guidance:
```json
"_README5A": "=== AZURE OPENAI DEPLOYMENT CONFIGURATION ===",
"_README5B": "The default deployment name is used when evaluator types don't specify their own",
"_README5C": "Individual evaluators can override by setting mcicaps_deploymentname in Dataverse",
"_README5D": "Example deployment names: gpt-4o, gpt-4-turbo, gpt-35-turbo"
```

---

## Acceptance Criteria

### Functional Requirements
- [ ] **AC1**: Evaluator types can have optional `mcicaps_deploymentname` field in Dataverse
- [ ] **AC2**: When evaluator specifies deployment name, system uses that deployment
- [ ] **AC3**: When evaluator doesn't specify deployment, system uses default from AppSettings
- [ ] **AC4**: Logs clearly indicate which deployment is being used (e.g., "Using gpt-35-turbo (from evaluator)")
- [ ] **AC5**: Existing evaluators without deployment name continue working with system default
- [ ] **AC6**: ChatClient instances are created on-demand (not singleton)
- [ ] **AC7**: Health check uses default deployment name

### Non-Functional Requirements
- [ ] **AC8**: No performance degradation (on-demand ChatClient creation is lightweight)
- [ ] **AC9**: All unit tests pass (>80% code coverage on new code)
- [ ] **AC10**: Integration tests verify deployment name resolution
- [ ] **AC11**: Application Insights logs show deployment usage per evaluation
- [ ] **AC12**: Backward compatible with existing evaluators (no breaking changes)

### Documentation Requirements
- [ ] **AC13**: Architecture.md updated with deployment resolution flow
- [ ] **AC14**: PRD updated with deployment name feature requirements
- [ ] **AC15**: Implementation guide created with exact code changes
- [ ] **AC16**: Dataverse schema documentation updated
- [ ] **AC17**: Configuration guidance added to local.settings.example.json

---

## Implementation Tasks

See: [`docs/implementation/dynamic-deployment-names-implementation-guide.md`](../implementation/dynamic-deployment-names-implementation-guide.md) for detailed step-by-step implementation instructions.

### Summary Checklist
1. ✅ Update `EvaluatorType.cs` model with `DeploymentName` property
2. ✅ Refactor `LLMJudgeService` from singleton ChatClient to on-demand creation
3. ✅ Add `GetPromptTemplateAndDeploymentAsync()` method
4. ✅ Update `ILLMJudgeService` interface
5. ✅ Verify WorkerFunction, MultiTurnExecutionService, MultiTurnDurableActivities (no changes needed)
6. ✅ Update AppSettings documentation
7. ✅ Update local.settings.example.json with guidance
8. ✅ Write/update unit tests for dynamic deployment name scenarios
9. ✅ Update Architecture.md with deployment resolution diagram
10. ✅ Update PRD with feature requirements
11. ✅ Create this story document

---

## Testing Strategy

### Unit Tests
**File**: `backend/tests/AiAgentEvaluation.UnitTests/Services/LLMJudgeServiceTests.cs`

**New Test Cases**:
1. `EvaluateResponseAsync_UsesEvaluatorDeploymentName_WhenSpecified`
2. `EvaluateResponseAsync_UsesDefaultDeploymentName_WhenEvaluatorDoesNotSpecify`
3. `GetPromptTemplateAndDeploymentAsync_ReturnsCustomDeployment_WhenSpecified`
4. `GetPromptTemplateAndDeploymentAsync_ReturnsDefaultDeployment_WhenNotSpecified`

**Coverage Target**: >80% on new/modified code

### Integration Tests
1. Create test evaluator in Dataverse with `mcicaps_deploymentname = "gpt-35-turbo"`
2. Execute evaluation and verify Application Insights logs show "Using gpt-35-turbo (from evaluator)"
3. Create test evaluator without deployment name
4. Execute evaluation and verify logs show "Using gpt-4o (system default)"

### Manual Testing Checklist
- [ ] Deploy to staging environment
- [ ] Create evaluator with custom deployment name (e.g., "gpt-4-turbo")
- [ ] Run evaluation and check Application Insights for deployment log
- [ ] Create evaluator without deployment name
- [ ] Run evaluation and verify it uses system default
- [ ] Check Azure OpenAI metrics to confirm correct deployment is being called
- [ ] Verify cost tracking by deployment name

---

## Deployment Plan

### Prerequisites
1. Dataverse schema update: Add `mcicaps_deploymentname` column to `mcicaps_evaluatortypes` table
2. Ensure Azure OpenAI has multiple deployments available (gpt-4o, gpt-4-turbo, gpt-35-turbo)
3. Verify Managed Identity has access to all deployments (Cognitive Services OpenAI User role)

### Deployment Steps
1. **Build and Test**:
   ```powershell
   cd backend/AiAgentEvaluation
   dotnet build --configuration Release
   dotnet test ../tests/AiAgentEvaluation.UnitTests/
   ```

2. **Deploy to Staging**:
   ```powershell
   func azure functionapp publish func-aiestagingmbwc
   ```

3. **Verify Health Check**:
   ```powershell
   curl https://func-aiestagingmbwc.azurewebsites.net/api/health
   ```

4. **Create Test Evaluators**:
   - Evaluator A: Deployment = "gpt-4o" (custom)
   - Evaluator B: Deployment = NULL (system default)

5. **Run Test Evaluations**:
   - Execute evaluation run with both evaluators
   - Check Application Insights for deployment logs

6. **Monitor Costs**:
   - Review Azure OpenAI usage by deployment
   - Compare costs before/after feature

### Rollback Plan
If issues arise:
1. Revert to previous function app deployment
2. Dataverse column remains (optional, unused if code reverted)
3. No data migration needed

**Risk Level**: LOW (backward compatible, optional field)

---

## Success Metrics

### Immediate Success Indicators
- ✅ All unit tests pass
- ✅ Deployment logs show correct deployment name per evaluator
- ✅ Existing evaluators continue working with system default
- ✅ No increase in evaluation execution time
- ✅ Application Insights shows deployment source (evaluator vs default)

### Long-Term Success Indicators
- 📊 **Cost Reduction**: Track Azure OpenAI costs by deployment over 30 days
- 📊 **Adoption Rate**: Monitor how many evaluators use custom deployments
- 📊 **Performance**: Compare evaluation latency by deployment type
- 📊 **Usage Patterns**: Identify which evaluators benefit most from specific models

**Target Cost Savings**: 30-50% reduction in Azure OpenAI costs for simple evaluations

---

## Dependencies

### Technical Dependencies
- Azure OpenAI resource with multiple deployments provisioned
- Managed Identity with access to all deployments
- Dataverse schema update capability
- Application Insights for observability

### Team Dependencies
- **Dataverse Admin**: Create/update `mcicaps_deploymentname` field
- **Azure Admin**: Ensure multiple deployments exist and are accessible
- **Implementation Agent/Developer**: Execute code changes per implementation guide

### External Dependencies
- None (all changes internal to evaluation system)

---

## Related Documentation

- [Implementation Guide](../implementation/dynamic-deployment-names-implementation-guide.md) - Exact code changes
- [Architecture Documentation](../ARCHITECTURE.md) - Section 3: Azure OpenAI (LLM Judge)
- [PRD](../prd/PRD-Agent-Evaluation-Function.md) - Section 3.3.3: Azure OpenAI Judge Integration
- [Story 09: Multi-Evaluator Support](09-multi-evaluator-support.md) - Related feature

---

## Questions & Decisions

### Q1: Should we cache ChatClient instances per deployment name?
**Decision**: No, ChatClient creation is lightweight (~ms). On-demand creation keeps code simple and avoids memory overhead for many deployments.

### Q2: What happens if deployment name doesn't exist in Azure OpenAI?
**Decision**: Azure OpenAI SDK will throw exception. Service's existing retry logic and error handling will capture and log. Consider adding deployment name validation in future enhancement.

### Q3: Should we support deployment name at test case level?
**Decision**: No, evaluator type level is sufficient. Test cases inherit deployment from their evaluator type. This keeps configuration manageable.

### Q4: Do we need to update multi-turn flows?
**Decision**: No changes needed. Multi-turn already passes `EvaluatorTypeId`, so deployment resolution happens automatically in LLMJudgeService.

---

## Future Enhancements

### Potential Follow-Up Stories
1. **Deployment Name Validation**: Validate deployment exists before evaluation starts
2. **Cost Analytics Dashboard**: Track costs by evaluator and deployment
3. **Smart Deployment Selection**: ML-based recommendation for optimal deployment per evaluator
4. **A/B Testing**: Compare evaluator performance across different deployments
5. **Rate Limit Optimization**: Use cheaper deployments during peak times to avoid throttling

---

## Implementation Status

- **Story Created**: November 13, 2025
- **Implementation Guide**: ✅ Complete
- **Architecture Documentation**: ✅ Updated
- **PRD Documentation**: ✅ Updated
- **Code Implementation**: ⏳ Pending
- **Unit Tests**: ⏳ Pending
- **Deployment**: ⏳ Pending
- **Validation**: ⏳ Pending

**Next Steps**: Hand off to implementation agent/developer to execute code changes per implementation guide.
