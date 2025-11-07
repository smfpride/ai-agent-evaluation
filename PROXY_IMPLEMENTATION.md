# DataVerse Backend Proxy - Implementation Guide

This guide provides complete implementation examples for creating a backend proxy to solve the CORS issue when accessing Microsoft Entra ID and DataVerse from a browser-based application.

## Overview

The proxy acts as an intermediary between your browser application and Microsoft's services:

```
Browser App → Backend Proxy → Microsoft Entra ID (OAuth)
             ↓
Browser App ← Backend Proxy → DataVerse API
```

The proxy receives requests from your browser, handles OAuth authentication server-side, forwards requests to DataVerse, and returns the results.

## Request/Response Format

### Request from Browser to Proxy

```typescript
POST https://your-proxy.azurewebsites.net/api/dataverse
Content-Type: application/json
X-DataVerse-Config: {JSON config}

{
  "method": "GET",
  "endpoint": "https://org.crm.dynamics.com/api/data/v9.2/mcicaps_testcases?$count=true&$top=100",
  "body": null,
  "headers": {
    "OData-MaxVersion": "4.0",
    "OData-Version": "4.0",
    "Accept": "application/json",
    "Content-Type": "application/json"
  }
}
```

### Response from Proxy to Browser

```json
{
  "success": true,
  "data": {
    "@odata.count": 150,
    "value": [
      {
        "mcicaps_testcasesid": "guid-here",
        "mcicaps_prompt": "Test prompt",
        "mcicaps_expectedresponse": "Expected response"
      }
    ]
  }
}
```

On error:

```json
{
  "success": false,
  "error": "Detailed error message"
}
```

## Implementation Options

---

## Option 1: Azure Functions (Recommended for Azure Deployments)

Azure Functions provides serverless hosting with automatic scaling and low maintenance.

### Step 1: Create Azure Function

```bash
# Install Azure Functions Core Tools
npm install -g azure-functions-core-tools@4

# Create a new function app
func init DataVerseProxy --typescript
cd DataVerseProxy

# Create the HTTP trigger function
func new --name DataVerseProxy --template "HTTP trigger"
```

### Step 2: Implement the Proxy (index.ts)

```typescript
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import { ClientSecretCredential } from "@azure/identity";

interface ProxyRequest {
  method: string;
  endpoint: string;
  body?: any;
  headers?: Record<string, string>;
}

interface DataVerseConfig {
  environmentUrl: string;
  clientId: string;
  clientSecret: string;
  tenantId: string;
}

const tokenCache = new Map<string, { token: string; expiry: number }>();

async function getAccessToken(config: DataVerseConfig): Promise<string> {
  const cacheKey = `${config.tenantId}-${config.clientId}`;
  const cached = tokenCache.get(cacheKey);

  if (cached && Date.now() < cached.expiry - 60000) {
    return cached.token;
  }

  const credential = new ClientSecretCredential(
    config.tenantId,
    config.clientId,
    config.clientSecret
  );

  const tokenResponse = await credential.getToken(`${config.environmentUrl}/.default`);

  if (!tokenResponse) {
    throw new Error("Failed to acquire access token");
  }

  tokenCache.set(cacheKey, {
    token: tokenResponse.token,
    expiry: tokenResponse.expiresOnTimestamp,
  });

  return tokenResponse.token;
}

export async function DataVerseProxy(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  context.log("DataVerse proxy request received");

  try {
    if (request.method !== "POST") {
      return {
        status: 405,
        jsonBody: { success: false, error: "Method not allowed" },
      };
    }

    const configHeader = request.headers.get("X-DataVerse-Config");
    if (!configHeader) {
      return {
        status: 400,
        jsonBody: { success: false, error: "Missing X-DataVerse-Config header" },
      };
    }

    const config: DataVerseConfig = JSON.parse(configHeader);
    const proxyRequest: ProxyRequest = await request.json();

    const accessToken = await getAccessToken(config);

    const requestOptions: RequestInit = {
      method: proxyRequest.method,
      headers: {
        ...proxyRequest.headers,
        Authorization: `Bearer ${accessToken}`,
      },
    };

    if (proxyRequest.body && proxyRequest.method !== "GET") {
      requestOptions.body = JSON.stringify(proxyRequest.body);
    }

    const response = await fetch(proxyRequest.endpoint, requestOptions);

    if (!response.ok) {
      const errorText = await response.text();
      context.error(`DataVerse API error: ${response.status} ${errorText}`);
      return {
        status: response.status,
        jsonBody: {
          success: false,
          error: `DataVerse API error: ${response.status} ${response.statusText} - ${errorText}`,
        },
      };
    }

    let data;
    const contentType = response.headers.get("content-type");
    if (contentType?.includes("application/json")) {
      data = await response.json();
    } else if (response.status === 204) {
      data = null;
    } else {
      data = await response.text();
    }

    return {
      status: 200,
      jsonBody: {
        success: true,
        data,
      },
    };
  } catch (error) {
    context.error("Proxy error:", error);
    return {
      status: 500,
      jsonBody: {
        success: false,
        error: error instanceof Error ? error.message : "Unknown error",
      },
    };
  }
}

app.http("DataVerseProxy", {
  methods: ["POST"],
  authLevel: "anonymous",
  handler: DataVerseProxy,
});
```

### Step 3: Configure and Deploy

```json
// package.json
{
  "name": "dataverse-proxy",
  "version": "1.0.0",
  "scripts": {
    "build": "tsc",
    "start": "func start",
    "deploy": "func azure functionapp publish YOUR-FUNCTION-APP-NAME"
  },
  "dependencies": {
    "@azure/functions": "^4.0.0",
    "@azure/identity": "^4.0.0"
  },
  "devDependencies": {
    "@types/node": "^20.0.0",
    "typescript": "^5.0.0"
  }
}
```

```bash
# Deploy to Azure
npm run build
func azure functionapp publish YOUR-FUNCTION-APP-NAME
```

### Step 4: Configure CORS in Azure Portal

1. Go to your Function App in Azure Portal
2. Navigate to "CORS" settings
3. Add your Spark app's origin (e.g., `https://your-app.github.io`)
4. Save the settings

---

## Option 2: Express.js Server (Simple, Flexible)

A standalone Node.js Express server that can run anywhere (Azure App Service, AWS, Heroku, etc.).

### Implementation (server.ts)

```typescript
import express, { Request, Response } from "express";
import cors from "cors";
import { ClientSecretCredential } from "@azure/identity";

interface ProxyRequest {
  method: string;
  endpoint: string;
  body?: any;
  headers?: Record<string, string>;
}

interface DataVerseConfig {
  environmentUrl: string;
  clientId: string;
  clientSecret: string;
  tenantId: string;
}

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors({
  origin: process.env.ALLOWED_ORIGINS?.split(',') || '*',
  methods: ['POST'],
  allowedHeaders: ['Content-Type', 'X-DataVerse-Config'],
}));

app.use(express.json({ limit: '10mb' }));

const tokenCache = new Map<string, { token: string; expiry: number }>();

async function getAccessToken(config: DataVerseConfig): Promise<string> {
  const cacheKey = `${config.tenantId}-${config.clientId}`;
  const cached = tokenCache.get(cacheKey);

  if (cached && Date.now() < cached.expiry - 60000) {
    return cached.token;
  }

  const credential = new ClientSecretCredential(
    config.tenantId,
    config.clientId,
    config.clientSecret
  );

  const tokenResponse = await credential.getToken(`${config.environmentUrl}/.default`);

  if (!tokenResponse) {
    throw new Error("Failed to acquire access token");
  }

  tokenCache.set(cacheKey, {
    token: tokenResponse.token,
    expiry: tokenResponse.expiresOnTimestamp,
  });

  return tokenResponse.token;
}

app.post("/api/dataverse", async (req: Request, res: Response) => {
  try {
    const configHeader = req.headers["x-dataverse-config"];
    if (!configHeader || typeof configHeader !== "string") {
      return res.status(400).json({
        success: false,
        error: "Missing X-DataVerse-Config header",
      });
    }

    const config: DataVerseConfig = JSON.parse(configHeader);
    const proxyRequest: ProxyRequest = req.body;

    if (!proxyRequest.method || !proxyRequest.endpoint) {
      return res.status(400).json({
        success: false,
        error: "Invalid proxy request format",
      });
    }

    const accessToken = await getAccessToken(config);

    const requestOptions: RequestInit = {
      method: proxyRequest.method,
      headers: {
        ...proxyRequest.headers,
        Authorization: `Bearer ${accessToken}`,
      },
    };

    if (proxyRequest.body && proxyRequest.method !== "GET") {
      requestOptions.body = JSON.stringify(proxyRequest.body);
    }

    const response = await fetch(proxyRequest.endpoint, requestOptions);

    if (!response.ok) {
      const errorText = await response.text();
      console.error(`DataVerse API error: ${response.status} ${errorText}`);
      return res.status(response.status).json({
        success: false,
        error: `DataVerse API error: ${response.status} ${response.statusText} - ${errorText}`,
      });
    }

    let data;
    const contentType = response.headers.get("content-type");
    if (contentType?.includes("application/json")) {
      data = await response.json();
    } else if (response.status === 204) {
      data = null;
    } else {
      data = await response.text();
    }

    res.json({
      success: true,
      data,
    });
  } catch (error) {
    console.error("Proxy error:", error);
    res.status(500).json({
      success: false,
      error: error instanceof Error ? error.message : "Unknown error",
    });
  }
});

app.get("/health", (req: Request, res: Response) => {
  res.json({ status: "healthy", timestamp: new Date().toISOString() });
});

app.listen(PORT, () => {
  console.log(`DataVerse proxy server running on port ${PORT}`);
});
```

### Package Configuration

```json
{
  "name": "dataverse-proxy",
  "version": "1.0.0",
  "scripts": {
    "start": "node dist/server.js",
    "dev": "ts-node-dev --respawn src/server.ts",
    "build": "tsc"
  },
  "dependencies": {
    "@azure/identity": "^4.0.0",
    "express": "^4.18.0",
    "cors": "^2.8.5"
  },
  "devDependencies": {
    "@types/express": "^4.17.0",
    "@types/cors": "^2.8.0",
    "@types/node": "^20.0.0",
    "typescript": "^5.0.0",
    "ts-node-dev": "^2.0.0"
  }
}
```

### Deployment to Azure App Service

```bash
# Build the project
npm run build

# Deploy using Azure CLI
az webapp up --name your-proxy-app --runtime "NODE:20-lts"

# Set environment variables
az webapp config appsettings set --name your-proxy-app --resource-group your-rg \
  --settings ALLOWED_ORIGINS="https://your-spark-app.github.io"
```

---

## Option 3: Azure API Management (Enterprise-Grade)

For enterprise deployments requiring rate limiting, caching, and advanced API management.

### Step 1: Create APIM Instance

```bash
az apim create --name your-apim --resource-group your-rg \
  --publisher-name "Your Company" --publisher-email admin@company.com \
  --sku-name Developer
```

### Step 2: Create Backend Policy

In Azure Portal → API Management → APIs → Add API → Blank API

**Inbound Policy:**

```xml
<policies>
  <inbound>
    <base />
    <cors>
      <allowed-origins>
        <origin>https://your-spark-app.github.io</origin>
      </allowed-origins>
      <allowed-methods>
        <method>POST</method>
        <method>OPTIONS</method>
      </allowed-methods>
      <allowed-headers>
        <header>Content-Type</header>
        <header>X-DataVerse-Config</header>
      </allowed-headers>
    </cors>
    <set-variable name="config" value="@(context.Request.Headers.GetValueOrDefault("X-DataVerse-Config", ""))" />
    <set-variable name="configJson" value="@(JsonConvert.DeserializeObject<JObject>(context.Variables.GetValueOrDefault<string>("config")))" />
    
    <authentication-certificate thumbprint="YOUR-CERT-THUMBPRINT" />
    <set-header name="Authorization" exists-action="override">
      <value>@{
        var configJson = context.Variables.GetValueOrDefault<JObject>("configJson");
        var tenantId = configJson["tenantId"].ToString();
        var clientId = configJson["clientId"].ToString();
        var clientSecret = configJson["clientSecret"].ToString();
        var envUrl = configJson["environmentUrl"].ToString();
        
        // Call OAuth endpoint to get token
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, 
          $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string> {
          { "client_id", clientId },
          { "client_secret", clientSecret },
          { "scope", $"{envUrl}/.default" },
          { "grant_type", "client_credentials" }
        });
        
        var tokenResponse = await context.SendAsync(tokenRequest, CancellationToken.None);
        var tokenJson = JObject.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return $"Bearer {tokenJson["access_token"]}";
      }</value>
    </set-header>
    
    <rate-limit calls="100" renewal-period="60" />
    <cache-lookup vary-by-developer="false" vary-by-developer-groups="false" />
  </inbound>
  
  <backend>
    <forward-request />
  </backend>
  
  <outbound>
    <base />
    <cache-store duration="60" />
  </outbound>
  
  <on-error>
    <base />
  </on-error>
</policies>
```

This approach provides enterprise features but requires APIM subscription and more complex configuration.

---

## Security Considerations

### 1. Secure Credential Storage

**Never store credentials in browser:**
```typescript
// ❌ BAD - Credentials exposed in browser
const config = {
  clientSecret: "exposed-secret"  // Visible in browser memory!
}

// ✅ GOOD - Credentials only in proxy
// Browser only sends request, proxy handles credentials
```

**For production, use Azure Key Vault:**
```typescript
import { SecretClient } from "@azure/keyvault-secrets";

const credential = new DefaultAzureCredential();
const client = new SecretClient(vaultUrl, credential);

const clientSecret = await client.getSecret("dataverse-client-secret");
```

### 2. Input Validation

```typescript
function validateConfig(config: any): DataVerseConfig {
  if (!config.environmentUrl?.match(/^https:\/\/.*\.crm\.dynamics\.com$/)) {
    throw new Error("Invalid environment URL");
  }
  if (!config.tenantId?.match(/^[a-f0-9-]{36}$/)) {
    throw new Error("Invalid tenant ID");
  }
  // Add more validation...
  return config as DataVerseConfig;
}
```

### 3. Rate Limiting

```typescript
import rateLimit from "express-rate-limit";

const limiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 100, // limit each IP to 100 requests per windowMs
});

app.use("/api/dataverse", limiter);
```

### 4. CORS Configuration

```typescript
// Strict CORS for production
app.use(cors({
  origin: [
    'https://your-spark-app.github.io',
    'https://your-custom-domain.com'
  ],
  methods: ['POST'],
  credentials: false,
  maxAge: 86400
}));
```

---

## Testing the Proxy

### Using curl

```bash
curl -X POST https://your-proxy.azurewebsites.net/api/dataverse \
  -H "Content-Type: application/json" \
  -H "X-DataVerse-Config: {\"environmentUrl\":\"https://org.crm.dynamics.com\",\"clientId\":\"your-client-id\",\"clientSecret\":\"your-secret\",\"tenantId\":\"your-tenant\"}" \
  -d '{
    "method": "GET",
    "endpoint": "https://org.crm.dynamics.com/api/data/v9.2/mcicaps_testcases?$top=1",
    "headers": {
      "OData-MaxVersion": "4.0",
      "OData-Version": "4.0",
      "Accept": "application/json"
    }
  }'
```

### Using the Spark App

1. Open Settings in your Spark app
2. Navigate to DataVerse tab
3. Enable "Use Backend Proxy"
4. Enter your proxy URL: `https://your-proxy.azurewebsites.net/api/dataverse`
5. Fill in your DataVerse credentials
6. Click "Test Connection"

---

## Troubleshooting

### Common Issues

**1. CORS errors still appear**
- Verify CORS is properly configured on your proxy
- Check that the origin matches exactly (including https://)
- Test using curl first to isolate browser issues

**2. 401 Unauthorized**
- Verify client credentials are correct
- Check that the service principal has proper permissions in DataVerse
- Ensure token scope matches environment URL

**3. 500 Internal Server Error**
- Check proxy logs for detailed error messages
- Verify the DataVerse endpoint URL is correct
- Test OAuth token acquisition separately

**4. Timeout errors**
- Increase timeout limits in proxy configuration
- Check network connectivity from proxy to DataVerse
- Consider implementing retry logic with exponential backoff

### Debug Logging

```typescript
// Add detailed logging in development
if (process.env.NODE_ENV === 'development') {
  app.use((req, res, next) => {
    console.log(`${req.method} ${req.url}`);
    console.log('Headers:', req.headers);
    console.log('Body:', req.body);
    next();
  });
}
```

---

## Cost Considerations

| Option | Estimated Monthly Cost | Best For |
|--------|----------------------|----------|
| Azure Functions (Consumption) | $0-20 | Low-moderate traffic, testing |
| Azure Functions (Premium) | $150+ | High traffic, guaranteed performance |
| Azure App Service (Basic) | $55+ | 24/7 availability, predictable load |
| Azure API Management (Developer) | $50 | Development, testing |
| Azure API Management (Standard) | $700+ | Enterprise production |

---

## Next Steps

1. Choose your deployment option based on your requirements
2. Implement the proxy using the code examples above
3. Deploy to your chosen hosting platform
4. Configure CORS and security settings
5. Test the connection using the Spark app
6. Monitor performance and errors
7. Scale as needed

For additional support, refer to:
- [Azure Functions Documentation](https://learn.microsoft.com/en-us/azure/azure-functions/)
- [DataVerse Web API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [Microsoft Identity Platform](https://learn.microsoft.com/en-us/entra/identity-platform/)
