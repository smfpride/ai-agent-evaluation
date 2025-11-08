# AI Agent Evaluation Function Infrastructure

This directory contains the Azure infrastructure deployment scripts and templates for the AI Agent Evaluation Function using managed identity authentication.

## Quick Start

1. **Prerequisites**
   ```powershell
   # Install required PowerShell modules
   Install-Module -Name Az -Force -AllowClobber
   Install-Module -Name Az.KeyVault -Force
   Install-Module -Name Az.Storage -Force
   ```

2. **Deploy Infrastructure**
   ```powershell
   .\deploy.ps1 -SubscriptionId "your-subscription-id" -ResourceGroupName "rg-ai-agent-eval" -DataverseBaseUrl "https://yourorg.crm.dynamics.com/" -CopilotStudioDirectLineSecret "your-secret"
   ```

3. **Validate Deployment**
   ```powershell
   .\validate-deployment.ps1 -SubscriptionId "your-subscription-id" -ResourceGroupName "rg-ai-agent-eval" -FunctionAppName "ai-agent-eval-dev-func"
   ```

## Deployment Scripts

### deploy.ps1
Main deployment script that:
- Creates Azure resource group
- Deploys all infrastructure using Bicep templates
- Configures managed identity RBAC permissions
- Uploads evaluation prompt templates
- Updates Key Vault secrets
- Runs post-deployment validation
- Provides comprehensive deployment status

**Enhanced Features:**
- ✅ RBAC propagation wait periods
- ✅ Post-deployment validation integration
- ✅ Function App runtime status verification
- ✅ Key Vault connectivity testing
- ✅ Comprehensive resource information display
- ✅ Enhanced error handling and warnings

### validate-deployment.ps1
Comprehensive validation script that:
- Verifies managed identity configuration
- Validates all RBAC role assignments
- Tests Function App configuration
- Checks service connectivity
- Provides detailed validation reports

**Validation Checks:**
- ✅ System-assigned managed identity enabled
- ✅ Key Vault Secrets User role assignment
- ✅ Storage Blob Data Contributor role assignment
- ✅ Azure Service Bus Data Owner role assignment
- ✅ Cognitive Services OpenAI User role assignment
- ✅ Function App configuration settings
- ✅ Endpoint connectivity testing

### validate.ps1
Bicep template validation script for testing infrastructure changes.

## Infrastructure Components

### Azure Resources Deployed
- **Azure Function App** (Isolated .NET 8.0)
- **Key Vault** (Secret management with RBAC)
- **Storage Account** (Blob storage for templates and data)
- **Service Bus** (Message queuing for workflow orchestration)
- **Azure OpenAI** (LLM judge evaluation)
- **Application Insights** (Monitoring and diagnostics)
- **Log Analytics Workspace** (Centralized logging)

### Managed Identity Configuration
All services are configured to use **system-assigned managed identity** for secure, password-less authentication:

```
Function App → [Managed Identity] → Azure Services
    ├── Key Vault (Key Vault Secrets User)
    ├── Storage Account (Storage Blob Data Contributor)  
    ├── Service Bus (Azure Service Bus Data Owner)
    └── Azure OpenAI (Cognitive Services OpenAI User)
```

## Usage Examples

### Basic Deployment
```powershell
# Deploy to development environment
.\deploy.ps1 -SubscriptionId "12345678-1234-1234-1234-123456789012" -ResourceGroupName "rg-ai-agent-eval-dev"
```

### Production Deployment
```powershell
# Deploy to production with custom parameters
.\deploy.ps1 `
    -SubscriptionId "12345678-1234-1234-1234-123456789012" `
    -ResourceGroupName "rg-ai-agent-eval-prod" `
    -Location "westus2" `
    -EnvironmentName "prod" `
    -AppName "ai-agent-eval" `
    -DataverseBaseUrl "https://yourorg.crm.dynamics.com/" `
    -CopilotStudioDirectLineSecret "your-production-secret"
```

### Infrastructure-Only Deployment (No Secrets)
```powershell
# Deploy infrastructure without updating secrets
.\deploy.ps1 `
    -SubscriptionId "12345678-1234-1234-1234-123456789012" `
    -ResourceGroupName "rg-ai-agent-eval-test" `
    -SkipSecretUpdate
```

### Post-Deployment Validation
```powershell
# Validate existing deployment
.\validate-deployment.ps1 `
    -SubscriptionId "12345678-1234-1234-1234-123456789012" `
    -ResourceGroupName "rg-ai-agent-eval-dev" `
    -FunctionAppName "ai-agent-eval-dev-func"
```

## Troubleshooting

### Common Issues

**RBAC Assignment Failures**
- Wait 5-10 minutes for Azure AD propagation
- Re-run validation script: `.\validate-deployment.ps1`
- Check Azure portal for role assignment status

**Key Vault Access Denied**
- Verify managed identity is enabled on Function App
- Confirm "Key Vault Secrets User" role assignment
- Check Key Vault access policies (should use RBAC, not access policies)

**Service Bus Connection Issues**
- Verify "Azure Service Bus Data Owner" role assignment
- Check Service Bus namespace connection string format
- Ensure Function App has correct environment variables

**Storage Account Access Issues**
- Confirm "Storage Blob Data Contributor" role assignment  
- Verify storage account name in Function App settings
- Check blob container exists and has correct permissions

### Debugging Commands

```powershell
# Check Function App managed identity
Get-AzWebApp -ResourceGroupName "your-rg" -Name "your-function-app" | Select-Object Identity

# List RBAC assignments for managed identity
Get-AzRoleAssignment -ObjectId "managed-identity-principal-id"

# Test Key Vault access
Get-AzKeyVaultSecret -VaultName "your-keyvault"

# Check Function App configuration
Get-AzWebAppSetting -ResourceGroupName "your-rg" -Name "your-function-app"
```

## File Structure

```
infrastructure/
├── main.bicep                 # Main Bicep template
├── main.json                  # Compiled ARM template  
├── main.parameters.dev.json   # Development parameters
├── main.parameters.prod.json  # Production parameters
├── deploy.ps1                 # Enhanced deployment script
├── validate-deployment.ps1    # Deployment validation script
├── validate.ps1              # Template validation script
└── README.md                 # This documentation
```

## Security Features

- ✅ **Managed Identity Authentication** - No stored credentials
- ✅ **RBAC-based Access Control** - Principle of least privilege
- ✅ **Key Vault Integration** - Secure secret management
- ✅ **Network Security** - Private endpoints (configurable)
- ✅ **Audit Logging** - All operations logged to Application Insights
- ✅ **Encryption** - Data encrypted at rest and in transit

## Monitoring and Observability

The deployment includes comprehensive monitoring:
- **Application Insights** for application telemetry
- **Log Analytics** for centralized logging
- **Azure Monitor** for infrastructure metrics
- **Diagnostic settings** on all resources

Access monitoring dashboards:
- Application Insights: `https://portal.azure.com/#@/resource/subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.Insights/components/{app}-ai`
- Log Analytics: `https://portal.azure.com/#@/resource/subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.OperationalInsights/workspaces/{app}-law`

## Next Steps

After successful deployment:

1. **Deploy Function Code**
   ```bash
   func azure functionapp publish {function-app-name}
   ```

2. **Run Integration Tests**
   ```bash
   dotnet test ../AiAgentEvaluation.IntegrationTests/
   ```

3. **Configure Evaluation Workflows**
   - Update Key Vault secrets with your service endpoints
   - Configure evaluation test cases
   - Set up monitoring alerts

4. **Performance Tuning**
   - Monitor Application Insights for bottlenecks
   - Adjust Function App scaling settings
   - Optimize Service Bus message processing

## Support

For issues and questions:
- Check the troubleshooting section above
- Review deployment logs and validation output
- Monitor Application Insights for runtime errors
- Use Azure portal for resource status verification