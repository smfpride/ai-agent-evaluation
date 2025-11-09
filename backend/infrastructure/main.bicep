@description('Main deployment template for AI Agent Evaluation Function')
param location string = resourceGroup().location
param environmentName string = 'dev'
param appName string = 'ai-agent-eval'

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

// Create Service Bus Queue
resource testCaseProcessingQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'test-case-processing'
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
      version: '2024-08-06'
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
        // Managed Identity Configuration (Primary)
        {
          name: 'StorageAccountName'
          value: storageAccount.name
        }
        {
          name: 'ServiceBusNamespace'
          value: serviceBusNamespace.name
        }
        {
          name: 'AzureOpenAIEndpoint'
          value: azureOpenAI.properties.endpoint
        }
        {
          name: 'DataverseBaseUrl'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=DataverseBaseUrl)'
        }
        {
          name: 'CopilotStudioTokenEndpoint'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=CopilotStudioTokenEndpoint)'
        }
        {
          name: 'KeyVaultUrl'
          value: keyVault.properties.vaultUri
        }
        // Fallback Connection Strings for Local Development
        {
          name: 'ServiceBusConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=ServiceBusConnectionString)'
        }
        {
          name: 'DataverseConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=DataverseConnectionString)'
        }
        {
          name: 'AzureOpenAIApiKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=AzureOpenAIApiKey)'
        }
        {
          name: 'StorageAccountConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
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

// Store secrets in Key Vault (placeholders - will need to be updated with actual values)
resource serviceBusConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ServiceBusConnectionString'
  properties: {
    value: serviceBusNamespace.listKeys().primaryConnectionString
  }
}

resource azureOpenAIApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'AzureOpenAIApiKey'
  properties: {
    value: azureOpenAI.listKeys().key1
  }
}

// Placeholder secrets (need to be updated with actual values during deployment)
resource dataverseBaseUrlSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DataverseBaseUrl'
  properties: {
    value: 'PLACEHOLDER - Update with actual Dataverse base URL (e.g., https://orgname.crm.dynamics.com/)'
  }
}

resource dataverseConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DataverseConnectionString'
  properties: {
    value: 'PLACEHOLDER - Update with actual Dataverse connection string'
  }
}

resource copilotStudioTokenEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'CopilotStudioTokenEndpoint'
  properties: {
    value: 'PLACEHOLDER - Update with actual Copilot Studio token endpoint URL'
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
