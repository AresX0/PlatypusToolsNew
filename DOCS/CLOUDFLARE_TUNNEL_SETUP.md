# Cloudflare Tunnel Setup Guide

Access Platypus Remote from anywhere using Cloudflare Tunnel - no port forwarding required!

## Overview

Cloudflare Tunnel creates a secure outbound connection from your PC to Cloudflare's network, allowing external access to Platypus Remote without opening ports on your router.

**Benefits:**
- No port forwarding required
- Free SSL certificate (HTTPS)
- DDoS protection
- Works behind any firewall/NAT

## Quick Start (Recommended)

### Option 1: Quick Tunnel (Easiest - No Account)

1. Open **PlatypusTools**
2. Go to **Settings → Remote Control**
3. Scroll down to **External Access (Cloudflare Tunnel)**
4. Click **📥 Install cloudflared** (first time only)
5. Check **Enable Cloudflare Tunnel**
6. Keep **Quick Tunnel** selected
7. Start the **Remote Server** first
8. Click **▶ Start Tunnel**
9. Copy the generated URL (e.g., `https://random-words.trycloudflare.com`)

**Note:** Quick Tunnel URLs change each time you restart the tunnel.

---

## Option 2: Named Tunnel (Custom Domain)

For a permanent URL like `https://platypus.yourdomain.com`:

### Prerequisites

- A domain managed by Cloudflare (free tier works)
- Cloudflare account

### Step 1: Install cloudflared

1. In PlatypusTools, go to **Settings → Remote Control**
2. Click **📥 Install cloudflared**

### Step 2: Authenticate with Cloudflare

1. Click **🔐 Setup Cloudflare Account**
2. A browser window opens to Cloudflare login
3. Log in and authorize cloudflared
4. The certificate is saved to `~\.cloudflared\cert.pem`

### Step 3: Create the Tunnel

Open PowerShell and run:

```powershell
$cf = "$env:LOCALAPPDATA\PlatypusTools\cloudflared\cloudflared.exe"
& $cf tunnel create platypus-remote
```

Save the **Tunnel ID** shown (e.g., `61937cb7-81c2-47b9-8b04-7c32661b8191`)

### Step 4: Configure DNS

**Option A: Automatic (via cloudflared)**
```powershell
& $cf tunnel route dns platypus-remote platypus.yourdomain.com
```

**Option B: Manual (in Cloudflare Dashboard)**
1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com)
2. Select your domain → **DNS**
3. Add a CNAME record:
   - **Name:** `platypus`
   - **Target:** `<tunnel-id>.cfargotunnel.com`
   - **Proxy status:** Proxied (orange cloud)

### Step 5: Create Config File

Create `~\.cloudflared\config.yml`:

```yaml
tunnel: <your-tunnel-id>
credentials-file: C:\Users\<username>\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: platypus.yourdomain.com
    service: https://localhost:47392
    originRequest:
      noTLSVerify: true
  - service: http_status:404
```

### Step 6: Run the Named Tunnel

```powershell
& $cf tunnel run platypus-remote
```

Your Platypus Remote is now accessible at `https://platypus.yourdomain.com`!

---

## Running as Windows Service (Optional)

To start the tunnel automatically on boot:

```powershell
$cf = "$env:LOCALAPPDATA\PlatypusTools\cloudflared\cloudflared.exe"
& $cf service install
```

To uninstall:
```powershell
& $cf service uninstall
```

---

## Troubleshooting

### "502 Bad Gateway"

**Cause:** The tunnel can't reach your local server.

**Solution:**
1. Make sure Remote Server is running in PlatypusTools
2. Check that port 47392 is configured correctly
3. Verify `noTLSVerify: true` in config (for self-signed certs)

### "Not Secure" Warning in Browser

**Cause:** Cloudflare SSL mode isn't configured correctly.

**Solution:**
1. Go to [Cloudflare Dashboard](https://dash.cloudflare.com)
2. Select your domain → **SSL/TLS**
3. Set encryption mode to **Full** or **Full (strict)**

### DNS Not Resolving

**Cause:** CNAME record not set up correctly.

**Solution:**
1. Wait 5 minutes for DNS propagation
2. Verify the CNAME points to `<tunnel-id>.cfargotunnel.com`
3. Make sure the proxy status is **Proxied** (orange cloud), not DNS only

### Tunnel Disconnects

**Cause:** Network instability or cloudflared crash.

**Solution:**
- Install as a Windows service for automatic restarts
- Check Windows Event Viewer for cloudflared errors

---

## Security Considerations

1. **No authentication by default** - Anyone with the URL can access your player
2. Consider adding:
   - Cloudflare Access (free for up to 50 users) - **See below**
   - Basic auth via Cloudflare Workers
   - IP restrictions in Cloudflare firewall rules

---

## Adding Microsoft Authentication (Cloudflare Access)

Require users to sign in with their Microsoft account before accessing Platypus Remote.

### Step 1: Enable Cloudflare Zero Trust

1. Go to [Cloudflare Zero Trust Dashboard](https://one.dash.cloudflare.com)
2. Sign in with your Cloudflare account
3. If prompted, create a team name (e.g., `platypus`)

### Step 2: Add Microsoft as Identity Provider

1. In Zero Trust dashboard, go to **Settings → Authentication**
2. Click **Add new** under Login methods
3. Select **Microsoft**
4. You'll need to create an Azure AD app (or use existing):

   **In Azure Portal:**
   1. Go to **App registrations → New registration**
   2. Name: `Cloudflare Access`
   3. Redirect URI: `https://<your-team-name>.cloudflareaccess.com/cdn-cgi/access/callback`
   4. Copy the **Application (client) ID**
   5. Go to **Certificates & secrets → New client secret**
   6. Copy the **secret value**

   **Back in Cloudflare:**
   1. Enter the **Client ID** and **Client Secret**
   2. For **Auth URL**: `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
   3. For **Token URL**: `https://login.microsoftonline.com/common/oauth2/v2.0/token`
   4. Click **Save**

### Step 3: Create Access Application

1. Go to **Access → Applications**
2. Click **Add an application**
3. Select **Self-hosted**
4. Configure:

| Field | Value |
|-------|-------|
| **Application name** | `Platypus Remote` |
| **Session duration** | `24 hours` (or your preference) |
| **Application domain** | `platypus.yourdomain.com` |

5. Click **Next**

### Step 4: Create Access Policy

1. **Policy name**: `Microsoft Login Required`
2. **Action**: `Allow`
3. **Configure rules**:
   - **Include**: `Login Methods` → `Microsoft`
   
   To restrict to specific emails:
   - **Include**: `Emails` → Add allowed email addresses
   
   Or restrict to your domain:
   - **Include**: `Emails ending in` → `@yourdomain.com`

4. Click **Next** then **Add application**

### Step 5: Test Authentication

1. Open `https://platypus.josephtheplatypus.com` in incognito/private window
2. You should see the Cloudflare Access login page
3. Click **Microsoft** to sign in
4. After authentication, you'll be redirected to Platypus Remote

### Managing Users

- **View active sessions**: Zero Trust → Users
- **Revoke access**: Click user → Revoke all sessions
- **Add more providers**: Settings → Authentication (Google, GitHub, etc.)

### Access Logs

Monitor who's accessing your app:
1. Go to **Logs → Access**
2. See all login attempts and active sessions

---

## File Locations

| File | Location |
|------|----------|
| cloudflared.exe | `%LOCALAPPDATA%\PlatypusTools\cloudflared\cloudflared.exe` |
| Certificate | `%USERPROFILE%\.cloudflared\cert.pem` |
| Config | `%USERPROFILE%\.cloudflared\config.yml` |
| Credentials | `%USERPROFILE%\.cloudflared\<tunnel-id>.json` |

---

## Quick Reference

| Task | Command |
|------|---------|
| List tunnels | `cloudflared tunnel list` |
| Create tunnel | `cloudflared tunnel create <name>` |
| Delete tunnel | `cloudflared tunnel delete <name>` |
| Run tunnel | `cloudflared tunnel run <name>` |
| Route DNS | `cloudflared tunnel route dns <name> <hostname>` |
| Install service | `cloudflared service install` |
| Check version | `cloudflared --version` |

---

## See Also

- [Cloudflare Tunnel Documentation](https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/)
- [Remote Control Setup](REMOTE_ACCESS_PLAN.md)
- [Entra ID Authentication](ENTRA_ID_SETUP.md)
