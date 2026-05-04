using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// High-level orchestrator for the Platytalk secure-messaging feature.
    /// Owns the local identity, contact list, conversation cache, and the
    /// background send / receive pipeline. Encryption is performed in
    /// <see cref="PlatytalkCrypto"/>; transport is delegated to
    /// <see cref="PlatytalkRelayClient"/>.
    ///
    /// Public surface kept deliberately narrow so multiple UIs (WPF, Avalonia,
    /// web) can bind to the same observable collections.
    /// </summary>
    public sealed class PlatytalkService : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly string _dataDir;
        private readonly PlatytalkRelayClient _relay;
        private readonly PlatytalkMessageStore _store;
        private readonly PlatytalkSenderKeyCache _senderKeys = new();
        private PlatytalkIdentity? _identity;
        private long _outboundCounter;

        public ObservableCollection<PlatytalkContact> Contacts { get; } = new();
        public ObservableCollection<PlatytalkConversation> Conversations { get; } = new();
        public ObservableCollection<PlatytalkDevice> LinkedDevices { get; } = new();

        // ConversationId -> ordered messages
        private readonly Dictionary<string, ObservableCollection<PlatytalkMessage>> _messagesByConv = new();

        public event EventHandler<PlatytalkMessage>? MessageReceived;
        public event EventHandler<string>? StatusChanged;

        public bool IsRegistered => _identity is { IsRegistered: true };
        public PlatytalkIdentity? Identity => _identity;

        public PlatytalkService(string? dataDir = null, PlatytalkRelayClient? relay = null)
        {
            _dataDir = dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "Platytalk");
            Directory.CreateDirectory(_dataDir);
            _relay = relay ?? new PlatytalkRelayClient();
            _relay.EnvelopeReceived += OnEnvelopeReceived;
            _relay.ConnectionStatusChanged += (_, s) => StatusChanged?.Invoke(this, s);
            _relay.WsEventReceived += OnWsEventReceived;
            _store = new PlatytalkMessageStore(_dataDir);
            TryLoadIdentity();
            TryLoadSession();
            // For sessions persisted before Handle was tracked locally, refresh from /v1/me
            // so the UI shows @handle instead of falling back to Microsoft display name.
            // Also pull contacts + pending and open the WS so a returning user sees
            // their full state without needing to sign out / back in.
            if (_identity is not null && _relay.HasBearer)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var me = await _relay.GetMeAsync().ConfigureAwait(false);
                        if (_identity is not null && !string.IsNullOrWhiteSpace(me.Handle))
                        {
                            _identity.Handle = me.Handle;
                            if (!string.IsNullOrWhiteSpace(me.DisplayName)) _identity.DisplayName = me.DisplayName;
                            PersistIdentity();
                            StatusChanged?.Invoke(this, $"Signed in as @{me.Handle}");
                        }
                        // Detect identity-key mismatch (server holds a different public
                        // key than this device's private key — we can't decrypt).
                        if (_identity is not null && _identity.IdentityKeyPublic is { Length: > 0 }
                            && !string.IsNullOrEmpty(me.IdentityKeyPublic))
                        {
                            var localPubB64 = Convert.ToBase64String(_identity.IdentityKeyPublic);
                            if (!string.Equals(me.IdentityKeyPublic, localPubB64, StringComparison.Ordinal))
                            {
                                StatusChanged?.Invoke(this,
                                    "⚠ Identity key mismatch. Restore the encrypted backup from your phone/web client (BACKUP panel) so this device can decrypt incoming messages.");
                            }
                        }
                    }
                    catch { /* offline/unauthorized — UI will recover on next sign-in */ }

                    try { await RefreshContactsAsync().ConfigureAwait(false); }
                    catch (Exception ex) { StatusChanged?.Invoke(this, "Contact refresh failed: " + ex.Message); }
                    try { await FlushPendingAsync().ConfigureAwait(false); }
                    catch (Exception ex) { StatusChanged?.Invoke(this, "Pending fetch failed: " + ex.Message); }
                    try { await _relay.ConnectAsync().ConfigureAwait(false); }
                    catch (Exception ex) { StatusChanged?.Invoke(this, "Realtime connect failed: " + ex.Message); }
                });
            }
        }

        // ---------- Identity / registration -------------------------------

        public ObservableCollection<PlatytalkMessage> GetMessages(string conversationId)
        {
            lock (_gate)
            {
                if (!_messagesByConv.TryGetValue(conversationId, out var list))
                {
                    list = new ObservableCollection<PlatytalkMessage>();
                    foreach (var m in _store.LoadMessages(conversationId)) list.Add(m);
                    _messagesByConv[conversationId] = list;
                }
                return list;
            }
        }

        public Task<RegistrationChallenge> StartRegistrationAsync(string handle, VerificationChannel channel, CancellationToken ct = default)
            => _relay.StartRegistrationAsync(handle, channel, ct);

        /// <summary>
        /// Drives "Sign in with Microsoft" via the loopback PKCE flow. After a
        /// successful sign-in, the local Platytalk identity (X25519 / Ed25519
        /// key pairs) is generated, persisted, and uploaded to the relay so
        /// other users can find this account by handle.
        /// </summary>
        public async Task<MicrosoftSignIn.SignInResult> SignInWithMicrosoftAsync(
            Action<string>? openBrowser = null, CancellationToken ct = default)
        {
            var result = await MicrosoftSignIn.SignInAsync(_relay.BaseUrl, openBrowser, ct).ConfigureAwait(false);
            _relay.SetBearer(result.Token);

            // Resolve what the server already knows about this account so we
            // don't clobber an existing identity key (which would lock other
            // devices of the same user out of decrypting historical / new
            // messages).
            MeResponse? me = null;
            try { me = await _relay.GetMeAsync(ct).ConfigureAwait(false); } catch { }

            var sameUserAsBefore = _identity is not null && _identity.UserId == result.UserId
                                   && _identity.IdentityKeyPublic is { Length: > 0 };

            byte[] idPub, idPriv, sigPub, sigPriv;
            if (sameUserAsBefore)
            {
                // Reuse local keys; we are the same user signing back in on the
                // same machine. Do NOT regenerate or re-upload.
                idPub  = _identity!.IdentityKeyPublic;
                idPriv = _identity.IdentityKeyPrivate;
                sigPub = _identity.SigningKeyPublic;
                sigPriv = _identity.SigningKeyPrivate;
            }
            else
            {
                (idPub, idPriv) = PlatytalkCrypto.GenerateKeyExchangeKeyPair();
                (sigPub, sigPriv) = PlatytalkCrypto.GenerateSigningKeyPair();
            }

            _identity = new PlatytalkIdentity
            {
                UserId = result.UserId,
                DeviceId = _identity?.DeviceId ?? Guid.NewGuid().ToString("N"),
                DisplayName = string.IsNullOrWhiteSpace(result.DisplayName) ? result.Handle : result.DisplayName,
                Handle = result.Handle ?? me?.Handle ?? string.Empty,
                IdentityKeyPublic = idPub,
                IdentityKeyPrivate = idPriv,
                SigningKeyPublic = sigPub,
                SigningKeyPrivate = sigPriv,
                Email = null,
                PhoneE164 = null,
                IsRegistered = true,
                MfaEnabled = true,
            };
            PersistIdentity();
            PersistSession(result.Token);

            // Only upload identity keys if the server has none yet for this user.
            // Otherwise another device already published a key — overwriting it
            // would brick that device's incoming messages.
            var serverHasKey = me is not null && !string.IsNullOrEmpty(me.IdentityKeyPublic);
            if (!serverHasKey)
            {
                try { await _relay.UploadIdentityKeysAsync(idPub, sigPub, ct).ConfigureAwait(false); }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Key upload failed: " + ex.Message); }
            }
            else
            {
                // Compare server's identity key with what we have locally. If they
                // differ for the same user, this device's private key cannot decrypt
                // anything other devices send (they encrypt to the server-stored
                // public key) — only restoring the encrypted backup from a device
                // that owns the matching private key will fix it.
                var localPubB64 = Convert.ToBase64String(idPub);
                if (!string.Equals(me!.IdentityKeyPublic, localPubB64, StringComparison.Ordinal))
                {
                    StatusChanged?.Invoke(this,
                        "⚠ Identity key mismatch. Your phone/web client holds the active private key. Use the BACKUP panel on that device → Backup, then on this device → Restore. Or click 'Reset Identity' to take over (will brick your other devices).");
                }
                else if (!sameUserAsBefore)
                {
                    StatusChanged?.Invoke(this,
                        "Signed in. Restore your encrypted backup from your other device to receive messages from existing contacts.");
                }
            }

            StatusChanged?.Invoke(this, $"Signed in as @{_identity.Handle}");

            // Pull initial state in the background so the UI is populated even
            // before the WebSocket connects (and so queued messages flush).
            _ = Task.Run(async () =>
            {
                try { await RefreshContactsAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Contact refresh failed: " + ex.Message); }
                try { await FlushPendingAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Pending fetch failed: " + ex.Message); }
                try { await _relay.ConnectAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Realtime connect failed: " + ex.Message); }
            }, CancellationToken.None);

            return result;
        }

        /// <summary>Pulls the server-side contact list and merges it into
        /// <see cref="Contacts"/>. Safe to call repeatedly.</summary>
        public async Task RefreshContactsAsync(CancellationToken ct = default)
        {
            EnsureIdentity();
            var hits = await _relay.ListContactsAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var hit in hits)
                {
                    if (Contacts.Any(c => c.ContactId == hit.ContactId)) continue;
                    if (string.IsNullOrEmpty(hit.IdentityKeyPublicBase64)) continue;
                    byte[] pub;
                    try { pub = Convert.FromBase64String(hit.IdentityKeyPublicBase64); }
                    catch { continue; }
                    var c = new PlatytalkContact
                    {
                        ContactId = hit.ContactId,
                        DisplayName = string.IsNullOrEmpty(hit.DisplayName) ? hit.ContactId : hit.DisplayName,
                        IdentityKeyPublic = pub,
                    };
                    c.SafetyNumber = PlatytalkCrypto.ComputeSafetyNumber(_identity!.IdentityKeyPublic, pub);
                    Contacts.Add(c);
                }
            }
        }

        /// <summary>Fetches every pending envelope (queued while we were
        /// offline) and replays them through the normal receive pipeline.</summary>
        public async Task FlushPendingAsync(CancellationToken ct = default)
        {
            EnsureIdentity();
            var envs = await _relay.FetchPendingAsync(ct).ConfigureAwait(false);
            if (envs.Length == 0) return;
            var ackIds = new List<string>(envs.Length);
            foreach (var env in envs)
            {
                try
                {
                    OnEnvelopeReceived(this, env);
                    ackIds.Add(env.MessageId);
                }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Pending decrypt failed: " + ex.Message); }
            }
            if (ackIds.Count > 0)
                try { await _relay.AckMessagesAsync(ackIds, ct).ConfigureAwait(false); } catch { }
        }


        /// <summary>Signs the current device out of the relay (token is forgotten,
        /// keys remain locally). To purge keys too, call <see cref="WipeLocalData"/>.</summary>
        public void SignOut()
        {
            _relay.ClearBearer();
            try
            {
                var sessionPath = Path.Combine(_dataDir, "session.bin");
                if (File.Exists(sessionPath)) File.Delete(sessionPath);
            }
            catch { /* best effort */ }
            _identity = null;
            StatusChanged?.Invoke(this, "Signed out");
        }

        public async Task CompleteRegistrationAsync(RegistrationChallenge challenge, string code, string displayName, string deviceName, CancellationToken ct = default)
        {
            var (idPub, idPriv) = PlatytalkCrypto.GenerateKeyExchangeKeyPair();
            var (sigPub, sigPriv) = PlatytalkCrypto.GenerateSigningKeyPair();
            var auth = await _relay.CompleteRegistrationAsync(challenge.ChallengeId, code, idPub, sigPub, deviceName, ct).ConfigureAwait(false);
            _relay.SetBearer(auth.Token);

            _identity = new PlatytalkIdentity
            {
                UserId = auth.UserId,
                DeviceId = auth.DeviceId,
                DisplayName = displayName,
                IdentityKeyPublic = idPub,
                IdentityKeyPrivate = idPriv,
                SigningKeyPublic = sigPub,
                SigningKeyPrivate = sigPriv,
                PhoneE164 = challenge.Channel == VerificationChannel.Sms ? challenge.Handle : null,
                Email = challenge.Channel == VerificationChannel.Email ? challenge.Handle : null,
                IsRegistered = true,
                MfaEnabled = true,
            };
            PersistIdentity();
            StatusChanged?.Invoke(this, "Registered");
        }

        // ---------- Contacts ----------------------------------------------

        public async Task<PlatytalkContact?> AddContactByHandleAsync(string handle, CancellationToken ct = default)
        {
            EnsureIdentity();
            var clean = handle.Trim().TrimStart('@');
            // Server does the mutual-add and broadcasts contactAdded over WS.
            try { await _relay.AddContactAsync(clean, ct).ConfigureAwait(false); }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Add contact failed: " + ex.Message); return null; }

            // Look up the contact's keys so we can encrypt to them immediately.
            var hits = await _relay.FindContactAsync(clean, ct).ConfigureAwait(false);
            var hit = hits.FirstOrDefault();
            if (hit is null || string.IsNullOrEmpty(hit.IdentityKeyPublicBase64)) return null;
            var contact = new PlatytalkContact
            {
                ContactId = hit.ContactId,
                DisplayName = string.IsNullOrEmpty(hit.DisplayName) ? clean : hit.DisplayName,
                IdentityKeyPublic = Convert.FromBase64String(hit.IdentityKeyPublicBase64),
                PhoneE164 = clean.StartsWith('+') ? clean : null,
                Email = clean.Contains('@') ? clean : null,
            };
            contact.SafetyNumber = PlatytalkCrypto.ComputeSafetyNumber(_identity!.IdentityKeyPublic, contact.IdentityKeyPublic);
            lock (_gate)
            {
                if (!Contacts.Any(c => c.ContactId == contact.ContactId))
                    Contacts.Add(contact);
            }
            return contact;
        }

        /// <summary>Builds a deep-link / QR payload that recipients can scan
        /// to add this user as a contact.</summary>
        public string BuildInviteLink()
        {
            EnsureIdentity();
            var payload = new
            {
                v = 1,
                u = _identity!.UserId,
                d = _identity.DisplayName,
                k = Convert.ToBase64String(_identity.IdentityKeyPublic),
            };
            var b64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));
            return $"https://platytalk.platysoft.com/i/{b64}";
        }

        // ---------- Conversations / messaging -----------------------------

        public PlatytalkConversation StartDirectConversation(PlatytalkContact contact)
        {
            EnsureIdentity();
            lock (_gate)
            {
                var existing = Conversations.FirstOrDefault(c => c.Kind == ConversationKind.Direct && c.ParticipantIds.Contains(contact.ContactId));
                if (existing is not null) return existing;
                var conv = new PlatytalkConversation
                {
                    ConversationId = Guid.NewGuid().ToString("N"),
                    Kind = ConversationKind.Direct,
                    Title = contact.DisplayName,
                };
                conv.ParticipantIds.Add(_identity!.UserId);
                conv.ParticipantIds.Add(contact.ContactId);
                Conversations.Add(conv);
                return conv;
            }
        }

        public PlatytalkConversation CreateGroup(string title, IEnumerable<PlatytalkContact> members)
        {
            EnsureIdentity();
            var conv = new PlatytalkConversation
            {
                ConversationId = Guid.NewGuid().ToString("N"),
                Kind = ConversationKind.Group,
                Title = title,
            };
            conv.ParticipantIds.Add(_identity!.UserId);
            foreach (var m in members) conv.ParticipantIds.Add(m.ContactId);
            lock (_gate) Conversations.Add(conv);
            return conv;
        }

        public void SetDisappearing(PlatytalkConversation conv, TimeSpan? after)
        {
            conv.DisappearingAfter = after;
        }

        public async Task SendMessageAsync(PlatytalkConversation conv, string body, CancellationToken ct = default)
        {
            EnsureIdentity();
            var msg = new PlatytalkMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ConversationId = conv.ConversationId,
                SenderId = _identity!.UserId,
                Direction = MessageDirection.Outgoing,
                Body = body,
            };
            if (conv.DisappearingAfter is { } ttl) msg.ExpiresUtc = DateTime.UtcNow.Add(ttl);
            GetMessages(conv.ConversationId).Add(msg);
            _store.UpsertMessage(msg);

            try
            {
                if (conv.Kind == ConversationKind.Group)
                    await SendGroupMessageAsync(conv, msg, body, ct).ConfigureAwait(false);
                else
                    await SendDirectMessageAsync(conv, msg, body, ct).ConfigureAwait(false);
                msg.Status = MessageStatus.Sent;
                _store.UpsertMessage(msg);
                conv.LastMessagePreview = body;
                conv.LastActivityUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                msg.Status = MessageStatus.Failed;
                _store.UpsertMessage(msg);
                StatusChanged?.Invoke(this, "Send failed: " + ex.Message);
            }
        }

        private async Task SendDirectMessageAsync(PlatytalkConversation conv, PlatytalkMessage msg, string body, CancellationToken ct)
        {
            // Web/mobile-compatible 1:1 protocol.
            //   contextInfo = "{conversationId}|{messageId}"
            //   AES key     = HKDF-SHA256( ECDH(eph, peerIdPub), salt="platytalk/v1", info=contextInfo )
            //   wire        = iv(12) || AES-GCM ct||tag, base64 in cipherBlob
            //   send body   = { messageId, conversationId, recipients:[{userId,cipherBlob,ephemeralPublic}],
            //                   cipherBlob, ephemeralPublic, counter:0 }
            // Fan-out: one /messages/send call per recipient (matches web client).
            var recipientIds = conv.ParticipantIds.Where(p => p != _identity!.UserId).Distinct().ToList();
            if (recipientIds.Count == 0) return;
            var ctx = $"{conv.ConversationId}|{msg.MessageId}";
            foreach (var recipientId in recipientIds)
            {
                var contact = Contacts.FirstOrDefault(c => c.ContactId == recipientId);
                if (contact is null)
                {
                    // Try to look them up so we can still send.
                    try
                    {
                        var hit = (await _relay.FindContactAsync(recipientId, ct).ConfigureAwait(false)).FirstOrDefault();
                        if (hit is null || string.IsNullOrEmpty(hit.IdentityKeyPublicBase64)) continue;
                        contact = new PlatytalkContact
                        {
                            ContactId = hit.ContactId,
                            DisplayName = hit.DisplayName,
                            IdentityKeyPublic = Convert.FromBase64String(hit.IdentityKeyPublicBase64),
                        };
                        lock (_gate) Contacts.Add(contact);
                    }
                    catch { continue; }
                }

                var (cipherBlob, ephPub) = PlatytalkCrypto.EncryptToPeerWeb(body, contact.IdentityKeyPublic, ctx);
                var env = new RelayEnvelope
                {
                    MessageId = msg.MessageId,
                    ConversationId = conv.ConversationId,
                    SenderId = _identity!.UserId,
                    CipherBlob = cipherBlob,
                    EphemeralPublic = ephPub,
                    Counter = 0,
                    Recipients = new[]
                    {
                        new RelayRecipient { UserId = recipientId, CipherBlob = cipherBlob, EphemeralPublic = ephPub }
                    },
                };
                await _relay.SendEnvelopeAsync(env, ct).ConfigureAwait(false);
            }
        }

        private async Task SendGroupMessageAsync(PlatytalkConversation conv, PlatytalkMessage msg, string body, CancellationToken ct)
        {
            // B3 — sender-key per-message ratchet for groups.
            // Generation = settings_version-style monotonic; we use 1 for v1 and rotate
            // when membership changes (caller responsibility).
            const long generation = 1L;
            var state = _senderKeys.GetOrCreateOutgoing(conv.ConversationId, _identity!.DeviceId, generation);
            var firstUseInProcess = state.Counter == 0;
            // Snapshot the pre-advance chain key for distribution on first use; this
            // way receivers can derive every message in this generation from counter 1.
            var initialChainForDistribution = firstUseInProcess ? (byte[])state.ChainKey.Clone() : null;
            state.Counter += 1;
            var (msgKey, nextCk) = PlatytalkSenderKeys.AdvanceChain(state.ChainKey);
            state.ChainKey = nextCk;

            var aad = Encoding.UTF8.GetBytes($"{_identity.UserId}|{conv.ConversationId}|{generation}|{state.Counter}");
            var ciphertext = PlatytalkCrypto.EncryptMessage(msgKey, Encoding.UTF8.GetBytes(body), aad, out _);
            var env = new RelayEnvelope
            {
                MessageId = msg.MessageId,
                ConversationId = conv.ConversationId,
                SenderId = _identity.UserId,
                RecipientIds = conv.ParticipantIds.Where(p => p != _identity.UserId).ToArray(),
                CipherBlobBase64 = Convert.ToBase64String(ciphertext),
                EphemeralPublicBase64 = string.Empty,
                Counter = state.Counter,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Headers = new Dictionary<string, string>
                {
                    ["groupId"] = conv.ConversationId,
                    ["generation"] = generation.ToString(),
                    ["senderDeviceId"] = _identity.DeviceId,
                },
            };
            await _relay.SendEnvelopeAsync(env, ct).ConfigureAwait(false);

            // Distribute the *initial* chain key to every recipient on first use of
            // this generation. Each distribution is encrypted under the recipient's
            // identity key using the same X3DH-lite root scheme as 1:1 messages.
            if (firstUseInProcess && initialChainForDistribution is not null)
            {
                try
                {
                    var dists = new List<SenderKeyDistribution>();
                    foreach (var recipientId in env.RecipientIds)
                    {
                        var contact = Contacts.FirstOrDefault(c => c.ContactId == recipientId);
                        if (contact is null) continue;
                        var (ePub, ePriv) = PlatytalkCrypto.GenerateKeyExchangeKeyPair();
                        var root = PlatytalkCrypto.DeriveRootKey(
                            _identity.IdentityKeyPrivate, contact.IdentityKeyPublic,
                            ePriv, contact.IdentityKeyPublic,
                            info: $"sk:{conv.ConversationId}:{generation}");
                        // Distribution AAD uses fixed counter 0 ("initial chain"); the
                        // chain key inside is pre-advance so the receiver derives msg N
                        // by replaying AdvanceChain N times.
                        var aadDist = Encoding.UTF8.GetBytes($"sk|{conv.ConversationId}|{generation}|{_identity.DeviceId}|0");
                        var sealed_ = PlatytalkCrypto.EncryptMessage(root, initialChainForDistribution, aadDist, out _);
                        dists.Add(new SenderKeyDistribution
                        {
                            RecipientUserId = recipientId,
                            RecipientDeviceId = recipientId,
                            CipherBlob = Convert.ToBase64String(sealed_),
                            EphemeralPublic = Convert.ToBase64String(ePub),
                        });
                    }
                    if (dists.Count > 0)
                        await _relay.PostSenderKeyDistributionsAsync(conv.ConversationId, _identity.DeviceId, generation, dists, ct).ConfigureAwait(false);
                }
                catch (Exception ex) { StatusChanged?.Invoke(this, "Sender-key fan-out failed: " + ex.Message); }
            }
        }

        /// <summary>Removes a message locally AND on every device it was sent
        /// to by publishing a tombstone envelope.</summary>
        public async Task DeleteMessageEverywhereAsync(PlatytalkMessage msg, CancellationToken ct = default)
        {
            EnsureIdentity();
            msg.IsTombstoned = true;
            msg.Body = string.Empty;
            msg.Status = MessageStatus.RemotelyDeleted;
            _store.Tombstone(msg.MessageId);
            try
            {
                var conv = Conversations.FirstOrDefault(c => c.ConversationId == msg.ConversationId);
                var recips = conv?.ParticipantIds ?? new List<string> { msg.SenderId };
                await _relay.DeleteRemoteAsync(msg.MessageId, msg.ConversationId, recips, ct).ConfigureAwait(false);
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Tombstone failed: " + ex.Message); }
        }

        /// <summary>Wipes every byte of Platytalk data on this device.</summary>
        public void WipeLocalData()
        {
            lock (_gate)
            {
                Contacts.Clear();
                Conversations.Clear();
                _messagesByConv.Clear();
                LinkedDevices.Clear();
                _identity = null;
                _senderKeys.Clear();
                try { _store.Wipe(); } catch { }
                try { if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true); } catch { }
                Directory.CreateDirectory(_dataDir);
                StatusChanged?.Invoke(this, "Local data wiped");
            }
        }

        // ---------- Background pipeline -----------------------------------

        public Task ConnectAsync(CancellationToken ct = default) => _relay.ConnectAsync(ct);

        private void OnEnvelopeReceived(object? sender, RelayEnvelope env)
        {
            if (_identity is null) return;
            try
            {
                if (env.IsTombstone)
                {
                    var list = GetMessages(env.ConversationId);
                    var existing = list.FirstOrDefault(m => m.MessageId == env.MessageId);
                    if (existing is not null)
                    {
                        existing.IsTombstoned = true;
                        existing.Body = string.Empty;
                        existing.Status = MessageStatus.RemotelyDeleted;
                    }
                    _store.Tombstone(env.MessageId);
                    return;
                }

                // Self-copy envelope (B4): the sender is me. Don't add a duplicate
                // outgoing message; the local copy is already present.
                if (env.SenderId == _identity.UserId && env.MessageId.EndsWith(":self", StringComparison.Ordinal))
                    return;

                byte[] plain;
                if (env.Headers != null && env.Headers.TryGetValue("groupId", out var gid) && !string.IsNullOrEmpty(gid))
                {
                    // B3 \u2014 group sender-key path.
                    var generation = long.TryParse(env.Headers.GetValueOrDefault("generation"), out var g) ? g : 1L;
                    var senderDevice = env.Headers.GetValueOrDefault("senderDeviceId") ?? env.SenderId;
                    var state = _senderKeys.FindIncoming(gid, env.SenderId, senderDevice, generation);
                    if (state is null)
                    {
                        // No distribution received yet \u2014 try to fetch and cache.
                        _ = TryConsumePendingSenderKeysAsync();
                        StatusChanged?.Invoke(this, "Group message awaiting sender-key distribution.");
                        return;
                    }
                    var msgKey = PlatytalkSenderKeys.DeriveMessageKeyAt(state.ChainKey, env.Counter - 1);
                    var aad = Encoding.UTF8.GetBytes($"{env.SenderId}|{env.ConversationId}|{generation}|{env.Counter}");
                    var blob = Convert.FromBase64String(env.CipherBlobBase64);
                    plain = PlatytalkCrypto.DecryptMessage(msgKey, blob, aad);
                }
                else
                {
                    // 1:1 web/mobile-compatible decrypt path.
                    //   contextInfo = "{conversationId}|{messageId}"
                    //   AES key     = HKDF-SHA256( ECDH(myIdPriv, env.ephemeralPublic),
                    //                              salt="platytalk/v1", info=contextInfo )
                    var contact = Contacts.FirstOrDefault(c => c.ContactId == env.SenderId);
                    if (contact is null && env.SenderId != _identity.UserId)
                    {
                        // Unknown sender — try to resolve their record so we can label the message,
                        // but decrypt doesn't actually need the sender's identity key (web protocol
                        // uses ephemeral × my identity).
                        try
                        {
                            var hit = (_relay.FindContactAsync(env.SenderId).GetAwaiter().GetResult()).FirstOrDefault();
                            if (hit is not null && !string.IsNullOrEmpty(hit.IdentityKeyPublicBase64))
                            {
                                contact = new PlatytalkContact
                                {
                                    ContactId = hit.ContactId,
                                    DisplayName = string.IsNullOrEmpty(hit.DisplayName) ? hit.ContactId : hit.DisplayName,
                                    IdentityKeyPublic = Convert.FromBase64String(hit.IdentityKeyPublicBase64),
                                };
                                lock (_gate) Contacts.Add(contact);
                            }
                        }
                        catch { /* not fatal */ }
                    }
                    var ctx = $"{env.ConversationId}|{env.MessageId}";
                    var text = PlatytalkCrypto.DecryptFromPeerWeb(
                        env.CipherBlob, env.EphemeralPublic, _identity.IdentityKeyPrivate, ctx);
                    plain = Encoding.UTF8.GetBytes(text);
                }

                var msg = new PlatytalkMessage
                {
                    MessageId = env.MessageId,
                    ConversationId = env.ConversationId,
                    SenderId = env.SenderId,
                    Direction = env.SenderId == _identity.UserId ? MessageDirection.Outgoing : MessageDirection.Incoming,
                    Body = Encoding.UTF8.GetString(plain),
                    Status = MessageStatus.Delivered,
                };
                GetMessages(env.ConversationId).Add(msg);
                _store.UpsertMessage(msg);
                EnsureConversationFor(env, msg);
                MessageReceived?.Invoke(this, msg);
            }
            catch (CryptographicException ex)
            {
                StatusChanged?.Invoke(this, "Decrypt failed: " + ex.Message);
            }
        }

        /// <summary>Polls the relay for pending sender-key distributions
        /// addressed to this device, decrypts them with our identity key, and
        /// installs the chain key for future group-message decryption.</summary>
        public async Task TryConsumePendingSenderKeysAsync(CancellationToken ct = default)
        {
            if (_identity is null) return;
            try
            {
                var resp = await _relay.GetPendingSenderKeysAsync(_identity.DeviceId, ct).ConfigureAwait(false);
                if (resp?.Distributions is null || resp.Distributions.Count == 0) return;
                var ackIds = new List<long>();
                foreach (var d in resp.Distributions)
                {
                    try
                    {
                        var senderContact = Contacts.FirstOrDefault(c => c.ContactId == d.SenderUserId);
                        if (senderContact is null) continue;
                        var ePub = Convert.FromBase64String(d.EphemeralPublic);
                        var root = PlatytalkCrypto.DeriveRootKey(
                            _identity.IdentityKeyPrivate, senderContact.IdentityKeyPublic,
                            _identity.IdentityKeyPrivate, ePub,
                            info: $"sk:{d.GroupId}:{d.Generation}");
                        // Distributions are sealed with fixed counter 0 in their AAD;
                        // the chain inside is pre-advance, so the receiver replays
                        // AdvanceChain to reach any message in this generation.
                        var blob = Convert.FromBase64String(d.CipherBlob);
                        byte[]? chain = null;
                        try
                        {
                            var aad = Encoding.UTF8.GetBytes($"sk|{d.GroupId}|{d.Generation}|{d.SenderDeviceId}|0");
                            chain = PlatytalkCrypto.DecryptMessage(root, blob, aad);
                        }
                        catch (CryptographicException) { /* leave null */ }
                        if (chain is null) continue;
                        _senderKeys.StoreIncoming(d.GroupId, d.SenderUserId, d.SenderDeviceId, d.Generation, chain);
                        ackIds.Add(d.Id);
                    }
                    catch { /* continue with next distribution */ }
                }
                if (ackIds.Count > 0) await _relay.AckSenderKeysAsync(ackIds, ct).ConfigureAwait(false);
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Sender-key fetch failed: " + ex.Message); }
        }

        // ---------- Persistence -------------------------------------------

        private void PersistIdentity()
        {
            if (_identity is null) return;
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(_identity);
                var sealed_ = LocalSecretBox.Protect(json, _dataDir);
                CryptographicOperations.ZeroMemory(json);
                File.WriteAllBytes(Path.Combine(_dataDir, "identity.bin"), sealed_);
                // Migration: remove legacy plaintext file if present.
                var legacy = Path.Combine(_dataDir, "identity.json");
                try { if (File.Exists(legacy)) File.Delete(legacy); } catch { }
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Persist failed: " + ex.Message); }
        }

        private void TryLoadIdentity()
        {
            try
            {
                var encPath = Path.Combine(_dataDir, "identity.bin");
                if (File.Exists(encPath))
                {
                    var sealed_ = File.ReadAllBytes(encPath);
                    var plain = LocalSecretBox.Unprotect(sealed_, _dataDir);
                    _identity = JsonSerializer.Deserialize<PlatytalkIdentity>(plain);
                    CryptographicOperations.ZeroMemory(plain);
                    return;
                }
                // Best-effort migration from any legacy plaintext file.
                var legacy = Path.Combine(_dataDir, "identity.json");
                if (File.Exists(legacy))
                {
                    var json = File.ReadAllBytes(legacy);
                    _identity = JsonSerializer.Deserialize<PlatytalkIdentity>(json);
                    PersistIdentity();
                    try { File.Delete(legacy); } catch { }
                }
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Identity load failed: " + ex.Message); }
        }

        private void PersistSession(string token)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(token);
                var sealed_ = LocalSecretBox.Protect(bytes, _dataDir);
                CryptographicOperations.ZeroMemory(bytes);
                File.WriteAllBytes(Path.Combine(_dataDir, "session.bin"), sealed_);
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Session persist failed: " + ex.Message); }
        }

        private void TryLoadSession()
        {
            try
            {
                var path = Path.Combine(_dataDir, "session.bin");
                if (!File.Exists(path)) return;
                var sealed_ = File.ReadAllBytes(path);
                var plain = LocalSecretBox.Unprotect(sealed_, _dataDir);
                var token = Encoding.UTF8.GetString(plain);
                CryptographicOperations.ZeroMemory(plain);
                if (!string.IsNullOrWhiteSpace(token))
                    _relay.SetBearer(token);
            }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Session load failed: " + ex.Message); }
        }

        private void EnsureIdentity()
        {
            if (_identity is null) throw new InvalidOperationException("Platytalk identity has not been registered.");
        }

        // ---------- Server-backed groups / TTLs / devices / backup -------

        /// <summary>Exposes the underlying relay for screens (e.g. ViewModels)
        /// that want to call new endpoints directly.</summary>
        public PlatytalkRelayClient Relay => _relay;

        public Task<DeviceListResponse> ListMyDevicesAsync(CancellationToken ct = default) => _relay.ListMyDevicesAsync(ct);

        // Mirror server's profileUpdated event into local identity so the WPF/Avalonia
        // UI sees a handle change made from another client (e.g. web client) without restart.
        private void OnWsEventReceived(object? sender, RelayWsEvent e)
        {
            if (_identity is null) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(e.RawJson);
                var root = doc.RootElement;
                switch (e.Type)
                {
                    case "profileUpdated":
                        if (root.TryGetProperty("userId", out var uidEl)
                            && string.Equals(uidEl.GetString(), _identity.UserId, StringComparison.Ordinal)
                            && root.TryGetProperty("handle", out var hEl)
                            && hEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var newHandle = hEl.GetString();
                            if (!string.IsNullOrWhiteSpace(newHandle) && !string.Equals(_identity.Handle, newHandle, StringComparison.Ordinal))
                            {
                                _identity.Handle = newHandle!;
                                PersistIdentity();
                                StatusChanged?.Invoke(this, "Handle synced from another client: @" + newHandle);
                            }
                        }
                        break;

                    case "contactAdded":
                        if (root.TryGetProperty("contact", out var cEl) && cEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            var id = cEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            var handle = cEl.TryGetProperty("handle", out var hh) ? hh.GetString() : null;
                            var dn = cEl.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null;
                            var idPub = cEl.TryGetProperty("identityKeyPublic", out var ipEl) ? ipEl.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(idPub))
                            {
                                lock (_gate)
                                {
                                    if (!Contacts.Any(c => c.ContactId == id))
                                    {
                                        try
                                        {
                                            var pub = Convert.FromBase64String(idPub!);
                                            var c = new PlatytalkContact
                                            {
                                                ContactId = id!,
                                                DisplayName = string.IsNullOrEmpty(dn) ? (handle ?? id!) : dn!,
                                                IdentityKeyPublic = pub,
                                            };
                                            c.SafetyNumber = PlatytalkCrypto.ComputeSafetyNumber(_identity!.IdentityKeyPublic, pub);
                                            Contacts.Add(c);
                                            StatusChanged?.Invoke(this, "Contact added: @" + (handle ?? id));
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            catch { /* best-effort */ }
        }

        // Ensures a Conversation exists for the given incoming envelope so it
        // shows up in the chat list immediately.
        private void EnsureConversationFor(RelayEnvelope env, PlatytalkMessage msg)
        {
            if (_identity is null) return;
            lock (_gate)
            {
                var conv = Conversations.FirstOrDefault(c => c.ConversationId == env.ConversationId);
                if (conv is null)
                {
                    var otherId = env.SenderId == _identity.UserId
                        ? env.ConversationId.Split(':').FirstOrDefault(p => p != _identity.UserId) ?? env.SenderId
                        : env.SenderId;
                    var contact = Contacts.FirstOrDefault(c => c.ContactId == otherId);
                    conv = new PlatytalkConversation
                    {
                        ConversationId = env.ConversationId,
                        Kind = ConversationKind.Direct,
                        Title = contact?.DisplayName ?? otherId,
                    };
                    conv.ParticipantIds.Add(_identity.UserId);
                    conv.ParticipantIds.Add(otherId);
                    Conversations.Add(conv);
                }
                conv.LastMessagePreview = msg.Body;
                conv.LastActivityUtc = DateTime.UtcNow;
            }
        }

        public Task<HandleResponse> SetHandleAsync(string handle, CancellationToken ct = default)
        {
            return _relay.SetHandleAsync(handle, ct).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && _identity is not null)
                {
                    _identity.Handle = t.Result.Handle;
                    PersistIdentity();
                    StatusChanged?.Invoke(this, "Handle updated to @" + t.Result.Handle);
                }
                return t.Result;
            }, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        public Task<GroupListResponse> ListServerGroupsAsync(CancellationToken ct = default) => _relay.ListGroupsAsync(ct);
        public Task<GroupResponse> GetServerGroupAsync(string groupId, CancellationToken ct = default) => _relay.GetGroupAsync(groupId, ct);

        public Task<GroupResponse> CreateServerGroupAsync(string name, IEnumerable<string> memberIds,
            int disappearingSeconds = 0, bool invitesAdminOnly = false, CancellationToken ct = default)
            => _relay.CreateGroupAsync(name, memberIds, disappearingSeconds, invitesAdminOnly, ct);

        public Task<GroupResponse> UpdateServerGroupSettingsAsync(string groupId,
            int? disappearingSeconds = null, bool? invitesAdminOnly = null, string? name = null, CancellationToken ct = default)
            => _relay.UpdateGroupSettingsAsync(groupId, disappearingSeconds, invitesAdminOnly, name, ct);

        public Task<GroupResponse> AddServerGroupMembersAsync(string groupId, IEnumerable<string> userIds, CancellationToken ct = default)
            => _relay.AddGroupMembersAsync(groupId, userIds, ct);

        public Task<GroupResponse> RemoveServerGroupMemberAsync(string groupId, string userId, CancellationToken ct = default)
            => _relay.RemoveGroupMemberAsync(groupId, userId, ct);

        public Task<GroupResponse> SetServerGroupAdminAsync(string groupId, string userId, bool isAdmin, CancellationToken ct = default)
            => _relay.SetGroupMemberAdminAsync(groupId, userId, isAdmin, ct);

        public Task DeleteServerGroupAsync(string groupId, CancellationToken ct = default)
            => _relay.DeleteGroupAsync(groupId, ct);

        public Task<ConvSettingsResponse> GetConvSettingsAsync(string conversationId, CancellationToken ct = default)
            => _relay.GetConvSettingsAsync(conversationId, ct);

        public Task<ConvSettingsResponse> SetConvSettingsAsync(string conversationId, int disappearingSeconds, CancellationToken ct = default)
            => _relay.PutConvSettingsAsync(conversationId, disappearingSeconds, ct);

        /// <summary>Builds a 1:1 conversation id by lex-sorting the two user
        /// ids, joined by ':'. Matches the relay convention.</summary>
        public string BuildDirectConversationId(string otherUserId)
        {
            EnsureIdentity();
            var a = _identity!.UserId;
            var b = otherUserId;
            return string.CompareOrdinal(a, b) < 0 ? $"{a}:{b}" : $"{b}:{a}";
        }

        /// <summary>Snapshot of the user's current identity, contacts, conversations
        /// and groups for upload to the encrypted backup endpoint. Private keys
        /// are included so a freshly-installed device can fully restore.</summary>
        public BackupSnapshot CaptureSnapshot(bool includePrivateIdentity = true)
        {
            EnsureIdentity();
            var snap = new BackupSnapshot
            {
                UserId = _identity!.UserId,
                Handle = _identity.DisplayName,
                DisplayName = _identity.DisplayName,
                IdentityKeyPublicBase64 = Convert.ToBase64String(_identity.IdentityKeyPublic),
                SigningKeyPublicBase64 = Convert.ToBase64String(_identity.SigningKeyPublic),
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };
            if (includePrivateIdentity)
            {
                snap.IdentityKeyPrivateBase64 = Convert.ToBase64String(_identity.IdentityKeyPrivate);
                snap.SigningKeyPrivateBase64 = Convert.ToBase64String(_identity.SigningKeyPrivate);
            }
            foreach (var c in Contacts)
            {
                snap.Contacts.Add(new BackupContact
                {
                    ContactId = c.ContactId,
                    DisplayName = c.DisplayName,
                    IdentityKeyPublicBase64 = Convert.ToBase64String(c.IdentityKeyPublic),
                    PhoneE164 = c.PhoneE164,
                    Email = c.Email,
                    IsVerified = c.IsVerified,
                });
            }
            foreach (var conv in Conversations)
            {
                var bc = new BackupConversation
                {
                    ConversationId = conv.ConversationId,
                    Kind = conv.Kind.ToString(),
                    Title = conv.Title,
                    DisappearingSeconds = conv.DisappearingAfter is { } ts ? (int)Math.Min(ts.TotalSeconds, int.MaxValue) : 0,
                };
                bc.ParticipantIds.AddRange(conv.ParticipantIds);
                snap.Conversations.Add(bc);
            }
            return snap;
        }

        /// <summary>Restores observable collections from an encrypted backup
        /// snapshot. Existing local state is *replaced*. Identity private
        /// keys, if present in the snapshot, are merged into the current
        /// identity so this device can decrypt previously-sent material.</summary>
        public void ApplySnapshot(BackupSnapshot snap)
        {
            if (snap is null) throw new ArgumentNullException(nameof(snap));
            lock (_gate)
            {
                Contacts.Clear();
                foreach (var c in snap.Contacts)
                {
                    var pub = !string.IsNullOrEmpty(c.IdentityKeyPublicBase64)
                        ? Convert.FromBase64String(c.IdentityKeyPublicBase64)
                        : Array.Empty<byte>();
                    Contacts.Add(new PlatytalkContact
                    {
                        ContactId = c.ContactId,
                        DisplayName = c.DisplayName,
                        IdentityKeyPublic = pub,
                        PhoneE164 = c.PhoneE164,
                        Email = c.Email,
                        IsVerified = c.IsVerified,
                    });
                }
                Conversations.Clear();
                foreach (var conv in snap.Conversations)
                {
                    var pc = new PlatytalkConversation
                    {
                        ConversationId = conv.ConversationId,
                        Kind = Enum.TryParse<ConversationKind>(conv.Kind, out var k) ? k : ConversationKind.Direct,
                        Title = conv.Title,
                        DisappearingAfter = conv.DisappearingSeconds > 0 ? TimeSpan.FromSeconds(conv.DisappearingSeconds) : (TimeSpan?)null,
                    };
                    foreach (var p in conv.ParticipantIds) pc.ParticipantIds.Add(p);
                    Conversations.Add(pc);
                }
                if (_identity is not null && !string.IsNullOrEmpty(snap.IdentityKeyPrivateBase64))
                {
                    // Replace identity with the backed-up keys to retain decrypt-ability of historical messages.
                    var idPriv = Convert.FromBase64String(snap.IdentityKeyPrivateBase64!);
                    var idPub = !string.IsNullOrEmpty(snap.IdentityKeyPublicBase64) ? Convert.FromBase64String(snap.IdentityKeyPublicBase64!) : _identity.IdentityKeyPublic;
                    var sigPriv = !string.IsNullOrEmpty(snap.SigningKeyPrivateBase64) ? Convert.FromBase64String(snap.SigningKeyPrivateBase64!) : _identity.SigningKeyPrivate;
                    var sigPub = !string.IsNullOrEmpty(snap.SigningKeyPublicBase64) ? Convert.FromBase64String(snap.SigningKeyPublicBase64!) : _identity.SigningKeyPublic;
                    _identity = new PlatytalkIdentity
                    {
                        UserId = _identity.UserId,
                        DeviceId = _identity.DeviceId,
                        DisplayName = string.IsNullOrEmpty(snap.DisplayName) ? _identity.DisplayName : snap.DisplayName!,
                        Handle = _identity.Handle,
                        IdentityKeyPublic = idPub,
                        IdentityKeyPrivate = idPriv,
                        SigningKeyPublic = sigPub,
                        SigningKeyPrivate = sigPriv,
                        Email = _identity.Email,
                        PhoneE164 = _identity.PhoneE164,
                        IsRegistered = true,
                        MfaEnabled = _identity.MfaEnabled,
                    };
                    PersistIdentity();
                }
                StatusChanged?.Invoke(this, $"Restored {Contacts.Count} contacts, {Conversations.Count} conversations from backup.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { _store.Dispose(); } catch { }
            await _relay.DisposeAsync().ConfigureAwait(false);
        }
    }
}
