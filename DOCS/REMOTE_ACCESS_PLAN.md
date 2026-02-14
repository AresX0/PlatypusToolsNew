# Platypus Remote - Secure Web-Based Remote Access

**Version**: 2.0  
**Created**: February 12, 2026  
**Updated**: February 14, 2026 (audited)  
**Status**: ✅ Core Implemented (Entra ID OAuth, Rate Limiting, SignalR, PWA, Cloudflare Tunnel)  
**Remaining**: IP allowlist, audit logging, Tailscale documentation  
**Priority**: Security-First Design

---

## Executive Summary

**Platypus Remote** is a secure, web-based (HTML5 PWA) remote control interface for PlatypusTools. No native mobile apps - runs entirely in the browser with Progressive Web App capabilities for mobile-like experience.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Web-only (PWA)** | No app store approval, instant updates, works everywhere |
| **Entra ID OAuth** | Enterprise-grade security, SSO, MFA built-in |
| **User-owned URL** | Full control, custom domain, no vendor lock-in |
| **Port 47392** | High port, unlikely to conflict with other services |
| **TLS 1.3 mandatory** | All traffic encrypted, no exceptions |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           PLATYPUS REMOTE                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    HTTPS/TLS 1.3    ┌──────────────────────────────────┐  │
│  │   Browser    │◄──────────────────►│  PlatypusTools Desktop           │  │
│  │   (PWA)      │                     │  ┌────────────────────────────┐  │  │
│  │              │    SignalR WS       │  │  ASP.NET Core API          │  │  │
│  │  HTML5/JS    │◄──────────────────►│  │  Port 47392                │  │  │
│  │  Blazor WASM │                     │  │                            │  │  │
│  └──────────────┘                     │  │  ┌──────────────────────┐  │  │  │
│        │                              │  │  │ Entra ID OAuth      │  │  │  │
│        │ OAuth 2.0 + PKCE             │  │  │ Token Validation    │  │  │  │
│        ▼                              │  │  └──────────────────────┘  │  │  │
│  ┌──────────────┐                     │  │                            │  │  │
│  │ Microsoft    │                     │  │  ┌──────────────────────┐  │  │  │
│  │ Entra ID     │                     │  │  │ Audio Service       │  │  │  │
│  │ (Azure AD)   │                     │  │  │ Control Interface   │  │  │  │
│  └──────────────┘                     │  │  └──────────────────────┘  │  │  │
│                                       │  └────────────────────────────┘  │  │
│                                       └──────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Security Architecture

### Authentication: Microsoft Entra ID (Azure AD)

#### Why Entra ID?

| Benefit | Description |
|---------|-------------|
| **Enterprise SSO** | Use existing Microsoft/work account |
| **MFA Built-in** | Authenticator app, FIDO2, phone |
| **Conditional Access** | Block from untrusted locations |
| **No Password Storage** | Tokens only, no credentials on device |
| **Audit Logs** | Full sign-in history in Azure |
| **Free Tier** | 50,000 MAU free for personal use |

#### OAuth 2.0 + PKCE Flow

```
┌─────────────┐                                    ┌─────────────┐
│   Browser   │                                    │  Entra ID   │
│   (PWA)     │                                    │  (Azure AD) │
└──────┬──────┘                                    └──────┬──────┘
       │                                                  │
       │  1. User clicks "Sign In"                        │
       │─────────────────────────────────────────────────►│
       │     redirect to login.microsoftonline.com        │
       │     with PKCE code_challenge                     │
       │                                                  │
       │  2. User authenticates (+ MFA if enabled)        │
       │◄─────────────────────────────────────────────────│
       │     redirect back with authorization_code        │
       │                                                  │
       │  3. Exchange code for tokens                     │
       │─────────────────────────────────────────────────►│
       │     POST /oauth2/v2.0/token                      │
       │     with code_verifier (PKCE)                    │
       │                                                  │
       │  4. Receive access_token + id_token              │
       │◄─────────────────────────────────────────────────│
       │                                                  │
       │  5. Call API with Bearer token                   │
       │────────────────────────────┐                     │
       │                            ▼                     │
       │                    ┌──────────────┐              │
       │                    │ PlatypusTools│              │
       │                    │    API       │              │
       │                    └──────────────┘              │
       │  6. API validates token signature                │
       │◄───────────────────────────┘                     │
       │     (no call to Entra needed)                    │
```

#### Token Security

| Token | Purpose | Lifetime | Storage |
|-------|---------|----------|---------|
| Access Token | API authorization | 1 hour | Memory only |
| ID Token | User identity | 1 hour | Memory only |
| Refresh Token | Get new access token | 24 hours | Secure HttpOnly cookie |

#### Entra ID App Registration

```json
{
  "appId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "displayName": "Platypus Remote",
  "signInAudience": "AzureADMyOrg",  // or "PersonalMicrosoftAccount"
  "spa": {
    "redirectUris": [
      "https://localhost:47392/auth/callback",
      "https://platypus.yourdomain.com/auth/callback"
    ]
  },
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [
      {
        "value": "audio.control",
        "userConsentDisplayName": "Control audio playback",
        "userConsentDescription": "Play, pause, skip tracks"
      },
      {
        "value": "audio.read",
        "userConsentDisplayName": "View playback status",
        "userConsentDescription": "See what's playing"
      }
    ]
  }
}
```

### Transport Security

| Layer | Implementation | Notes |
|-------|----------------|-------|
| **TLS 1.3** | Kestrel with HTTPS | Mandatory, no fallback |
| **Certificate** | Let's Encrypt or self-signed | Auto-renewal with Certbot |
| **HSTS** | 1 year, includeSubDomains | Force HTTPS always |
| **CSP** | Strict Content-Security-Policy | Prevent XSS |
| **CORS** | Whitelist specific origins | No wildcards |

### Port Selection: 47392

```
Why 47392?
├── High port (> 1024) - no root required
├── Not in common port lists
├── Not used by known services
├── Memorable: 4-7-3-9-2 (easy to type)
└── Falls in dynamic/private range (49152-65535 adjacent)
```

---

## Hosting Options

### Option 1: LAN Only (Simplest, Most Secure)

```
┌────────────────────────────────────────────────────────┐
│                    Home Network                         │
│                                                         │
│  ┌─────────────────┐         ┌─────────────────┐       │
│  │  PlatypusTools  │◄───────►│  Phone Browser  │       │
│  │  Desktop        │  HTTPS  │  PWA            │       │
│  │  :47392         │         │                 │       │
│  └─────────────────┘         └─────────────────┘       │
│         │                                               │
│         │ Also accessible from:                         │
│         ▼                                               │
│  ┌─────────────────┐                                    │
│  │  Laptop Browser │  https://192.168.1.x:47392        │
│  └─────────────────┘                                    │
└────────────────────────────────────────────────────────┘

URL: https://192.168.1.x:47392 or https://desktop.local:47392
```

### Option 2: User-Owned URL with Cloudflare Tunnel (Recommended for Remote)

```
┌──────────────────────────────────────────────────────────────────────────┐
│                                                                           │
│  Desktop                   Cloudflare                      Mobile        │
│  ┌─────────────┐          ┌─────────────┐               ┌─────────────┐  │
│  │ Platypus    │──tunnel──│  Cloudflare │◄──HTTPS──────│  Browser    │  │
│  │ :47392      │          │  Edge       │               │  PWA        │  │
│  │             │          │             │               │             │  │
│  │ cloudflared │          │  tunnel.    │               │  platypus.  │  │
│  │ daemon      │          │  cfargotunnel│              │  mydomain.  │  │
│  └─────────────┘          └─────────────┘               │  com        │  │
│                                  │                      └─────────────┘  │
│  No open ports!                  ▼                                       │
│  No port forwarding!      ┌─────────────┐                               │
│                           │  Your DNS   │                               │
│                           │  (Cloudflare│                               │
│                           │   managed)  │                               │
│                           └─────────────┘                               │
└──────────────────────────────────────────────────────────────────────────┘

URL: https://platypus.yourdomain.com
```

**Benefits:**
- Your own domain (e.g., `remote.smith.family`)
- No port forwarding needed
- DDoS protection included
- Free tier available
- SSL certificate auto-managed

### Option 3: Tailscale (Zero Config Remote)

```
URL: https://desktop.tailnet-name.ts.net:47392
```

Uses WireGuard VPN mesh - only accessible to your Tailscale devices.

---

## Progressive Web App (PWA)

### Why PWA?

| Feature | Native App | PWA |
|---------|-----------|-----|
| App Store required | ✅ Yes | ❌ No |
| Install on home screen | ✅ Yes | ✅ Yes |
| Offline capable | ✅ Yes | ✅ Yes |
| Push notifications | ✅ Yes | ✅ Yes |
| Instant updates | ❌ No (review) | ✅ Yes |
| Camera/mic access | ✅ Yes | ✅ Yes |
| Works on iOS/Android | ✅ Separate apps | ✅ Single codebase |

### PWA Manifest

```json
{
  "name": "Platypus Remote",
  "short_name": "Platypus",
  "description": "Remote control for PlatypusTools",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#1a1a2e",
  "theme_color": "#00d4ff",
  "icons": [
    {
      "src": "/icons/icon-192.png",
      "sizes": "192x192",
      "type": "image/png"
    },
    {
      "src": "/icons/icon-512.png",
      "sizes": "512x512",
      "type": "image/png"
    }
  ]
}
```

### Service Worker

```javascript
// Caches UI shell for offline/fast loading
const CACHE_NAME = 'platypus-remote-v1';
const urlsToCache = [
  '/',
  '/index.html',
  '/app.js',
  '/styles.css',
  '/icons/icon-192.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(urlsToCache))
  );
});
```

---

## Technology Stack

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| **API Server** | ASP.NET Core 9 Minimal API | Native .NET, lightweight, Kestrel |
| **Web UI** | Blazor WebAssembly | C# everywhere, compiles to WASM |
| **Real-time** | SignalR | WebSocket bi-directional, auto-reconnect |
| **Auth** | Microsoft.Identity.Web | Official Entra ID library |
| **CSS** | Tailwind CSS or MudBlazor | Modern, responsive |
| **PWA** | Blazor PWA template | Service worker, manifest included |

### Project Structure

```
PlatypusTools.Remote/
├── PlatypusTools.Remote.Server/     # ASP.NET Core API
│   ├── Program.cs                   # Kestrel config, port 47392
│   ├── Controllers/
│   │   └── AudioController.cs       # REST endpoints
│   ├── Hubs/
│   │   └── RemoteHub.cs             # SignalR hub
│   ├── Auth/
│   │   └── EntraIdConfig.cs         # OAuth configuration
│   └── appsettings.json             # Entra ID settings
│
├── PlatypusTools.Remote.Client/     # Blazor WASM PWA
│   ├── wwwroot/
│   │   ├── manifest.json            # PWA manifest
│   │   ├── service-worker.js        # Offline caching
│   │   └── icons/                   # App icons
│   ├── Pages/
│   │   ├── Index.razor              # Now Playing
│   │   ├── Queue.razor              # Playlist
│   │   └── Settings.razor           # Connection settings
│   ├── Components/
│   │   ├── NowPlaying.razor         # Full-screen now playing
│   │   ├── Controls.razor           # Play/pause/skip
│   │   ├── VolumeSlider.razor       # Volume control
│   │   └── Visualizer.razor         # Optional mini-visualizer
│   └── Services/
│       ├── ApiClient.cs             # HTTP client wrapper
│       └── SignalRService.cs        # Real-time updates
│
└── PlatypusTools.Remote.Shared/     # Shared models
    └── Models/
        ├── PlaybackState.cs
        ├── TrackInfo.cs
        └── QueueItem.cs
```

---

## API Endpoints

### Authentication (Entra ID handles this)
```
# OAuth flow handled by MSAL.js in browser
# No custom auth endpoints needed - tokens validated via JWKS
```

### Audio Control
```
GET  /api/audio/status        # Current playback state
POST /api/audio/play          # Play current track
POST /api/audio/pause         # Pause playback  
POST /api/audio/next          # Next track
POST /api/audio/previous      # Previous track
POST /api/audio/seek          # Seek to position
GET  /api/audio/queue         # Get playlist
POST /api/audio/queue/{id}    # Play specific track
PUT  /api/audio/volume        # Set volume (0-100)
GET  /api/audio/art           # Album art (base64 or URL)
```

### System
```
GET  /api/system/health       # Server status (no auth required)
GET  /api/system/info         # Version, uptime
GET  /api/system/sessions     # Active sessions
DELETE /api/system/sessions/{id}  # End session
```

### SignalR Hub (/hubs/remote)
```javascript
// Client subscribes to real-time events
connection.on("PlaybackChanged", (state) => { ... });
connection.on("TrackChanged", (track) => { ... });
connection.on("VolumeChanged", (volume) => { ... });
connection.on("QueueChanged", (queue) => { ... });
connection.on("VisualizerData", (spectrum) => { ... });  // Optional
```

---

## Web UI Design

### Now Playing (Main Screen)

```
┌─────────────────────────────────────────────────────────────┐
│  ≡  Platypus Remote                              🔊 85%    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                    ┌─────────────────┐                      │
│                    │                 │                      │
│                    │   Album Art     │                      │
│                    │                 │                      │
│                    │   (400x400)     │                      │
│                    │                 │                      │
│                    └─────────────────┘                      │
│                                                             │
│                    Bohemian Rhapsody                        │
│                         Queen                               │
│               A Night at the Opera (1975)                   │
│                                                             │
│           ──●────────────────────────────  3:42 / 5:55     │
│                                                             │
│               ⏮️    ▶️    ⏭️                               │
│                      ⏸️                                     │
│                                                             │
│         🔀 Shuffle    🔁 Repeat    📋 Queue                 │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Queue View

```
┌─────────────────────────────────────────────────────────────┐
│  ←  Queue                                    Clear All  🗑️ │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ▶️ 1. Bohemian Rhapsody - Queen              5:55  ≡      │
│     ─────────────────────────────────────────────────       │
│    2. Don't Stop Me Now - Queen              3:29  ≡      │
│    3. We Will Rock You - Queen               2:02  ≡      │
│    4. Somebody to Love - Queen               4:56  ≡      │
│    5. Under Pressure - Queen & David Bowie   4:08  ≡      │
│                                                             │
│                    ─ ─ ─ ─ ─ ─ ─                           │
│                                                             │
│    6. Another One Bites the Dust - Queen     3:35  ≡      │
│    7. Killer Queen - Queen                   3:01  ≡      │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Responsive Design

| Breakpoint | Layout |
|------------|--------|
| Mobile (< 640px) | Stack, large touch targets |
| Tablet (640-1024px) | Side-by-side queue |
| Desktop (> 1024px) | Full layout with visualizer |

---

## Entra ID Setup Guide

### Step 1: Register Application in Azure Portal

1. Go to https://portal.azure.com
2. Navigate to **Microsoft Entra ID** → **App registrations**
3. Click **+ New registration**
4. Configure:
   - Name: `Platypus Remote`
   - Supported account types: Choose based on needs
     - `Single tenant` - Only your organization
     - `Personal Microsoft accounts` - Any Microsoft account
   - Redirect URI: `Single-page application (SPA)`
     - Add: `https://localhost:47392/authentication/login-callback`

### Step 2: Configure API Permissions

```
API Permissions:
├── Microsoft Graph
│   └── User.Read (delegated) - Sign in and read user profile
└── Platypus Remote API (if using custom scopes)
    ├── audio.read
    └── audio.control
```

### Step 3: Note Configuration Values

```json
// appsettings.json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id-or-common",
    "ClientId": "your-client-id-guid",
    "CallbackPath": "/authentication/login-callback"
  }
}
```

### Step 4: Personal Microsoft Account Option

For personal use without Azure subscription:
- Use `consumers` tenant: `https://login.microsoftonline.com/consumers/`
- Or mixed: `https://login.microsoftonline.com/common/`

---

## Implementation Phases

### Phase 1: API Server Foundation (Week 1)

| Task | Priority | Effort |
|------|----------|--------|
| Create `PlatypusTools.Remote.Server` project | P0 | 2h |
| Configure Kestrel for port 47392 with HTTPS | P0 | 4h |
| Add Microsoft.Identity.Web for Entra ID | P0 | 4h |
| Implement `/api/audio/*` endpoints | P0 | 6h |
| Integrate with EnhancedAudioPlayerService | P0 | 4h |
| Add SignalR hub for real-time updates | P0 | 4h |
| Add health check endpoint | P0 | 1h |
| CORS configuration for localhost dev | P0 | 1h |

### Phase 2: Blazor PWA Client (Week 2)

| Task | Priority | Effort |
|------|----------|--------|
| Create `PlatypusTools.Remote.Client` Blazor WASM | P0 | 2h |
| Configure PWA manifest and service worker | P0 | 2h |
| Implement Entra ID login flow (MSAL.js) | P0 | 6h |
| Create Now Playing component | P0 | 6h |
| Create Queue view | P0 | 4h |
| Add SignalR connection for real-time | P0 | 4h |
| Volume slider + progress bar | P0 | 4h |
| Responsive CSS (mobile-first) | P0 | 4h |

### Phase 3: Security Hardening (Week 3)

| Task | Priority | Effort |
|------|----------|--------|
| TLS 1.3 configuration with auto-cert | P0 | 4h |
| Rate limiting middleware | P0 | 2h |
| CORS lockdown for production | P0 | 2h |
| Security headers (CSP, HSTS, etc.) | P0 | 2h |
| Token validation and refresh | P0 | 4h |
| Session management | P0 | 4h |
| Activity logging | P1 | 2h |
| Input validation | P0 | 2h |

### Phase 4: Hosting & Polish (Week 4)

| Task | Priority | Effort |
|------|----------|--------|
| Cloudflare Tunnel setup wizard | P1 | 6h |
| Settings UI in main PlatypusTools app | P0 | 4h |
| Let's Encrypt integration | P1 | 4h |
| mDNS discovery for LAN | P1 | 4h |
| Final UI polish and animations | P1 | 4h |
| Documentation and help | P1 | 4h |
| Testing on iOS Safari, Android Chrome | P0 | 4h |
| Security review | P0 | 4h |

---

## Security Checklist

### Before Release

- [ ] TLS 1.3 only (disable TLS 1.2 and older)
- [ ] Entra ID tokens validated via JWKS (no local storage of secrets)
- [ ] Access tokens stored in memory only (not localStorage)
- [ ] Refresh tokens in HttpOnly secure cookies
- [ ] Rate limiting (100 req/min per user)
- [ ] Brute force protection via Entra ID
- [ ] Input validation on all API endpoints
- [ ] XSS prevention (Content-Security-Policy strict)
- [ ] CORS restricted to known origins only
- [ ] Security headers configured:
  - [ ] Strict-Transport-Security (HSTS)
  - [ ] X-Content-Type-Options: nosniff
  - [ ] X-Frame-Options: DENY
  - [ ] Referrer-Policy: strict-origin-when-cross-origin
- [ ] Audit logging for all API calls
- [ ] Session timeout enforced server-side
- [ ] No sensitive data in URLs (use POST/body)
- [ ] SignalR uses authenticated connections only
- [ ] PWA service worker doesn't cache auth tokens
- [ ] Security headers in all responses

---

## Settings UI in PlatypusTools

Add new section to Settings page:

```
┌─────────────────────────────────────────────────────────────────┐
│  Platypus Remote                                                │
├─────────────────────────────────────────────────────────────────┤
│  ☑ Enable Remote Access                                         │
│                                                                  │
│  Server Status: ● Running on port 47392                         │
│                                                                  │
│  Local URL:     https://192.168.1.100:47392                     │
│                 [📋 Copy]                                        │
│                                                                  │
│  ─────────────────────────────────────────────────────────────  │
│  Authentication                                                  │
│  ─────────────────────────────────────────────────────────────  │
│                                                                  │
│  Microsoft Entra ID (Azure AD)                                   │
│  Tenant ID:   [common                    ]                      │
│  Client ID:   [xxxxxxxx-xxxx-xxxx-xxxx-x ]  [Configure in Azure]│
│                                                                  │
│  ─────────────────────────────────────────────────────────────  │
│  Remote Access (Optional)                                        │
│  ─────────────────────────────────────────────────────────────  │
│                                                                  │
│  ☐ Enable Cloudflare Tunnel                                      │
│    Public URL: https://platypus.yourdomain.com                   │
│    [Configure Tunnel]                                            │
│                                                                  │
│  ─────────────────────────────────────────────────────────────  │
│  Active Sessions                                                 │
│  ─────────────────────────────────────────────────────────────  │
│                                                                  │
│  👤 john@example.com                                            │
│     Chrome on Windows • Last active: 2 min ago    [End Session] │
│                                                                  │
│  👤 john@example.com                                            │
│     Safari on iPhone • Last active: 1 hour ago    [End Session] │
│                                                                  │
│                                           [End All Sessions]     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Estimated Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| Phase 1 | 1 week | Secure API with Entra ID auth |
| Phase 2 | 1 week | Blazor PWA with Now Playing + Queue |
| Phase 3 | 1 week | Security hardening, production ready |
| Phase 4 | 1 week | Cloudflare Tunnel, Settings UI, polish |
| **Total** | **4 weeks** | Complete web-based remote access |

---

## Summary

### What We're Building

| Component | Technology | Description |
|-----------|-----------|-------------|
| **API Server** | ASP.NET Core 9 | REST + SignalR on port 47392 |
| **Web Client** | Blazor WASM PWA | HTML5, works on any device |
| **Authentication** | Microsoft Entra ID | OAuth 2.0 + PKCE, MFA support |
| **Hosting** | User-owned URL | Cloudflare Tunnel or LAN-only |

### Security Guarantees

| Attack | Mitigation |
|--------|------------|
| Man-in-the-Middle | TLS 1.3, certificate pinning |
| Credential theft | OAuth tokens only, no passwords |
| Brute force | Entra ID handles lockout |
| Session hijacking | Short-lived tokens, secure cookies |
| XSS | Strict CSP, no eval() |
| CSRF | SameSite cookies, CORS |

### No Native Apps Needed

- ✅ Works in any modern browser
- ✅ PWA installs to home screen like an app
- ✅ Push notifications (future)
- ✅ Offline UI shell
- ✅ Instant updates, no app store review

---

## Next Steps

1. **Approve** this plan
2. **Register** Entra ID application in Azure Portal
3. **Create** `PlatypusTools.Remote.Server` project
4. **Implement** Phase 1 API endpoints
5. **Create** `PlatypusTools.Remote.Client` Blazor PWA
6. **Deploy** and test on mobile browsers

Ready to start implementation?
