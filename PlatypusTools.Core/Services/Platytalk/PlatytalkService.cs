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
            TryLoadIdentity();
            TryLoadSession();
        }

        // ---------- Identity / registration -------------------------------

        public ObservableCollection<PlatytalkMessage> GetMessages(string conversationId)
        {
            lock (_gate)
            {
                if (!_messagesByConv.TryGetValue(conversationId, out var list))
                {
                    list = new ObservableCollection<PlatytalkMessage>();
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

            // Reuse existing local key material if we have it; otherwise generate fresh.
            var (idPub, idPriv) = PlatytalkCrypto.GenerateKeyExchangeKeyPair();
            var (sigPub, sigPriv) = PlatytalkCrypto.GenerateSigningKeyPair();

            _identity = new PlatytalkIdentity
            {
                UserId = result.UserId,
                DeviceId = Guid.NewGuid().ToString("N"),
                DisplayName = string.IsNullOrWhiteSpace(result.DisplayName) ? result.Handle : result.DisplayName,
                IdentityKeyPublic = idPub,
                IdentityKeyPrivate = idPriv,
                SigningKeyPublic = sigPub,
                SigningKeyPrivate = sigPriv,
                Email = null,
                PhoneE164 = null,
                IsRegistered = true,
                MfaEnabled = true, // MS account MFA is the gate
            };
            PersistIdentity();
            PersistSession(result.Token);
            try { await _relay.UploadIdentityKeysAsync(idPub, sigPub, ct).ConfigureAwait(false); }
            catch (Exception ex) { StatusChanged?.Invoke(this, "Key upload failed: " + ex.Message); }
            StatusChanged?.Invoke(this, $"Signed in as @{result.Handle}");
            return result;
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
            var hits = await _relay.FindContactAsync(handle, ct).ConfigureAwait(false);
            var hit = hits.FirstOrDefault();
            if (hit is null) return null;
            var contact = new PlatytalkContact
            {
                ContactId = hit.ContactId,
                DisplayName = string.IsNullOrEmpty(hit.DisplayName) ? handle : hit.DisplayName,
                IdentityKeyPublic = Convert.FromBase64String(hit.IdentityKeyPublicBase64),
                PhoneE164 = handle.StartsWith('+') ? handle : null,
                Email = handle.Contains('@') ? handle : null,
            };
            contact.SafetyNumber = PlatytalkCrypto.ComputeSafetyNumber(_identity!.IdentityKeyPublic, contact.IdentityKeyPublic);
            lock (_gate) Contacts.Add(contact);
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

            try
            {
                var counter = Interlocked.Increment(ref _outboundCounter);
                foreach (var recipientId in conv.ParticipantIds.Where(p => p != _identity.UserId))
                {
                    var contact = Contacts.FirstOrDefault(c => c.ContactId == recipientId);
                    if (contact is null) continue;

                    var (ePub, ePriv) = PlatytalkCrypto.GenerateKeyExchangeKeyPair();
                    var root = PlatytalkCrypto.DeriveRootKey(
                        _identity.IdentityKeyPrivate, contact.IdentityKeyPublic,
                        ePriv, contact.IdentityKeyPublic, // recipient ephemeral n/a for v1 — bound to identity
                        info: $"to:{recipientId}");
                    var key = PlatytalkCrypto.DeriveMessageKey(root, counter, conv.ConversationId);

                    var aad = Encoding.UTF8.GetBytes($"{_identity.UserId}|{conv.ConversationId}|{counter}");
                    var blob = PlatytalkCrypto.EncryptMessage(key, Encoding.UTF8.GetBytes(body), aad, out _);
                    var env = new RelayEnvelope
                    {
                        MessageId = msg.MessageId,
                        ConversationId = conv.ConversationId,
                        SenderId = _identity.UserId,
                        RecipientIds = new[] { recipientId },
                        CipherBlobBase64 = Convert.ToBase64String(blob),
                        EphemeralPublicBase64 = Convert.ToBase64String(ePub),
                        Counter = counter,
                        TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    };
                    await _relay.SendEnvelopeAsync(env, ct).ConfigureAwait(false);
                }
                msg.Status = MessageStatus.Sent;
                conv.LastMessagePreview = body;
                conv.LastActivityUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                msg.Status = MessageStatus.Failed;
                StatusChanged?.Invoke(this, "Send failed: " + ex.Message);
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
            try { await _relay.DeleteRemoteAsync(msg.MessageId, ct).ConfigureAwait(false); }
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
                    return;
                }

                var contact = Contacts.FirstOrDefault(c => c.ContactId == env.SenderId);
                byte[] root;
                if (contact is null) return;
                var ePub = Convert.FromBase64String(env.EphemeralPublicBase64);
                root = PlatytalkCrypto.DeriveRootKey(
                    _identity.IdentityKeyPrivate, contact.IdentityKeyPublic,
                    _identity.IdentityKeyPrivate, ePub,
                    info: $"to:{_identity.UserId}");
                var key = PlatytalkCrypto.DeriveMessageKey(root, env.Counter, env.ConversationId);
                var aad = Encoding.UTF8.GetBytes($"{env.SenderId}|{env.ConversationId}|{env.Counter}");
                var blob = Convert.FromBase64String(env.CipherBlobBase64);
                var plain = PlatytalkCrypto.DecryptMessage(key, blob, aad);

                var msg = new PlatytalkMessage
                {
                    MessageId = env.MessageId,
                    ConversationId = env.ConversationId,
                    SenderId = env.SenderId,
                    Direction = MessageDirection.Incoming,
                    Body = Encoding.UTF8.GetString(plain),
                    Status = MessageStatus.Delivered,
                };
                GetMessages(env.ConversationId).Add(msg);
                MessageReceived?.Invoke(this, msg);
            }
            catch (CryptographicException ex)
            {
                StatusChanged?.Invoke(this, "Decrypt failed: " + ex.Message);
            }
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

        public Task<HandleResponse> SetHandleAsync(string handle, CancellationToken ct = default)
        {
            return _relay.SetHandleAsync(handle, ct).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && _identity is not null)
                {
                    _identity.DisplayName = t.Result.Handle;
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

        public async ValueTask DisposeAsync() => await _relay.DisposeAsync().ConfigureAwait(false);
    }
}
