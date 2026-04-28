using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.Services.Scripting;

namespace PlatypusTools.UI.Views
{
    public partial class ScriptingConsoleView : UserControl
    {
        private CancellationTokenSource? _cts;

        public ScriptingConsoleView() { InitializeComponent(); }

        private void Clear_Click(object sender, RoutedEventArgs e) { ActiveOutput().Clear(); }

        private void Inject_Changed(object sender, RoutedEventArgs e)
        {
            ScriptingHostService.Instance.InjectPlatypusProxy = InjectProxyBox.IsChecked == true;
            if (StatusText != null)
                StatusText.Text = ScriptingHostService.Instance.InjectPlatypusProxy ? "$Platypus injection: ON" : "$Platypus injection: OFF";
        }

        private void Engine_Changed(object sender, SelectionChangedEventArgs e) { /* no-op */ }

        private async void Run_Click(object sender, RoutedEventArgs e) { await RunAsync(); }

        private async void ScriptBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await RunAsync();
            }
        }

        private bool IsPython() => ReferenceEquals(EngineTabs.SelectedItem, PythonTab);
        private TextBox ActiveScript() => IsPython() ? PyScriptBox : PsScriptBox;
        private TextBox ActiveOutput() => IsPython() ? PyOutputBox : PsOutputBox;

        private async Task RunAsync()
        {
            var script = ActiveScript().Text ?? "";
            if (string.IsNullOrWhiteSpace(script)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            RunButton.IsEnabled = false;
            StatusText.Text = "Running…";
            try
            {
                var engine = IsPython() ? "Python" : "PowerShell";
                ScriptResult result = engine == "Python"
                    ? await ScriptingHostService.Instance.RunPythonAsync(script, ct)
                    : await ScriptingHostService.Instance.RunPowerShellAsync(script, ct);

                Append($"--- {engine} (exit {result.ExitCode}) ---{Environment.NewLine}");
                if (!string.IsNullOrEmpty(result.StdOut)) Append(result.StdOut);
                if (!string.IsNullOrEmpty(result.StdErr))
                    Append($"{Environment.NewLine}[stderr]{Environment.NewLine}{result.StdErr}");
                Append(Environment.NewLine);
                StatusText.Text = result.Success ? "OK" : $"Exit {result.ExitCode}";
            }
            catch (Exception ex)
            {
                Append($"[error] {ex.Message}{Environment.NewLine}");
                StatusText.Text = "Error";
            }
            finally { RunButton.IsEnabled = true; }
        }

        private void Append(string text)
        {
            var box = ActiveOutput();
            box.AppendText(text);
            box.ScrollToEnd();
        }
    }
}
