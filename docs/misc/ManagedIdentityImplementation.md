# Managed Identity Implementation Guide

## Overview

This document outlines the implementation of managed identity authentication across all Azure services in the AI Agent Evaluation Function App, eliminating the need for connection strings and API keys.

## ✅ Updated Configuration

### AppSettings.cs Changes

The configuration model has been updated to support managed identity:

```csharp
// OLD: Connection string approach
public class StorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}

// NEW: Managed identity approach  
public class StorageSettings
{
    public string ConnectionString { get; set; } = string.Empty; // Legacy for local dev
    public string AccountName { get; set; } = string.Empty; // For managed identity
}
```

### Configuration Updates Made:

1. **DataverseSettings**: Added `ClientId` for managed identity
2. **AzureOpenAISettings**: Removed dependency on `ApiKey` 
3. **ServiceBusSettings**: Added `Namespace` property for managed identity
4. **StorageSettings**: Added `AccountName` for managed identity

## 🔧 Service Implementation Updates

### 1. BlobStorageService - ✅ UPDATED

```csharp
// NEW: Uses managed identity with storage account name
var credential = new DefaultAzureCredential();
var storageUri = new Uri($"https://{_settings.Storage.AccountName}.blob.core.windows.net");
_blobServiceClient = new BlobServiceClient(storageUri, credential);

// Fallback to connection string for local development only
```

**Benefits:**
- No storage keys needed in production
- Automatic credential rotation
- Better security compliance

### 2. LLMJudgeService - 🚧 IN PROGRESS  

**Target Implementation:**
```csharp
// Use AzureOpenAIClient with managed identity
var credential = new DefaultAzureCredential();
_openAIClient = new AzureOpenAIClient(new Uri(_settings.AzureOpenAI.Endpoint), credential);
```

**Current Status:** 
- Updated to use `AzureOpenAIClient` instead of `HttpClient`
- Removed API key dependency for production
- Need to verify correct SDK class names

### 3. ServiceBusService - ✅ COMPLETED

**Implementation:**
```csharp
var credential = new DefaultAzureCredential();
_serviceBusClient = new ServiceBusClient(_settings.ServiceBus.Namespace, credential);
_sender = _serviceBusClient.CreateSender(_settings.ServiceBus.QueueName);
```

**Current Status:** 
- Fully implemented with managed identity
- Includes health check capability
- Fallback to connection string for local development

### 4. DataverseService - ✅ COMPLETED

**Implementation:**
```csharp
var credential = new DefaultAzureCredential();
var connectionString = $"Url={_settings.Dataverse.BaseUrl};AuthType=Managed Identity";
_serviceClient = new ServiceClient(connectionString);
```

**Current Status:** 
- Fully implemented with managed identity using PowerPlatform.Dataverse.Client
- Includes health check capability using WhoAmI request
- Fallback to connection string for local development

## 🎯 Required RBAC Role Assignments

The Function App's system-assigned managed identity must have these roles:

### Azure OpenAI Service
- **Role**: `Cognitive Services OpenAI User`
- **Scope**: Azure OpenAI resource
- **PowerShell Assignment:**
```powershell
$functionAppPrincipalId = (Get-AzWebApp -ResourceGroupName "rg-ai-eval" -Name "func-ai-eval").Identity.PrincipalId
$openAIResourceId = "/subscriptions/{subscriptionId}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{openai-account}"

New-AzRoleAssignment -ObjectId $functionAppPrincipalId -RoleDefinitionName "Cognitive Services OpenAI User" -Scope $openAIResourceId
```

### Service Bus
- **Roles**: 
  - `Azure Service Bus Data Sender`
  - `Azure Service Bus Data Receiver`
- **Scope**: Service Bus namespace
- **PowerShell Assignment:**
```powershell
$serviceBusResourceId = "/subscriptions/{subscriptionId}/resourceGroups/{rg}/providers/Microsoft.ServiceBus/namespaces/{namespace}"

New-AzRoleAssignment -ObjectId $functionAppPrincipalId -RoleDefinitionName "Azure Service Bus Data Sender" -Scope $serviceBusResourceId
New-AzRoleAssignment -ObjectId $functionAppPrincipalId -RoleDefinitionName "Azure Service Bus Data Receiver" -Scope $serviceBusResourceId
```

### Blob Storage  
- **Role**: `Storage Blob Data Contributor`
- **Scope**: Storage account
- **PowerShell Assignment:**
```powershell
$storageResourceId = "/subscriptions/{subscriptionId}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{storage-account}"

New-AzRoleAssignment -ObjectId $functionAppPrincipalId -RoleDefinitionName "Storage Blob Data Contributor" -Scope $storageResourceId
```

### Key Vault
- **Role**: `Key Vault Secrets User`
- **Scope**: Key Vault (for Copilot Studio secrets only)
- **PowerShell Assignment:**
```powershell
$keyVaultResourceId = "/subscriptions/{subscriptionId}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{keyvault-name}"

New-AzRoleAssignment -ObjectId $functionAppPrincipalId -RoleDefinitionName "Key Vault Secrets User" -Scope $keyVaultResourceId
```

### Dataverse
- **Setup**: Application user in Power Platform admin center
- **Roles**: Custom security roles for evaluation data access

## 📋 Implementation Checklist

### Phase 1: Configuration ✅
- [x] Updated AppSettings.cs with managed identity properties
- [x] Updated PRD to reflect managed identity requirements  
- [x] Documented RBAC requirements

### Phase 2: Service Updates 🚧
- [x] BlobStorageService - Updated to use managed identity
- [ ] LLMJudgeService - Verify Azure OpenAI SDK classes
- [ ] ServiceBusService - Update to use managed identity
- [ ] DataverseService - Configure managed identity authentication

### Phase 3: Infrastructure 📋
- [ ] Create infrastructure deployment scripts (Bicep)
- [ ] Configure managed identity role assignments
- [ ] Update local.settings.json template
- [ ] Test managed identity authentication

### Phase 4: Testing 📋
- [ ] Local development testing (fallback to connection strings)
- [ ] Azure deployment testing (managed identity)
- [ ] End-to-end authentication validation
- [ ] Security compliance verification

## 🔒 Security Benefits

### Production Environment:
- ✅ **No secrets in configuration** (except external service keys)
- ✅ **Automatic credential rotation** by Azure
- ✅ **Audit trails** via Azure Activity Log
- ✅ **Principle of least privilege** with scoped RBAC
- ✅ **Zero trust authentication** between services

### Development Environment:
- ✅ **Fallback authentication** for local development
- ✅ **Gradual migration** from connection strings
- ✅ **Consistent authentication patterns** across services

## 🚀 Deployment Configuration

### Function App Settings (Production):
```json
{
  "Storage__AccountName": "aievalstorageaccount",
  "AzureOpenAI__Endpoint": "https://ai-eval-openai.openai.azure.com/",
  "ServiceBus__Namespace": "ai-eval-servicebus.servicebus.windows.net",
  "Dataverse__BaseUrl": "https://yourorg.crm.dynamics.com/",
  "CopilotStudio__DirectLineSecret": "@Microsoft.KeyVault(SecretUri=https://ai-eval-kv.vault.azure.net/secrets/copilot-directline-secret/)"
}
```

### Local Development (local.settings.json):
```json
{
  "Values": {
    "Storage__ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
    "AzureOpenAI__ApiKey": "your-local-api-key",
    "ServiceBus__ConnectionString": "Endpoint=sb://...",
    "Dataverse__ConnectionString": "AuthType=ClientSecret;..."
  }
}
```

## 📚 Next Steps

1. **Complete Service Updates**: Finish updating remaining services to use managed identity
2. **Infrastructure Deployment**: Create Bicep templates with proper RBAC assignments  
3. **Testing Strategy**: Implement comprehensive testing for both local and Azure environments
4. **Documentation**: Update deployment guides and troubleshooting documentation

This approach ensures maximum security while maintaining development flexibility and follows Azure security best practices.