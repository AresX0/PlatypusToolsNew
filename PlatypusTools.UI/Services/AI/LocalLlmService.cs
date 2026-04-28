using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.AI
{
    /// <summary>
    /// Phase 3.1 — LLM client. Supports three backends:
    ///   1. Ollama  (loopback default, http://localhost:11434)
    ///   2. OpenAI-compatible /v1/chat/completions  (LM Studio, llama.cpp,
    ///      vLLM, or any self-hosted proxy URL the user trusts)
    ///   3. Anthropic Messages API  (https://api.anthropic.com/v1/messages
    ///      OR a self-hosted proxy that speaks the same wire format, e.g.
    ///      LiteLLM, Anthropic-compatible reverse proxies)
    ///
    /// Privacy posture:
    ///   • <see cref="LocalOnlyMode"/> defaults to TRUE — the service refuses
    ///     to contact any host that does not resolve to a loopback address.
    ///     This is enforced for every backend, including Anthropic (so even
    ///     accidental clicks on the cloud model can’t leak data while the
    ///     toggle is on).
    ///   • To use a self-hosted server on another machine, the user must
    ///     explicitly turn LocalOnlyMode off in Settings. Any URL is then
    ///     accepted.
    ///   • To use real api.anthropic.com the user must (a) turn off
    ///     LocalOnlyMode and (b) provide an API key.
    /// </summary>
    public sealed class LocalLlmService
    {
        public enum Backend { Ollama, OpenAiCompat, Anthropic }

        private static readonly Lazy<LocalLlmService> _instance = new(() => new LocalLlmService());
        public static LocalLlmService Instance => _instance.Value;

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

        public sealed record ChatMessage(
            [property: JsonPropertyName("role")] string Role,
            [property: JsonPropertyName("content")] string Content);

        // ---- configuration -------------------------------------------------

        public Backend ActiveBackend { get; set; } = Backend.Ollama;

        /// <summary>If true, ALL backends are restricted to loopback hosts.</summary>
        public bool LocalOnlyMode { get; set; } = true;

        // Ollama
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
        public string DefaultModel { get; set; } = "llama3.2";

        // OpenAI-compatible (LM Studio, llama.cpp server, vLLM, self-host)
        public string? OpenAiCompatBaseUrl { get; set; } = "http://localhost:1234/v1";
        public string? OpenAiApiKey { get; set; }

        // Anthropic Messages API (api.anthropic.com or any compatible proxy)
        public string AnthropicBaseUrl { get; set; } = "https://api.anthropic.com";
        public string? AnthropicApiKey { get; set; }
        public string AnthropicVersion { get; set; } = "2023-06-01";
        public string AnthropicDefaultModel { get; set; } = "claude-3-5-sonnet-latest";
        public int AnthropicMaxTokens { get; set; } = 1024;

        // ---- diagnostics ---------------------------------------------------

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                switch (ActiveBackend)
                {
                    case Backend.Ollama:
                        EnforceLocalOnly(OllamaBaseUrl);
                        using (var resp = await _http.GetAsync($"{OllamaBaseUrl}/api/tags", ct).ConfigureAwait(false))
                            return resp.IsSuccessStatusCode;
                    case Backend.OpenAiCompat:
                        if (string.IsNullOrEmpty(OpenAiCompatBaseUrl)) return false;
                        EnforceLocalOnly(OpenAiCompatBaseUrl);
                        using (var r2 = await _http.GetAsync($"{OpenAiCompatBaseUrl.TrimEnd('/')}/models", ct).ConfigureAwait(false))
                            return r2.IsSuccessStatusCode;
                    case Backend.Anthropic:
                        EnforceLocalOnly(AnthropicBaseUrl);
                        // Anthropic has no cheap health endpoint; treat
                        // "have key + URL" as reachable.
                        return !string.IsNullOrEmpty(AnthropicApiKey);
                }
            }
            catch { return false; }
            return false;
        }

        public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            try
            {
                switch (ActiveBackend)
                {
                    case Backend.Ollama:
                        EnforceLocalOnly(OllamaBaseUrl);
                        using (var resp = await _http.GetAsync($"{OllamaBaseUrl}/api/tags", ct).ConfigureAwait(false))
                        {
                            if (!resp.IsSuccessStatusCode) return list;
                            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("models", out var models))
                                foreach (var m in models.EnumerateArray())
                                    if (m.TryGetProperty("name", out var nm)) list.Add(nm.GetString() ?? "");
                        }
                        break;
                    case Backend.OpenAiCompat:
                        if (string.IsNullOrEmpty(OpenAiCompatBaseUrl)) break;
                        EnforceLocalOnly(OpenAiCompatBaseUrl);
                        using (var resp2 = await _http.GetAsync($"{OpenAiCompatBaseUrl.TrimEnd('/')}/models", ct).ConfigureAwait(false))
                        {
                            if (!resp2.IsSuccessStatusCode) break;
                            var json = await resp2.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("data", out var arr))
                                foreach (var m in arr.EnumerateArray())
                                    if (m.TryGetProperty("id", out var id)) list.Add(id.GetString() ?? "");
                        }
                        break;
                    case Backend.Anthropic:
                        // Anthropic has /v1/models since 2024-09; gated on key.
                        if (string.IsNullOrEmpty(AnthropicApiKey)) { list.AddRange(SuggestedAnthropicModels); break; }
                        EnforceLocalOnly(AnthropicBaseUrl);
                        using (var req = new HttpRequestMessage(HttpMethod.Get, $"{AnthropicBaseUrl.TrimEnd('/')}/v1/models"))
                        {
                            req.Headers.Add("x-api-key", AnthropicApiKey);
                            req.Headers.Add("anthropic-version", AnthropicVersion);
                            using var resp3 = await _http.SendAsync(req, ct).ConfigureAwait(false);
                            if (!resp3.IsSuccessStatusCode) { list.AddRange(SuggestedAnthropicModels); break; }
                            var json = await resp3.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("data", out var arr))
                                foreach (var m in arr.EnumerateArray())
                                    if (m.TryGetProperty("id", out var id)) list.Add(id.GetString() ?? "");
                        }
                        break;
                }
            }
            catch { /* leave whatever we have */ }
            return list;
        }

        private static readonly string[] SuggestedAnthropicModels = new[]
        {
            "claude-3-5-sonnet-latest",
            "claude-3-5-haiku-latest",
            "claude-3-opus-latest",
            "claude-3-haiku-20240307",
        };

        // ---- chat ---------------------------------------------------------

        public async Task<string> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            string? model = null,
            CancellationToken ct = default)
        {
            switch (ActiveBackend)
            {
                case Backend.OpenAiCompat: return await ChatOpenAiAsync(messages, model, ct).ConfigureAwait(false);
                case Backend.Anthropic:    return await ChatAnthropicAsync(messages, model, ct).ConfigureAwait(false);
                case Backend.Ollama:
                default:                    return await ChatOllamaAsync(messages, model, ct).ConfigureAwait(false);
            }
        }

        private async Task<string> ChatOllamaAsync(IReadOnlyList<ChatMessage> messages, string? model, CancellationToken ct)
        {
            EnforceLocalOnly(OllamaBaseUrl);
            model ??= DefaultModel;
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{OllamaBaseUrl}/api/chat")
            {
                Content = JsonContent.Create(new { model, messages, stream = false })
            };
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Ollama HTTP {(int)resp.StatusCode}: {err}");
            }
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
            return "";
        }

        private async Task<string> ChatOpenAiAsync(IReadOnlyList<ChatMessage> messages, string? model, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(OpenAiCompatBaseUrl))
                throw new InvalidOperationException("OpenAiCompatBaseUrl is not configured.");
            EnforceLocalOnly(OpenAiCompatBaseUrl);
            model ??= DefaultModel;
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{OpenAiCompatBaseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = JsonContent.Create(new { model, messages, stream = false })
            };
            if (!string.IsNullOrEmpty(OpenAiApiKey))
                req.Headers.Add("Authorization", "Bearer " + OpenAiApiKey);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        private async Task<string> ChatAnthropicAsync(IReadOnlyList<ChatMessage> messages, string? model, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(AnthropicApiKey))
                throw new InvalidOperationException("Anthropic API key is required (set in Settings → AI).");
            EnforceLocalOnly(AnthropicBaseUrl);
            model ??= AnthropicDefaultModel;

            // Anthropic separates the system prompt from the messages array,
            // and only accepts user/assistant roles in the array.
            string? system = null;
            var anthMessages = new List<object>();
            foreach (var m in messages)
            {
                if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    system = string.IsNullOrEmpty(system) ? m.Content : system + "\n\n" + m.Content;
                }
                else if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    anthMessages.Add(new { role = m.Role.ToLowerInvariant(), content = m.Content });
                }
                // "error" or other custom roles are dropped before sending.
            }

            var payload = system == null
                ? (object)new { model, max_tokens = AnthropicMaxTokens, messages = anthMessages }
                : (object)new { model, max_tokens = AnthropicMaxTokens, system, messages = anthMessages };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{AnthropicBaseUrl.TrimEnd('/')}/v1/messages")
            {
                Content = JsonContent.Create(payload)
            };
            req.Headers.Add("x-api-key", AnthropicApiKey);
            req.Headers.Add("anthropic-version", AnthropicVersion);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Anthropic HTTP {(int)resp.StatusCode}: {err}");
            }
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            // Response shape: { content: [ { type: "text", text: "..." }, ... ], ... }
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var blk in content.EnumerateArray())
                {
                    if (blk.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                        blk.TryGetProperty("text", out var txt))
                        sb.Append(txt.GetString());
                }
                return sb.ToString();
            }
            return "";
        }

        // ---- guard --------------------------------------------------------

        /// <summary>
        /// Throws if <see cref="LocalOnlyMode"/> is on and <paramref name="url"/>
        /// resolves to anything other than a loopback host. Loopback hosts are
        /// `localhost`, `127.0.0.0/8`, `::1`, and `0.0.0.0` (some local servers
        /// bind there).
        /// </summary>
        private void EnforceLocalOnly(string? url)
        {
            if (!LocalOnlyMode) return;
            if (string.IsNullOrEmpty(url)) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
                throw new InvalidOperationException($"Local-only mode is on; refusing to contact malformed URL '{url}'.");
            if (!IsLoopback(u.Host))
                throw new InvalidOperationException(
                    $"Local-only mode is on; '{u.Host}' is not loopback. Disable Local-only in Settings → AI to allow self-hosted servers or cloud providers.");
        }

        public static bool IsLoopback(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            host = host.Trim().Trim('[', ']').ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "0.0.0.0") return true;
            // 127.0.0.0/8
            if (host.StartsWith("127.") && System.Net.IPAddress.TryParse(host, out var ip))
            {
                var b = ip.GetAddressBytes();
                if (b.Length == 4 && b[0] == 127) return true;
            }
            return false;
        }
    }
}
