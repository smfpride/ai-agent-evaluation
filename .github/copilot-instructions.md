# AI Agent Evaluation System - Copilot Instructions

## Architecture Overview

This is a **serverless Azure Functions** system (.NET 8) that automates testing and evaluation of Copilot Studio agents using an **LLM-as-a-Judge** approach. The architecture uses a queue-based design with two execution paths:

- **Single-turn tests**: Service Bus queue → Worker Functions → Copilot Studio + Azure OpenAI
- **Multi-turn tests**: Durable Functions orchestration → Activities → Maintain conversation context

**Critical**: Multi-turn does NOT use Service Bus. The `multi-turn-test-case-processing` queue exists but is unused. Multi-turn execution happens via `MultiTurnDurableOrchestrator`.

## Core Components

### Functions (in `backend/AiAgentEvaluation/Functions/`)
- `OrchestratorFunction`: HTTP entry point - routes test cases to Service Bus (single-turn) or starts Durable orchestrations (multi-turn)
- `WorkerFunction`: Service Bus triggered - executes single-turn tests only
- `MultiTurnDurableOrchestrator`: Long-running orchestrations for multi-turn conversations (no 5-min timeout)
- `MultiTurnDurableActivities`: Helper activities for turn execution (`ExecuteSingleTurn`, `GetTestCaseWithTurns`, `CreateAggregateResult`)
- `MonitorFunction`: Timer-triggered - polls Dataverse every minute to update run completion status

### Services (in `backend/AiAgentEvaluation/Services/`)
All follow interface-based dependency injection pattern:
- `IDataverseService`: CRUD for test cases, runs, results (OAuth - Managed Identity NOT supported by Dataverse SDK)
- `ICopilotStudioService`: Direct Line API integration for agent conversations
- `ILLMJudgeService`: Azure OpenAI evaluation using prompts from Dataverse `EvaluatorType` records
- `IServiceBusService`: Message queuing for single-turn tests only
- `IMultiTurnExecutionService`: Turn-by-turn execution logic (used by Durable activities)

## Authentication Pattern

**Default: Managed Identity everywhere possible** using `DefaultAzureCredential`:
- Azure OpenAI: Cognitive Services OpenAI User role
- Service Bus: Azure Service Bus Data Owner role  
- Key Vault: Key Vault Secrets User role
- Storage: Blob Data Contributor + File Data Privileged Contributor

**Exception: Dataverse** requires ClientSecret (ClientId + ClientSecret stored in Key Vault) because the SDK doesn't support Managed Identity.

See `Program.cs` lines 74+ for credential setup and service registration with try-catch blocks to prevent startup failures.

## Data Model (Dataverse)

**Core tables** (all prefixed `mcicaps_`):
- `evaluationruns`: Test run metadata with StatusCode enum (1=Created, 778760001=Execute, 778760002=Running, 778760003=Errored, 778760004=Completed)
- `agenttoevals`: Agent configuration including Direct Line URL (centralized lookup)
- `testcases`: Test definitions with `mcicaps_ismultiturn` flag and `mcicaps_evaluatortypeid` lookup
- `testcaseturn`: Individual turns for multi-turn tests (sequence, prompttext, expectedresponse)
- `evaluatortypes`: Evaluation prompt templates (Similarity, Accuracy, Relevancy, or custom) with placeholder support
- `evaluationresults`: Individual test results with `mcicaps_isaggregate` flag for multi-turn summaries

**Key relationships**:
- EvaluationRun → N TestCases via TestSet
- EvaluationRun → AgentToEval (1:1 lookup for Direct Line URL)
- TestCase → N TestCaseTurns (multi-turn only)
- TestCase → EvaluatorType (lookup by GUID for prompt templates)
- TestCase → N EvaluationResults (one per turn, plus one aggregate if multi-turn)

## Workflow Patterns

### Starting an Evaluation
1. POST to `OrchestratorFunction.StartEvaluationRun` with `evaluationRunId`
2. Updates status to Running (778760002), sets `mcicaps_starttime`, initializes `mcicaps_totaltestcases`
3. Fetches all test cases and Direct Line URL from `AgentToEval` lookup
4. Single-turn: Queue messages to `test-case-processing` Service Bus queue
5. Multi-turn: Call `durableClient.ScheduleNewOrchestrationInstanceAsync` per test case with instance ID pattern `multiturn-{testCaseId}`
6. Returns HTTP 202 with estimated completion time

### Worker Execution (Single-Turn)
1. Service Bus trigger receives `ProcessTestCaseMessage` from `test-case-processing` queue
2. Loads evaluator prompt template from Dataverse `EvaluatorType` table
3. Sends prompt to Copilot Studio Direct Line API
4. Evaluates response with Azure OpenAI using type-specific criteria
5. Saves result to Dataverse and atomically increments `mcicaps_completedtestcases`
6. Dead letter queue after 10 retries

### Durable Orchestration (Multi-Turn)
1. `MultiTurnTestCaseOrchestrator` calls `GetTestCaseWithTurns` activity
2. For each turn sequentially: `ExecuteSingleTurn` activity maintains conversationId from Turn 1
3. After all turns: `CreateAggregateResult` activity calculates overall pass rate
4. State persisted to Azure Storage (no timeout limits)
5. Supports pause/resume and replay on failure

### Completion Tracking
`MonitorFunction` timer (every 1 min):
- Queries runs with status=Running
- Compares `mcicaps_totaltestcases` vs count of results
- Detects stalls: 30+ min no progress or 120+ min total runtime
- On completion: Updates status to Completed (778760004), sets `mcicaps_endtime`, calculates `mcicaps_overallscore`

## Configuration Management

See `local.settings.example.json` for template. **Key differences from typical Azure Functions**:
- Most settings use Managed Identity (no connection strings in production)
- Dataverse requires connection string: `@Microsoft.KeyVault(VaultName=...;SecretName=DataverseConnectionString)`
- Service Bus uses `ServiceBus__Namespace` (double underscore maps to nested config)
- Direct Line URLs are per-agent in Dataverse, not in app settings
- `host.json` configures Durable Functions: `maxConcurrentActivityFunctions: 100`

## Development Workflows

### Local Development
```powershell
cd backend/AiAgentEvaluation
# Copy and configure local.settings.json
func start
```
Emulator fallbacks: Uses connection strings instead of Managed Identity when running locally.

### Testing Requirements ⚠️ MANDATORY

**CRITICAL: All code changes MUST include unit tests and pass before completion.**

#### Pre-Completion Checklist
Before marking any code change as complete, you MUST:

1. **Write Unit Tests** for all new/modified code:
   - Create tests in `backend/tests/AiAgentEvaluation.UnitTests/`
   - Use Moq for service mocking (follow existing patterns)
   - Cover success cases, error cases, and edge cases
   - Aim for >80% code coverage on new code

2. **Build the Solution**:
   ```powershell
   cd backend/AiAgentEvaluation
   dotnet build --configuration Release
   ```
   - Must complete with zero errors
   - Address all warnings related to your changes

3. **Run All Unit Tests**:
   ```powershell
   dotnet test backend/tests/AiAgentEvaluation.UnitTests/AiAgentEvaluation.UnitTests.csproj
   ```
   - All tests must pass (green)
   - Review any test failures and fix before proceeding

4. **Verify Integration Tests** (if applicable):
   ```powershell
   dotnet test backend/tests/AiAgentEvaluation.IntegrationTests/AiAgentEvaluation.IntegrationTests.csproj
   ```
   - Only if changes affect external integrations
   - Requires Azure resources configured

#### Testing Patterns
- **Functions**: Mock `ILogger`, service dependencies, test triggers and responses
- **Services**: Mock external dependencies (HttpClient, Dataverse, Azure SDK clients)
- **Models**: Validate serialization, property mapping, validation logic
- **Extensions**: Test configuration loading, error handling, fallback behavior

Example test structure:
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var mockService = new Mock<IService>();
    mockService.Setup(x => x.Method()).ReturnsAsync(expectedValue);
    
    // Act
    var result = await sut.MethodUnderTest();
    
    // Assert
    Assert.Equal(expected, result);
    mockService.Verify(x => x.Method(), Times.Once);
}
```

### Documentation Requirements ⚠️ MANDATORY

**CRITICAL: All code changes MUST update relevant documentation before completion.**

#### Required Documentation Updates

For ANY code change, update these documents in order:

1. **`docs/ARCHITECTURE.md`** - Update if changes affect:
   - System architecture or component interactions
   - Data flows or execution paths
   - Technology stack or Azure services
   - Security/authentication patterns
   - Add/update architecture diagrams (Mermaid syntax)

2. **`docs/prd/PRD-Agent-Evaluation-Function.md`** - Update if changes affect:
   - Functional requirements or features
   - API contracts or data schemas
   - Configuration settings
   - Performance targets or SLAs
   - Integration points with external services

3. **Relevant Story** in `docs/stories/` - Update the specific story file:
   - `01-orchestrator-function.md` - OrchestratorFunction changes
   - `02-worker-function.md` - WorkerFunction changes
   - `03-monitor-function.md` - MonitorFunction changes
   - `06-multi-turn-support.md` - Multi-turn/Durable Functions changes
   - Add implementation notes, decisions, learnings
   - Update acceptance criteria if behavior changes

4. **Test Documentation** in `docs/tests/` - Update if changes affect:
   - Test strategies or approaches
   - New test scenarios or edge cases
   - Integration test setup requirements
   - Create test plan for new features

#### Documentation Update Pattern

When making changes:

1. **Before coding**: Review relevant docs to understand current design
2. **During coding**: Note what docs will need updates
3. **After testing**: Update all relevant documentation files
4. **Final check**: Verify documentation accurately reflects implementation

Example commit pattern:
```
feat: Add custom evaluator type support

- Implemented IEvaluatorTypeService for dynamic prompt loading
- Added EvaluatorTypeCache for performance optimization
- Unit tests: 12 new tests, all passing

Documentation updates:
- ARCHITECTURE.md: Added Evaluator Type Management section
- PRD-Agent-Evaluation-Function.md: Updated section 3.3.4
- 06-multi-turn-support.md: Added custom evaluator notes
- tests/: Created custom-evaluator-test-plan.md
```

#### Documentation Standards

- Use clear, concise language
- Include code examples for new patterns
- Update diagrams when architecture changes
- Cross-reference related sections
- Add dates to document headers when making significant updates
- Maintain consistency with existing documentation style

### Deployment

**Infrastructure is already deployed** - only function code needs to be deployed for updates.

```powershell
# Navigate to function app directory
cd backend/AiAgentEvaluation

# Build and test FIRST (mandatory)
dotnet build --configuration Release
dotnet test ../tests/AiAgentEvaluation.UnitTests/AiAgentEvaluation.UnitTests.csproj

# Deploy to Azure (staging environment)
func azure functionapp publish func-aiestagingmbwc
```

**Function app**: `func-aiestagingmbwc` (staging environment)
**Resource group**: `rg-agent-eval-staging`

**Post-deployment verification**:
- Check Application Insights for function startup logs
- Verify all functions appear in Azure portal Functions list
- Test health endpoint: `https://func-aiestagingmbwc.azurewebsites.net/api/health`

See `docs/deployment/DEPLOYMENT_CHECKLIST.md` for complete validation steps.

## Key Conventions

### Naming Patterns
- Bicep resources: `{type}-{prefix}{env}{suffix}` (e.g., `func-aiedev1234`)
- Functions: PascalCase with descriptive suffixes (e.g., `StartEvaluationRun`)
- Dataverse fields: `mcicaps_` prefix (solution-specific)
- Durable instance IDs: `multiturn-{testCaseId}` for idempotency

### Error Handling
- Services implement `IDisposable` for resource cleanup
- Retry logic: Exponential backoff for transient failures (see `CopilotStudioService` and `LLMJudgeService`)
- Structured logging: Use `ILogger` with structured properties: `_logger.LogInformation("Test {TestCaseId} score: {Score}", id, score)`
- Try-catch in `Program.cs` service registration prevents startup failures

### Code Organization
- Models: DTOs in `Models/` - separated by domain (e.g., `ApiModels.cs`, `EvaluationRun.cs`)
- Extensions: Utility methods in `Extensions/` (e.g., `ConfigurationExtensions.AddAzureKeyVaultWithManagedIdentity`)
- No shared state between function invocations (stateless workers)

## Common Pitfalls

1. **Multi-turn queue confusion**: The `multi-turn-test-case-processing` queue is provisioned but NEVER used. Multi-turn uses Durable Functions exclusively.

2. **Dataverse authentication**: Unlike other Azure services, Dataverse requires OAuth connection string in Key Vault - do NOT try to use Managed Identity.

3. **Status code values**: Dataverse uses numeric option sets (778760002 for Running), not string enums. See `EvaluationRunStatus` static class.

4. **Conversation context**: In multi-turn, conversationId from Turn 1 must be reused in all subsequent turns or context is lost.

5. **Prompt templates**: Stored in Dataverse `EvaluatorType` records, NOT Azure Blob Storage. Fallback to embedded prompts in code if Dataverse fetch fails.

6. **Progress tracking**: Uses atomic `SetProgressCounts` method in `DataverseService` to avoid race conditions with concurrent workers.

7. **Forgetting documentation**: Code changes without documentation updates create drift. Always update Architecture, PRD, and relevant story BEFORE marking work complete.

8. **Skipping tests**: Untested code causes production issues. Write unit tests for ALL new/modified code and verify they pass before completion.

## Documentation Structure

- `docs/ARCHITECTURE.md`: System design, data flows, component interactions
- `docs/prd/PRD-Agent-Evaluation-Function.md`: Complete functional requirements
- `docs/stories/`: Feature-by-feature implementation stories (01-orchestrator, 02-worker, 06-multi-turn-support, etc.)
- `docs/deployment/`: Step-by-step deployment guides with validation scripts
- `docs/security/`: Managed Identity setup and RBAC requirements
- `backend/infrastructure/README.md`: Bicep templates and IaC patterns

## Change Management Workflow

**MANDATORY PROCESS for all code changes:**

### 1. Planning Phase
- Identify which components/services will be modified
- Review existing documentation (Architecture, PRD, relevant story)
- Identify which tests need to be updated/created
- Plan documentation updates needed

### 2. Implementation Phase
- Write code following existing patterns
- Follow error handling conventions (IDisposable, retry logic, structured logging)
- Ensure Managed Identity usage for new Azure service integrations
- Add XML documentation comments for public APIs

### 3. Testing Phase (MANDATORY - DO NOT SKIP)
```powershell
# Build first
dotnet build --configuration Release

# Run unit tests - MUST PASS
dotnet test backend/tests/AiAgentEvaluation.UnitTests/

# Run integration tests if external dependencies changed
dotnet test backend/tests/AiAgentEvaluation.IntegrationTests/
```

### 4. Documentation Phase (MANDATORY - DO NOT SKIP)
Update in this order:
1. `docs/ARCHITECTURE.md` - Architecture/design changes
2. `docs/prd/PRD-Agent-Evaluation-Function.md` - Requirements/API changes
3. Relevant story in `docs/stories/` - Implementation details
4. Test docs in `docs/tests/` - Test strategies/plans

### 5. Completion Checklist
- [ ] Code compiles with no errors
- [ ] All unit tests pass
- [ ] New unit tests written for new/modified code
- [ ] Integration tests pass (if applicable)
- [ ] Architecture.md updated (if applicable)
- [ ] PRD updated (if applicable)
- [ ] Relevant story updated
- [ ] Test documentation updated (if applicable)
- [ ] Code follows project conventions
- [ ] Structured logging added for observability

**Only after ALL checkboxes are complete, mark the change as done.**

## Quick Reference

**Add a new evaluator type**: Create record in Dataverse `mcicaps_evaluatortypes` with prompt template → Reference by GUID in test cases → Update ARCHITECTURE.md section 5.3 and PRD section 3.3.4.

**Add a new function**: Create in `Functions/`, add interface to `Services/` if needed, register in `Program.cs`, write unit tests, update ARCHITECTURE.md components section.

**Modify data model**: Update Dataverse schema, update Models classes, update ARCHITECTURE.md section 2, update PRD data schema section.

**Debug multi-turn**: Check Durable Functions state in Azure Storage Table `DurableTaskPartitions` or enable `traceInputsAndOutputs` in `host.json`.

**Scale limits**: 1000 concurrent single-turn workers (Service Bus), unlimited multi-turn orchestrations (Durable Functions).

**Monitoring**: Application Insights custom metrics - search for `TestCase.Executed`, `EvaluationRun.Duration`.
