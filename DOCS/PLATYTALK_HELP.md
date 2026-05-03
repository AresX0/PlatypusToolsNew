# Platytalk Help — Microsoft sign-in, security, and day-to-day use

Platytalk is a zero-knowledge encrypted messenger built into PlatypusTools.
Your **Microsoft account** is your identity. **Microsoft Authenticator**
handles MFA. The relay (`platytalk.platysoft.com`) only ever sees ciphertext
and routing metadata — it never has your messages, your passwords, or the
keys that decrypt your messages.

This document covers:

1. The 7-step secure-account walkthrough
2. Signing in from the desktop apps and the web client
3. Adding contacts, groups, disappearing messages, and "delete everywhere"
4. Multi-device sync and key rotation
5. Account / data wipe
6. Troubleshooting
7. Security model FAQ

---

## 1. The 7-step secure-account walkthrough

Do this **once**, before you sign in to Platytalk.

### Step 1 — Get a free Microsoft account if you don't have one

- Open <https://signup.live.com> in your browser.
- Use any email address you control (it doesn't have to be `@outlook.com` —
  Gmail, ProtonMail, your own domain all work).
- Pick a long, unique password. A passphrase like
  `correct-horse-battery-staple-2025` is fine; better, store a 24-character
  random one in PlatypusTools' Vault or another password manager.

### Step 2 — Turn on two-step verification with Microsoft Authenticator

- Install **Microsoft Authenticator** from the App Store / Google Play.
- Sign in to <https://account.microsoft.com/security> on a desktop browser.
- Click **Advanced security options → Add a new way to sign in or
  verify → Use an app**.
- Scan the QR code with Microsoft Authenticator. Confirm the test prompt.
- Enable **Two-step verification**.

### Step 3 — Set Authenticator as your primary sign-in method

- Still on the security page, choose **Change verification options** and put
  **Microsoft Authenticator** at the top of the list.
- This makes every sign-in (including Platytalk) push a notification to your
  phone. SMS and email codes become a fallback only.

### Step 4 — Add a recovery method you trust

- Add a recovery email or phone number that **you alone control**.
- Print or save the one-time recovery code to a safe place (a password
  manager, a paper safe, a sealed envelope). Without it, losing your
  authenticator phone can lock you out of Platytalk forever.

### Step 5 — Lock down sign-in surfaces

- On the security page, review **Sign-in activity** and sign out any
  sessions you don't recognize.
- Under **App passwords**, remove any you no longer use.
- Turn on **Passwordless account** if your phone supports it — the password
  becomes optional and Authenticator becomes the primary credential.

### Step 6 — Bring your device into known-good state

- Make sure Windows / macOS / Linux is patched.
- Run PlatypusTools' **System Hardening** view at least once.
- If you're on Windows, enable BitLocker on the drive that holds
  `%APPDATA%\PlatypusTools\Platytalk\` — that's where your private keys live.

### Step 7 — Sign in to Platytalk

- Open PlatypusTools → **Platytalk** view.
- Click **🔑 Sign in with Microsoft**.
- Your default browser opens `login.microsoftonline.com`.
- Approve the Authenticator push.
- The browser tab says "Signed in — you can close this tab" and Platytalk
  flips to the chat UI with your handle in the top right.

---

## 2. Signing in

### From the desktop apps (Windows WPF / Linux / macOS Avalonia)

- The right rail shows a **Sign in with Microsoft** button when you're not
  signed in.
- Clicking it spins up a tiny loopback HTTP listener on
  `http://localhost:53682/` (falling back to 53683 / 53684 if busy), opens
  your default browser at the Microsoft consent page, and waits for the
  redirect back.
- The relay never sees your Microsoft password — only the short-lived
  authorization code, which it exchanges (with its server-side secret)
  for an ID token. The relay then issues you a **Platytalk JWT** valid for
  30 days and stored in `%APPDATA%\PlatypusTools\Platytalk\session.json`.

### From the web client (`platytalk.platysoft.com`)

- Click **🔑 Sign in with Microsoft** on the home page.
- After consent, you're redirected to `/auth/callback` and then back to the
  home page with `#ptk=<token>` in the URL fragment. The web client
  stores the token in **IndexedDB** and immediately strips the fragment so
  it never hits a browser history entry.

### Signing out

- Desktop: **SIGN OUT** button on the right rail clears the JWT but keeps
  your local keys (so you can sign back in to the same identity).
- To purge keys too, use **WIPE LOCAL DATA** in the header.

---

## 3. Day-to-day use

### Adding a contact

- Type the contact's Platytalk handle (e.g. `quiet-otter-4-h7k2c`) into the
  **ADD CONTACT** field on the left rail and click **+**.
- The relay returns the contact's public identity key. The app derives a
  **safety number** — a 60-character fingerprint — that you can compare in
  person, on a phone call, or by reading aloud. If the safety numbers
  match, you have authenticated the channel.

### Starting a chat

- Select a contact, click **OPEN CHAT**. Messages are encrypted with a
  per-message key derived from a fresh ECDH share, so even if one message
  key leaks, prior and future messages stay private (forward secrecy).

### Groups

- Type a group title in **NEW GROUP** and click **GROUP**. All currently
  selected contacts are added. Each recipient gets an individually
  encrypted copy of every message — there is no group key the server can
  reuse.

### Disappearing messages

- Click **DISAPPEARING** in the chat header to toggle a 24-hour TTL. Both
  sides will purge messages locally after that window. (The relay always
  deletes once acknowledged, regardless.)

### Delete everywhere

- Click the small **DELETE EVERYWHERE** badge on any message you sent. The
  app sends a tombstone to all recipients; their clients remove the
  message from view and storage on receipt.

---

## 4. Multi-device sync and key rotation

- Each device generates its **own** ECDH/Ed25519 key pair. The relay stores
  one set of keys per `(userId, deviceId)`. Adding a second device adds a
  second recipient on every incoming message.
- To **rotate** a device's keys, click **WIPE LOCAL DATA** then sign back
  in. New keys are uploaded; old contacts will see a *safety number changed*
  badge until they re-verify.
- Lost a device? Sign in to <https://account.microsoft.com/security>, sign
  out the device's Microsoft session, then on a remaining device call
  the relay's `/v1/keys/identity` endpoint to overwrite the public keys.
  (PlatypusTools' **Sign out everywhere** menu does this for you.)

---

## 5. Account / data wipe

- **WIPE LOCAL DATA** removes the local SQLite store, key files, and JWT.
- **Delete my Platytalk account** (Settings → Platytalk) calls `DELETE
  /v1/me` on the relay, which cascades to your contacts, groups, prekeys,
  and pending messages. Your Microsoft account is **not** affected — sign
  out of Microsoft separately if you also want that gone.

---

## 6. Troubleshooting

### "Could not bind any loopback port"

Some other app is using ports 53682 – 53685. Close VPN clients, other
OAuth-using apps (gh CLI, Azure CLI), or restart the machine.

### "Sign-in returned error: invalid_client"

The relay's Entra app credentials are wrong or expired. Email
`platytalk@platysoft.com`. (For self-hosters: rotate the secret in the
Azure Portal and update `PLATYTALK_MS_CLIENT_SECRET`.)

### "Contact has no key"

The contact has signed up but has not yet completed `/v1/keys/identity`
upload (rare). Ask them to open Platytalk once on a device that's online.

### Browser opens but never returns to the app

Your firewall is blocking inbound on `localhost`. Allow `PlatypusTools.UI.exe`
through Windows Defender Firewall (Private network), or sign in via the web
client at <https://platytalk.platysoft.com>.

### "Safety number changed" warning

The contact rotated keys (new device, wiped data, or — worst case — account
takeover). **Do not send sensitive messages until you re-verify the safety
number out-of-band.**

### Lost access to your Microsoft account

If you lose Authenticator and your recovery codes, you cannot get back in.
Microsoft account recovery can take 30 days and is not guaranteed. Your
Platytalk identity is gone with it; create a new account and ask contacts
to re-add you.

---

## 7. Security model FAQ

**Q: What does the Platytalk relay actually see?**
A: Routing metadata — your Platytalk userId, recipient userIds, message
size, timestamp, and the **ciphertext blob**. It never sees plaintext, and
it never holds your private keys.

**Q: Why Microsoft sign-in instead of phone numbers like Signal?**
A: Phone numbers leak your real-world identity, are easily SIM-swapped,
and are expensive to verify. Microsoft accounts are free, support
phishing-resistant MFA out of the box, and let you rotate the underlying
email without rotating your Platytalk handle.

**Q: What crypto is used?**
A: P-256 ECDH for key agreement, AES-256-GCM for encryption, HKDF-SHA256
for key derivation, Ed25519 (or P-256 ECDSA on the web) for signatures.
Each message uses a fresh ephemeral DH share; the AAD binds the ciphertext
to `${conversationId}|${messageId}`.

**Q: Who controls the relay?**
A: The PlatypusTools project. Source for every relay endpoint is in
`platytalk/{db,auth,routes,ws}.js` so you can audit it. You can also
self-host: set `PLATYTALK_MS_CLIENT_ID`, `PLATYTALK_MS_CLIENT_SECRET`,
`PLATYTALK_REDIRECT_URI`, and `PLATYTALK_JWT_SECRET` to your own values
and point the desktop / web clients at your domain.

**Q: Is this audited?**
A: Not yet by a third party. The crypto primitives are standard and the
code is small (~600 lines for the relay, ~900 for the clients). If you're
relying on Platytalk for high-stakes communication, treat it as
**alpha-grade** and prefer Signal Desktop until a formal audit lands.

---

## See also

- [Web How-To](https://platytalk.platysoft.com/#howto)
- [Microsoft account security](https://account.microsoft.com/security)
- [Microsoft Authenticator](https://www.microsoft.com/security/mobile-authenticator-app)
