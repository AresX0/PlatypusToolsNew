# Platypus Remote - Entra ID Setup Guide

This guide walks you through registering a Microsoft Entra ID (Azure AD) application and configuring PlatypusTools Remote for secure OAuth 2.0 authentication.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Register the Application](#step-1-register-the-application)
3. [Configure Authentication](#step-2-configure-authentication)
4. [Configure API Permissions](#step-3-configure-api-permissions)
5. [Expose an API](#step-4-expose-an-api)
6. [Configure PlatypusTools](#step-5-configure-platypustools)
7. [Test Authentication](#step-6-test-authentication)
8. [Troubleshooting](#troubleshooting)

---

## Prerequisites

- **Microsoft 365 account** (Personal, Work, or School)
- **Azure Portal access**: https://portal.azure.com
- **PlatypusTools** installed with Remote modules built

> **Note**: You do NOT need an Azure subscription. Entra ID app registrations are free.

---

## Step 1: Register the Application

### 1.1 Open Azure Portal

1. Navigate to **https://portal.azure.com**
2. Sign in with your Microsoft account
3. Search for **"App registrations"** in the top search bar
4. Click on **App registrations** under Services

### 1.2 Create New Registration

1. Click **+ New registration**
2. Fill in the registration form:

| Field | Value |
|-------|-------|
| **Name** | `Platypus Remote` |
| **Supported account types** | Select based on your needs (see below) |
| **Redirect URI (optional)** | Leave blank for now |

#### Account Type Options:

| Option | Use Case |
|--------|----------|
| **Accounts in this organizational directory only** | Work/School accounts in your tenant only |
| **Accounts in any organizational directory** | Any Microsoft 365 work/school account |
| **Accounts in any organizational directory and personal Microsoft accounts** | Anyone with a Microsoft account (recommended for personal use) |
| **Personal Microsoft accounts only** | Consumer accounts only (outlook.com, hotmail.com, etc.) |

3. Click **Register**

### 1.3 Copy Application IDs

After registration, you'll see the app overview page. **Copy these values immediately**:

| Field | Example Value | Where to Use |
|-------|---------------|--------------|
| **Application (client) ID** | `12345678-abcd-1234-abcd-123456789abc` | Both Server and Client config |
| **Directory (tenant) ID** | `87654321-dcba-4321-dcba-987654321cba` | Server config (or use "common") |

> **Important**: Save these values in a secure location. You'll need them later.

---

## Step 2: Configure Authentication

### 2.1 Add Platform Configuration

1. In your app registration, click **Authentication** in the left menu
2. Click **+ Add a platform**
3. Select **Single-page application** (for Blazor WASM)

### 2.2 Configure Redirect URIs

Add the following redirect URIs:

```
https://localhost:47392/authentication/login-callback
https://127.0.0.1:47392/authentication/login-callback
```

If you plan to use Cloudflare Tunnel, also add:

```
https://platypus.yourdomain.com/authentication/login-callback
```

### 2.3 Configure Token Settings

Under **Implicit grant and hybrid flows**, ensure:

| Setting | Value |
|---------|-------|
| **Access tokens** | ☐ Unchecked (we use PKCE) |
| **ID tokens** | ☐ Unchecked (we use PKCE) |

Under **Advanced settings**:

| Setting | Value |
|---------|-------|
| **Allow public client flows** | Yes |

4. Click **Save**

---

## Step 3: Configure API Permissions

### 3.1 Add Microsoft Graph Permissions

1. Click **API permissions** in the left menu
2. Click **+ Add a permission**
3. Select **Microsoft Graph**
4. Select **Delegated permissions**
5. Add these permissions:

| Permission | Purpose |
|------------|---------|
| `openid` | Required for OpenID Connect |
| `profile` | Access user's name and profile picture |
| `email` | Access user's email address |
| `offline_access` | Get refresh tokens for long sessions |

6. Click **Add permissions**

### 3.2 Grant Admin Consent (Optional)

If you're an admin and want to pre-approve for all users:

1. Click **Grant admin consent for [Your Tenant]**
2. Confirm by clicking **Yes**

> **Note**: For personal accounts, users will consent on first login.

---

## Step 4: Expose an API

### 4.1 Set Application ID URI

1. Click **Expose an API** in the left menu
2. Next to **Application ID URI**, click **Set**
3. Accept the default format or customize:
   - Default: `api://12345678-abcd-1234-abcd-123456789abc`
   - Custom: `api://platypus-remote`
4. Click **Save**

### 4.2 Add a Scope

1. Click **+ Add a scope**
2. Fill in the scope details:

| Field | Value |
|-------|-------|
| **Scope name** | `access_as_user` |
| **Who can consent?** | Admins and users |
| **Admin consent display name** | `Access Platypus Remote` |
| **Admin consent description** | `Allows the app to access Platypus Remote API on behalf of the signed-in user.` |
| **User consent display name** | `Access Platypus Remote` |
| **User consent description** | `Allow the app to control your Platypus audio player.` |
| **State** | Enabled |

3. Click **Add scope**

### 4.3 Copy the Scope URI

After adding, you'll see the full scope URI:

```
api://12345678-abcd-1234-abcd-123456789abc/access_as_user
```

**Copy this value** - you'll need it for the client configuration.

---

## Step 5: Configure PlatypusTools

### 5.1 Update Server Configuration

Open `PlatypusTools.Remote.Server/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "YOUR_TENANT_ID_HERE",
    "ClientId": "YOUR_CLIENT_ID_HERE",
    "Audience": "api://YOUR_CLIENT_ID_HERE"
  },
  
  "AllowedOrigins": [
    "https://localhost:47392",
    "https://127.0.0.1:47392"
  ]
}
```

**Replace the placeholders:**

| Placeholder | Replace With | Example |
|-------------|--------------|---------|
| `YOUR_TENANT_ID_HERE` | Directory (tenant) ID from Step 1.3, or `common` for multi-tenant | `87654321-dcba-4321-dcba-987654321cba` or `common` |
| `YOUR_CLIENT_ID_HERE` | Application (client) ID from Step 1.3 | `12345678-abcd-1234-abcd-123456789abc` |

**Example completed configuration:**

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "12345678-abcd-1234-abcd-123456789abc",
    "Audience": "api://12345678-abcd-1234-abcd-123456789abc"
  }
}
```

### 5.2 Update Client Configuration

Open `PlatypusTools.Remote.Client/wwwroot/appsettings.json`:

```json
{
  "AzureAd": {
    "Authority": "https://login.microsoftonline.com/common",
    "ClientId": "YOUR_CLIENT_ID_HERE",
    "ValidateAuthority": true
  },
  "ApiBaseAddress": "https://localhost:47392"
}
```

**Replace the placeholder:**

| Placeholder | Replace With |
|-------------|--------------|
| `YOUR_CLIENT_ID_HERE` | Application (client) ID from Step 1.3 |

### 5.3 Update Scope in Program.cs

Open `PlatypusTools.Remote.Client/Program.cs` and update the scope:

Find this line:
```csharp
options.ProviderOptions.DefaultAccessTokenScopes.Add("api://YOUR_CLIENT_ID_HERE/access_as_user");
```

Replace with your actual scope URI from Step 4.3:
```csharp
options.ProviderOptions.DefaultAccessTokenScopes.Add("api://12345678-abcd-1234-abcd-123456789abc/access_as_user");
```

---

## Step 6: Test Authentication

### 6.1 Start the Server

```powershell
cd C:\Projects\PlatypusToolsNew
dotnet run --project PlatypusTools.Remote.Server
```

The server will start on `https://localhost:47392`

### 6.2 Start the Client (in a separate terminal)

```powershell
cd C:\Projects\PlatypusToolsNew
dotnet run --project PlatypusTools.Remote.Client
```

The client will start on `https://localhost:5000` or similar.

### 6.3 Test the Flow

1. Open the client URL in a browser
2. You should be redirected to Microsoft login
3. Sign in with your Microsoft account
4. Grant consent for the requested permissions
5. You should be redirected back to Platypus Remote
6. The Now Playing page should load with a "Connected" status

---

## Troubleshooting

### Common Issues

#### "AADSTS50011: The redirect URI does not match"

**Cause**: The redirect URI in your request doesn't match what's registered.

**Solution**:
1. Go to Azure Portal → App registrations → Your app → Authentication
2. Ensure the exact redirect URI is listed (including `https://`, port, and path)
3. Check for trailing slashes - they must match exactly

#### "AADSTS700016: Application not found"

**Cause**: The Client ID is incorrect.

**Solution**:
1. Verify the Client ID in both `appsettings.json` files
2. Ensure you're using the Application (client) ID, not the Object ID

#### "AADSTS65001: The user or administrator has not consented"

**Cause**: Required permissions haven't been granted.

**Solution**:
1. If using admin accounts, go to API permissions and click "Grant admin consent"
2. If using personal accounts, the consent dialog should appear on first login

#### "Failed to fetch" or CORS errors

**Cause**: The API server isn't allowing requests from the client origin.

**Solution**:
1. Verify `AllowedOrigins` in server `appsettings.json` includes your client URL
2. Ensure the server is running before starting the client

#### Certificate errors in development

**Cause**: Self-signed development certificates aren't trusted.

**Solution**:
```powershell
dotnet dev-certs https --trust
```

### Getting Help

If you encounter issues not covered here:

1. Check the browser developer console (F12) for detailed error messages
2. Check the server console output for authentication errors
3. Enable verbose logging in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.Identity": "Debug"
    }
  }
}
```

---

## Security Best Practices

### For Production Deployment

1. **Never commit secrets**: Use environment variables or Azure Key Vault
2. **Restrict redirect URIs**: Only add URIs you actually use
3. **Use specific tenant**: If possible, use your tenant ID instead of "common"
4. **Enable MFA**: Require multi-factor authentication in Conditional Access
5. **Monitor sign-ins**: Review sign-in logs in Azure Portal → Entra ID → Sign-ins
6. **Rotate secrets**: If you ever use a client secret, rotate it regularly

### Environment Variables (Recommended for Production)

Instead of hardcoding in `appsettings.json`, use environment variables:

```powershell
# Server
$env:AzureAd__TenantId = "your-tenant-id"
$env:AzureAd__ClientId = "your-client-id"

# Then run
dotnet run --project PlatypusTools.Remote.Server
```

---

## Quick Reference

### Key Configuration Values

| Value | Where to Find | Used In |
|-------|---------------|---------|
| **Client ID** | Azure Portal → App registrations → Overview | Server + Client |
| **Tenant ID** | Azure Portal → App registrations → Overview | Server (or "common") |
| **Scope** | Azure Portal → Expose an API | Client `Program.cs` |
| **Redirect URI** | Azure Portal → Authentication | Must match client URL |

### Required Files to Update

| File | Values to Set |
|------|---------------|
| `PlatypusTools.Remote.Server/appsettings.json` | TenantId, ClientId, Audience |
| `PlatypusTools.Remote.Client/wwwroot/appsettings.json` | ClientId |
| `PlatypusTools.Remote.Client/Program.cs` | Scope URI |

---

## Next Steps

After completing this setup:

1. **Test with mobile browser**: Access the client URL from your phone
2. **Install as PWA**: Use "Add to Home Screen" on iOS/Android
3. **Configure Cloudflare Tunnel** (optional): For remote access outside your network

See [REMOTE_ACCESS_PLAN.md](REMOTE_ACCESS_PLAN.md) for the full architecture documentation.
