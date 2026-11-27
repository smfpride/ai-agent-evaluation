@description('Main deployment template for AI Agent Evaluation Function')
param location string = resourceGroup().location
param environmentName string = 'dev'

@description('Unique suffix for resource naming')
param uniqueSuffix string = uniqueString(resourceGroup().id)

@description('Function App configuration')
param functionAppSku string = 'Y1'
param functionAppTier string = 'Dynamic'

@description('Service Bus configuration')  
param serviceBusSkuName string = 'Standard'

@description('Storage Account configuration')
param storageAccountType string = 'Standard_LRS'

@description('Azure OpenAI configuration')
param azureOpenAILocation string = 'eastus'
param azureOpenAISkuName string = 'S0'
@description('gpt-4o model version (2024-08-06 is blocked for new deployments since 2026-03-31)')
param gpt4oModelVersion string = '2024-11-20'

@description('Application Insights configuration')
param applicationInsightsType string = 'web'

// Variables - Simplified naming for Azure constraints
var shortPrefix = 'aie${environmentName}${take(uniqueSuffix, 4)}'  // ai-eval shortened to aie
var storageAccountName = replace('st${shortPrefix}', '-', '')  // Storage account needs 'st' prefix
var functionAppName = 'func-${shortPrefix}'
var serviceBusNamespaceName = 'sbus-${shortPrefix}'  // Changed from -sb (reserved) to sbus
var keyVaultName = 'kv-${shortPrefix}'  // Short kv prefix
var applicationInsightsName = 'ai-${shortPrefix}'
var logAnalyticsWorkspaceName = 'logs-${shortPrefix}'
var azureOpenAIAccountName = 'openai-${shortPrefix}'

// Resource naming validation (ensure all names meet Azure constraints)
var storageAccountNameValid = length(storageAccountName) <= 24 ? storageAccountName : 'st${take(shortPrefix, 20)}'
var keyVaultNameValid = length(keyVaultName) <= 24 ? keyVaultName : take(keyVaultName, 24)

// Deploy Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Deploy Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: applicationInsightsType
  properties: {
    Application_Type: applicationInsightsType
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Deploy Storage Account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountNameValid
  location: location
  sku: {
    name: storageAccountType
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

// Deploy Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespaceName
  location: location
  sku: {
    name: serviceBusSkuName
    tier: serviceBusSkuName
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

// Phase 1: Agent Call Queue - for Copilot Studio agent calls
resource agentCallQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'agent-call-queue'
  properties: {
    maxSizeInMegabytes: 5120
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    enablePartitioning: false
    requiresSession: false
  }
}

// Phase 1: Evaluator Processing Queue - for LLM Judge evaluations
resource evaluatorProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'evaluator-processing-queue'
  properties: {
    maxSizeInMegabytes: 5120
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    enablePartitioning: false
    requiresSession: false
  }
}

// Story 11: Multi-Turn Agent Call Queue - for multi-turn agent calls with conversation context
resource multiTurnAgentCallQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'multi-turn-agent-call-queue'
  properties: {
    maxSizeInMegabytes: 5120
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    enablePartitioning: false
    requiresSession: false
  }
}

// Orchestration Queue - consumed by ProcessOrchestration (was missing from template)
resource orchestrationQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'orchestration-queue'
  properties: {
    maxSizeInMegabytes: 5120
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    enablePartitioning: false
    requiresSession: false
  }
}

// Deploy Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultNameValid
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    accessPolicies: []
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Deploy Azure OpenAI Account
resource azureOpenAI 'Microsoft.CognitiveServices/accounts@2024-06-01-preview' = {
  name: azureOpenAIAccountName
  location: azureOpenAILocation
  kind: 'OpenAI'
  sku: {
    name: azureOpenAISkuName
  }
  properties: {
    customSubDomainName: azureOpenAIAccountName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
    disableLocalAuth: false
  }
}

// Deploy GPT-4o model
resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-06-01-preview' = {
  parent: azureOpenAI
  name: 'gpt-4o'
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: gpt4oModelVersion
    }
    raiPolicyName: 'Microsoft.Default'
  }
  sku: {
    name: 'Standard'
    capacity: 150
  }
}

// Deploy App Service Plan
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${functionAppName}-plan'
  location: location
  sku: {
    name: functionAppSku
    tier: functionAppTier
  }
  properties: {
    reserved: false
  }
}

// Deploy Function App
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: false
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: functionAppName
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsights.properties.ConnectionString
        }
        // Azure Service Configuration (Managed Identity)
        // Service Bus Trigger uses ServiceBusNamespace__fullyQualifiedNamespace
        {
          name: 'ServiceBusNamespace__fullyQualifiedNamespace'
          value: '${serviceBusNamespace.name}.servicebus.windows.net'
        }
        // Service Bus SDK uses ServiceBus:Namespace (converted from ServiceBus__Namespace)
        {
          name: 'ServiceBus__Namespace'
          value: '${serviceBusNamespace.name}.servicebus.windows.net'
        }
        {
          name: 'AzureOpenAIEndpoint'
          value: azureOpenAI.properties.endpoint
        }
        {
          name: 'KeyVaultUrl'
          value: keyVault.properties.vaultUri
        }
        // External Service Configuration (Dataverse - requires connection string)
        {
          name: 'DataverseConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=DataverseConnectionString)'
        }
        // Application configuration
        {
          name: 'DefaultEvaluationTimeoutMinutes'
          value: '5'
        }
        {
          name: 'MaxConcurrentTestCases'
          value: '100'
        }
        // Copilot Studio configuration
        {
          name: 'CopilotStudio__TimeoutSeconds'
          value: '60'
        }
        {
          name: 'CopilotStudio__BotResponseTimeoutSeconds'
          value: '60'
        }
        {
          name: 'CopilotStudio__MaxRetryAttempts'
          value: '3'
        }
        // Azure OpenAI configuration
        {
          name: 'AzureOpenAI__MaxRetryAttempts'
          value: '5'
        }
        {
          name: 'AzureOpenAI__TimeoutSeconds'
          value: '60'
        }
      ]
    }
  }
}

// Role assignments for Function App Managed Identity
resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, functionApp.id, 'KeyVaultSecretsUser')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusDataOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: serviceBusNamespace
  name: guid(serviceBusNamespace.id, functionApp.id, 'ServiceBusDataOwner')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419') // Azure Service Bus Data Owner
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource cognitiveServicesOpenAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: azureOpenAI
  name: guid(azureOpenAI.id, functionApp.id, 'CognitiveServicesOpenAIUser')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') // Cognitive Services OpenAI User
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageBlobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, functionApp.id, 'StorageBlobDataContributor')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe') // Storage Blob Data Contributor
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageFileDataPrivilegedContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storageAccount
  name: guid(storageAccount.id, functionApp.id, 'StorageFileDataPrivilegedContributor')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69566ab7-960f-475b-8e7c-b3118f30c6bd') // Storage File Data Privileged Contributor
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Store secrets in Key Vault (placeholders - will need to be updated with actual values during deployment)
// Note: Azure OpenAI and Service Bus use Managed Identity (no secrets needed)
// Only external services like Dataverse require connection strings

@description('Create the placeholder DataverseConnectionString secret. Set false on redeploys so the real secret is not overwritten.')
param seedDataverseSecret bool = true

resource dataverseConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (seedDataverseSecret) {
  parent: keyVault
  name: 'DataverseConnectionString'
  properties: {
    value: 'PLACEHOLDER - Update with actual Dataverse connection string (e.g., AuthenticationType=ClientSecret;Url=https://...)'
  }
}

// Outputs
output functionAppName string = functionApp.name
output functionAppResourceId string = functionApp.id
output functionAppPrincipalId string = functionApp.identity.principalId
output storageAccountName string = storageAccount.name
output serviceBusNamespaceName string = serviceBusNamespace.name
output keyVaultName string = keyVault.name
output applicationInsightsName string = applicationInsights.name
output azureOpenAIAccountName string = azureOpenAI.name
output resourceGroupName string = resourceGroup().name
