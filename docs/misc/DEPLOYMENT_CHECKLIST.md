# Deployment Checklist - AI Agent Evaluation Function

## Pre-Deployment Verification ✅

### Code Status
- ✅ **All Services Updated** - BlobStorageService, LLMJudgeService, ServiceBusService, DataverseService
- ✅ **AppSettings Configuration** - Managed identity properties added with fallback support
- ✅ **Build Success** - No compilation errors or warnings
- ✅ **Azure SDK Compatibility** - Latest Azure.AI.OpenAI SDK patterns implemented
- ✅ **Authentication Flow** - DefaultAzureCredential integration complete

### Infrastructure Status  
- ✅ **Bicep Template** - System-Assigned Managed Identity with RBAC role assignments
- ✅ **Deployment Script** - PowerShell 7 script with comprehensive error handling
- ✅ **Documentation** - Complete README with managed identity architecture
- ✅ **Security Configuration** - Proper RBAC permissions for all Azure services

## Deployment Steps

### 1. Infrastructure Deployment
```powershell
# Navigate to infrastructure folder
cd backend\infrastructure

# Deploy infrastructure with your specific values
.\deploy.ps1 -SubscriptionId "YOUR_SUBSCRIPTION_ID" `
             -ResourceGroupName "rg-ai-agent-eval-dev" `
             -DataverseBaseUrl "https://YOURORG.crm.dynamics.com/" `
             -CopilotStudioDirectLineSecret "YOUR_DIRECT_LINE_SECRET"
```

**Expected Outputs:**
- Function App name
- Key Vault name  
- Storage Account name
- Service Bus namespace name
- Azure OpenAI account name

### 2. Function App Code Deployment
```powershell
# From the backend\AiAgentEvaluation folder
cd backend\AiAgentEvaluation
func azure functionapp publish YOUR_FUNCTION_APP_NAME
```

### 3. Post-Deployment Validation
```powershell
# Test health endpoint
Invoke-WebRequest https://YOUR_FUNCTION_APP_NAME.azurewebsites.net/api/health

# Test evaluation endpoint
$testBody = @{
    testCases = @(@{
        id = "test-001"
        question = "What is Microsoft Azure?"
        expectedResponse = "Azure is Microsoft's cloud computing platform"
        testType = "similarity"
    })
} | ConvertTo-Json

Invoke-WebRequest -Uri "https://YOUR_FUNCTION_APP_NAME.azurewebsites.net/api/StartEvaluation" `
                  -Method POST -Body $testBody -ContentType "application/json"
```

## Required Information for Deployment

### Azure Subscription Details
- [ ] **Subscription ID**: `_________________________________`
- [ ] **Resource Group Name**: `_________________________________`
- [ ] **Azure Region**: `_________________________________` (default: eastus)

### Dataverse Configuration
- [ ] **Dataverse Base URL**: `https://______.crm.dynamics.com/`
- [ ] **Dataverse Environment**: `_________________________________`
- [ ] **Test Entity Schema**: Verify entity exists with required fields

### Copilot Studio Configuration  
- [ ] **Direct Line Channel**: Enabled in Copilot Studio
- [ ] **Direct Line Secret**: `_________________________________`
- [ ] **Bot ID**: `_________________________________`

### Azure OpenAI Setup
- [ ] **Model Deployment**: GPT-4o model will be deployed automatically
- [ ] **Quota Verification**: Ensure sufficient quota in target region
- [ ] **Capacity Planning**: 150 TPM initially configured

## Security Validation Checklist

### Managed Identity Verification
- [ ] Function App has System-Assigned Managed Identity enabled
- [ ] RBAC roles assigned to Function App identity:
  - [ ] Storage Blob Data Contributor on Storage Account
  - [ ] Azure Service Bus Data Owner on Service Bus Namespace
  - [ ] Cognitive Services OpenAI User on Azure OpenAI
  - [ ] Key Vault Secrets User on Key Vault

### Key Vault Configuration
- [ ] RBAC authorization enabled (not Access Policies)
- [ ] Required secrets populated:
  - [ ] DataverseBaseUrl
  - [ ] CopilotStudioDirectLineSecret
  - [ ] ServiceBusConnectionString (fallback)
  - [ ] DataverseConnectionString (fallback)
  - [ ] AzureOpenAIApiKey (fallback)

### Network Security
- [ ] Function App HTTPS only enabled
- [ ] Storage Account public access disabled
- [ ] Service Bus public network access configured
- [ ] Azure OpenAI public access configured

## Testing Scenarios

### 1. Authentication Testing
```powershell
# Test managed identity authentication
$headers = @{ 'Authorization' = 'Bearer ' + (Get-AzAccessToken).Token }
Invoke-WebRequest "https://YOUR_FUNCTION_APP_NAME.azurewebsites.net/api/health" -Headers $headers
```

### 2. Service Integration Testing
```powershell
# Test blob storage access (prompt templates)
# Test Service Bus queue operations
# Test Azure OpenAI connectivity
# Test Dataverse entity access
```

### 3. End-to-End Evaluation Testing
```json
{
  "testCases": [
    {
      "id": "similarity-001",
      "question": "What is artificial intelligence?",
      "expectedResponse": "AI is computer systems performing tasks that typically require human intelligence",
      "testType": "similarity"
    },
    {
      "id": "accuracy-001", 
      "question": "What is the capital of France?",
      "expectedResponse": "Paris",
      "testType": "accuracy"
    },
    {
      "id": "relevancy-001",
      "question": "How do I reset my password?",
      "expectedResponse": "You can reset your password by clicking the 'Forgot Password' link on the login page",
      "testType": "relevancy"
    }
  ]
}
```

## Monitoring Setup

### Application Insights Configuration
- [ ] Application Insights connected to Function App
- [ ] Custom metrics configured for evaluation workflows
- [ ] Alert rules set up for failures and performance issues

### Log Analytics Queries
```kql
// Function execution traces
traces
| where cloud_RoleName == "YOUR_FUNCTION_APP_NAME"
| where timestamp > ago(1h)
| order by timestamp desc

// Dependency calls to Azure services  
dependencies
| where cloud_RoleName == "YOUR_FUNCTION_APP_NAME"
| where timestamp > ago(1h)
| summarize count() by target, resultCode

// Exception tracking
exceptions
| where cloud_RoleName == "YOUR_FUNCTION_APP_NAME"
| where timestamp > ago(1h)
| project timestamp, outerMessage, innerMessage
```

## Troubleshooting Guide

### Common Issues

1. **RBAC Permission Delays**
   - **Symptom**: 403 Forbidden errors immediately after deployment
   - **Solution**: Wait 10-15 minutes for Azure RBAC propagation
   - **Verification**: Check role assignments in Azure Portal

2. **Managed Identity Not Found**
   - **Symptom**: "ManagedIdentityCredential authentication unavailable"
   - **Solution**: Verify System-Assigned identity is enabled on Function App
   - **Verification**: Check Function App Identity blade in portal

3. **Key Vault Access Denied**
   - **Symptom**: "Access denied" when reading secrets
   - **Solution**: Ensure Key Vault uses RBAC (not Access Policies)
   - **Verification**: Check Key Vault Access Control (IAM)

4. **Azure OpenAI Rate Limits**
   - **Symptom**: 429 Rate Limit Exceeded errors
   - **Solution**: Adjust deployment capacity or implement retry logic
   - **Verification**: Check Azure OpenAI quota usage in portal

5. **Dataverse Authentication**
   - **Symptom**: "Unauthorized" from Dataverse service
   - **Solution**: Verify Dataverse base URL and managed identity setup
   - **Verification**: Test connection from Azure Cloud Shell

### Diagnostic Commands
```powershell
# Check Function App managed identity
Get-AzWebApp -Name "YOUR_FUNCTION_APP_NAME" -ResourceGroupName "YOUR_RG" | Select-Object Identity

# List role assignments
Get-AzRoleAssignment -ObjectId "FUNCTION_APP_PRINCIPAL_ID"

# Test Key Vault access
Get-AzKeyVaultSecret -VaultName "YOUR_VAULT_NAME" -Name "DataverseBaseUrl"

# Check Azure OpenAI deployment
Get-AzCognitiveServicesAccountDeployment -ResourceGroupName "YOUR_RG" -AccountName "YOUR_OPENAI_NAME"
```

## Success Criteria

### Deployment Success
- [ ] All Azure resources created without errors
- [ ] Function App deployed and running
- [ ] Managed identity authentication working
- [ ] No secrets stored in application configuration
- [ ] All integration tests passing

### Performance Validation
- [ ] Health endpoint responds in < 2 seconds
- [ ] Evaluation requests process within expected timeframes
- [ ] Service Bus queue processing working correctly
- [ ] Azure OpenAI responses received successfully

### Security Validation
- [ ] No API keys or connection strings in Function App settings
- [ ] All service-to-service calls use managed identity
- [ ] Key Vault access properly secured with RBAC
- [ ] Audit logs showing managed identity usage

## Support Contacts

- **Infrastructure Issues**: Azure Support Portal
- **Development Questions**: Review PRD and implementation documentation
- **Security Concerns**: Azure Security Center recommendations

---

✅ **Deployment Ready**: All prerequisites met and implementation complete
🎯 **Next Step**: Execute infrastructure deployment with your subscription details