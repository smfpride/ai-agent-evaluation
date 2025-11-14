# Test Documentation

This folder contains comprehensive test plans, test cases, and testing strategies for the AI Agent Evaluation System.

## Overview

The test suite includes **155+ test cases** across 7 categories covering unit tests, integration tests, performance tests, security tests, and end-to-end scenarios.

## Test Files

### Summary
- **[00-test-summary.md](00-test-summary.md)** - Complete test suite overview, execution guide, and priority matrix

### Component Tests
- **[01-orchestrator-tests.md](01-orchestrator-tests.md)** - OrchestratorFunction tests (13 test cases)
- **[02-worker-tests.md](02-worker-tests.md)** - WorkerFunction tests (19 test cases)
- **[03-monitor-tests.md](03-monitor-tests.md)** - MonitorFunction tests (19 test cases)

### Integration & System Tests
- **[04-integration-tests.md](04-integration-tests.md)** - External service integration tests (33 test cases)
- **[05-performance-tests.md](05-performance-tests.md)** - Performance and scalability tests (26 test cases)
- **[06-security-tests.md](06-security-tests.md)** - Security and compliance tests (25 test cases)
- **[07-e2e-tests.md](07-e2e-tests.md)** - End-to-end workflow tests (20 test cases)

### Feature-Specific Tests
- **[08-orchestration-idempotency-tests.md](08-orchestration-idempotency-tests.md)** - Durable Functions idempotency tests
- **[09-multi-evaluator-sample-cases.md](09-multi-evaluator-sample-cases.md)** - Multi-evaluator support test cases
- **[10-dynamic-deployment-name-test-cases.md](10-dynamic-deployment-name-test-cases.md)** - Dynamic resource naming tests
- **[11-configurable-llm-judge-test-plan.md](11-configurable-llm-judge-test-plan.md)** - Configurable LLM Judge test plan

## Test Execution Guide

### Quick Start
1. Read [00-test-summary.md](00-test-summary.md) for overview
2. Execute **Phase 1: Foundation Testing** (Priority 1) tests first
3. Progress through **Phase 2: Integration Testing** (Priority 2)
4. Complete **Phase 3: Scale and Performance** (Priority 3)

### Test Phases

#### Phase 1: Foundation Testing
Core functionality validation including:
- Basic evaluation run initialization
- Managed identity authentication
- Test case processing (similarity, accuracy, relevancy)
- Evaluation completion detection

#### Phase 2: Integration Testing
Service integration validation including:
- Direct Line API integration
- Dataverse connectivity
- Azure OpenAI integration
- Service Bus messaging
- Error handling and retries

#### Phase 3: Scale and Performance
System limits and performance including:
- Large scale processing (50,000+ test cases)
- Concurrent evaluation runs
- Auto-scaling validation
- Resource optimization

### Running Tests

#### Unit Tests
```powershell
# Run all unit tests
dotnet test backend/tests/AiAgentEvaluation.UnitTests/

# Run specific test class
dotnet test --filter FullyQualifiedName~OrchestratorFunctionTests
```

#### Integration Tests
```powershell
# Run all integration tests (requires Azure resources)
dotnet test backend/tests/AiAgentEvaluation.IntegrationTests/

# Run specific integration test
dotnet test --filter FullyQualifiedName~DataverseServiceTests
```

#### End-to-End Tests
Follow procedures in [07-e2e-tests.md](07-e2e-tests.md) for manual E2E test execution.

## Test Coverage Goals

- **Unit Tests**: ≥80% code coverage
- **Integration Tests**: All external service integrations
- **Security Tests**: All authentication paths and RBAC assignments
- **Performance Tests**: Maximum scale validation (50K+ test cases)
- **E2E Tests**: All critical user workflows

## Related Documentation

- [Phase 5 Test Summary](../phase-reports/PHASE5_TEST_SUMMARY.md) - Latest test execution results
- [Architecture](../ARCHITECTURE.md) - System design for context
- [Deployment Checklist](../deployment/DEPLOYMENT_CHECKLIST.md) - Pre-deployment testing requirements
- [Operations Runbook](../operations/production-runbook.md) - Production troubleshooting

## Test Maintenance

### Adding New Tests
1. Follow existing test file naming convention (##-feature-tests.md)
2. Update [00-test-summary.md](00-test-summary.md) with new test counts
3. Add test cases to appropriate category
4. Update this README with new file

### Test Review Schedule
- **Weekly**: Review failing tests and flaky tests
- **Monthly**: Update test plans for new features
- **Quarterly**: Review test coverage and add missing scenarios

---

**Last Updated**: November 14, 2025
