# Deployment Checklist - AI Agent Evaluation Function

## Pre-Deployment Verification ✅

### Code Status
- ✅ **All Services Updated** - BlobStorageService, LLMJudgeService, ServiceBusService, DataverseService
- ✅ **Durable Functions** - Multi-turn orchestrator and activity functions implemented
- ✅ **AppSettings Configuration** - Managed identity properties added with fallback support
- ✅ **Build Success** - No compilation errors or warnings
- ✅ **Unit Tests** - 15 Durable Functions tests passing
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

# Build and test before deployment
dotnet build --configuration Release
dotnet test ../tests/AiAgentEvaluation.UnitTests/AiAgentEvaluation.UnitTests.csproj

# Deploy to Azure
func azure functionapp publish YOUR_FUNCTION_APP_NAME
```

**Expected Functions Deployed:**
- `StartEvaluationRun` - HTTP trigger for starting evaluations
- `WorkerFunction` - Service Bus trigger for single-turn test cases
- `MultiTurnTestCaseOrchestrator` - Orchestration trigger for multi-turn execution
- `StartMultiTurnTestCase` - HTTP trigger for multi-turn orchestration
- `ExecuteSingleTurn` - Activity for executing one turn
- `GetTestCaseWithTurns` - Activity for fetching test data
- `CreateAggregateResult` - Activity for creating aggregate results
- `MonitorFunction` - Timer trigger for monitoring
- `GetEvaluationRunStatus` - HTTP trigger for status checks

### 3. Post-Deployment Validation

#### Verify Durable Functions Configuration
```powershell
# Check that AzureWebJobsStorage is configured (required for Durable Functions)
$settings = az functionapp config appsettings list `
    --name YOUR_FUNCTION_APP_NAME `
    --resource-group YOUR_RESOURCE_GROUP | ConvertFrom-Json

$settings | Where-Object { $_.name -eq "AzureWebJobsStorage" }
```

#### Test Single-Turn Evaluation (Existing Feature)
```powershell
# Create test evaluation run in Dataverse with single-turn test cases
$body = @{
    evaluationRunId = "YOUR_EVALUATION_RUN_ID"
    maxConcurrency = 10
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP_NAME.azurewebsites.net/api/evaluations/start" `
                  -Method POST -Body $body -ContentType "application/json"
```

#### Test Multi-Turn Evaluation (New Feature)
```powershell
# Create test evaluation run with multi-turn test case (IsMultiTurn = true, with Turns collection)
$body = @{
    evaluationRunId = "YOUR_MULTI_TURN_EVALUATION_RUN_ID"
    maxConcurrency = 10
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "https://YOUR_FUNCTION_APP_NAME.azurewebsites.net/api/evaluations/start" `
                            -Method POST -Body $body -ContentType "application/json"

Write-Host "Evaluation started. Check Durable Functions Monitor in Azure Portal."
```

#### Monitor Durable Functions Execution
```powershell
# View orchestration instances in Azure Portal:
# Function App → Functions → MultiTurnTestCaseOrchestrator → Monitor

# Or check Application Insights
az monitor app-insights query `
    --app YOUR_APP_INSIGHTS_NAME `
    --analytics-query "traces | where message contains 'orchestration' | order by timestamp desc | take 20"
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

### 3. Single-Turn Test Case (Baseline)
Create an evaluation run with single-turn test cases in Dataverse:
- IsMultiTurn = false
- UserInput and ExpectedResponse populated
- EvaluatorTypeId set

**Expected**: Test cases routed to Service Bus, processed by WorkerFunction.

### 4. Multi-Turn Test Case (New - Story 7)
Create an evaluation run with multi-turn test case:
- IsMultiTurn = true
- Turns collection with 2-10 turns
- Each turn has Sequence, PromptText, ExpectedResponse

**Expected**: 
- Test case routed to Durable Functions orchestrator
- Orchestration visible in Durable Functions Monitor
- Each turn executes sequentially (can see in Application Insights)
- Conversation ID maintained across all turns
- Execution continues beyond 5 minutes without timeout
- Individual result created per turn
- Aggregate result created at completion

### 5. Long-Running Multi-Turn Test (Stress Test)
Create a test case with:
- 15-20 turns
- Complex prompts requiring 60+ seconds per turn
- Total expected time: 15-20 minutes

**Expected**:
- No timeout errors
- All turns complete successfully
- If function restarts, orchestration resumes from last checkpoint
- Storage costs remain reasonable (< $5/month for 100 tests)

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

// Durable Functions orchestration tracking
traces
| where cloud_RoleName == "YOUR_FUNCTION_APP_NAME"
| where message contains "Starting orchestration" 
    or message contains "Executing turn" 
    or message contains "Completed multi-turn"
| order by timestamp desc
| project timestamp, message, severityLevel, customDimensions

// Multi-turn execution metrics
traces
| where cloud_RoleName == "YOUR_FUNCTION_APP_NAME"
| where message contains "Completed multi-turn test case"
| extend passRate = extract("Pass Rate: ([0-9.]+)%", 1, message)
| project timestamp, passRate, message

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

6. **Multi-Turn Test Case Not Using Durable Functions**
   - **Symptom**: Multi-turn test times out after 5 minutes
   - **Solution**: Verify test case has `IsMultiTurn = true` and `Turns` collection populated
   - **Verification**: Check Application Insights for "Started Durable Functions orchestration" message

7. **Durable Functions Orchestration Not Starting**
   - **Symptom**: Multi-turn test case queued but orchestration never starts
   - **Solution**: Verify `AzureWebJobsStorage` is configured in Function App settings
   - **Verification**: Check function logs for Durable Functions extension initialization

8. **Conversation ID Not Maintained Across Turns**
   - **Symptom**: Each turn creates new conversation
   - **Solution**: Verify first turn returns ConversationId and subsequent turns receive it
   - **Verification**: Query Dataverse for turn results - all should have same ConversationId

9. **Orchestration State Corruption**
   - **Symptom**: Orchestration stuck or replaying incorrectly
   - **Solution**: Clear orchestration history from storage account
   - **Verification**: Delete task hub containers in storage account, orchestrator will recreate

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
- [ ] All 9 functions deployed successfully (including Durable Functions)
- [ ] Managed identity authentication working
- [ ] No secrets stored in application configuration
- [ ] `AzureWebJobsStorage` configured for Durable Functions
- [ ] All unit tests passing (15 Durable Functions tests)

### Performance Validation
- [ ] Health endpoint responds in < 2 seconds
- [ ] Single-turn evaluation requests process within expected timeframes
- [ ] Multi-turn orchestrations complete successfully (no 5-minute timeout)
- [ ] Service Bus queue processing working correctly
- [ ] Durable Functions orchestrations visible in Monitor
- [ ] Azure OpenAI responses received successfully
- [ ] Conversation ID maintained across all turns in multi-turn tests

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