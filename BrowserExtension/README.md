# PlatypusTools Browser Extension (MV3)

Sends selections, links and downloads to your local PlatypusTools instance via the `/api/v1` REST surface (Phase 2.2).

## Install (unpacked)
1. Open `chrome://extensions` and enable Developer Mode.
2. Click "Load unpacked" and choose this folder.
3. Open the extension popup and paste:
   - **Base URL**: `https://localhost:47392` (or your remote host)
   - **API Token**: contents of `%APPDATA%/PlatypusTools/api-token.txt`
4. Click Save, then Ping to confirm.

## Endpoints used
- `POST /api/v1/clipboard/plain` — push selection / link
- `GET  /api/v1/health` — connectivity test

The extension trusts the local self-signed cert via the host_permission entry; the user must accept the cert once in the browser.
