# AI Agent Evaluation - Documentation

This directory contains all project documentation organized into logical categories for easy navigation.

## 📁 Core Documentation

### 📘 Essential Reading (Root Level)

Three critical documents live at the root for quick access:

- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Complete system architecture, data flows, and component interactions
  - Start here to understand the system design
  - Required reading for all developers
  
- **[implementation.md](implementation.md)** - Master implementation status tracking document
  - Comprehensive project status across all phases
  - Implementation notes and decisions
  - Current and historical development progress
  
- **[durable-functions-guide.md](durable-functions-guide.md)** - Durable Functions implementation guide
  - Technical guide for multi-turn conversation orchestrations
  - Architecture, patterns, and best practices
  - Required for understanding multi-turn execution

### 📋 Requirements & Design
- **[prd/](prd/)** - Product Requirements Documents
- **[stories/](stories/)** - User stories and feature specifications
- **[tests/](tests/)** - Testing documentation and test plans

## 📂 Organized Documentation

### 🚀 [deployment/](deployment/)
Deployment guides, checklists, and validation procedures.
- Pre-deployment verification checklist
- Production deployment guide with rollback procedures
- Multi-turn staging validation and test data setup

[View Deployment README →](deployment/README.md)

### 📝 [implementation/](implementation/)
Implementation tracking, gap analyses, and development summaries.
- Implementation status across all stories
- Gap analysis between requirements and actual code
- Feature-specific implementation guides

[View Implementation README →](implementation/README.md)

### 🔌 [integrations/](integrations/)
Integration guides for external services and components.
- Copilot Studio Direct Line API integration
- Dataverse OAuth configuration
- Azure Service Bus setup
- Monitor function implementation

[View Integrations README →](integrations/README.md)

### ⚙️ [operations/](operations/)
Operational procedures, monitoring, and performance optimization.
- Production operations runbook with troubleshooting
- Application Insights monitoring configuration
- Performance tuning strategies

[View Operations README →](operations/README.md)

### 📊 [phase-reports/](phase-reports/)
Completion reports and summaries for each development phase.
- Phase 4: LLM Judge Implementation
- Phase 5: Testing & Quality Assurance
- Historical phase completion details

[View Phase Reports README →](phase-reports/README.md)

### 🔐 [security/](security/)
Security implementation, managed identity, and compliance.
- Managed Identity implementation guide
- Security hardening checklist (SOC 2, ISO 27001)
- RBAC configuration and authentication patterns

[View Security README →](security/README.md)

### 🗄️ [archive/](archive/) & [archive-fixes/](archive-fixes/)
Historical documentation and one-time reports kept for reference.
- Documentation update records
- Historical hotfixes and bug fix records
- Superseded documents

[View Archive README →](archive/README.md) | [View Fixes Archive →](archive-fixes/README.md)

## 📖 Quick Reference

### For Deployment
1. Review [ARCHITECTURE.md](ARCHITECTURE.md) to understand the system
2. Check [deployment/DEPLOYMENT_CHECKLIST.md](deployment/DEPLOYMENT_CHECKLIST.md) before deploying
3. Follow [deployment/phase6-production-deployment.md](deployment/phase6-production-deployment.md) for production
4. Verify [security/security-hardening-checklist.md](security/security-hardening-checklist.md) requirements

### For Operations
1. Use [operations/production-runbook.md](operations/production-runbook.md) for day-to-day operations
2. Configure monitoring per [operations/monitoring-configuration.md](operations/monitoring-configuration.md)
3. Optimize performance with [operations/performance-optimization-guide.md](operations/performance-optimization-guide.md)

### For Development
1. Review [prd/PRD-Agent-Evaluation-Function.md](prd/PRD-Agent-Evaluation-Function.md) for requirements
2. Check [stories/](stories/) for user stories and acceptance criteria
3. Review [implementation/IMPLEMENTATION_GAP_ANALYSIS.md](implementation/IMPLEMENTATION_GAP_ANALYSIS.md) for known gaps
4. Consult [integrations/](integrations/) for service integration guides
5. Reference [ARCHITECTURE.md](ARCHITECTURE.md) for design patterns

### For Testing
1. Review [tests/](tests/) folder for test plans
2. Check [phase-reports/PHASE5_TEST_SUMMARY.md](phase-reports/PHASE5_TEST_SUMMARY.md) for test results
3. Follow [deployment/multi-turn-test-data-setup.md](deployment/multi-turn-test-data-setup.md) for test data

### For Security & Compliance
1. Complete [security/security-hardening-checklist.md](security/security-hardening-checklist.md) before production
2. Implement [security/ManagedIdentityImplementation.md](security/ManagedIdentityImplementation.md) guidance
3. Review RBAC assignments per [ARCHITECTURE.md](ARCHITECTURE.md) authentication section

## 🔄 Document Maintenance

- **Frequency**: Review and update quarterly or after major changes
- **Owner**: Development and Operations teams
- **Feedback**: Submit improvements via pull requests
- **Archive Policy**: Move superseded documents to [archive/](archive/) with README updates

## 📐 Documentation Standards

When updating documentation:
1. Update [ARCHITECTURE.md](ARCHITECTURE.md) for design changes
2. Update [prd/PRD-Agent-Evaluation-Function.md](prd/PRD-Agent-Evaluation-Function.md) for requirement changes
3. Update relevant story in [stories/](stories/) for implementation details
4. Add README.md to new folders explaining their purpose
5. Archive superseded documents with dates and explanations

---

**Last Organized**: November 14, 2025  
**Organization Structure Version**: 2.0
