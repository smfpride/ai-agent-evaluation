# Phase 6: Production Deployment Guide

**Last Updated**: November 8, 2025  
**Status**: IN PROGRESS  
**Prerequisites**: Phases 0-5 Complete (125 tests passing at 100%)

## 📋 Overview

This guide provides comprehensive instructions for deploying the AI Agent Evaluation Function to production environments. It covers staging validation, production deployment, security hardening, monitoring setup, and operational procedures.

## 🎯 Deployment Objectives

1. **Zero-Downtime Deployment**: Deploy without impacting existing evaluations
2. **Security First**: Ensure all security controls are properly configured
3. **Performance Validation**: Verify 50K test case processing capability
4. **Monitoring & Alerting**: Full observability from day one
5. **Operational Readiness**: Complete runbooks and troubleshooting guides

## 🏗️ Environment Strategy

### Environment Tiers

| Environment | Purpose | Infrastructure | Data | Duration |
|------------|---------|----------------|------|----------|
| **Development** | Local dev & unit testing | Local emulators, dev Azure services | Synthetic test data | Continuous |
| **Staging** | Integration & pre-prod validation | Production-like Azure infrastructure | Anonymized production data | 2-3 days |
| **Production** | Live evaluation processing | Full-scale Azure infrastructure | Live production data | Ongoing |

### Environment Configuration Files

- `main.parameters.dev.json` - Development environment (existing)
- `main.parameters.staging.json` - Staging environment (to be created)
- `main.parameters.prod.json` - Production environment (existing)

## 📝 Phase 6 Implementation Checklist

### 1. Pre-Deployment Preparation (Week 1, Days 1-2)

#### 1.1 Documentation Review
- [ ] Review PRD for production requirements
- [ ] Review all user stories and acceptance criteria
- [ ] Verify test coverage meets requirements (125 tests, 100% passing) ✅
- [ ] Review security requirements and compliance needs

#### 1.2 Infrastructure Preparation
- [ ] Create staging environment Bicep parameters
- [ ] Validate Bicep templates for production readiness
- [ ] Review Azure subscription quotas and limits
- [ ] Plan resource naming conventions and tagging strategy
- [ ] Document network topology and security boundaries

#### 1.3 CI/CD Pipeline Setup
- [ ] Create GitHub Actions workflow for automated deployments
- [ ] Configure environment-specific secrets and variables
- [ ] Set up deployment approval gates for production
- [ ] Configure automated testing in CI pipeline
- [ ] Set up deployment notifications and alerts

### 2. Security Hardening (Week 1, Days 3-4)

#### 2.1 Network Security
- [ ] Configure Virtual Network integration for Function App
- [ ] Set up Private Endpoints for Azure services
- [ ] Configure Network Security Groups (NSGs)
- [ ] Enable Azure Firewall rules for outbound traffic
- [ ] Configure service endpoints for Dataverse connectivity
- [ ] Validate HTTPS-only and TLS 1.2 minimum settings ✅

#### 2.2 Identity & Access Management
- [ ] Validate System-Assigned Managed Identity configuration ✅
- [ ] Review and minimize RBAC role assignments
- [ ] Configure Conditional Access policies for admin access
- [ ] Set up Privileged Identity Management (PIM) for elevated access
- [ ] Document all role assignments and justifications
- [ ] Test managed identity authentication end-to-end ✅

#### 2.3 Data Protection
- [ ] Enable encryption at rest for all storage services
- [ ] Configure customer-managed keys (CMK) if required
- [ ] Enable encryption in transit validation ✅
- [ ] Configure data retention policies
- [ ] Set up backup and disaster recovery procedures
- [ ] Test data recovery procedures

#### 2.4 Audit & Compliance
- [ ] Enable Azure Policy compliance scanning
- [ ] Configure Microsoft Defender for Cloud
- [ ] Set up audit logging for all administrative actions ✅
- [ ] Configure diagnostic settings for all resources
- [ ] Document compliance mapping (SOC 2, ISO 27001, etc.)
- [ ] Perform security vulnerability assessment

### 3. Monitoring & Observability (Week 1, Day 5)

#### 3.1 Application Insights Configuration
- [ ] Configure Application Insights with proper instrumentation ✅
- [ ] Set up custom metrics for business KPIs
- [ ] Create application performance monitoring (APM) dashboards
- [ ] Configure distributed tracing with correlation IDs ✅
- [ ] Set up availability tests for health endpoints
- [ ] Configure smart detection and anomaly alerts

#### 3.2 Alerting Strategy
- [ ] **Critical Alerts** (Immediate Response)
  - [ ] Function app down or unhealthy
  - [ ] All evaluation runs failing (>95% error rate)
  - [ ] Azure OpenAI quota exhausted
  - [ ] Service Bus dead letter queue threshold exceeded
  - [ ] Managed identity authentication failures
  
- [ ] **Warning Alerts** (Investigation within 1 hour)
  - [ ] High error rate (>10% of evaluations failing)
  - [ ] Performance degradation (>5 min per test case)
  - [ ] Service Bus message backlog building
  - [ ] Storage or compute approaching quota limits
  - [ ] Increased p95/p99 latency
  
- [ ] **Informational Alerts** (Daily review)
  - [ ] Daily evaluation run summary
  - [ ] Cost anomalies detected
  - [ ] Unusual usage patterns
  - [ ] Approaching auto-scale limits

#### 3.3 Dashboard Creation
- [ ] **Operations Dashboard**
  - Real-time function execution metrics
  - Service Bus queue depth and throughput
  - Error rates by function and service
  - Active evaluation runs status
  
- [ ] **Performance Dashboard**
  - Average test case processing time
  - Throughput (test cases per minute)
  - Auto-scale instance count over time
  - p50, p95, p99 latency percentiles
  
- [ ] **Cost Dashboard**
  - Daily and monthly cost trends
  - Cost per evaluation run
  - Cost per test case
  - Resource utilization efficiency

#### 3.4 Log Analytics
- [ ] Configure Log Analytics workspace integration ✅
- [ ] Create custom Kusto queries for common investigations
- [ ] Set up log retention policies
- [ ] Configure log sampling for high-volume scenarios
- [ ] Document query library for troubleshooting

### 4. Staging Deployment & Validation (Week 2, Days 1-2)

#### 4.1 Staging Environment Deployment
```powershell
# Deploy to staging environment
cd c:\dev\ai-agent-evaluation\backend\infrastructure
.\deploy.ps1 -EnvironmentName "staging" -ResourceGroupName "rg-agent-eval-staging"
```

- [ ] Execute staging deployment with Bicep templates
- [ ] Validate all Azure resources provisioned correctly
- [ ] Verify managed identity and RBAC assignments
- [ ] Confirm Key Vault secrets are accessible
- [ ] Test connectivity to all external services

#### 4.2 Integration Testing in Staging
- [ ] Run full test suite against staging environment (125 tests)
- [ ] Execute end-to-end evaluation workflows for all test types
- [ ] Validate Dataverse integration with test data
- [ ] Test Copilot Studio Direct Line API connectivity
- [ ] Verify Azure OpenAI evaluation judge functionality
- [ ] Test Service Bus message processing at scale
- [ ] Validate prompt template loading from Blob Storage

#### 4.3 Performance Validation
- [ ] **Small Scale Test**: 100 test cases
  - Verify successful completion
  - Measure average processing time per test case
  - Validate scoring accuracy
  
- [ ] **Medium Scale Test**: 5,000 test cases
  - Monitor auto-scaling behavior
  - Check Service Bus throughput (target: 1,000+ msg/min)
  - Validate no timeouts or failures
  - Measure orchestrator response time (target: <30 sec)
  
- [ ] **Large Scale Test**: 50,000 test cases
  - Execute full-scale evaluation run
  - Monitor all resources during processing
  - Validate completion within expected timeframe
  - Review cost per test case
  - Verify data consistency in Dataverse

#### 4.4 Failure & Recovery Testing
- [ ] Test orchestrator retry logic with transient failures
- [ ] Validate worker function error handling
- [ ] Test Service Bus dead letter queue processing
- [ ] Simulate Azure OpenAI throttling (429 responses)
- [ ] Test circuit breaker behavior under load
- [ ] Validate evaluation run failure recovery

#### 4.5 Security Validation in Staging
- [ ] Attempt access without managed identity (should fail)
- [ ] Test RBAC enforcement for each service
- [ ] Validate no secrets in application configuration ✅
- [ ] Test Key Vault fallback authentication for local dev
- [ ] Verify audit logs capture all operations
- [ ] Run automated security scanning tools

### 5. Production Deployment (Week 2, Days 3-4)

#### 5.1 Pre-Production Checklist
- [ ] All staging tests passed with 100% success rate
- [ ] Security review completed and signed off
- [ ] Performance validation meets SLA requirements
- [ ] Deployment runbook reviewed and approved
- [ ] Rollback plan documented and tested
- [ ] On-call team identified and briefed
- [ ] Deployment change request approved
- [ ] Communication sent to stakeholders

#### 5.2 Production Deployment Execution

**Deployment Window**: Schedule during low-usage period

```powershell
# Production deployment with validation
cd c:\dev\ai-agent-evaluation\backend\infrastructure

# Step 1: Validate Bicep template
.\validate.ps1 -EnvironmentName "prod"

# Step 2: Deploy infrastructure
.\deploy.ps1 -EnvironmentName "prod" -ResourceGroupName "rg-agent-eval-prod"

# Step 3: Validate deployment
.\validate-deployment.ps1 -ResourceGroupName "rg-agent-eval-prod"

# Step 4: Upload prompt templates
# (Execute from Function App or via script)

# Step 5: Run smoke tests
# (Execute subset of integration tests)
```

- [ ] Execute production deployment script
- [ ] Validate all resources provisioned successfully
- [ ] Verify managed identity authentication
- [ ] Test health check endpoint responds successfully
- [ ] Upload production prompt templates to Blob Storage
- [ ] Run smoke tests (subset of 10 test cases)
- [ ] Monitor Application Insights for errors
- [ ] Validate first production evaluation run

#### 5.3 Post-Deployment Validation
- [ ] Execute health check against all endpoints
- [ ] Run small evaluation run (10 test cases)
- [ ] Verify results written to Dataverse correctly
- [ ] Check Application Insights telemetry flowing
- [ ] Confirm all alerts are configured and active
- [ ] Review cost tracking is operational
- [ ] Validate backup and retention policies active

#### 5.4 Gradual Rollout Strategy
- [ ] **Phase 1**: Enable for 10% of evaluation runs (monitoring closely)
- [ ] **Phase 2**: Increase to 50% after 24 hours of stability
- [ ] **Phase 3**: Full rollout to 100% after 72 hours

### 6. Performance Optimization (Week 2, Day 5)

#### 6.1 Function App Optimization
- [ ] Configure optimal concurrency settings for workers
- [ ] Enable HTTP/2 for better performance
- [ ] Configure connection pooling limits
- [ ] Optimize cold start performance
- [ ] Review and tune memory allocation
- [ ] Configure pre-warmed instances for critical functions

#### 6.2 Service Bus Optimization
- [ ] Configure message prefetch count for optimal throughput
- [ ] Set appropriate max concurrent calls per instance
- [ ] Enable partitioning for high-throughput queues
- [ ] Configure message batching for efficiency
- [ ] Set optimal message lock duration
- [ ] Review and tune auto-scaling triggers

#### 6.3 Dataverse Optimization
- [ ] Optimize query patterns with proper filtering
- [ ] Configure bulk operation batching
- [ ] Enable caching where appropriate
- [ ] Review and optimize indexes
- [ ] Configure connection pooling
- [ ] Monitor API rate limits and throttling

#### 6.4 Azure OpenAI Optimization
- [ ] Configure optimal token limits for prompts
- [ ] Set appropriate temperature and top_p values
- [ ] Enable response caching where possible
- [ ] Configure retry logic for rate limiting
- [ ] Monitor quota usage and scaling needs
- [ ] Optimize prompt templates for efficiency

#### 6.5 Blob Storage Optimization
- [ ] Enable caching for frequently accessed templates
- [ ] Configure appropriate access tier (Hot/Cool)
- [ ] Optimize blob naming for query performance
- [ ] Enable CDN if appropriate for template delivery
- [ ] Configure lifecycle management policies

### 7. Operational Readiness (Ongoing)

#### 7.1 Runbook Creation
- [ ] **Deployment Runbook**: Step-by-step deployment procedures ✅
- [ ] **Monitoring Runbook**: How to read dashboards and respond to alerts
- [ ] **Incident Response Runbook**: Triage and resolution procedures
- [ ] **Scaling Runbook**: How to scale up/down resources
- [ ] **Backup & Recovery Runbook**: DR procedures and RTO/RPO
- [ ] **Cost Optimization Runbook**: Regular cost review and optimization

#### 7.2 Troubleshooting Guides
- [ ] **Common Issues**: Known issues and quick fixes
- [ ] **Performance Issues**: Diagnosing and resolving slow processing
- [ ] **Integration Issues**: Service connectivity troubleshooting
- [ ] **Authentication Issues**: Managed identity and RBAC troubleshooting ✅
- [ ] **Data Issues**: Dataverse query and result troubleshooting

#### 7.3 On-Call Procedures
- [ ] Define escalation matrix and contact information
- [ ] Set up on-call rotation schedule
- [ ] Configure on-call notification channels
- [ ] Document after-hours access procedures
- [ ] Create on-call handoff template
- [ ] Schedule on-call training sessions

#### 7.4 Knowledge Transfer
- [ ] Conduct architecture walkthrough for operations team
- [ ] Provide hands-on training for common scenarios
- [ ] Document tribal knowledge and gotchas
- [ ] Create FAQ based on common questions
- [ ] Record video walkthroughs for complex procedures

### 8. Compliance & Governance (Ongoing)

#### 8.1 Compliance Validation
- [ ] Complete compliance checklist for target frameworks
- [ ] Document data handling and privacy controls
- [ ] Validate encryption at rest and in transit ✅
- [ ] Review audit logging completeness ✅
- [ ] Complete security questionnaire
- [ ] Obtain necessary compliance certifications

#### 8.2 Governance Policies
- [ ] Configure Azure Policy for compliance enforcement
- [ ] Set up resource tagging requirements
- [ ] Document data retention and deletion policies
- [ ] Configure access review procedures
- [ ] Set up regular compliance scanning
- [ ] Document change management procedures

## 🚀 Quick Start: Deploy to Staging

```powershell
# Navigate to infrastructure directory
cd c:\dev\ai-agent-evaluation\backend\infrastructure

# Validate Bicep template
.\validate.ps1 -EnvironmentName "staging"

# Deploy to staging
.\deploy.ps1 -EnvironmentName "staging" -ResourceGroupName "rg-agent-eval-staging"

# Validate deployment
.\validate-deployment.ps1 -ResourceGroupName "rg-agent-eval-staging"

# Run integration tests
cd ..\..\backend\tests\AiAgentEvaluation.IntegrationTests
dotnet test
```

## 🚀 Quick Start: Deploy to Production

```powershell
# Ensure all staging tests passed
dotnet test --configuration Release

# Navigate to infrastructure directory
cd c:\dev\ai-agent-evaluation\backend\infrastructure

# Production deployment
.\deploy.ps1 -EnvironmentName "prod" -ResourceGroupName "rg-agent-eval-prod"

# Validate and smoke test
.\validate-deployment.ps1 -ResourceGroupName "rg-agent-eval-prod"
```

## 📊 Success Criteria

### Production Readiness Gates

Before marking Phase 6 as complete, the following criteria must be met:

#### Functionality (100%)
- [ ] All 125 tests passing in production environment
- [ ] All three functions operational (Orchestrator, Worker, Monitor)
- [ ] All external integrations working (Dataverse, Copilot Studio, Azure OpenAI, Service Bus, Blob Storage)
- [ ] End-to-end evaluation run completes successfully

#### Performance (100%)
- [ ] 50,000 test case evaluation completes within SLA
- [ ] Orchestrator responds within 30 seconds
- [ ] Individual test case processes within 5 minutes
- [ ] Throughput exceeds 1,000 test cases per minute at peak
- [ ] Auto-scaling functions correctly up to 1,000 instances

#### Security (100%)
- [ ] All managed identity authentication working ✅
- [ ] Zero secrets in application configuration ✅
- [ ] All RBAC permissions properly assigned ✅
- [ ] Audit logging capturing all operations ✅
- [ ] Network security controls in place
- [ ] Compliance requirements validated

#### Observability (100%)
- [ ] Application Insights telemetry flowing
- [ ] All critical alerts configured and tested
- [ ] Dashboards created and accessible
- [ ] Log Analytics queries documented
- [ ] Health check endpoint responding

#### Operations (100%)
- [ ] All runbooks created and reviewed
- [ ] On-call team trained and ready
- [ ] Incident response procedures documented
- [ ] Rollback plan tested
- [ ] Knowledge transfer completed

## 🆘 Rollback Procedures

### Immediate Rollback (Critical Issues)

If critical issues are detected in production:

```powershell
# Rollback to previous Function App version
az functionapp deployment slot swap --resource-group rg-agent-eval-prod --name <function-app-name> --slot staging --target-slot production --action swap

# Or disable new evaluations at orchestrator
# Update Function App setting to disable new submissions
az functionapp config appsettings set --name <function-app-name> --resource-group rg-agent-eval-prod --settings "AcceptNewEvaluations=false"
```

### Rollback Decision Tree
- **All evaluation runs failing (>95% error rate)**: IMMEDIATE ROLLBACK
- **Security vulnerability detected**: IMMEDIATE ROLLBACK
- **Data corruption detected**: IMMEDIATE ROLLBACK
- **High error rate (10-95%)**: Investigate for 15 minutes, then rollback if not resolved
- **Performance degradation (<50% of expected)**: Monitor for 30 minutes, rollback if persistent
- **Intermittent issues (<10% error rate)**: Continue monitoring, prepare rollback plan

## 📞 Support & Escalation

### Contact Information
- **Primary On-Call**: [Name] - [Contact]
- **Secondary On-Call**: [Name] - [Contact]
- **Engineering Manager**: [Name] - [Contact]
- **Azure Support**: [Support Plan Details]

### Escalation Matrix
1. **Level 1** (On-Call Engineer): Initial response within 15 minutes
2. **Level 2** (Engineering Lead): Escalate after 1 hour if unresolved
3. **Level 3** (Engineering Manager): Escalate after 2 hours if unresolved
4. **Level 4** (Azure Support): Engage for platform-level issues

## 📚 Reference Documentation

### Internal Documentation
- [PRD: Product Requirements](../PRD-Agent-Evaluation-Function.md)
- [Implementation Status](../implementation.md)
- [Test Summary](../tests/00-test-summary.md)
- [User Stories](../stories/)
- [Managed Identity Guide](../../backend/MANAGED_IDENTITY_SUMMARY.md)
- [Deployment Checklist](../../backend/DEPLOYMENT_CHECKLIST.md)

### External Documentation
- [Azure Functions Best Practices](https://learn.microsoft.com/azure/azure-functions/functions-best-practices)
- [Azure Service Bus Performance](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-performance-improvements)
- [Application Insights Monitoring](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [Azure Security Baseline](https://learn.microsoft.com/security/benchmark/azure/)

## 🎯 Phase 6 Milestones

| Milestone | Target Date | Status |
|-----------|-------------|--------|
| Phase 6 Planning Complete | Nov 8, 2025 | ✅ COMPLETE |
| CI/CD Pipeline Configured | TBD | 🚧 IN PROGRESS |
| Staging Environment Deployed | TBD | ⏳ PENDING |
| Security Hardening Complete | TBD | ⏳ PENDING |
| Production Deployment | TBD | ⏳ PENDING |
| Performance Validation | TBD | ⏳ PENDING |
| Phase 6 Complete | TBD | ⏳ PENDING |

---

**Last Updated**: November 8, 2025  
**Next Review**: After staging deployment  
**Owner**: Development Team
