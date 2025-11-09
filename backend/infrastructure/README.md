# AI Agent Evaluation Function Infrastructure

Azure infrastructure deployment using managed identity authentication.

## Prerequisites

```powershell
Install-Module -Name Az -Force -AllowClobber
```

## Quick Deployment

```powershell
.\deploy.ps1 -SubscriptionId "your-sub-id" -ResourceGroupName "rg-agent-eval-staging"
```

With Dataverse connection string:
```powershell
.\deploy.ps1 -SubscriptionId "your-sub-id" -ResourceGroupName "rg-agent-eval-staging" -DataverseConnectionString "AuthenticationType=ClientSecret;Url=https://..."
```

## Deployed Resources

- **Function App**: .NET 8 isolated worker, consumption plan
- **Key Vault**: Stores DataverseConnectionString secret
- **Storage Account**: Function storage and evaluation data
- **Service Bus**: Test case message queue (test-case-processing)
- **Azure OpenAI**: GPT-4o for LLM judge evaluation
- **App Insights**: Monitoring and diagnostics
- **Log Analytics**: Centralized logging

## Managed Identity RBAC

System-assigned managed identity with roles:
- **Key Vault Secrets User** - Read secrets
- **Storage Blob Data Contributor** - Access blob storage
- **Storage File Data Privileged Contributor** - Access file storage
- **Azure Service Bus Data Owner** - Send/receive messages
- **Cognitive Services OpenAI User** - Call OpenAI API

## Function App Settings

15 application settings configured:
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
- `FUNCTIONS_EXTENSION_VERSION`: ~4
- `FUNCTIONS_WORKER_RUNTIME`: dotnet-isolated
- `WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED`: 1
- `AzureWebJobsStorage__blobServiceUri`
- `AzureWebJobsStorage__queueServiceUri`
- `AzureWebJobsStorage__tableServiceUri`
- `ServiceBusNamespace__fullyQualifiedNamespace` (for trigger binding)
- `ServiceBus__Namespace` (for SDK usage)
- `ServiceBus__QueueName`: test-case-processing
- `AzureOpenAI__Endpoint`
- `AzureOpenAI__DeploymentName`: gpt-4o
- `KeyVault__VaultUri`
- `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`
- `WEBSITE_CONTENTSHARE`

## Deploy Function Code

```bash
cd ../AiAgentEvaluation
func azure functionapp publish {function-app-name}
```

## Files

- `main.bicep` - Infrastructure as Code template
- `deploy.ps1` - Deployment automation script
- `main.parameters.*.json` - Environment-specific parameters
