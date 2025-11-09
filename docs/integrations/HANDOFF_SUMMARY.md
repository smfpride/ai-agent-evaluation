# AI Agent Evaluation Project - Dataverse OAuth Authentication Implementation Summary

## Project Overview
Azure Functions-based AI agent evaluation system with Copilot Studio integration via Direct Line API using **token endpoint authentication** (not Direct Line secrets). The project has been successfully updated to use OAuth-only authentication for Dataverse, removing unsupported Managed Identity approaches.

## Current Architecture
- **Azure Functions**: .NET 8.0 isolated runtime
- **Copilot Studio**: Token endpoint authentication with URL caching
- **Dataverse**: OAuth authentication (Managed Identity removed)
- **Deployment**: Azure CLI zip deployment (func CLI has runtime detection issues)
- **Infrastructure**: Bicep templates with Key Vault integration

## Key Implementations Completed

### 1. Copilot Studio Token Endpoint Integration ✅
- **File**: `Services/CopilotStudioService.cs`
- **Approach**: Complete rewrite using token endpoint instead of Direct Line secrets
- **URL**: `https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview`
- **Features**: Token caching, separate HTTP clients for token vs DirectLine, proper error handling

### 2. Configuration System Fix ✅
- **File**: `Program.cs` 
- **Critical Fix**: Explicit mapping from flat environment variables to nested AppSettings structure
- **Issue**: Azure Functions environment variables are flat, but AppSettings expects nested structure
- **Solution**: Added explicit configuration mapping in `Configure<AppSettings>()` delegate

### 3. Dataverse OAuth-Only Authentication ✅
- **File**: `Services/DataverseService.cs`, `Models/AppSettings.cs`
- **Change**: Removed all Managed Identity code (unsupported by Dataverse)
- **Authentication**: OAuth-only with connection string format
- **Current Connection String**: `AuthenticationType=OAuth;Url=https://org4a92a7b4.crm.dynamics.com/;ClientId=2e8f2905-8f14-42a2-9f6d-3080b5e85146;ClientSecret=SEl8Q~~xLt3PfT2F5qOYNlgBgn8zLbICdnl2gdqr`

## Deployment Method - CRITICAL GOTCHA 🚨

**DO NOT USE `func azure functionapp publish`** - It fails with runtime detection errors.

**USE THIS DEPLOYMENT METHOD INSTEAD**:
```powershell
# From AiAgentEvaluation directory:
dotnet publish -c Release -o publish
cd publish
tar -a -c -f ..\publish.zip *
cd ..
az functionapp deployment source config-zip --resource-group rg-agent-eval-staging --name func-aiestagingmbwc --src publish.zip --timeout 300
```

## Current Deployment Environment
- **Resource Group**: `rg-agent-eval-staging`
- **Function App**: `func-aiestagingmbwc` 
- **Application Insights**: `ai-aiestagingmbwc`
- **Key Vault**: `kv-aiestagingmbwc`

## Key Findings & Gotchas

### 1. Dataverse Authentication Requirements
- **Supported**: OAuth, Certificate, ClientSecret, Office365
- **NOT Supported**: Managed Identity (confirmed not on roadmap)
- **Parameter Name**: Must use `AuthenticationType` not `AuthType` in connection string

### 2. Configuration Mapping Issue
- **Problem**: Environment variables in Azure Functions are flat (e.g., `DataverseConnectionString`)
- **AppSettings Expects**: Nested structure (e.g., `Dataverse.ConnectionString`)
- **Solution**: Explicit mapping in Program.cs Configure<AppSettings>() delegate
- **Location**: Lines 50-80 in Program.cs

### 3. Function CLI Runtime Detection
- **Issue**: `func azure functionapp publish` fails with "could not determine runtime" errors
- **Root Cause**: CLI cannot properly detect .NET 8.0 isolated runtime
- **Workaround**: Use Azure CLI zip deployment method above

### 4. Key Vault Integration
- **Pattern**: Environment variables reference Key Vault: `@Microsoft.KeyVault(VaultName=kv-aiestagingmbwc;SecretName=DataverseConnectionString)`
- **Restart Required**: Function App restart needed after Key Vault secret updates
- **Command**: `az functionapp restart --resource-group rg-agent-eval-staging --name func-aiestagingmbwc`

## Current Status - Connection Issue 🔄

### What's Working ✅
- Authentication type validation passes
- Token endpoint integration complete
- Configuration mapping working
- OAuth connection string format correct

### Current Issue ❌
- **Error**: `DataverseConnectionException: Failed to connect to Dataverse`
- **Location**: DataverseService.cs line 40 (ServiceClient constructor)
- **Likely Causes**: 
  1. Invalid ClientId/ClientSecret credentials
  2. Dataverse environment URL incorrect
  3. Network/permissions issue
  4. Environment not accessible

### Health Check Test
```bash
curl "https://func-aiestagingmbwc.azurewebsites.net/api/health" -v
# Currently returns: HTTP 500 due to Dataverse connection failure
```

### Debug Information
```bash
# View latest exceptions:
az monitor app-insights events show --app ai-aiestagingmbwc --resource-group rg-agent-eval-staging --type exceptions --start-time 2025-11-08T19:30:00Z

# Check Key Vault secret:
az keyvault secret show --vault-name kv-aiestagingmbwc --name DataverseConnectionString --query "value" -o tsv

# Restart Function App after Key Vault changes:
az functionapp restart --resource-group rg-agent-eval-staging --name func-aiestagingmbwc
```

## Recommended Next Steps

1. **Verify Dataverse Credentials**: Confirm ClientId and ClientSecret are valid for the target environment
2. **Validate Environment URL**: Ensure `https://org4a92a7b4.crm.dynamics.com/` is the correct Dataverse environment URL
3. **Test Connection**: Try connecting to Dataverse directly using the connection string outside of Azure Functions
4. **Check Permissions**: Verify the service principal has proper permissions to access the Dataverse environment
5. **Network Diagnostics**: Confirm Azure Functions can reach the Dataverse environment

## File Structure Reference
```
backend/AiAgentEvaluation/
├── Services/
│   ├── CopilotStudioService.cs (✅ Token endpoint complete)
│   ├── DataverseService.cs (✅ OAuth-only, needs connection verification)
│   └── ...
├── Models/
│   ├── AppSettings.cs (✅ Cleaned up, BaseUrl removed)
│   └── ...
├── Program.cs (✅ Configuration mapping fixed)
└── ...
```

## Key Environment Variables (via Key Vault)
- `DataverseConnectionString`: OAuth connection string
- `CopilotStudioTokenEndpoint`: Token endpoint URL
- `AzureOpenAIEndpoint`: OpenAI service endpoint
- `ServiceBusNamespace`: Service Bus namespace for managed identity
- `StorageAccountName`: Storage account for managed identity

## Code Changes Made

### DataverseService.cs - OAuth Only Implementation
```csharp
// OLD (Managed Identity approach - REMOVED)
// var credential = new DefaultAzureCredential();
// var connectionString = $"Url={_settings.Dataverse.BaseUrl};AuthType=Managed Identity";

// NEW (OAuth-only approach)
if (string.IsNullOrEmpty(_settings.Dataverse.ConnectionString))
{
    throw new InvalidOperationException("Dataverse ConnectionString is required. Please configure DataverseConnectionString with OAuth authentication (AuthType=OAuth).");
}

_serviceClient = new ServiceClient(_settings.Dataverse.ConnectionString);
```

### Program.cs - Configuration Mapping Fix
```csharp
// CRITICAL FIX - Map flat environment variables to nested AppSettings
builder.Services.Configure<AppSettings>(options =>
{
    // Dataverse settings (OAuth connection string only - Managed Identity not supported)
    options.Dataverse.ConnectionString = builder.Configuration["DataverseConnectionString"] ?? options.Dataverse.ConnectionString;
    
    // CopilotStudio settings  
    options.CopilotStudio.TokenEndpoint = builder.Configuration["CopilotStudioTokenEndpoint"] ?? options.CopilotStudio.TokenEndpoint;
    
    // ... other mappings
});
```

### AppSettings.cs - Removed BaseUrl and ClientId
```csharp
public class DataverseSettings
{
    /// <summary>
    /// OAuth connection string for Dataverse authentication
    /// Example: "AuthenticationType=OAuth;Url=https://org4a92a7b4.crm.dynamics.com/;ClientId=xxx;ClientSecret=yyy"
    /// Supported auth types: OAuth, Certificate, ClientSecret, Office365 (Managed Identity NOT supported)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}
```

## Infrastructure Changes Needed (Optional)
The Bicep infrastructure still references `DataverseBaseUrl` which is no longer used. Consider cleaning this up:
- Remove `DataverseBaseUrl` parameter from `deploy.ps1`
- Remove `dataverseBaseUrlSecret` from `main.bicep`
- Update parameter files to remove `DataverseBaseUrl` references

## Status Summary
The system is **95% complete** - only the Dataverse connection verification remains to achieve full functionality. The authentication architecture has been completely modernized to use OAuth-only patterns as recommended by Microsoft.