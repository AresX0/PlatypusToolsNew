# PlatypusTools v4.0.4.7

## Platytalk: full Signal-feature parity wave (B2 + B3 + B4 + D)

This release closes the four messenger features that were deferred from
v4.0.4.6. With this build, the WPF, Avalonia, and web clients all use the
same encrypted-store + sender-key + multi-device + backup primitives.

### B2 — Local encrypted-at-rest message store
- New `PlatytalkMessageStore` (System.Data.SQLite, WAL mode) under
  `%LOCALAPPDATA%\PlatypusTools\Platytalk\messages.db`.
- Every message body is sealed via `LocalSecretBox` (DPAPI on Windows,
  AES-GCM keyfile elsewhere) before it ever touches disk.
- `GetMessages` now hydrates conversations from the encrypted store on
  first read; tombstones, expirations, and wipes are persisted.

### B3 — Sender-keys + per-message group ratchet
- New `PlatytalkSenderKeys` ratchet:
  `msgKey = HMAC-SHA256(chain, 0x01)`,
  `nextChain = HMAC-SHA256(chain, 0x02)`.
- Each group send advances the chain; out-of-order receivers replay the
  derivation up to the message counter.
- Initial chain key is distributed to each group member encrypted under
  the existing X3DH root key (`info: sk:{conversationId}:{generation}`).

### B4 — Multi-device fan-out
- Direct messages now ship an additional self-copy envelope tagged
  `:self` so every signed-in device of the sender sees its own outgoing
  history.
- Self-copies are detected and dropped on receive to avoid loops.

### D — Web client parity
The browser client at <https://platytalk.platysoft.com> now exposes:
- **Groups** rail: list / create / open settings (TTL combo with the
  desktop options 0/30s/5m/1h/8h/1d/1w/4w).
- **Devices** rail: list signed-in devices and revoke them.
- **Backup** rail: passphrase-protected encrypted backup / restore via
  PBKDF2-SHA256 (600 000 iterations) + AES-256-GCM. The desktop clients
  use Argon2id for the same blob; each platform restores its own format.

### Compatibility / known caveats
- Sender-key distribution currently sends the post-advance chain key;
  receivers brute-force counters 1..5 to find the correct decryption
  point. Within those bounds, no message is lost.
- Web-client backups (PBKDF2) and desktop backups (Argon2id) are
  cross-readable only on the platform that produced them.

## Build artifacts
- `PlatypusToolsSetup-v4.0.4.7.msi` — Full Windows installer.
- `PlatypusToolsMediaSetup-v4.0.4.7.msi` — Media-only Windows installer.
- `PlatypusTools-{Full,Media}-{linux-x64,linux-arm64,osx-x64,osx-arm64,win-arm64}-v4.0.4.7.{tar.gz,zip}`
  — self-contained cross-platform Avalonia builds.
