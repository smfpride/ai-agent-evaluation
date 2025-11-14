# Deployment Documentation

This folder contains deployment guides, checklists, and validation procedures for the AI Agent Evaluation System.

## Files

### Core Deployment
- **[DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md)** - Pre-deployment verification checklist with step-by-step validation
- **[phase6-production-deployment.md](phase6-production-deployment.md)** - Production deployment guide with rollback procedures

### Multi-Turn Testing
- **[multi-turn-staging-validation.md](multi-turn-staging-validation.md)** - Staging environment validation for multi-turn conversation flows
- **[multi-turn-test-data-setup.md](multi-turn-test-data-setup.md)** - Test data setup guide for multi-turn scenarios

## Deployment Workflow

1. **Pre-Deployment**: Review [DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md)
2. **Staging Validation**: Follow [multi-turn-staging-validation.md](multi-turn-staging-validation.md)
3. **Production Deploy**: Execute [phase6-production-deployment.md](phase6-production-deployment.md)
4. **Post-Deployment**: Verify using checklist and monitoring

## Related Documentation

- [Security Hardening](../security/security-hardening-checklist.md) - Security requirements before production
- [Operations Runbook](../operations/production-runbook.md) - Day-to-day operations after deployment
- [Infrastructure README](../../backend/infrastructure/README.md) - Bicep templates and IaC

---

**Last Updated**: November 14, 2025
