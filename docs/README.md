# AI Agent Evaluation - Documentation

This directory contains all project documentation organized into logical categories for easy navigation.

## 📁 Top-Level Documentation

- **[implementation.md](implementation.md)** - Complete implementation status and technical overview
- **[prd/](prd/)** - Product Requirements Documents
- **[stories/](stories/)** - User stories and feature specifications
- **[tests/](tests/)** - Testing documentation and test plans

## 📂 misc/ - Organized Documentation

The `misc/` folder contains supporting documentation organized into the following categories:

### 🚀 [misc/deployment/](misc/deployment/)
Deployment guides, checklists, and procedures for deploying the application to various environments.

**Files:**
- `DEPLOYMENT_CHECKLIST.md` - Pre-deployment verification checklist
- `phase6-production-deployment.md` - Phase 6 production deployment guide

### 📝 [misc/implementation/](misc/implementation/)
Implementation tracking documents, gap analyses, and development summaries.

**Files:**
- `IMPLEMENTATION_GAP_ANALYSIS.md` - Analysis of gaps between user stories and implementation
- `IMPLEMENTATION_SUMMARY.md` - Summary of implementation status
- `STORY_IMPLEMENTATION_REVIEW.md` - Review of story implementation against requirements

### 🔌 [misc/integrations/](misc/integrations/)
Integration guides for external services and components.

**Files:**
- `copilot-studio-token-endpoint-integration.md` - Copilot Studio token endpoint authentication guide
- `HANDOFF_SUMMARY.md` - Project handoff and integration summary (includes Dataverse OAuth)
- `MONITOR_FUNCTION_IMPLEMENTATION.md` - Monitor function implementation details
- `SERVICE_BUS_SETUP.md` - Azure Service Bus setup and configuration

### ⚙️ [misc/operations/](misc/operations/)
Operational procedures, monitoring configurations, and performance optimization guides.

**Files:**
- `monitoring-configuration.md` - Monitoring and alerting configuration
- `performance-optimization-guide.md` - Performance tuning and optimization strategies
- `production-runbook.md` - Production operations runbook with troubleshooting procedures

### 📊 [misc/phase-reports/](misc/phase-reports/)
Completion reports and summaries for each development phase.

**Files:**
- `PHASE4_COMPLETION_SUMMARY.md` - LLM Judge implementation completion (Phase 4)
- `PHASE5_COMPLETION_REPORT.md` - Testing & quality assurance completion (Phase 5)
- `PHASE5_TEST_SUMMARY.md` - Phase 5 test execution summary

### 🔐 [misc/security/](misc/security/)
Security hardening, managed identity implementation, and compliance documentation.

**Files:**
- `ManagedIdentityImplementation.md` - Managed identity implementation guide
- `MANAGED_IDENTITY_CHANGE_SUMMARY.md` - Summary of managed identity changes
- `MANAGED_IDENTITY_SUMMARY.md` - Managed identity architecture overview
- `security-hardening-checklist.md` - Comprehensive security hardening checklist (SOC 2, ISO 27001)

## 📖 Quick Reference

### For Deployment
1. Start with [implementation.md](implementation.md) for current status
2. Review [misc/deployment/DEPLOYMENT_CHECKLIST.md](misc/deployment/DEPLOYMENT_CHECKLIST.md) before deploying
3. Follow [misc/deployment/phase6-production-deployment.md](misc/deployment/phase6-production-deployment.md) for production deployment
4. Check [misc/security/security-hardening-checklist.md](misc/security/security-hardening-checklist.md) for security requirements

### For Operations
1. Use [misc/operations/production-runbook.md](misc/operations/production-runbook.md) for day-to-day operations
2. Refer to [misc/operations/monitoring-configuration.md](misc/operations/monitoring-configuration.md) for monitoring setup
3. Consult [misc/operations/performance-optimization-guide.md](misc/operations/performance-optimization-guide.md) for performance issues

### For Development
1. Review [prd/PRD-Agent-Evaluation-Function.md](prd/PRD-Agent-Evaluation-Function.md) for requirements
2. Check [stories/](stories/) for user stories and feature specifications
3. Review [misc/implementation/IMPLEMENTATION_GAP_ANALYSIS.md](misc/implementation/IMPLEMENTATION_GAP_ANALYSIS.md) for known gaps
4. Check [misc/integrations/](misc/integrations/) folder for service integration guides
5. Refer to [misc/phase-reports/](misc/phase-reports/) for historical phase completion details

### For Testing
1. Review [tests/](tests/) folder for test plans and summaries
2. Check [misc/phase-reports/PHASE5_TEST_SUMMARY.md](misc/phase-reports/PHASE5_TEST_SUMMARY.md) for latest test results

### For Security & Compliance
1. Complete [misc/security/security-hardening-checklist.md](misc/security/security-hardening-checklist.md) before production
2. Review managed identity documents in [misc/security/](misc/security/) folder
3. Ensure all security controls are implemented and documented

## 🔄 Document Maintenance

- **Frequency**: Review and update documents quarterly or after major changes
- **Owner**: Development and Operations teams
- **Feedback**: Submit documentation improvements via pull requests

---

**Last Organized**: November 9, 2025  
**Organization Structure Version**: 1.0
