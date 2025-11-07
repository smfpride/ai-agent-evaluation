# DataVerse CORS Issue - Troubleshooting Guide

## The Problem

When testing the DataVerse connection in this browser-based application, you'll encounter this error:

```
Access to fetch at 'https://login.microsoftonline.com/...' from origin 'https://your-app.com' 
has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present on the 
requested resource.
```

## Why This Happens

Modern web browsers enforce the **Same-Origin Policy** for security. When a web page makes a request to a different domain (cross-origin request), the browser requires the target server to explicitly allow it via CORS (Cross-Origin Resource Sharing) headers.

Microsoft's services do NOT include these headers:
- **Microsoft Entra ID** (`login.microsoftonline.com`) - OAuth token endpoint
- **DataVerse APIs** (`*.crm.dynamics.com`) - DataVerse OData endpoints

This is intentional and by design - these services are meant to be called from:
- Backend servers
- Desktop applications  
- Mobile applications
- Service-to-service scenarios

But NOT from:
- Browser-based web applications making direct calls

## Solutions

### Option 1: Backend Proxy (Recommended)

Set up a backend service that acts as a proxy between your browser app and DataVerse.

**Architecture:**
```
Browser App → Your Backend Proxy → Microsoft Entra ID (OAuth)
                ↓
Browser App ← Your Backend Proxy → DataVerse API
```

**Benefits:**
- Most secure approach
- Keeps credentials server-side
- Full control over authentication flow
- Can implement rate limiting, caching, etc.

**Implementation Options:**

1. **Azure Functions** (serverless, easy deployment)
2. **Azure API Management** (enterprise-grade, built-in features)
3. **Express.js server** (simple, flexible)
4. **ASP.NET Core API** (full-featured, Azure-native)

See the main README.md for code examples.

### Option 2: Use Browser Storage Instead of DataVerse

For development, testing, or non-enterprise use cases, you can modify the app to use browser storage (localStorage or IndexedDB) instead of DataVerse.

**Trade-offs:**
- ✅ No backend needed
- ✅ Works immediately in browser
- ❌ Data only stored locally (not shared across devices/users)
- ❌ Limited storage capacity
- ❌ No enterprise features

**Implementation:**
1. Create a new storage service in `src/lib/localstorage.ts`
2. Replace DataVerse calls with localStorage calls
3. Update the Settings dialog to remove DataVerse configuration

### Option 3: Different Authentication Method

Instead of using service principal (client credentials), use **user-delegated authentication** where the user signs in interactively. This can work with MSAL (Microsoft Authentication Library) which handles CORS properly.

**Trade-offs:**
- ✅ Works in browser without proxy
- ✅ User-based permissions
- ❌ Requires user to sign in each session
- ❌ More complex authentication flow
- ❌ Can't run automated/unattended

**Implementation:**
1. Install `@azure/msal-browser`
2. Replace service principal auth with interactive sign-in
3. Use acquired user token for DataVerse calls

## Recommended Approach by Use Case

### Production Enterprise Deployment
→ **Use Backend Proxy** (Azure Functions or API Management)

### Team Testing/QA Tool
→ **Use Backend Proxy** (simple Express.js or Azure Function)

### Personal Development/Prototyping
→ **Use Browser Storage** (localStorage with useKV hook)

### Interactive Demo/POC
→ **Use User Authentication** (MSAL interactive flow)

## Common Questions

### Q: Can I disable CORS in my browser?
**A:** Technically yes, but this is a terrible idea:
- Major security risk
- Only works on your machine
- Won't work for other users
- Not a solution for production

### Q: Can Microsoft enable CORS for my domain?
**A:** No, Microsoft does not offer CORS configuration for Entra ID or DataVerse APIs. This is by design.

### Q: Why do Direct Line and Azure OpenAI work but not DataVerse?
**A:** Some Azure services (like Azure OpenAI) DO include CORS headers because they're designed for browser use. DataVerse and Entra ID do NOT because they're designed for server-to-server communication.

### Q: Is this a bug in the app?
**A:** No, this is a limitation of browser-based applications accessing enterprise APIs. The app is correctly attempting to call the APIs, but the browser blocks it for security reasons.

## Next Steps

1. **Decide on your approach** based on your use case above
2. **For proxy approach**: Choose your proxy technology and deploy it
3. **Update the app**: Modify `src/lib/dataverse.ts` to call your proxy instead of DataVerse directly
4. **Test**: Verify connection works through the proxy

## Additional Resources

- [Understanding CORS](https://developer.mozilla.org/en-US/docs/Web/HTTP/CORS)
- [Azure Functions Proxies](https://learn.microsoft.com/en-us/azure/azure-functions/)
- [Microsoft Entra ID OAuth](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow)
- [DataVerse Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [MSAL.js Browser Library](https://github.com/AzureAD/microsoft-authentication-library-for-js)
