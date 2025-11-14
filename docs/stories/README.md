# User Stories

This folder contains detailed user stories and feature specifications for the AI Agent Evaluation System. Each story follows Agile methodology with clear acceptance criteria, implementation details, and status tracking.

## Story Overview

| Story # | Title | Status | Phase |
|---------|-------|--------|-------|
| 01 | Orchestrator Function | ✅ Complete | Phase 2 |
| 02 | Worker Function | ✅ Complete | Phase 3 |
| 03 | Monitor Function | ✅ Complete | Phase 3 |
| 04 | API Integration | ✅ Complete | Phase 3 |
| 05 | Configuration & Security | ✅ Complete | Phase 2-3 |
| 06 | Multi-Turn Support | ✅ Complete | Phase 7 |
| 07 | Durable Functions Multi-Turn | ✅ Complete | Phase 7 |
| 08 | Orchestration Idempotency & Status Tracking | ✅ Complete | Phase 7 |
| 09 | Multi-Evaluator Support | 🚧 In Progress | Current |
| 10 | Dynamic Deployment Names | ✅ Complete | Phase 6 |
| 11 | Configurable LLM Judge | 📋 Planned | Future |

## Story Files

### Core Function Stories
- **[01-orchestrator-function.md](01-orchestrator-function.md)** - HTTP-triggered orchestrator for evaluation run initialization
- **[02-worker-function.md](02-worker-function.md)** - Service Bus-triggered worker for test case execution
- **[03-monitor-function.md](03-monitor-function.md)** - Timer-triggered monitor for completion tracking

### Integration Stories
- **[04-api-integration.md](04-api-integration.md)** - External service integrations (Copilot Studio, Dataverse, Azure OpenAI)
- **[05-configuration-security.md](05-configuration-security.md)** - Managed Identity, Key Vault, and security configuration

### Multi-Turn Conversation Stories
- **[06-multi-turn-support.md](06-multi-turn-support.md)** - Multi-turn conversation test case support
- **[07-durable-functions-multi-turn.md](07-durable-functions-multi-turn.md)** - Durable Functions orchestration for long-running multi-turn tests
- **[08-orchestration-idempotency-status-tracking.md](08-orchestration-idempotency-status-tracking.md)** - Idempotency and status tracking improvements

### Evaluation Enhancement Stories
- **[09-multi-evaluator-support.md](09-multi-evaluator-support.md)** - Support for multiple evaluator types per test case
- **[11-configurable-llm-judge.md](11-configurable-llm-judge.md)** - Configurable LLM Judge with custom prompts

### Infrastructure Stories
- **[10-dynamic-deployment-names.md](10-dynamic-deployment-names.md)** - Dynamic resource naming with unique suffixes

## Story Template

Each story follows this structure:

```markdown
# Story ##: [Title]

## User Story
As a [role], I want [feature] so that [benefit].

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2

## Technical Details
### Architecture
### Data Model Changes
### API Changes

## Implementation Notes
### Code Changes
### Configuration Changes

## Testing Strategy
### Unit Tests
### Integration Tests

## Status
Current implementation status and notes
```

## Using Story Documentation

### For Development
1. Read the story to understand requirements
2. Review acceptance criteria for definition of done
3. Follow implementation notes for guidance
4. Write tests per testing strategy
5. Update story status and implementation notes

### For Code Review
1. Verify all acceptance criteria are met
2. Check implementation against technical details
3. Ensure tests cover testing strategy requirements
4. Confirm status is updated accurately

### For Gap Analysis
1. Compare story requirements with actual code
2. Document gaps in [../implementation/IMPLEMENTATION_GAP_ANALYSIS.md](../implementation/IMPLEMENTATION_GAP_ANALYSIS.md)
3. Create follow-up stories for missing features

## Story Dependencies

```
Story 01 (Orchestrator) → Story 02 (Worker)
                       ↘ Story 03 (Monitor)
                       
Story 04 (API Integration) → Story 02, 03

Story 05 (Security) → All Stories

Story 06 (Multi-Turn) → Story 07 (Durable Functions)
                      → Story 08 (Idempotency)

Story 09 (Multi-Evaluator) → Story 02, 06, 07

Story 10 (Dynamic Names) → Infrastructure

Story 11 (Configurable Judge) → Story 02, 09
```

## Related Documentation

- [PRD](../prd/PRD-Agent-Evaluation-Function.md) - Overall product requirements
- [Architecture](../ARCHITECTURE.md) - System architecture and design
- [Implementation Summary](../implementation/IMPLEMENTATION_SUMMARY.md) - Current implementation status
- [Test Plans](../tests/) - Test cases for each story

---

**Last Updated**: November 14, 2025
