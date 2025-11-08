# Production Operations Runbook

**Last Updated**: November 8, 2025  
**Version**: 1.0  
**Owner**: Operations Team

## 📋 Overview

This runbook provides step-by-step procedures for common operational tasks for the AI Agent Evaluation Function. It covers deployments, scaling, troubleshooting, incident response, and disaster recovery.

## 🚀 Deployment Procedures

### Standard Deployment to Production

**Pre-Deployment Checklist:**
- [ ] All tests passing in staging (125 tests at 100%)
- [ ] Change request approved
- [ ] Deployment window scheduled
- [ ] Team notified
- [ ] Rollback plan ready

**Deployment Steps:**

```powershell
# 1. Navigate to infrastructure directory
cd c:\dev\ai-agent-evaluation\backend\infrastructure

# 2. Validate Bicep template
.\validate.ps1 -EnvironmentName "prod"

# 3. Create backup of current configuration
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
az functionapp config appsettings list --name <function-app-name> --resource-group rg-agent-eval-prod > "backup-appsettings-$timestamp.json"

# 4. Deploy infrastructure updates
.\deploy.ps1 -EnvironmentName "prod" -ResourceGroupName "rg-agent-eval-prod"

# 5. Validate deployment
.\validate-deployment.ps1 -ResourceGroupName "rg-agent-eval-prod"

# 6. Run smoke tests
Invoke-RestMethod -Uri "https://<function-app-name>.azurewebsites.net/api/health/ready" -Method Get

# 7. Monitor for 15 minutes
# Check Application Insights for errors
# Monitor alert channels
```

**Post-Deployment:**
- [ ] Verify health check endpoint returns 200
- [ ] Run small test evaluation (10 test cases)
- [ ] Monitor Application Insights for 1 hour
- [ ] Update deployment log
- [ ] Notify stakeholders of completion

### Emergency Hotfix Deployment

For critical production issues requiring immediate fix:

```powershell
# 1. Create hotfix branch
git checkout -b hotfix/<issue-number>

# 2. Make minimal fix
# (Edit necessary files)

# 3. Run quick validation
dotnet test --filter "Critical"

# 4. Deploy directly to production slot staging
az functionapp deployment source config-zip `
    --resource-group rg-agent-eval-prod `
    --name <function-app-name> `
    --src ./hotfix.zip `
    --slot staging

# 5. Validate on staging slot
# Test the specific fix

# 6. Swap to production
az functionapp deployment slot swap `
    --resource-group rg-agent-eval-prod `
    --name <function-app-name> `
    --slot staging `
    --target-slot production

# 7. Monitor closely for 30 minutes
```

### Rollback Procedure

**When to Rollback:**
- Critical functionality broken (>95% failure rate)
- Security vulnerability detected
- Data corruption occurring
- Performance degradation >50%

**Rollback Steps:**

```powershell
# Option 1: Swap slots back (if deployed via slots)
az functionapp deployment slot swap `
    --resource-group rg-agent-eval-prod `
    --name <function-app-name> `
    --slot staging `
    --target-slot production `
    --action swap

# Option 2: Redeploy previous version
az functionapp deployment source config-zip `
    --resource-group rg-agent-eval-prod `
    --name <function-app-name> `
    --src ./previous-version.zip

# Option 3: Disable new evaluations (emergency stop)
az functionapp config appsettings set `
    --name <function-app-name> `
    --resource-group rg-agent-eval-prod `
    --settings "AcceptNewEvaluations=false"
```

## 📊 Monitoring & Alerting

### Access Dashboards

```powershell
# Open Azure Portal to monitoring dashboards
az portal open --url "https://portal.azure.com/#@<tenant-id>/resource/subscriptions/<sub-id>/resourceGroups/rg-agent-eval-prod/providers/Microsoft.Insights/components/<app-insights-name>/overview"
```

**Key Dashboards:**
1. **Operations Dashboard**: Real-time metrics and error rates
2. **Performance Dashboard**: Latency, throughput, scaling
3. **Cost Dashboard**: Usage and cost tracking

### Respond to Critical Alerts

#### Alert: Function App Down

**Symptoms:**
- HTTP 5xx errors spiking
- Health check failing
- No requests being processed

**Investigation Steps:**
```powershell
# Check function app status
az functionapp show --name <function-app-name> --resource-group rg-agent-eval-prod --query state

# Check recent deployments
az functionapp deployment list --name <function-app-name> --resource-group rg-agent-eval-prod

# Check Application Insights for errors
az monitor app-insights query `
    --app <app-insights-name> `
    --analytics-query "exceptions | order by timestamp desc | take 20"
```

**Resolution:**
1. If recent deployment: Execute rollback procedure
2. If Azure platform issue: Check Azure Status page
3. If resource exhaustion: Scale up immediately
4. If other: Engage Level 2 support

#### Alert: High Failure Rate

**Symptoms:**
- >10% of evaluation runs failing
- Increasing error count in logs

**Investigation Steps:**
```kusto
// Query in Application Insights
exceptions
| where timestamp > ago(15m)
| summarize count() by type, outerMessage
| order by count_ desc
```

**Common Causes & Resolutions:**
- **Dataverse throttling**: Implement backoff, scale down processing rate
- **Azure OpenAI quota**: Request quota increase, enable retry logic
- **Service Bus issues**: Check queue health, restart workers if needed
- **Network issues**: Check NSG rules and connectivity

#### Alert: Managed Identity Authentication Failure

**Symptoms:**
- Auth errors in logs
- Services returning 401/403

**Investigation Steps:**
```powershell
# Verify managed identity is enabled
az functionapp identity show --name <function-app-name> --resource-group rg-agent-eval-prod

# Check RBAC assignments
az role assignment list --assignee <principal-id> --all

# Test specific service connectivity
Invoke-RestMethod -Uri "https://<function-app-name>.azurewebsites.net/api/health/ready" -Method Get
```

**Resolution:**
1. Verify system-assigned identity is enabled
2. Validate RBAC role assignments are present
3. Check if roles were recently removed
4. Re-assign roles if missing using infrastructure script
5. Restart function app to refresh token cache

## ⚡ Scaling Operations

### Manual Scale Up (Emergency)

**When to Scale Up:**
- High queue backlog (>5000 messages)
- Processing time exceeding SLA
- Approaching auto-scale limits

```powershell
# Scale up function app instances
az functionapp plan update `
    --name <app-service-plan-name> `
    --resource-group rg-agent-eval-prod `
    --max-elastic-worker-count 500

# Scale up Service Bus
az servicebus namespace update `
    --name <sb-namespace> `
    --resource-group rg-agent-eval-prod `
    --sku Premium `
    --capacity 4
```

### Manual Scale Down

```powershell
# Scale down after peak load
az functionapp plan update `
    --name <app-service-plan-name> `
    --resource-group rg-agent-eval-prod `
    --max-elastic-worker-count 200
```

### Monitor Auto-Scaling

```kusto
// Query current instance count
performanceCounters
| where name == "Instance Count"
| where timestamp > ago(1h)
| project timestamp, value
| render timechart
```

## 🔍 Troubleshooting Guide

### Issue: Evaluation Runs Not Starting

**Symptoms:**
- POST to orchestrator returns 202 but nothing happens
- No messages in Service Bus queue
- Status remains "Created" in Dataverse

**Investigation:**
```powershell
# Check orchestrator function logs
az functionapp log tail --name <function-app-name> --resource-group rg-agent-eval-prod

# Query Application Insights
az monitor app-insights query `
    --app <app-insights-name> `
    --analytics-query "traces | where message contains 'StartEvaluationRun' | order by timestamp desc | take 50"
```

**Common Causes:**
1. **Service Bus connection issue**: Check managed identity permissions
2. **Dataverse query timeout**: Check network connectivity
3. **Function app stopped**: Verify app is running
4. **Configuration error**: Validate app settings

**Resolution:**
- Restart function app
- Validate service connectivity via health check
- Check Application Settings for missing config

### Issue: Test Cases Processing Slowly

**Symptoms:**
- Processing time >5 minutes per test case
- Service Bus queue growing faster than draining
- P95 latency elevated

**Investigation:**
```kusto
// Check processing times
customMetrics
| where name == "TestCaseProcessingTime"
| where timestamp > ago(1h)
| summarize avg(value), percentile(value, 95) by bin(timestamp, 5m)
| render timechart
```

**Common Causes:**
1. **Azure OpenAI throttling**: Check for 429 responses
2. **Copilot Studio slow responses**: Check Direct Line API latency
3. **Dataverse throttling**: Check for 429 responses
4. **Insufficient workers**: Scale up function instances

**Resolution:**
- Enable retry logic with backoff
- Scale up function instances
- Optimize batch sizes
- Contact service support for quota increases

### Issue: Dead Letter Queue Growing

**Symptoms:**
- Messages accumulating in dead letter queue
- Failed test cases increasing
- Error rate climbing

**Investigation:**
```powershell
# Check dead letter queue
az servicebus queue show `
    --name <queue-name> `
    --namespace-name <sb-namespace> `
    --resource-group rg-agent-eval-prod `
    --query "deadLetteringOnMessageExpiration"

# Peek at dead letter messages
az servicebus queue peek `
    --name <queue-name> `
    --namespace-name <sb-namespace> `
    --resource-group rg-agent-eval-prod `
    --max-message-count 10
```

**Common Causes:**
1. **Message lock timeout**: Processing taking too long
2. **Poison messages**: Invalid message format
3. **Transient failures exhausting retries**: Service downtime
4. **Code bug**: Exception in worker function

**Resolution:**
- Analyze dead letter messages for patterns
- Fix code bug if identified
- Resubmit valid messages back to main queue
- Increase message lock timeout if needed

### Issue: Memory Pressure

**Symptoms:**
- Function instances restarting frequently
- Out of memory exceptions
- Slow performance

**Investigation:**
```kusto
// Check memory usage
performanceCounters
| where name == "Memory Working Set"
| where timestamp > ago(1h)
| project timestamp, value
| render timechart
```

**Resolution:**
- Review code for memory leaks
- Ensure proper disposal of resources
- Reduce batch sizes
- Scale up to instances with more memory
- Implement object pooling

## 🔐 Security Operations

### Rotate Secrets (If Using Key Vault Fallback)

```powershell
# Generate new secret
$newSecret = New-Guid

# Update Key Vault
az keyvault secret set `
    --vault-name <kv-name> `
    --name "DataverseConnectionString" `
    --value $newSecret

# Restart function app to pick up new secret
az functionapp restart --name <function-app-name> --resource-group rg-agent-eval-prod
```

### Review RBAC Permissions

```powershell
# List all role assignments for function app identity
$principalId = (az functionapp identity show --name <function-app-name> --resource-group rg-agent-eval-prod --query principalId -o tsv)

az role assignment list --assignee $principalId --all --output table
```

### Audit Security Events

```kusto
// Query security-related events
traces
| where message contains "Authentication" or message contains "Authorization"
| where timestamp > ago(24h)
| project timestamp, severityLevel, message
| order by timestamp desc
```

## 💾 Backup & Recovery

### Backup Configuration

```powershell
# Backup function app settings
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
az functionapp config appsettings list `
    --name <function-app-name> `
    --resource-group rg-agent-eval-prod > "backup-config-$timestamp.json"

# Backup Bicep parameters
Copy-Item "main.parameters.prod.json" "main.parameters.prod.$timestamp.json"
```

### Restore Configuration

```powershell
# Restore from backup
az functionapp config appsettings set `
    --name <function-app-name> `
    --resource-group rg-agent-eval-prod `
    --settings @backup-config-$timestamp.json
```

### Disaster Recovery

**RTO/RPO Targets:**
- **RTO (Recovery Time Objective)**: 4 hours
- **RPO (Recovery Point Objective)**: 1 hour

**DR Procedures:**

1. **Identify failure scope**: Regional outage? Service-specific?
2. **Activate DR environment**: Deploy to secondary region
3. **Update DNS/routing**: Point traffic to DR environment
4. **Validate functionality**: Run smoke tests
5. **Monitor closely**: Watch for cascading failures
6. **Communicate status**: Update stakeholders every 30 minutes

```powershell
# Deploy to DR region
.\deploy.ps1 `
    -EnvironmentName "prod" `
    -ResourceGroupName "rg-agent-eval-prod-dr" `
    -Location "westus2"
```

## 📞 Escalation Procedures

### Escalation Matrix

| Level | Role | Response Time | Contact |
|-------|------|---------------|---------|
| L1 | On-Call Engineer | 15 minutes | PagerDuty/Phone |
| L2 | Engineering Lead | 1 hour | Teams/Phone |
| L3 | Engineering Manager | 2 hours | Teams/Phone |
| L4 | Azure Support | 4 hours | Support Portal |

### When to Escalate

**Immediate Escalation (L1 → L2):**
- Critical alert and unable to resolve in 1 hour
- Security incident detected
- Data corruption suspected
- Complete service outage

**L2 → L3 Escalation:**
- Issue persists after 2 hours
- Multiple services affected
- Requires architectural decision
- Budget approval needed for scaling

**L3 → L4 (Azure Support):**
- Azure platform issue confirmed
- Service quota increase needed urgently
- Assistance needed with Azure services

## 📝 On-Call Handoff

### Handoff Template

```markdown
## On-Call Handoff - [Date]

### Current Status
- System Health: [Green/Yellow/Red]
- Active Incidents: [Number]
- Pending Changes: [Description]

### Active Issues
1. [Issue description]
   - Status: [In Progress/Monitoring/Resolved]
   - Actions taken: [List actions]
   - Next steps: [What to do next]

### Upcoming Work
- [Scheduled maintenance]
- [Pending deployments]
- [Known issues to monitor]

### Important Notes
- [Any important context]
- [Quirks or workarounds]
- [Contact information for key stakeholders]
```

## 📚 Reference Links

### Internal Documentation
- [Phase 6 Deployment Guide](phase6-production-deployment.md)
- [Monitoring Configuration](monitoring-configuration.md)
- [Implementation Status](implementation.md)
- [PRD Document](PRD-Agent-Evaluation-Function.md)

### Azure Resources
- Production Function App: `https://portal.azure.com/#@<tenant>/resource<resource-id>`
- Application Insights: `https://portal.azure.com/#@<tenant>/resource<resource-id>`
- Service Bus Namespace: `https://portal.azure.com/#@<tenant>/resource<resource-id>`

### Support Channels
- Teams Channel: [Link]
- PagerDuty: [Link]
- Azure Support: [Support Plan]

---

**Document Maintenance**: Review and update monthly or after major incidents  
**Feedback**: Submit improvements via [process]  
**Owner**: Operations Team
