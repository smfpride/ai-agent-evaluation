# Copilot Studio Token Endpoint Integration

This document explains how to configure the AI Agent Evaluation system to work with Copilot Studio using token endpoints.

## Overview

The AI Agent Evaluation system uses Copilot Studio's token endpoint approach for Direct Line integration. This method is more secure than traditional secrets as it uses short-lived tokens that are automatically refreshed.

## Your Token Endpoint

Your Copilot Studio token endpoint is:
```
https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview
```

## Configuration

### Local Development

**In local.settings.json:**
```json
{
  "Values": {
    "CopilotStudio:TokenEndpoint": "https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview"
  }
}
```

### Azure Deployment

**In Azure Configuration (App Settings or Key Vault):**
- **Key**: `CopilotStudioTokenEndpoint`
- **Value**: `https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview`

### Using the Deployment Script

```powershell
.\deploy.ps1 `
    -SubscriptionId "your-subscription-id" `
    -ResourceGroupName "rg-ai-agent-eval" `
    -CopilotStudioTokenEndpoint "https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview"
```

## How It Works

When token endpoint mode is active:

1. **Token Acquisition**: The service calls your token endpoint to obtain a Direct Line token
2. **Token Caching**: Tokens are cached and automatically refreshed when they expire
3. **API Calls**: The Direct Line token is used for all Direct Line API calls
4. **Security**: No long-lived secrets are stored or transmitted

### Token Lifecycle

- **Initial Request**: GET request to your token endpoint returns a Direct Line token and expiration time
- **Caching**: Token is cached in memory with expiration tracking
- **Refresh**: New tokens are automatically obtained when the current token is close to expiration (5-minute buffer)
- **Error Handling**: If token acquisition fails, requests are retried with exponential backoff

## Advantages of Token Endpoint Mode

1. **Enhanced Security**: No long-lived secrets to manage or potentially expose
2. **Automatic Rotation**: Tokens are short-lived and automatically refreshed
3. **Audit Trail**: Token requests can be logged and monitored in Copilot Studio
4. **Simplified Configuration**: Single token endpoint URL instead of managing secrets

## Testing Your Configuration

To test that your token endpoint is working:

1. **Health Check**: The service includes a health check that verifies token acquisition
2. **Logs**: Check application logs for token acquisition success/failure messages
3. **Manual Test**: You can test the token endpoint directly:

```bash
curl -X GET "https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview"
```

Expected response:
```json
{
    "token": "DirectLineTokenValue...",
    "expires_in": 3600,
    "conversationId": "optional-conversation-id"
}
```

## Troubleshooting

### Common Issues

1. **401 Unauthorized**: 
   - Verify the token endpoint URL is correct
   - Check if the Copilot Studio agent requires authentication
   - Ensure the agent is published and accessible

2. **Token Expires Quickly**:
   - Normal behavior - tokens typically expire in 1 hour
   - The service automatically handles token refresh

3. **Service Registration Errors**:
   - Check logs for "CopilotStudioServiceRegistration" messages
   - Verify configuration is properly loaded

### Log Messages

Look for these log messages to verify correct operation:

- `"Fetching new Direct Line token from endpoint"`
- `"Successfully obtained Direct Line token, expires in {ExpiresIn} seconds"`

## Quick Start

### Option 1: Use Pre-configured File (Fastest)
1. **Copy the pre-configured file**:
   ```powershell
   Copy-Item "local.settings.example-your-config.json" "local.settings.json"
   ```
   This file already has your token endpoint configured and ready to use.

### Option 2: Configure from Main Example  
1. **Copy the main configuration file**:
   ```powershell
   Copy-Item "local.settings.example.json" "local.settings.json"
   ```

2. **Uncomment and configure the Copilot Studio settings** in your `local.settings.json`:
   ```json
   {
     "Values": {
       "CopilotStudio:TokenEndpoint": "https://5b015d5390b8ef2b879fc33717257d.0d.environment.api.powerplatform.com/powervirtualagents/botsbyschema/msdyn_indPersonalShoppingAgent/directline/token?api-version=2022-03-01-preview",
       "CopilotStudio:DirectLineEndpoint": "https://directline.botframework.com",
       "CopilotStudio:TimeoutSeconds": "30"
     }
   }
   ```

### Next Steps (Both Options)
3. **Configure other required services** (Azure OpenAI, DataVerse) as needed

4. **Test the configuration** by running the function app locally:
   ```powershell
   func start
   ```

5. **Verify the health check** endpoint shows the service is healthy

## Security Considerations

1. **Network Security**: Ensure the token endpoint is accessible from your Azure Function
2. **Monitoring**: Monitor token acquisition patterns for unusual activity
3. **Access Control**: Consider implementing additional access controls on the token endpoint if needed
4. **Logging**: Token acquisition attempts are logged for audit purposes

## Related Documentation

- [Copilot Studio Direct Line Integration](https://learn.microsoft.com/en-us/microsoft-copilot-studio/publication-connect-bot-to-custom-application)
- [Bot Framework Direct Line API](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-concepts)
- [Direct Line Authentication](https://learn.microsoft.com/en-us/azure/bot-service/rest-api/bot-framework-rest-direct-line-3-0-authentication)