# PlatypusTools v4.0.4.9

## Platytalk: sender-key protocol fix

Tightens the group sender-key distribution introduced in v4.0.4.7.

- Senders now distribute the **pre-advance** chain key (with a fixed
  counter-0 AAD) on first use of a generation. Receivers replay
  `AdvanceChain` deterministically to the message counter — no more
  brute-forcing 1..5 to recover the chain.
- This is a backwards-incompatible fix to the on-the-wire sender-key
  distribution format. Both sender and receiver must run v4.0.4.9+.
  v4.0.4.7 / v4.0.4.8 group history that has already been distributed
  will continue to decrypt because the message-key derivation itself
  is unchanged; only newly created groups (or rotated generations)
  use the new distribution format.

## Build artifacts
- `PlatypusToolsSetup-v4.0.4.9.msi` — Full Windows installer.
- `PlatypusToolsMediaSetup-v4.0.4.9.msi` — Media-only Windows installer.
- `PlatypusTools-{Full,Media}-{linux-x64,linux-arm64,osx-x64,osx-arm64,win-x64,win-arm64}-v4.0.4.9.{tar.gz,zip}`
  — self-contained cross-platform Avalonia builds.
