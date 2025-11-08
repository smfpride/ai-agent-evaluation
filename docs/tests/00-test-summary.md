# Test Cases: Test Summary and Execution Guide

## Test Suite Overview

This document provides a comprehensive summary of all test cases for the AI Agent Evaluation Azure Function system and guidance for test execution.

### Test Categories Summary

| Category | Test Files | Test Cases | Focus Area |
|----------|------------|------------|------------|
| **Orchestrator Function** | `01-orchestrator-tests.md` | 13 test cases | HTTP trigger, evaluation run initialization, queuing |
| **Worker Function** | `02-worker-tests.md` | 19 test cases | Service Bus processing, agent interaction, LLM evaluation |
| **Monitor Function** | `03-monitor-tests.md` | 19 test cases | Timer trigger, completion detection, metrics calculation |
| **API Integration** | `04-integration-tests.md` | 33 test cases | External service connectivity, authentication, error handling |
| **Performance & Scale** | `05-performance-tests.md` | 26 test cases | Load testing, throughput, scalability, resource optimization |
| **Security & Compliance** | `06-security-tests.md` | 25 test cases | Authentication, data protection, audit, compliance |
| **End-to-End** | `07-e2e-tests.md` | 20 test cases | Complete workflows, real-world scenarios, integration |

**Total Test Cases: 155**

### Test Execution Priority

#### Phase 1: Foundation Testing (Priority 1)
Execute these tests first to validate core functionality and managed identity implementation:
- **TC-ORD-001**: Basic evaluation run initialization
- **TC-WRK-001**: Similarity test case processing with managed identity  
- **TC-WRK-002**: Accuracy test case processing with managed identity
- **TC-WRK-003**: Relevancy test case processing with managed identity
- **TC-MON-001**: Evaluation completion detection
- **TC-INT-001**: Dataverse managed identity authentication
- **TC-INT-006**: Service Bus managed identity authentication
- **TC-SEC-001**: Managed Identity authentication validation (NEW)

#### Phase 2: Integration Testing (Priority 2)
Validate service integrations and managed identity error handling:
- **TC-INT-011**: Direct Line API with managed identity authentication
- **TC-INT-016**: Azure OpenAI managed identity authentication
- **TC-WRK-006**: Copilot Studio API failure handling with managed identity
- **TC-WRK-007**: Azure OpenAI rate limiting with managed identity
- **TC-ORD-007**: Service Bus unavailability with managed identity
- **TC-E2E-001**: Standard evaluation lifecycle with managed identity
- **TC-SEC-025**: RBAC permission validation (NEW)
- **TC-SEC-026**: Managed identity fallback to connection strings (NEW)

#### Phase 3: Scale and Performance (Priority 3)
Test system limits and performance characteristics:
- **TC-PERF-001**: Maximum scale (50,000 test cases)
- **TC-PERF-002**: Concurrent evaluation runs
- **TC-PERF-005**: Service Bus throughput
- **TC-PERF-013**: Auto-scaling validation
- **TC-E2E-003**: Large scale end-to-end

#### Phase 4: Security and Compliance (Priority 4)
Validate security controls and managed identity compliance requirements:
- **TC-SEC-001**: Managed Identity authentication across all services
- **TC-SEC-005**: Data encryption at rest
- **TC-SEC-006**: Data encryption in transit
- **TC-SEC-012**: Audit trail generation
- **TC-SEC-027**: Zero secrets in application configuration validation (NEW)
- **TC-SEC-028**: RBAC role assignment verification (NEW)
- **TC-SEC-029**: Key Vault RBAC access validation (NEW)
- **TC-SEC-030**: DefaultAzureCredential chain validation (NEW)

#### Phase 5: Edge Cases and Resilience (Priority 5)
Test error scenarios and edge cases:
- **TC-E2E-004**: Service failure recovery
- **TC-PERF-017**: Beyond capacity stress test
- **TC-WRK-012**: Malformed LLM Judge response
- **TC-SEC-019**: Input validation security

### Test Environment Requirements

#### Prerequisites
- Azure subscription with required resource quotas
- Test data sets for all three test types (Similarity, Accuracy, Relevancy)
- Copilot Studio agent configured for testing
- Test Dataverse environment with sample data
- Performance monitoring tools configured

#### Infrastructure Requirements
- **Function App**: Premium plan for scaling tests with System-Assigned Managed Identity enabled
- **Service Bus**: Premium tier for high throughput with managed identity configuration
- **Dataverse**: Test environment with managed identity access configured
- **Azure OpenAI**: GPT-4 deployment with managed identity permissions and adequate quota
- **Application Insights**: For monitoring and telemetry with managed identity integration
- **Storage Account**: With RBAC permissions for managed identity access
- **Key Vault**: RBAC-enabled vault with proper role assignments for Function App
- **Load Testing Tools**: For performance test execution with managed identity authentication

#### Test Data Requirements
- Small test set: 100 test cases (mixed types)
- Medium test set: 5,000 test cases (mixed types)  
- Large test set: 50,000 test cases (mixed types)
- Invalid test data for negative testing
- Unicode and international content samples
- Large content samples (approaching 2MB limit)

### Test Execution Guidelines

#### Automated Testing
```yaml
# Suggested CI/CD Pipeline Tests
Unit Tests:
  - Function logic validation
  - Data transformation tests
  - Business logic verification

Integration Tests:
  - Service connectivity
  - Authentication flows
  - Basic error handling

Performance Tests:
  - Load testing scenarios
  - Scalability validation
  - Resource optimization

Security Tests:
  - Vulnerability scanning
  - Configuration validation
  - Access control verification
```

#### Manual Testing
- End-to-end workflow validation
- User experience testing
- Edge case verification
- Disaster recovery scenarios

#### Load Testing Setup
```powershell
# Sample Azure Load Testing Configuration
$resourceGroup = "rg-ai-agent-eval-test"
$loadTestResource = "loadtest-ai-agent-eval"
$testPlan = "evaluation-run-load-test.jmx"

# Create load test
az load create --name "ai-agent-eval-load-test" \
               --resource-group $resourceGroup \
               --load-test-resource $loadTestResource \
               --test-plan $testPlan
```

### Test Metrics and Success Criteria

#### Functional Test Metrics
- **Pass Rate**: ≥ 95% of test cases pass
- **Coverage**: All user stories covered by test cases
- **Integration**: All external services tested
- **Error Handling**: All error scenarios tested

#### Performance Test Metrics
- **Throughput**: ≥ 850 test cases/minute at peak
- **Latency**: Orchestrator response < 30 seconds
- **Scalability**: Support 1000+ concurrent workers
- **Availability**: ≥ 99.9% uptime during testing

#### Security Test Metrics
- **Authentication**: 100% managed identity authentication required (no API keys or connection strings in app config)
- **Authorization**: Proper RBAC validation with principle of least privilege
- **Encryption**: All data encrypted in transit and at rest
- **Audit**: Complete audit trails for all operations
- **Zero Secrets**: No sensitive credentials in application configuration
- **Identity Validation**: DefaultAzureCredential chain working correctly across all services

### Test Reporting

#### Test Execution Report Template
```markdown
# Test Execution Report - [Date]

## Summary
- Total Tests Executed: [Number]
- Passed: [Number] ([Percentage]%)
- Failed: [Number] ([Percentage]%)
- Skipped: [Number] ([Percentage]%)

## Performance Results
- Peak Throughput: [Number] test cases/minute
- Average Response Time: [Number] seconds
- Maximum Concurrent Workers: [Number]
- Success Rate: [Percentage]%

## Issues Identified
| Test Case | Severity | Description | Status |
|-----------|----------|-------------|---------|
| TC-XXX-XXX | High/Med/Low | Issue description | Open/Resolved |

## Recommendations
- [List of recommendations based on test results]
```

### Continuous Testing Strategy

#### Regression Testing
- Execute core functionality tests with each deployment
- Automated performance baseline validation
- Security configuration verification
- Integration health checks

#### Monitoring Integration
- Test result integration with Application Insights
- Performance threshold alerting
- Automated test failure notifications
- Trend analysis and reporting

### Test Maintenance

#### Test Case Updates
- Review test cases quarterly
- Update for new features and requirements
- Retire obsolete test scenarios
- Add tests for discovered edge cases

#### Test Data Management
- Refresh test data sets regularly
- Maintain data privacy compliance
- Version control for test configurations
- Automated test data generation where possible

### Known Limitations and Constraints

#### Test Environment Limitations
- Azure OpenAI quota limits may affect large-scale testing
- Service Bus message size limits (1MB) for test data
- Dataverse API rate limits during high-volume testing
- Cost considerations for extensive performance testing

#### Test Coverage Gaps
- Long-term reliability testing (weeks/months)
- Cross-tenant evaluation scenarios
- Advanced security attack simulations
- Multi-language content edge cases

#### Recommendations for Production
- Gradual rollout with canary testing
- Enhanced monitoring during initial production use
- Regular performance baseline reviews
- Continuous security assessment