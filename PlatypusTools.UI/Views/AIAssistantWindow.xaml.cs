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
            Loaded += async (_, _) => await ReloadModelsAsync();
        }

        private async System.Threading.Tasks.Task ReloadModelsAsync()
        {
            StatusText.Text = "Probing local LLM…";
            var ok = await LocalLlmService.Instance.PingAsync();
            if (!ok)
            {
                StatusText.Text = "Ollama not reachable on localhost:11434";
                return;
            }
            var models = await LocalLlmService.Instance.ListModelsAsync();
            ModelBox.Items.Clear();
            foreach (var m in models) ModelBox.Items.Add(m);
            if (models.Count > 0 && string.IsNullOrEmpty(ModelBox.Text))
                ModelBox.Text = models[0];
            StatusText.Text = models.Count > 0 ? $"{models.Count} model(s) available" : "Ollama up; no models installed";
        }

        private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadModelsAsync();

        private void Clear_Click(object sender, RoutedEventArgs e) => _entries.Clear();

        private void Backend_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (BackendBox.SelectedIndex == 1)
            {
                LocalLlmService.Instance.OpenAiCompatBaseUrl = "http://localhost:1234/v1";
                StatusText.Text = "Backend: OpenAI-compat at http://localhost:1234/v1 (LM Studio default)";
            }
            else
            {
                LocalLlmService.Instance.OpenAiCompatBaseUrl = null;
                StatusText.Text = "Backend: Ollama";
            }
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
