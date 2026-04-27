# PlatypusTools v4.0.2.1 — Security Hardening Sweep

A focused security and stability release. No user-visible feature changes; existing data (vaults, settings, trusted devices) remain compatible.

## Security
- **Remote Desktop TLS pinning (Trust-On-First-Use)** — On first connect to a remote PlatypusTools server, you are prompted with the certificate's SHA-256 fingerprint. Approving pins it permanently; mismatched fingerprints on later connects are now rejected.
- **PBKDF2 modernized** — Vault key derivation moved to the new static `Rfc2898DeriveBytes.Pbkdf2` API. Iteration count, salt, and output size are unchanged; existing vaults open without re-keying.
- **X509 cert loading modernized** — `X509CertificateLoader` replaces the deprecated PKCS#12 constructor.
- **Process argument injection hardened** — All shell-out points (Bootable USB, DFIR tools, video upscaler, elevation helper) now use `ArgumentList`; long PowerShell payloads use `-EncodedCommand`.
- **URL launching whitelisted** — All `Process.Start(url)` paths route through a central `SafeProcessLauncher` that validates `http(s)` only.
- **HttpClient lifetime fixed** — Several services moved off per-call `new HttpClient(...)` to the shared `HttpClientFactory` to avoid socket exhaustion.
- **Secure RNG for password generator** — Entra ID password generation switched from `Random` to `RandomNumberGenerator.GetInt32`.
- **Remote server DoS guard** — Kestrel `MaxRequestBodySize` capped at 8 MB on the auth endpoints.
- **Startup exception observability** — async startup lambdas now log instead of swallowing.

## Reliability
- Null-safe `Dispatcher` access in `DuplicatesViewModel`.
- `CancellationToken` properly forwarded in `IntunePackagerViewModel.ReadOutputAsync`.

## Cleanup
- Removed unreferenced legacy `archive/PlatypusToolsNew-3.1.0/` tree (425 files, ~ MB).

## Compatibility
- **Existing vaults**: open as before.
- **Trusted Remote Desktop hosts**: you will be prompted to re-trust each remote on first connect after upgrade.
- **Settings, plugins, dependencies**: unchanged.

## Rollback
A full per-file change log is in `DOCS/SECURITY_AUDIT_2026-04-27_ROLLBACK.md`. Each change is independently revertable via `git checkout`.

## Build verification
- `dotnet build PlatypusTools.sln -c Release` — 0 errors.
- MSI: `PlatypusToolsSetup-v4.0.2.1.msi` (322 MB).
