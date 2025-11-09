# Security Hardening Checklist

**Last Updated**: November 8, 2025  
**Version**: 1.0  
**Compliance Frameworks**: SOC 2, ISO 27001, Azure Security Baseline

## 📋 Overview

This document provides a comprehensive security hardening checklist for the AI Agent Evaluation Function. All items should be completed and verified before production deployment.

## ✅ Security Checklist

### 1. Identity & Access Management

#### 1.1 Managed Identity Configuration
- [x] **System-Assigned Managed Identity Enabled**
  - Function App has System-Assigned Identity enabled
  - Principal ID documented and tracked
  - **Validation**: `az functionapp identity show --name <app-name> --resource-group <rg>`
  
- [x] **RBAC Permissions - Principle of Least Privilege**
  - Storage Blob Data Contributor (read/write prompt templates only)
  - Service Bus Data Owner (send/receive messages only)
  - Cognitive Services OpenAI User (inference only, no model management)
  - Key Vault Secrets User (read secrets for fallback authentication)
  - **Validation**: `az role assignment list --assignee <principal-id>`

- [x] **No Secrets in Application Code**
  - Zero hardcoded connection strings
  - Zero hardcoded API keys
  - All sensitive values via managed identity or Key Vault
  - **Validation**: Code review + grep for connection strings

- [x] **Service Principal Cleanup**
  - Remove any development service principals
  - Audit and remove unnecessary accounts
  - **Validation**: `az ad sp list --show-mine`

#### 1.2 Authentication & Authorization
- [x] **Function Authentication Level**
  - Orchestrator function: `Function` level (requires function key)
  - Worker function: Service Bus trigger (no HTTP access)
  - Monitor function: Timer trigger (no HTTP access)
  - Health checks: `Anonymous` (required for monitoring)
  - **Validation**: Review function decorators in code

- [ ] **API Management Integration** (Optional, recommended for production)
  - Configure Azure API Management in front of HTTP functions
  - Implement rate limiting and throttling
  - Enable OAuth 2.0 authentication
  - Configure IP filtering
  - **Implementation**: See API Management configuration guide

- [ ] **Conditional Access Policies**
  - Require MFA for administrative access
  - Restrict access from known IP ranges
  - Block legacy authentication protocols
  - **Configuration**: Azure AD Conditional Access

### 2. Network Security

#### 2.1 Virtual Network Integration
- [ ] **VNet Integration for Function App**
  - Deploy Function App into dedicated subnet
  - Configure route tables for outbound traffic
  - Enable Regional VNet Integration
  - **Configuration**: 
    ```powershell
    az functionapp vnet-integration add \
      --name <app-name> \
      --resource-group <rg> \
      --vnet <vnet-name> \
      --subnet <subnet-name>
    ```

- [ ] **Private Endpoints for Azure Services**
  - Storage Account: Private endpoint for blob storage
  - Service Bus: Private endpoint for namespace
  - Key Vault: Private endpoint for vault access
  - Azure OpenAI: Private endpoint if available
  - **Configuration**: Create private endpoint for each service

- [ ] **Network Security Groups (NSGs)**
  - Restrict inbound traffic to necessary ports only
  - Allow outbound to Azure services via Service Tags
  - Deny all other outbound traffic
  - Log all denied traffic
  - **Configuration**: Create NSG and associate with subnet

- [ ] **Azure Firewall**
  - Route all outbound traffic through Azure Firewall
  - Configure application rules for HTTPS to Azure services
  - Enable threat intelligence filtering
  - **Configuration**: Deploy Azure Firewall and configure UDRs

#### 2.2 Transport Security
- [x] **HTTPS Only**
  - Function App configured for HTTPS only
  - HTTP requests automatically redirected
  - **Validation**: `az functionapp show --name <app-name> --query httpsOnly`

- [x] **TLS Version**
  - Minimum TLS 1.2 enforced
  - TLS 1.3 enabled where supported
  - **Validation**: `az functionapp config show --name <app-name> --query minTlsVersion`

- [ ] **Custom Domain with TLS Certificate**
  - Configure custom domain if required
  - Use Azure-managed certificate or custom certificate
  - Enable automatic certificate renewal
  - **Configuration**: Azure Portal > Function App > Custom domains

### 3. Data Protection

#### 3.1 Encryption at Rest
- [x] **Storage Account Encryption**
  - Microsoft-managed keys by default
  - Option: Customer-managed keys via Key Vault
  - **Validation**: `az storage account show --name <account> --query encryption`

- [x] **Azure OpenAI Data Encryption**
  - Data encrypted at rest by default
  - No customer data retained after processing
  - **Validation**: Review Azure OpenAI documentation

- [x] **Service Bus Encryption**
  - Messages encrypted at rest
  - Premium tier supports customer-managed keys
  - **Validation**: Service Bus encryption is automatic

- [ ] **Customer-Managed Keys (CMK)** (Optional)
  - Configure Key Vault for CMK
  - Enable CMK for Storage Account
  - Enable CMK for Service Bus (Premium tier required)
  - Implement key rotation policy
  - **Configuration**: Azure Portal > Storage Account > Encryption

#### 3.2 Encryption in Transit
- [x] **All Communications Over TLS**
  - Function App: HTTPS only
  - Storage: HTTPS required
  - Service Bus: TLS enforced
  - Dataverse: HTTPS only
  - Azure OpenAI: HTTPS only
  - **Validation**: Review service configurations

- [x] **Certificate Validation**
  - Enable certificate validation in HttpClient
  - No certificate validation bypass in code
  - **Validation**: Code review for `ServerCertificateValidationCallback`

#### 3.3 Data Handling
- [ ] **Sensitive Data Identification**
  - Document what constitutes sensitive data
  - Classify data by sensitivity level
  - Map data flow through the system
  - **Deliverable**: Data flow diagram with classifications

- [ ] **Data Retention Policies**
  - Define retention period for evaluation results
  - Configure automatic deletion of old data
  - Document data lifecycle
  - **Configuration**: Dataverse retention policies

- [ ] **Data Sanitization**
  - Remove PII from logs and telemetry
  - Implement log scrubbing for sensitive data
  - Configure Application Insights data sampling
  - **Implementation**: Update logging middleware

- [x] **No Secrets in Logs**
  - Structured logging without sensitive values
  - Sanitize exception messages
  - **Validation**: Review logs for exposed secrets

### 4. Compliance & Auditing

#### 4.1 Audit Logging
- [x] **Application Insights Integration**
  - All operations logged with correlation IDs
  - Exceptions and errors captured
  - Performance metrics tracked
  - **Validation**: Review Application Insights data

- [x] **Azure Activity Log**
  - All administrative actions logged
  - Logs sent to Log Analytics workspace
  - **Validation**: Azure Portal > Activity Log

- [ ] **Diagnostic Settings for All Resources**
  - Function App: Send logs to Log Analytics
  - Storage Account: Diagnostic logs enabled
  - Service Bus: Diagnostic logs enabled
  - Key Vault: Audit logging enabled
  - **Configuration**: Enable diagnostic settings per resource

- [ ] **Log Retention Configuration**
  - Activity logs: 90 days minimum
  - Application logs: 30 days minimum
  - Audit logs: 1 year minimum (compliance requirement)
  - **Configuration**: Log Analytics workspace retention

#### 4.2 Security Monitoring
- [ ] **Microsoft Defender for Cloud**
  - Enable Defender for App Service
  - Enable Defender for Storage
  - Enable Defender for Key Vault
  - Review and remediate recommendations
  - **Configuration**: Azure Portal > Defender for Cloud

- [ ] **Security Alerts Configuration**
  - Alert on unauthorized access attempts
  - Alert on RBAC permission changes
  - Alert on Key Vault access anomalies
  - Alert on suspicious network activity
  - **Configuration**: Azure Monitor alert rules

- [ ] **Vulnerability Scanning**
  - Enable container scanning if using containers
  - Schedule regular dependency scans
  - Configure automated security updates
  - **Tool**: GitHub Dependabot or Azure DevOps

#### 4.3 Compliance Validation
- [ ] **SOC 2 Type II Requirements**
  - Access control policies documented
  - Audit logging configured and retained
  - Encryption at rest and in transit enabled
  - Incident response procedures documented
  - **Deliverable**: SOC 2 compliance matrix

- [ ] **ISO 27001 Requirements**
  - Information security policy documented
  - Risk assessment completed
  - Security controls implemented and tested
  - Management review procedures established
  - **Deliverable**: ISO 27001 compliance matrix

- [ ] **Azure Security Baseline**
  - Run Azure Security Benchmark assessment
  - Remediate high and critical findings
  - Document accepted risks with justification
  - **Tool**: Azure Policy compliance dashboard

- [ ] **GDPR Compliance** (if applicable)
  - Data processing agreement in place
  - User consent mechanisms implemented
  - Right to deletion capabilities
  - Data breach notification procedures
  - **Deliverable**: GDPR compliance documentation

### 5. Application Security

#### 5.1 Code Security
- [x] **Input Validation**
  - All API inputs validated
  - Request size limits enforced
  - Type checking implemented
  - **Validation**: Code review of API endpoints

- [x] **Error Handling**
  - No sensitive information in error messages
  - Generic errors returned to clients
  - Detailed errors logged securely
  - **Validation**: Review exception handling

- [x] **Dependency Management**
  - All NuGet packages from trusted sources
  - Dependencies regularly updated
  - Vulnerability scanning enabled
  - **Tool**: `dotnet list package --vulnerable`

- [ ] **Static Code Analysis**
  - Configure SonarQube or equivalent
  - Address high and critical findings
  - Set quality gates in CI/CD pipeline
  - **Configuration**: Add to GitHub Actions workflow

- [ ] **Dynamic Application Security Testing (DAST)**
  - Schedule penetration testing
  - Run OWASP ZAP or similar tool
  - Remediate identified vulnerabilities
  - **Tool**: OWASP ZAP, Burp Suite

#### 5.2 Secrets Management
- [x] **Key Vault for Secrets**
  - All secrets stored in Key Vault
  - RBAC-based access to secrets
  - Managed identity for secret retrieval
  - **Validation**: No secrets in code or config

- [x] **Secret Rotation**
  - Key Vault supports automatic rotation
  - Document secret rotation procedures
  - Test secret rotation in non-production
  - **Procedure**: Documented in production runbook

- [ ] **Secret Scanning in Git**
  - Enable GitHub secret scanning
  - Configure pre-commit hooks
  - Immediate revocation of exposed secrets
  - **Tool**: git-secrets, GitHub Advanced Security

### 6. Operational Security

#### 6.1 Backup & Recovery
- [ ] **Configuration Backup**
  - Regular backup of Function App settings
  - Bicep templates in source control
  - Key Vault secrets documented (not backed up)
  - **Automation**: Backup script in runbook

- [ ] **Disaster Recovery Plan**
  - RTO/RPO targets defined (4 hours / 1 hour)
  - Secondary region deployment ready
  - DR testing schedule (quarterly)
  - **Document**: Disaster recovery procedures

- [ ] **Data Backup**
  - Dataverse backup configured
  - Point-in-time recovery enabled
  - Backup retention policy defined
  - **Configuration**: Dataverse backup settings

#### 6.2 Incident Response
- [ ] **Security Incident Response Plan**
  - Incident classification criteria
  - Response team contacts and roles
  - Communication procedures
  - Post-incident review process
  - **Document**: IR-PLAN-001

- [ ] **Playbooks for Common Incidents**
  - Unauthorized access attempt
  - Data breach or exposure
  - Service denial or degradation
  - Malware or ransomware
  - **Documents**: Incident response playbooks

- [ ] **Incident Simulation Exercises**
  - Schedule tabletop exercises (semi-annually)
  - Document lessons learned
  - Update procedures based on findings
  - **Schedule**: Next exercise date

#### 6.3 Patch Management
- [ ] **Azure Platform Updates**
  - Enable automatic OS patching for Function App
  - Monitor Azure service health dashboard
  - Subscribe to security advisories
  - **Configuration**: Function App > Configuration > OS patching

- [ ] **Application Dependency Updates**
  - Monthly review of dependency updates
  - Security patches applied within 30 days
  - Non-security updates quarterly
  - **Process**: Documented in runbook

- [ ] **Function Runtime Updates**
  - Test .NET updates in non-production first
  - Schedule maintenance windows for updates
  - Rollback plan documented
  - **Process**: Update and test procedure

### 7. Access Control

#### 7.1 Administrative Access
- [ ] **Privileged Identity Management (PIM)**
  - Just-in-time access for admin roles
  - Time-limited access approvals
  - Access reviews quarterly
  - **Configuration**: Azure AD PIM

- [ ] **Break-glass Account**
  - Emergency access account created
  - Credentials stored securely offline
  - Account monitored for use
  - Regular testing of account access
  - **Documentation**: Break-glass procedures

- [ ] **Access Reviews**
  - Quarterly review of RBAC assignments
  - Remove unused accounts and permissions
  - Document review findings
  - **Schedule**: Next review date

#### 7.2 Service Accounts
- [x] **Managed Identity Only**
  - No service principal credentials
  - All automation uses managed identity
  - **Validation**: No service principals in use

- [ ] **Service Account Inventory**
  - Document all service accounts (if any)
  - Justification for each account
  - Review and cleanup schedule
  - **Deliverable**: Service account register

### 8. Third-Party Integration Security

#### 8.1 Dataverse Security
- [ ] **Dataverse Security Roles**
  - Minimal permissions for function app identity
  - Read/write access to specific tables only
  - No administrative permissions
  - **Configuration**: Dataverse security roles

- [ ] **Dataverse Network Security**
  - IP restrictions configured (if available)
  - Audit logging enabled
  - Connection monitoring
  - **Configuration**: Dataverse admin center

#### 8.2 Azure OpenAI Security
- [x] **Managed Identity Authentication**
  - No API keys in use
  - Cognitive Services OpenAI User role only
  - **Validation**: Check authentication method

- [ ] **Content Filtering**
  - Enable content safety filters
  - Monitor for policy violations
  - Configure appropriate filter levels
  - **Configuration**: Azure OpenAI Studio

- [ ] **Rate Limiting**
  - Configure rate limits appropriate for workload
  - Implement backoff and retry logic
  - Monitor quota usage
  - **Implementation**: Implemented in code

#### 8.3 Copilot Studio Security
- [ ] **Direct Line Secret Rotation**
  - Regular rotation schedule (90 days)
  - Stored in Key Vault only
  - Automation for rotation
  - **Process**: Documented rotation procedure

- [ ] **Bot Authentication**
  - Enable enhanced authentication if available
  - Monitor for unauthorized access
  - Log all bot interactions
  - **Configuration**: Copilot Studio settings

## 🔒 Security Testing

### Pre-Production Security Tests
- [ ] **Penetration Testing**
  - External penetration test conducted
  - Report reviewed and findings remediated
  - Retest after remediation
  - **Vendor**: [Security testing vendor]

- [ ] **Vulnerability Assessment**
  - Automated vulnerability scan completed
  - No high or critical vulnerabilities
  - Medium vulnerabilities accepted or remediated
  - **Tool**: Qualys, Nessus, or Azure Defender

- [ ] **Security Code Review**
  - Manual security code review completed
  - Automated SAST findings reviewed
  - Security-focused test cases passed
  - **Reviewer**: Security team sign-off

### Post-Production Security Monitoring
- [ ] **Continuous Vulnerability Scanning**
  - Weekly automated scans scheduled
  - Alert on new vulnerabilities
  - Track remediation SLAs
  - **Tool**: Azure Defender continuous assessment

- [ ] **Security Metrics Dashboard**
  - Failed authentication attempts
  - RBAC permission changes
  - Unusual access patterns
  - Certificate expiration tracking
  - **Tool**: Azure Monitor workbook

## 📝 Compliance Documentation

### Required Documents
- [ ] **Security Assessment Report**
  - All checklist items status
  - Risk assessment and mitigation
  - Approval signatures
  - **Template**: SEC-ASSESS-001

- [ ] **Data Flow Diagram**
  - Complete system architecture
  - Data classification markings
  - Trust boundaries identified
  - **Tool**: Draw.io, Visio

- [ ] **Threat Model**
  - STRIDE analysis completed
  - Threats identified and rated
  - Mitigations documented
  - **Tool**: Microsoft Threat Modeling Tool

- [ ] **Privacy Impact Assessment**
  - PII data inventory
  - Processing lawfulness basis
  - Data subject rights procedures
  - **Template**: PIA-001

## ✅ Sign-Off

### Pre-Production Approval
- [ ] **Security Team Approval**
  - Name: _________________
  - Date: _________________
  - Signature: _________________

- [ ] **Compliance Team Approval**
  - Name: _________________
  - Date: _________________
  - Signature: _________________

- [ ] **Engineering Manager Approval**
  - Name: _________________
  - Date: _________________
  - Signature: _________________

### Post-Production Verification
- [ ] **30-Day Security Review**
  - Date: _________________
  - Findings: _________________
  - Action items: _________________

- [ ] **90-Day Compliance Audit**
  - Date: _________________
  - Findings: _________________
  - Action items: _________________

---

**Document Owner**: Security Team  
**Review Frequency**: Quarterly or after significant changes  
**Last Security Review**: November 8, 2025  
**Next Security Review**: February 8, 2026
