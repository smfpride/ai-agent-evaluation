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
- **Endpoint**: Your Copilot Studio Direct Line endpoint
- **Secret**: Optional authentication secret

### 2. Azure OpenAI
- **Endpoint**: Your Azure OpenAI resource endpoint
- **API Key**: Azure OpenAI API key
- **Deployment Name**: Name of your GPT deployment
- **API Version**: API version (default: 2024-02-15-preview)

### 3. DataVerse (⚠️ Requires Backend Proxy)

## Important: DataVerse CORS Limitation

**Browser-based applications cannot directly access Microsoft Entra ID or DataVerse APIs due to CORS policies.** The Microsoft authentication endpoints and DataVerse APIs do not include `Access-Control-Allow-Origin` headers, which prevents direct browser access.

### Why This Happens

When this app tries to:
1. Get an OAuth token from `login.microsoftonline.com`
2. Call DataVerse APIs at your `*.crm.dynamics.com` endpoint

The browser blocks these requests because:
- Microsoft's authentication service doesn't allow cross-origin requests
- DataVerse API doesn't allow direct browser access for security reasons

### Solution: Backend Proxy

To use DataVerse with this application, you need to set up a backend proxy that:
1. Receives requests from this browser app
2. Handles OAuth authentication with Microsoft Entra ID
3. Makes requests to DataVerse on behalf of the browser
4. Returns results back to the browser

### Example Proxy Implementation

Here's a simple Azure Function or Express.js proxy example:

#### Azure Function (Node.js)

```javascript
// function.json
{
  "bindings": [
    {
      "authLevel": "function",
      "type": "httpTrigger",
      "direction": "in",
      "name": "req",
      "methods": ["get", "post", "patch", "delete"],
      "route": "dataverse/{*path}"
    },
    {
      "type": "http",
      "direction": "out",
      "name": "res"
    }
  ]
}

// index.js
const fetch = require('node-fetch');

module.exports = async function (context, req) {
  const { path } = context.bindingData;
  const method = req.method;
  const body = req.body;
  
  // Get OAuth token
  const tokenResponse = await fetch(
    `https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        client_id: process.env.CLIENT_ID,
        client_secret: process.env.CLIENT_SECRET,
        scope: `${process.env.DATAVERSE_URL}/.default`,
        grant_type: 'client_credentials',
      }),
    }
  );
  
  const { access_token } = await tokenResponse.json();
  
  // Forward request to DataVerse
  const dataverseResponse = await fetch(
    `${process.env.DATAVERSE_URL}/api/data/v9.2/${path}`,
    {
      method,
      headers: {
        'Authorization': `Bearer ${access_token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
      body: body ? JSON.stringify(body) : undefined,
    }
  );
  
  const data = await dataverseResponse.json();
  
  context.res = {
    status: dataverseResponse.status,
    headers: {
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*', // Configure appropriately
    },
    body: data,
  };
};
```

#### Express.js Server

```javascript
const express = require('express');
const cors = require('cors');
const fetch = require('node-fetch');

const app = express();
app.use(cors());
app.use(express.json());

let cachedToken = null;
let tokenExpiry = 0;

async function getToken() {
  if (cachedToken && Date.now() < tokenExpiry - 60000) {
    return cachedToken;
  }
  
  const response = await fetch(
    `https://login.microsoftonline.com/${process.env.TENANT_ID}/oauth2/v2.0/token`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        client_id: process.env.CLIENT_ID,
        client_secret: process.env.CLIENT_SECRET,
        scope: `${process.env.DATAVERSE_URL}/.default`,
        grant_type: 'client_credentials',
      }),
    }
  );
  
  const data = await response.json();
  cachedToken = data.access_token;
  tokenExpiry = Date.now() + data.expires_in * 1000;
  
  return cachedToken;
}

app.all('/api/dataverse/*', async (req, res) => {
  const path = req.path.replace('/api/dataverse/', '');
  const token = await getToken();
  
  const dataverseResponse = await fetch(
    `${process.env.DATAVERSE_URL}/api/data/v9.2/${path}${req.url.includes('?') ? req.url.split('?')[1] : ''}`,
    {
      method: req.method,
      headers: {
        'Authorization': `Bearer ${token}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
      body: req.body ? JSON.stringify(req.body) : undefined,
    }
  );
  
  const data = await dataverseResponse.json();
  res.status(dataverseResponse.status).json(data);
});

app.listen(3000, () => console.log('Proxy running on port 3000'));
```

### Update the App to Use Proxy

Once your proxy is deployed, update the `src/lib/dataverse.ts` file to use your proxy URL instead of calling DataVerse directly:

```typescript
// In getAccessToken(), remove the token acquisition code
// In all fetch calls, replace:
const url = `${this.config.environmentUrl}/api/data/v9.2/...`
// With:
const url = `https://your-proxy.azurewebsites.net/api/dataverse/...`
```

### Alternative: Local Storage

For development and testing without DataVerse, you can modify the app to use browser localStorage instead. This won't provide enterprise-scale storage but allows testing the evaluation workflow.

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
