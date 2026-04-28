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
    /// Phase 3.1 — local LLM client. Talks to Ollama (default) at
    /// http://localhost:11434 or any OpenAI-compatible /v1/chat/completions
    /// endpoint. Pure HTTP, no SDK dependency.
    /// </summary>
    public sealed class LocalLlmService
    {
        private static readonly Lazy<LocalLlmService> _instance = new(() => new LocalLlmService());
        public static LocalLlmService Instance => _instance.Value;

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

        public sealed record ChatMessage(
            [property: JsonPropertyName("role")] string Role,
            [property: JsonPropertyName("content")] string Content);

        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
        public string DefaultModel { get; set; } = "llama3.2";
        public string? OpenAiCompatBaseUrl { get; set; } // e.g. http://localhost:1234/v1
        public string? OpenAiApiKey { get; set; }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                using var resp = await _http.GetAsync($"{OllamaBaseUrl}/api/tags", ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            try
            {
                using var resp = await _http.GetAsync($"{OllamaBaseUrl}/api/tags", ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return list;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var models))
                {
                    foreach (var m in models.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nm)) list.Add(nm.GetString() ?? "");
                    }
                }
            }
            catch { /* leave empty */ }
            return list;
        }

        /// <summary>Non-streaming chat. Use Ollama if configured, else OpenAI-compatible endpoint.</summary>
        public async Task<string> ChatAsync(
            IReadOnlyList<ChatMessage> messages,
            string? model = null,
            CancellationToken ct = default)
        {
            model ??= DefaultModel;

            if (!string.IsNullOrEmpty(OpenAiCompatBaseUrl))
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{OpenAiCompatBaseUrl.TrimEnd('/')}/chat/completions")
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        messages,
                        stream = false
                    })
                };
                if (!string.IsNullOrEmpty(OpenAiApiKey))
                    req.Headers.Add("Authorization", "Bearer " + OpenAiApiKey);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content").GetString() ?? "";
            }

            // Ollama default
            var ollamaPayload = new
            {
                model,
                messages,
                stream = false
            };
            using var ollamaReq = new HttpRequestMessage(HttpMethod.Post, $"{OllamaBaseUrl}/api/chat")
            {
                Content = JsonContent.Create(ollamaPayload)
            };
            using var ollamaResp = await _http.SendAsync(ollamaReq, ct).ConfigureAwait(false);
            if (!ollamaResp.IsSuccessStatusCode)
            {
                var err = await ollamaResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Ollama HTTP {(int)ollamaResp.StatusCode}: {err}");
            }
            var ollamaJson = await ollamaResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var ollamaDoc = JsonDocument.Parse(ollamaJson);
            if (ollamaDoc.RootElement.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? "";
            }
            return "";
        }
    }
}
