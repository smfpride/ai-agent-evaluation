# Service Bus Configuration Guide

## Error: "No such host is known" (HostNotFound)

This error occurs when the Service Bus client cannot resolve the hostname. This typically means your Service Bus connection is not configured.

## Local Development Setup

For local development, you have two options:

### Option 1: Use Azure Service Bus Connection String (Recommended for Local Dev)

1. **Create an Azure Service Bus namespace** (if you don't have one):
   ```powershell
   az servicebus namespace create --resource-group <your-rg> --name <your-namespace> --location <location>
   ```

2. **Create a queue** named `test-case-processing`:
   ```powershell
   az servicebus queue create --resource-group <your-rg> --namespace-name <your-namespace> --name test-case-processing
   ```

3. **Get the connection string**:
   ```powershell
   az servicebus namespace authorization-rule keys list --resource-group <your-rg> --namespace-name <your-namespace> --name RootManageSharedAccessKey --query primaryConnectionString --output tsv
   ```

4. **Update `local.settings.json`**:
   ```json
   {
     "Values": {
       "ServiceBusConnectionString": "Endpoint=sb://your-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key"
     }
   }
   ```

### Option 2: Use Azure Service Bus with Managed Identity (Production-like)

1. **Update `local.settings.json`**:
   ```json
   {
     "Values": {
       "ServiceBusNamespace": "your-namespace.servicebus.windows.net"
     }
   }
   ```

2. **Ensure you're logged in with Azure CLI**:
   ```powershell
   az login
   ```

3. **Grant yourself Service Bus Data Sender role**:
   ```powershell
   az role assignment create --role "Azure Service Bus Data Sender" --assignee <your-email> --scope /subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.ServiceBus/namespaces/<namespace>
   ```

## Production Deployment

For production, use Managed Identity (Option 2 above) and configure in your Azure Function App Settings:

```powershell
az functionapp config appsettings set --name <function-app-name> --resource-group <rg> --settings "ServiceBusNamespace=your-namespace.servicebus.windows.net"
```

Ensure the Function App's Managed Identity has the "Azure Service Bus Data Sender" role on the namespace.

## Configuration Keys

The application supports these configuration keys:

- `ServiceBusConnectionString` - Connection string for local development
- `ServiceBusNamespace` - Namespace URL for managed identity (e.g., `yournamespace.servicebus.windows.net`)
- `QueueName` - Queue name (defaults to `test-case-processing`)

## Verifying Configuration

After updating your configuration:

1. Restart your Azure Functions host
2. The logs should show either:
   - ✅ "Initialized Service Bus client with managed identity for namespace..."
   - ✅ "Using connection string for Service Bus - this should only be used for local development"
   - ❌ "Service Bus is not configured..." (if still not configured)

## Troubleshooting

- **"No such host is known"**: Connection string/namespace is empty or invalid
- **"Unauthorized"**: Check your managed identity permissions or connection string key
- **"MessagingEntityNotFound"**: Queue doesn't exist - create it using the commands above
