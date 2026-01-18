using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for AI-powered features including smart suggestions, file analysis, and content generation.
    /// </summary>
    public class AIService
    {
        private static readonly Lazy<AIService> _instance = new(() => new AIService());
        public static AIService Instance => _instance.Value;
        
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string _provider = "OpenAI";
        private string _model = "gpt-4";
        
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public string Provider => _provider;
        public string Model => _model;
        
        public event EventHandler<AIResponseEventArgs>? ResponseReceived;
        public event EventHandler<AIErrorEventArgs>? ErrorOccurred;
        
        private AIService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(2)
            };
            LoadSettings();
        }
        
        /// <summary>
        /// Configures the AI service with an API key.
        /// </summary>
        public void Configure(string apiKey, string provider = "OpenAI", string model = "gpt-4")
        {
            _apiKey = apiKey;
            _provider = provider;
            _model = model;
            SaveSettings();
        }
        
        /// <summary>
        /// Suggests file names based on content or metadata.
        /// </summary>
        public async Task<List<string>> SuggestFileNamesAsync(string filePath, int count = 5, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return new List<string>();
            
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);
            
            var prompt = $"Suggest {count} better, descriptive file names for a file currently named '{fileName}'." +
                         $"Keep the extension as '{extension}'. Only return the file names, one per line, without explanations.";
            
            try
            {
                var response = await SendPromptAsync(prompt, cancellationToken);
                var suggestions = new List<string>();
                
                foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cleaned = line.Trim().TrimStart('-', '*', ' ', '\t');
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        suggestions.Add(cleaned);
                    }
                }
                
                return suggestions;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new AIErrorEventArgs { Operation = "SuggestFileNames", Error = ex });
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Suggests tags or categories for a file.
        /// </summary>
        public async Task<List<string>> SuggestTagsAsync(string filePath, int count = 10, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return new List<string>();
            
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);
            
            var prompt = $"Suggest {count} relevant tags or categories for a file named '{fileName}'." +
                         $"Only return single-word or short phrase tags, one per line.";
            
            try
            {
                var response = await SendPromptAsync(prompt, cancellationToken);
                var tags = new List<string>();
                
                foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cleaned = line.Trim().TrimStart('-', '*', ' ', '\t');
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        tags.Add(cleaned);
                    }
                }
                
                return tags;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new AIErrorEventArgs { Operation = "SuggestTags", Error = ex });
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Generates a description for a file based on its name and metadata.
        /// </summary>
        public async Task<string> GenerateDescriptionAsync(string filePath, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return string.Empty;
            
            var fileName = Path.GetFileName(filePath);
            var metadataInfo = metadata != null ? string.Join(", ", metadata.Select(kv => $"{kv.Key}: {kv.Value}")) : "";
            
            var prompt = $"Generate a brief, descriptive caption (1-2 sentences) for a file named '{fileName}'.";
            if (!string.IsNullOrEmpty(metadataInfo))
            {
                prompt += $" Available metadata: {metadataInfo}";
            }
            
            try
            {
                return await SendPromptAsync(prompt, cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new AIErrorEventArgs { Operation = "GenerateDescription", Error = ex });
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Analyzes file organization and suggests improvements.
        /// </summary>
        public async Task<FileOrganizationSuggestion> AnalyzeOrganizationAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return new FileOrganizationSuggestion();
            
            var fileNames = string.Join("\n", filePaths.Select(Path.GetFileName));
            
            var prompt = $"Analyze these file names and suggest how to organize them into folders:\n{fileNames}\n\n" +
                         "Return a JSON object with 'folders' array containing objects with 'name' and 'files' array.";
            
            try
            {
                var response = await SendPromptAsync(prompt, cancellationToken);
                // Parse JSON response
                return JsonSerializer.Deserialize<FileOrganizationSuggestion>(response) ?? new FileOrganizationSuggestion();
            }
            catch
            {
                return new FileOrganizationSuggestion();
            }
        }
        
        /// <summary>
        /// Creates a naming pattern based on file examples.
        /// </summary>
        public async Task<string> SuggestNamingPatternAsync(IEnumerable<string> exampleNames, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return string.Empty;
            
            var examples = string.Join("\n", exampleNames.Take(10));
            
            var prompt = $"Analyze these file names and suggest a consistent naming pattern:\n{examples}\n\n" +
                         "Return the pattern using placeholders like {counter}, {date}, {name}, etc.";
            
            try
            {
                return await SendPromptAsync(prompt, cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new AIErrorEventArgs { Operation = "SuggestNamingPattern", Error = ex });
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Sends a custom prompt to the AI.
        /// </summary>
        public async Task<string> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("AI service is not configured. Please set an API key.");
            }
            
            if (_provider == "OpenAI")
            {
                return await SendOpenAIPromptAsync(prompt, cancellationToken);
            }
            
            throw new NotSupportedException($"Provider '{_provider}' is not supported.");
        }
        
        private async Task<string> SendOpenAIPromptAsync(string prompt, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 1000
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"OpenAI API error: {response.StatusCode} - {responseJson}");
            }
            
            using var doc = JsonDocument.Parse(responseJson);
            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
                
            var result = messageContent ?? string.Empty;
            ResponseReceived?.Invoke(this, new AIResponseEventArgs { Prompt = prompt, Response = result });
            return result;
        }
        
        private void LoadSettings()
        {
            try
            {
                var settingsFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "ai_settings.json");
                    
                if (File.Exists(settingsFile))
                {
                    var json = File.ReadAllText(settingsFile);
                    using var doc = JsonDocument.Parse(json);
                    
                    _apiKey = doc.RootElement.TryGetProperty("apiKey", out var key) ? key.GetString() : null;
                    _provider = doc.RootElement.TryGetProperty("provider", out var provider) ? provider.GetString() ?? "OpenAI" : "OpenAI";
                    _model = doc.RootElement.TryGetProperty("model", out var model) ? model.GetString() ?? "gpt-4" : "gpt-4";
                }
            }
            catch { }
        }
        
        private void SaveSettings()
        {
            try
            {
                var settingsFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "ai_settings.json");
                    
                var settings = new { apiKey = _apiKey, provider = _provider, model = _model };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFile, json);
            }
            catch { }
        }
    }
    
    public class FileOrganizationSuggestion
    {
        public List<FolderSuggestion> Folders { get; set; } = new();
    }
    
    public class FolderSuggestion
    {
        public string Name { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }
    
    public class AIResponseEventArgs : EventArgs
    {
        public string Prompt { get; set; } = "";
        public string Response { get; set; } = "";
    }
    
    public class AIErrorEventArgs : EventArgs
    {
        public string Operation { get; set; } = "";
        public Exception? Error { get; set; }
    }
}
