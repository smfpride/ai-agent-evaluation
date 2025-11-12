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
- **Key Vault**: Stores DataverseConnectionString secret (OAuth credentials)
- **Storage Account**: Function storage and Durable Functions state
- **Service Bus**: Two message queues for test case distribution
  - `test-case-processing` - Single-turn test cases
  - `multi-turn-test-case-processing` - Multi-turn conversation test cases
- **Azure OpenAI**: GPT-4o deployment for LLM judge evaluation
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

Application settings configured via Bicep:
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
- `FUNCTIONS_EXTENSION_VERSION`: ~4
- `FUNCTIONS_WORKER_RUNTIME`: dotnet-isolated
- `WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED`: 1
- `AzureWebJobsStorage` (connection string for consumption plan)
- `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING`
- `WEBSITE_CONTENTSHARE`
- `ServiceBusNamespace__fullyQualifiedNamespace` (for Service Bus trigger binding)
- `ServiceBus__Namespace` (for Service Bus SDK usage)
- `ServiceBus__QueueName`: test-case-processing
- `AzureOpenAIEndpoint`
- `KeyVaultUrl`
- `DataverseConnectionString`: Key Vault reference
- `DefaultEvaluationTimeoutMinutes`: 5
- `MaxConcurrentTestCases`: 100
- `CopilotStudio__TimeoutSeconds`: 60
- `CopilotStudio__BotResponseTimeoutSeconds`: 60
- `CopilotStudio__MaxRetryAttempts`: 3
- `AzureOpenAI__MaxRetryAttempts`: 5
- `AzureOpenAI__TimeoutSeconds`: 60

## Deploy Function Code

```bash
cd ../AiAgentEvaluation
func azure functionapp publish {function-app-name}
```

## Files

- `main.bicep` - Infrastructure as Code template
- `deploy.ps1` - Deployment automation script
- `main.parameters.*.json` - Environment-specific parameters
