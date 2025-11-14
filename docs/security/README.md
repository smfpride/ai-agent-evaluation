# Security Documentation

This folder contains security implementation guides, managed identity setup, and compliance checklists for the AI Agent Evaluation System.

## Files

### Managed Identity Implementation
- **[ManagedIdentityImplementation.md](ManagedIdentityImplementation.md)** - Complete guide for implementing Azure Managed Identity across services
- **[MANAGED_IDENTITY_SUMMARY.md](MANAGED_IDENTITY_SUMMARY.md)** - Architecture overview of Managed Identity usage
- **[MANAGED_IDENTITY_CHANGE_SUMMARY.md](MANAGED_IDENTITY_CHANGE_SUMMARY.md)** - Summary of changes during Managed Identity migration

### Compliance & Hardening
- **[security-hardening-checklist.md](security-hardening-checklist.md)** - Comprehensive security checklist (SOC 2, ISO 27001 aligned)

## Security Architecture

The system uses **Managed Identity** for authentication to Azure services:
- ✅ Azure OpenAI (Cognitive Services OpenAI User role)
- ✅ Service Bus (Azure Service Bus Data Owner role)
- ✅ Key Vault (Key Vault Secrets User role)
- ✅ Storage (Blob Data Contributor + File Data Privileged Contributor)
- ⚠️ Dataverse (OAuth with ClientId/ClientSecret - SDK limitation)

See [ManagedIdentityImplementation.md](ManagedIdentityImplementation.md) for complete implementation details.

## Security Workflow

### Initial Setup
1. Review [ManagedIdentityImplementation.md](ManagedIdentityImplementation.md) for RBAC assignments
2. Configure Key Vault access for secrets
3. Complete [security-hardening-checklist.md](security-hardening-checklist.md)

### Production Hardening
1. Verify all items in [security-hardening-checklist.md](security-hardening-checklist.md)
2. Document exceptions with business justification
3. Schedule quarterly security reviews

### Compliance Audits
1. Use [security-hardening-checklist.md](security-hardening-checklist.md) as audit framework
2. Provide evidence for each control
3. Maintain audit trail in version control

## Related Documentation

- [Architecture](../ARCHITECTURE.md) - Authentication patterns and credential flow
- [Deployment Checklist](../deployment/DEPLOYMENT_CHECKLIST.md) - Pre-deployment security verification
- [Production Runbook](../operations/production-runbook.md) - Security incident response

---

**Last Updated**: November 14, 2025
