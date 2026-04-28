using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.Services.AI;

namespace PlatypusTools.UI.Views
{
    public partial class AIAssistantWindow : Window
    {
        public class ChatEntry
        {
            public string Role { get; set; } = "user";
            public string Content { get; set; } = "";
        }

        private readonly ObservableCollection<ChatEntry> _entries = new();

        public AIAssistantWindow()
        {
            InitializeComponent();
            ChatLog.ItemsSource = _entries;
            // Reflect persisted backend choice on open.
            try
            {
                BackendBox.SelectedIndex = LocalLlmService.Instance.ActiveBackend switch
                {
                    LocalLlmService.Backend.OpenAiCompat => 1,
                    LocalLlmService.Backend.Anthropic    => 2,
                    _                                    => 0,
                };
                if (LocalLlmService.Instance.ActiveBackend == LocalLlmService.Backend.Anthropic
                    && string.IsNullOrEmpty(ModelBox.Text))
                {
                    ModelBox.Text = LocalLlmService.Instance.AnthropicDefaultModel;
                }
            }
            catch { }
            Loaded += async (_, _) => await ReloadModelsAsync();
        }

        private async System.Threading.Tasks.Task ReloadModelsAsync()
        {
            StatusText.Text = "Probing LLM backend…";
            var llm = LocalLlmService.Instance;
            var ok = await llm.PingAsync();
            if (!ok)
            {
                StatusText.Text = llm.ActiveBackend switch
                {
                    LocalLlmService.Backend.Ollama       => "Ollama not reachable on " + llm.OllamaBaseUrl,
                    LocalLlmService.Backend.OpenAiCompat => "OpenAI-compat endpoint not reachable on " + llm.OpenAiCompatBaseUrl,
                    LocalLlmService.Backend.Anthropic    => "Anthropic backend requires an API key (Settings → AI).",
                    _ => "Backend not reachable.",
                };
                return;
            }
            var models = await llm.ListModelsAsync();
            ModelBox.Items.Clear();
            foreach (var m in models) ModelBox.Items.Add(m);
            if (models.Count > 0 && string.IsNullOrEmpty(ModelBox.Text))
                ModelBox.Text = models[0];
            StatusText.Text = models.Count > 0 ? $"{models.Count} model(s) available" : "Backend up; no models listed";
        }

        private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadModelsAsync();

        private void Clear_Click(object sender, RoutedEventArgs e) => _entries.Clear();

        private async void Backend_Changed(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChanged can fire during XAML parsing (because ComboBoxItem
            // IsSelected="True" lights up before later children construct).
            if (BackendBox == null) return;
            var llm = LocalLlmService.Instance;
            llm.ActiveBackend = BackendBox.SelectedIndex switch
            {
                1 => LocalLlmService.Backend.OpenAiCompat,
                2 => LocalLlmService.Backend.Anthropic,
                _ => LocalLlmService.Backend.Ollama,
            };
            // Persist the choice.
            try
            {
                Services.SettingsManager.Current.AiBackend = llm.ActiveBackend.ToString();
                Services.SettingsManager.SaveCurrent();
            }
            catch { }
            if (StatusText != null)
            {
                StatusText.Text = "Backend: " + llm.ActiveBackend +
                    (llm.LocalOnlyMode ? " (local-only mode ON)" : " (local-only mode OFF)");
            }
            // For Anthropic, prefill a sensible model when the box is empty.
            if (llm.ActiveBackend == LocalLlmService.Backend.Anthropic
                && ModelBox != null && string.IsNullOrEmpty(ModelBox.Text))
            {
                ModelBox.Text = llm.AnthropicDefaultModel;
            }
            // Auto-refresh model list.
            try { await ReloadModelsAsync(); } catch { }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Send_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = (InputBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            _entries.Add(new ChatEntry { Role = "user", Content = text });
            InputBox.Text = "";

            var msgs = new List<LocalLlmService.ChatMessage>();
            var sys = (SystemBox.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(sys))
                msgs.Add(new LocalLlmService.ChatMessage("system", sys));
            foreach (var m in _entries)
                msgs.Add(new LocalLlmService.ChatMessage(m.Role, m.Content));

            StatusText.Text = "Thinking…";
            try
            {
                var model = (ModelBox.Text ?? LocalLlmService.Instance.DefaultModel).Trim();
                var reply = await LocalLlmService.Instance.ChatAsync(msgs, model);
                _entries.Add(new ChatEntry { Role = "assistant", Content = reply });
                StatusText.Text = $"Replied ({reply.Length} chars)";
                ChatLog.ScrollIntoView(_entries.LastOrDefault());
            }
            catch (Exception ex)
            {
                _entries.Add(new ChatEntry { Role = "error", Content = ex.Message });
                StatusText.Text = "Error";
            }
        }
    }
}
