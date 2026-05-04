using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Thin client over the Platytalk relay service hosted at
    /// platytalk.platysoft.com. The relay never sees plaintext — it only
    /// stores opaque ciphertext blobs and routes them between devices.
    ///
    /// All public methods accept a CancellationToken and surface a
    /// PlatytalkRelayException on transport / auth failure.
    /// </summary>
    public sealed class PlatytalkRelayClient : IAsyncDisposable
    {
        public const string DefaultBaseUrl = "https://platytalk.platysoft.com";
        public const string DefaultWebSocketUrl = "wss://platytalk.platysoft.com/ws";

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        private readonly Uri _wsUri;
        private ClientWebSocket? _ws;
        private string? _bearerToken;

        public string BaseUrl { get; }
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public event EventHandler<RelayEnvelope>? EnvelopeReceived;
        public event EventHandler<RelayWsEvent>? WsEventReceived;
        public event EventHandler<string>? ConnectionStatusChanged;

        public PlatytalkRelayClient(string? baseUrl = null, string? webSocketUrl = null, HttpClient? httpClient = null)
        {
            BaseUrl = baseUrl ?? DefaultBaseUrl;
            _wsUri = new Uri(webSocketUrl ?? DefaultWebSocketUrl);
            _http = httpClient ?? new HttpClient { BaseAddress = new Uri(BaseUrl) };
            if (_http.BaseAddress is null) _http.BaseAddress = new Uri(BaseUrl);
        }

        public void SetBearer(string token)
        {
            _bearerToken = token;
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        public void ClearBearer()
        {
            _bearerToken = null;
            _http.DefaultRequestHeaders.Authorization = null;
        }

        public bool HasBearer => !string.IsNullOrEmpty(_bearerToken);

        // ---------- Identity / device keys --------------------------------

        public Task UploadIdentityKeysAsync(byte[] identityKeyPublic, byte[] signingKeyPublic, CancellationToken ct = default)
            => PostAsync("/v1/keys/identity", new
            {
                identityKeyPublic = Convert.ToBase64String(identityKeyPublic),
                signingKeyPublic = Convert.ToBase64String(signingKeyPublic),
            }, ct);

        // ---------- Registration / verification (legacy SMS/email) --------

        public Task<RegistrationChallenge> StartRegistrationAsync(string handle, VerificationChannel channel, CancellationToken ct = default)
            => PostAsync<RegistrationChallenge>("/v1/register/start",
                   new { handle, channel = channel.ToString().ToLowerInvariant() }, ct);

        public Task<AuthResponse> CompleteRegistrationAsync(string challengeId, string code, byte[] identityKeyPublic, byte[] signingKeyPublic, string deviceName, CancellationToken ct = default)
            => PostAsync<AuthResponse>("/v1/register/complete",
                   new
                   {
                       challengeId,
                       code,
                       identityKeyPublic = Convert.ToBase64String(identityKeyPublic),
                       signingKeyPublic = Convert.ToBase64String(signingKeyPublic),
                       deviceName
                   }, ct);

        public Task<AuthResponse> LoginAsync(string handle, string mfaCode, CancellationToken ct = default)
            => PostAsync<AuthResponse>("/v1/auth/login", new { handle, mfaCode }, ct);

        // ---------- Contacts / discovery ----------------------------------

        public async Task<DirectoryHit[]> FindContactAsync(string handle, CancellationToken ct = default)
        {
            // Server returns {results:[{id,handle,displayName,identityKeyPublic,signingKeyPublic}]}.
            var resp = await GetAsync<DirectorySearchResponse>(
                $"/v1/directory/find?handle={Uri.EscapeDataString(handle)}", ct).ConfigureAwait(false);
            return (resp.Results ?? new List<DirectoryRow>())
                .Select(r => new DirectoryHit
                {
                    ContactId = r.Id,
                    DisplayName = string.IsNullOrEmpty(r.DisplayName) ? r.Handle : r.DisplayName,
                    IdentityKeyPublicBase64 = r.IdentityKeyPublic ?? string.Empty,
                    SigningKeyPublicBase64 = r.SigningKeyPublic ?? string.Empty,
                }).ToArray();
        }

        public async Task<DirectoryHit[]> ListContactsAsync(CancellationToken ct = default)
        {
            var resp = await GetAsync<ContactsListResponse>("/v1/contacts", ct).ConfigureAwait(false);
            return (resp.Contacts ?? new List<DirectoryRow>())
                .Select(r => new DirectoryHit
                {
                    ContactId = r.Id,
                    DisplayName = string.IsNullOrEmpty(r.DisplayName) ? r.Handle : r.DisplayName,
                    IdentityKeyPublicBase64 = r.IdentityKeyPublic ?? string.Empty,
                    SigningKeyPublicBase64 = r.SigningKeyPublic ?? string.Empty,
                }).ToArray();
        }

        public Task AddContactAsync(string handle, CancellationToken ct = default)
            => PostAsync("/v1/contacts/add", new { handle }, ct);

        public Task<DirectoryHit> GetPrekeyBundleAsync(string contactId, CancellationToken ct = default)
            => GetAsync<DirectoryHit>($"/v1/directory/prekeys?contactId={Uri.EscapeDataString(contactId)}", ct);

        // ---------- Handle ------------------------------------------------

        public Task<HandleResponse> SetHandleAsync(string handle, CancellationToken ct = default)
            => SendAsync<HandleResponse>(HttpMethod.Put, "/v1/me/handle", new { handle }, ct);

        public Task<MeResponse> GetMeAsync(CancellationToken ct = default)
            => GetAsync<MeResponse>("/v1/me", ct);

        // ---------- Devices -----------------------------------------------

        public Task<DeviceListResponse> ListMyDevicesAsync(CancellationToken ct = default)
            => GetAsync<DeviceListResponse>("/v1/me/devices", ct);

        public Task DeleteMyDeviceAsync(string deviceId, CancellationToken ct = default)
            => SendAsync(HttpMethod.Delete, $"/v1/me/devices/{Uri.EscapeDataString(deviceId)}", null, ct);

        // ---------- Message envelopes -------------------------------------

        public Task SendEnvelopeAsync(RelayEnvelope env, CancellationToken ct = default)
            => PostAsync("/v1/messages/send", env, ct);

        public Task DeleteRemoteAsync(string messageId, string conversationId, IEnumerable<string> recipientUserIds, CancellationToken ct = default)
            => PostAsync("/v1/messages/delete", new
            {
                messageId,
                conversationId,
                recipients = recipientUserIds.Select(u => new { userId = u }).ToArray(),
            }, ct);

        public async Task<RelayEnvelope[]> FetchPendingAsync(CancellationToken ct = default)
        {
            var resp = await GetAsync<PendingEnvelopesResponse>("/v1/messages/pending", ct).ConfigureAwait(false);
            return resp?.Envelopes?.ToArray() ?? Array.Empty<RelayEnvelope>();
        }

        public Task AckMessagesAsync(IEnumerable<string> messageIds, CancellationToken ct = default)
            => PostAsync("/v1/messages/ack", new { messageIds = messageIds.ToArray() }, ct);

        // ---------- Groups -----------------------------------------------

        public Task<GroupResponse> CreateGroupAsync(string name, IEnumerable<string> memberIds,
            int disappearingSeconds = 0, bool invitesAdminOnly = false, CancellationToken ct = default)
            => PostAsync<GroupResponse>("/v1/groups",
                   new { name, memberIds, disappearingSeconds, invitesAdminOnly }, ct);

        public Task<GroupListResponse> ListGroupsAsync(CancellationToken ct = default)
            => GetAsync<GroupListResponse>("/v1/groups", ct);

        public Task<GroupResponse> GetGroupAsync(string groupId, CancellationToken ct = default)
            => GetAsync<GroupResponse>($"/v1/groups/{Uri.EscapeDataString(groupId)}", ct);

        public Task<GroupResponse> UpdateGroupSettingsAsync(string groupId,
            int? disappearingSeconds = null, bool? invitesAdminOnly = null, string? name = null, CancellationToken ct = default)
            => SendAsync<GroupResponse>(HttpMethod.Put,
                   $"/v1/groups/{Uri.EscapeDataString(groupId)}/settings",
                   new { disappearingSeconds, invitesAdminOnly, name }, ct);

        public Task<GroupResponse> AddGroupMembersAsync(string groupId, IEnumerable<string> userIds, CancellationToken ct = default)
            => PostAsync<GroupResponse>($"/v1/groups/{Uri.EscapeDataString(groupId)}/members",
                   new { userIds }, ct);

        public Task<GroupResponse> RemoveGroupMemberAsync(string groupId, string userId, CancellationToken ct = default)
            => SendAsync<GroupResponse>(HttpMethod.Delete,
                   $"/v1/groups/{Uri.EscapeDataString(groupId)}/members/{Uri.EscapeDataString(userId)}", null, ct);

        public Task<GroupResponse> SetGroupMemberAdminAsync(string groupId, string userId, bool isAdmin, CancellationToken ct = default)
            => SendAsync<GroupResponse>(HttpMethod.Put,
                   $"/v1/groups/{Uri.EscapeDataString(groupId)}/members/{Uri.EscapeDataString(userId)}/admin",
                   new { isAdmin }, ct);

        public Task DeleteGroupAsync(string groupId, CancellationToken ct = default)
            => SendAsync(HttpMethod.Delete, $"/v1/groups/{Uri.EscapeDataString(groupId)}", null, ct);

        // ---------- Sender keys (group ratchet distribution) -------------

        public Task<SenderKeyPostResponse> PostSenderKeyDistributionsAsync(
            string groupId, string senderDeviceId, long generation,
            IEnumerable<SenderKeyDistribution> distributions, CancellationToken ct = default)
            => PostAsync<SenderKeyPostResponse>(
                   $"/v1/groups/{Uri.EscapeDataString(groupId)}/sender-keys",
                   new { senderDeviceId, generation, distributions }, ct);

        public Task<SenderKeyPendingResponse> GetPendingSenderKeysAsync(string deviceId, CancellationToken ct = default)
            => GetAsync<SenderKeyPendingResponse>(
                   $"/v1/sender-keys/pending?deviceId={Uri.EscapeDataString(deviceId)}", ct);

        public Task AckSenderKeysAsync(IEnumerable<long> ids, CancellationToken ct = default)
            => PostAsync("/v1/sender-keys/ack", new { ids }, ct);

        // ---------- 1:1 conversation TTL settings ------------------------

        public Task<ConvSettingsResponse> GetConvSettingsAsync(string conversationId, CancellationToken ct = default)
            => GetAsync<ConvSettingsResponse>(
                   $"/v1/conversations/{Uri.EscapeDataString(conversationId)}/settings", ct);

        public Task<ConvSettingsResponse> PutConvSettingsAsync(string conversationId, int disappearingSeconds, CancellationToken ct = default)
            => SendAsync<ConvSettingsResponse>(HttpMethod.Put,
                   $"/v1/conversations/{Uri.EscapeDataString(conversationId)}/settings",
                   new { disappearingSeconds }, ct);

        // ---------- Encrypted backup -------------------------------------

        public Task<BackupPutResponse> PutBackupAsync(string cipherBlobBase64, string kdfSaltBase64,
            object kdfParams, string cipherAlg = "aes-256-gcm", CancellationToken ct = default)
            => SendAsync<BackupPutResponse>(HttpMethod.Put, "/v1/backup",
                   new { cipherBlob = cipherBlobBase64, kdfSalt = kdfSaltBase64, kdfParams, cipherAlg }, ct);

        public Task<BackupGetResponse?> GetBackupAsync(CancellationToken ct = default)
            => GetAsyncOrNull<BackupGetResponse>("/v1/backup", ct);

        public Task<BackupInfoResponse?> GetBackupInfoAsync(CancellationToken ct = default)
            => GetAsyncOrNull<BackupInfoResponse>("/v1/backup/info", ct);

        public Task DeleteBackupAsync(CancellationToken ct = default)
            => SendAsync(HttpMethod.Delete, "/v1/backup", null, ct);

        // ---------- Realtime ---------------------------------------------

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            // The Node WS hub authenticates via ?token=<jwt> on the query string.
            // Sending an Authorization header alone fails (server returns 4401 close).
            _ws = new ClientWebSocket();
            var uri = string.IsNullOrEmpty(_bearerToken)
                ? _wsUri
                : new Uri(_wsUri + "?token=" + Uri.EscapeDataString(_bearerToken));
            await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
            ConnectionStatusChanged?.Invoke(this, "Connected");
            _ = Task.Run(() => ReceiveLoopAsync(_ws), CancellationToken.None);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                        if (res.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, res.Count);
                    } while (!res.EndOfMessage);
                    ms.Position = 0;
                    try
                    {
                        // Server sends typed frames: {type, messageId, conversationId, senderId,
                        // cipherBlob, ephemeralPublic, counter, timestampMs} for envelope/tombstone,
                        // plus contactAdded/profileUpdated/convSettings/hello/error.
                        using var doc = JsonDocument.Parse(ms);
                        var root = doc.RootElement;
                        var type = root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                            ? (typeProp.GetString() ?? string.Empty)
                            : string.Empty;
                        WsEventReceived?.Invoke(this, new RelayWsEvent { Type = type, RawJson = root.GetRawText() });
                        if (type == "envelope" || type == "tombstone")
                        {
                            var env = JsonSerializer.Deserialize<RelayEnvelope>(root.GetRawText(), JsonOpts);
                            if (env != null)
                            {
                                if (type == "tombstone") env.IsTombstone = true;
                                EnvelopeReceived?.Invoke(this, env);
                            }
                        }
                    }
                    catch { /* best-effort */ }
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusChanged?.Invoke(this, "Disconnected: " + ex.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_ws is { State: WebSocketState.Open })
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            _ws?.Dispose();
            _http.Dispose();
        }

        // ---------- HTTP helpers ------------------------------------------

        private async Task<T> GetAsync<T>(string path, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "GET", path, ct).ConfigureAwait(false);
            var result = await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
            return result ?? throw new PlatytalkRelayException($"Empty response from {path}");
        }

        private async Task<T?> GetAsyncOrNull<T>(string path, CancellationToken ct) where T : class
        {
            using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(resp, "GET", path, ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
        }

        private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
        {
            using var resp = await _http.PostAsJsonAsync(path, body, JsonOpts, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "POST", path, ct).ConfigureAwait(false);
            var result = await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
            return result ?? throw new PlatytalkRelayException($"Empty response from {path}");
        }

        private async Task PostAsync(string path, object body, CancellationToken ct)
        {
            using var resp = await _http.PostAsJsonAsync(path, body, JsonOpts, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, "POST", path, ct).ConfigureAwait(false);
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, path);
            if (body is not null) req.Content = JsonContent.Create(body, options: JsonOpts);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, method.Method, path, ct).ConfigureAwait(false);
            var result = await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct).ConfigureAwait(false);
            return result ?? throw new PlatytalkRelayException($"Empty response from {path}");
        }

        private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(method, path);
            if (body is not null) req.Content = JsonContent.Create(body, options: JsonOpts);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(resp, method.Method, path, ct).ConfigureAwait(false);
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string method, string path, CancellationToken ct)
        {
            if (resp.IsSuccessStatusCode) return;
            string detail = string.Empty;
            try { detail = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }
            if (detail.Length > 200) detail = detail.Substring(0, 200) + "…";
            throw new PlatytalkRelayException($"{(int)resp.StatusCode} {resp.ReasonPhrase} on {method} {path}{(string.IsNullOrEmpty(detail) ? string.Empty : $" — {detail}")}");
        }
    }

    public sealed class PlatytalkRelayException : Exception
    {
        public PlatytalkRelayException(string message) : base(message) { }
    }

    public sealed class RelayWsEvent
    {
        public string Type { get; init; } = string.Empty;
        public string RawJson { get; init; } = string.Empty;
    }

    public sealed class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }

    public sealed class HandleResponse
    {
        public string Handle { get; set; } = string.Empty;
    }

    public sealed class DirectoryHit
    {
        public string ContactId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string IdentityKeyPublicBase64 { get; set; } = string.Empty;
        public string SigningKeyPublicBase64 { get; set; } = string.Empty;
    }

    public sealed class RelayEnvelope
    {
        // Server uses these exact JSON names. Keep [JsonPropertyName] so we
        // serialize correctly on outgoing /messages/send and deserialize
        // correctly on incoming WS "envelope" frames + /messages/pending rows.
        [JsonPropertyName("messageId")]      public string MessageId { get; set; } = string.Empty;
        [JsonPropertyName("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonPropertyName("senderId")]       public string SenderId { get; set; } = string.Empty;
        [JsonPropertyName("recipients")]     public RelayRecipient[]? Recipients { get; set; }
        [JsonPropertyName("cipherBlob")]     public string CipherBlob { get; set; } = string.Empty;
        [JsonPropertyName("ephemeralPublic")] public string EphemeralPublic { get; set; } = string.Empty;
        [JsonPropertyName("counter")]        public long Counter { get; set; }
        [JsonPropertyName("timestampMs")]    public long TimestampUnixMs { get; set; }
        [JsonIgnore] public bool IsTombstone { get; set; }
        [JsonIgnore] public Dictionary<string, string>? Headers { get; set; }

        // Back-compat aliases (older callers used "...Base64" suffixed names).
        [JsonIgnore] public string CipherBlobBase64 { get => CipherBlob; set => CipherBlob = value; }
        [JsonIgnore] public string EphemeralPublicBase64 { get => EphemeralPublic; set => EphemeralPublic = value; }
        [JsonIgnore] public string[] RecipientIds { get; set; } = Array.Empty<string>();
    }

    public sealed class RelayRecipient
    {
        [JsonPropertyName("userId")]          public string UserId { get; set; } = string.Empty;
        [JsonPropertyName("cipherBlob")]      public string? CipherBlob { get; set; }
        [JsonPropertyName("ephemeralPublic")] public string? EphemeralPublic { get; set; }
    }

    // ----- New response DTOs ---------------------------------------------

    public sealed class DeviceListResponse
    {
        public List<RelayDevice> Devices { get; set; } = new();
    }
    public sealed class RelayDevice
    {
        public string Id { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string? CreatedUtc { get; set; }
    }

    public sealed class GroupListResponse
    {
        public List<RelayGroup> Groups { get; set; } = new();
    }

    public sealed class GroupResponse
    {
        public RelayGroup? Group { get; set; }
    }

    public sealed class RelayGroup
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
        public int DisappearingSeconds { get; set; }
        public bool InvitesAdminOnly { get; set; }
        public int SettingsVersion { get; set; }
        public string? CreatedUtc { get; set; }
        public List<RelayGroupMember> Members { get; set; } = new();
    }

    public sealed class RelayGroupMember
    {
        public string Id { get; set; } = string.Empty;
        public string? Handle { get; set; }
        public string? DisplayName { get; set; }
        public bool IsAdmin { get; set; }
        public string? JoinedUtc { get; set; }
    }

    public sealed class MeResponse
    {
        [JsonPropertyName("id")]                 public string Id { get; set; } = string.Empty;
        [JsonPropertyName("handle")]             public string Handle { get; set; } = string.Empty;
        [JsonPropertyName("displayName")]        public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("identityKeyPublic")]  public string? IdentityKeyPublic { get; set; }
        [JsonPropertyName("signingKeyPublic")]   public string? SigningKeyPublic { get; set; }
    }

    internal sealed class DirectorySearchResponse
    {
        [JsonPropertyName("results")] public List<DirectoryRow>? Results { get; set; }
    }

    internal sealed class ContactsListResponse
    {
        [JsonPropertyName("contacts")] public List<DirectoryRow>? Contacts { get; set; }
    }

    internal sealed class PendingEnvelopesResponse
    {
        [JsonPropertyName("envelopes")] public List<RelayEnvelope>? Envelopes { get; set; }
    }

    internal sealed class DirectoryRow
    {
        [JsonPropertyName("id")]                 public string Id { get; set; } = string.Empty;
        [JsonPropertyName("handle")]             public string Handle { get; set; } = string.Empty;
        [JsonPropertyName("displayName")]        public string? DisplayName { get; set; }
        [JsonPropertyName("identityKeyPublic")]  public string? IdentityKeyPublic { get; set; }
        [JsonPropertyName("signingKeyPublic")]   public string? SigningKeyPublic { get; set; }
    }

    public sealed class SenderKeyDistribution
    {
        public string RecipientUserId { get; set; } = string.Empty;
        public string RecipientDeviceId { get; set; } = string.Empty;
        public string CipherBlob { get; set; } = string.Empty;
        public string EphemeralPublic { get; set; } = string.Empty;
    }

    public sealed class SenderKeyPostResponse
    {
        public bool Ok { get; set; }
        public int Count { get; set; }
    }

    public sealed class SenderKeyPendingResponse
    {
        public List<SenderKeyPendingItem> Distributions { get; set; } = new();
    }

    public sealed class SenderKeyPendingItem
    {
        public long Id { get; set; }
        public string GroupId { get; set; } = string.Empty;
        public string SenderUserId { get; set; } = string.Empty;
        public string SenderDeviceId { get; set; } = string.Empty;
        public long Generation { get; set; }
        public string CipherBlob { get; set; } = string.Empty;
        public string EphemeralPublic { get; set; } = string.Empty;
        public string? CreatedUtc { get; set; }
    }

    public sealed class ConvSettingsResponse
    {
        public string ConversationId { get; set; } = string.Empty;
        public int DisappearingSeconds { get; set; }
        public int Version { get; set; }
        public string? UpdatedBy { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    public sealed class BackupPutResponse
    {
        public bool Ok { get; set; }
        public int Version { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    public sealed class BackupGetResponse
    {
        public string CipherBlob { get; set; } = string.Empty;
        public string KdfSalt { get; set; } = string.Empty;
        public Dictionary<string, JsonElement>? KdfParams { get; set; }
        public string CipherAlg { get; set; } = "aes-256-gcm";
        public int? Version { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    public sealed class BackupInfoResponse
    {
        public Dictionary<string, JsonElement>? KdfParams { get; set; }
        public string CipherAlg { get; set; } = "aes-256-gcm";
        public int? Version { get; set; }
        public string? UpdatedUtc { get; set; }
        public int? BlobSizeBytes { get; set; }
    }
}
