# v4.0.4.6 — Platytalk: groups, TTL, backups, edit-handle, Avalonia parity

End-to-end Platytalk feature wave covering server schema + REST surface, desktop client (WPF + Avalonia), and shared crypto/backup primitives.

## Server (deployed to https://platytalk.platysoft.com)
- New tables: `conversation_settings`, `sender_keys`, `user_backups`
- New `groups` columns: `disappearing_seconds`, `invites_admin_only`, `settings_version`; `group_members.is_admin` with owner backfill
- Endpoints added/extended: `PUT /v1/me/handle`, `GET/DELETE /v1/me/devices`, full `/v1/groups` CRUD + `/settings` + `/members` + admin toggles, `/v1/conversations/:id/settings`, sender-key distribution (`POST /v1/groups/:id/sender-keys`, `GET /v1/sender-keys/pending`, `POST /v1/sender-keys/ack`), `PUT/GET/DELETE /v1/backup` + `/v1/backup/info`
- Cleanup interval purges expired messages and sender-keys >7d
- Test suite: 36/36 assertions passing

## Desktop (PlatypusTools.UI WPF + cross-platform/PlatypusTools.UI.Avalonia)
- **PlatytalkRelayClient** rewritten with full REST surface, typed WS frame parsing, and DTOs for handle/devices/groups/sender-keys/conv-settings/backup
- **PlatytalkBackupService** (NEW): Argon2id (Konscious 1.3.1, m=64MiB / t=3 / p=1) + AES-256-GCM versioned blob format, server `kdfParams` honored for forward-compat
- **PlatytalkService**: `Relay` accessor; `SetHandleAsync`, `ListMyDevicesAsync`, full `*ServerGroup*` methods, `GetConvSettings`/`SetConvSettings`, `BuildDirectConversationId`, `CaptureSnapshot`/`ApplySnapshot`
- **WPF and Avalonia** views + VMs:
  - Removed dead phone/SMS/OTP/registration scaffolding
  - Signal-style disappearing-message TTL combobox (off / 30s / 5m / 1h / 8h / 1d / 1w / 4w) bound to server settings (1:1 + groups)
  - INVITE QR rendered above invite link
  - Inline edit-handle (✎ → SAVE/CANCEL) with `409`/`400` mapped to friendly errors
  - BACKUP panel: passphrase + BACKUP / RESTORE buttons, snapshot includes contacts, conversations, groups, identity keys
  - `CreateGroupAsync` calls server group endpoint and replaces local conversation with server-side id

## Cross-platform editions
- Both Full and Media-only MSIs ship side-by-side (gated via `--edition=` flag, `edition.txt`, or HKLM registry)
- Linux portable + macOS portable + ARM variants built and uploaded

## Known deferrals (intentional, follow-up release)
- Local encrypted SQLite message store (history still in-memory between launches)
- Multi-device fan-out in send path (server-ready; client still encrypts to a single recipient identity)
- Sender-key per-message group ratchet in client (server endpoints live; group sends still use 1:1 path)
- Web client groups/devices/browser-Argon2 backup UI (handle editing already shipped; richer UI pending)

## Confidential
None of the above touches MIRCAT/CLAW or "Tier 0 Domain Block" public surface.
