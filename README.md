# AI Evaluation Dashboard

AI Evaluation Dashboard enables quality assurance teams to systematically test and evaluate Copilot Studio agents by running test cases against the Direct Line API and using Azure OpenAI as an LLM-as-judge to assess semantic similarity between expected and actual responses.

## Features

- **Test Case Management**: Create, edit, and manage test cases with prompts and expected responses
- **Direct Line Integration**: Connect to Copilot Studio agents via Direct Line API
- **LLM-as-Judge**: Use Azure OpenAI to evaluate semantic similarity between responses
- **DataVerse Storage**: Store test cases and results in Microsoft DataVerse (requires proxy)
- **Batch Evaluation**: Run multiple test cases and track progress
- **Results Dashboard**: View evaluation results with scores and detailed reasoning

## Configuration

The dashboard requires three service configurations:

### 1. Direct Line API

The system uses Copilot Studio's token endpoint approach for enhanced security:

- **Token Endpoint**: Your Copilot Studio token endpoint URL
- **Example**: `https://example.powerplatform.com/powervirtualagents/botsbyschema/agentname/directline/token?api-version=2022-03-01-preview`
- **Benefits**: Enhanced security with automatic token rotation, no long-lived secrets

The system automatically fetches and refreshes Direct Line tokens as needed.

**📖 See [Copilot Studio Token Endpoint Integration Guide](./docs/copilot-studio-token-endpoint-integration.md) for detailed setup instructions.**

### 2. Azure OpenAI
- **Endpoint**: Your Azure OpenAI resource endpoint
- **API Key**: Azure OpenAI API key
- **Deployment Name**: Name of your GPT deployment
- **API Version**: API version (default: 2024-02-15-preview)

### 3. DataVerse (⚠️ Requires Backend Proxy)

## Important: DataVerse CORS Limitation

**Browser-based applications cannot directly access Microsoft Entra ID or DataVerse APIs due to CORS policies.** The Microsoft authentication endpoints and DataVerse APIs do not include `Access-Control-Allow-Origin` headers, which prevents direct browser access.

### Solution: Backend Proxy (Now Supported!)

This application now includes built-in support for backend proxy configuration:

1. **Enable Proxy Mode**: In Settings → DataVerse tab, toggle "Use Backend Proxy"
2. **Enter Proxy URL**: Provide your deployed proxy endpoint
3. **Configure Credentials**: Fill in your DataVerse connection details
4. **Test Connection**: Verify the proxy is working correctly

### Implementing Your Proxy

See **[PROXY_IMPLEMENTATION.md](./PROXY_IMPLEMENTATION.md)** for complete implementation guides including:

- **Azure Functions** (serverless, recommended)
- **Express.js** (simple, flexible)
- **Azure API Management** (enterprise-grade)

Each guide includes:
- ✅ Complete, production-ready code
- ✅ Step-by-step deployment instructions
- ✅ Security best practices
- ✅ Troubleshooting tips

The proxy handles OAuth authentication server-side and forwards requests between your browser and DataVerse.

### Quick Start with Proxy

```bash
# See PROXY_IMPLEMENTATION.md for full instructions
# Example: Deploy Azure Function
func init DataVerseProxy --typescript
func new --name DataVerseProxy --template "HTTP trigger"
# Copy implementation from PROXY_IMPLEMENTATION.md
func azure functionapp publish YOUR-FUNCTION-APP
```

Then in the app:
1. Settings → DataVerse → Enable "Use Backend Proxy"
2. Enter: `https://YOUR-FUNCTION-APP.azurewebsites.net/api/DataVerseProxy`
3. Test Connection

### Alternative: DATAVERSE_CORS.md

For detailed explanation of the CORS issue and other solution options (localStorage, user authentication), see [DATAVERSE_CORS.md](./DATAVERSE_CORS.md).

## Getting Started

1. Open Settings (gear icon in header)
2. Configure Direct Line, Azure OpenAI, and DataVerse (with proxy) credentials
3. Test each connection
4. Save configuration
5. Add test cases or load from DataVerse
6. Select test cases and run evaluation

## Development

This is a browser-based React application built with:
- React 19
- TypeScript
- Tailwind CSS
- shadcn/ui components
- Vite

## License

The Spark Template files and resources from GitHub are licensed under the terms of the MIT license, Copyright GitHub, Inc.
