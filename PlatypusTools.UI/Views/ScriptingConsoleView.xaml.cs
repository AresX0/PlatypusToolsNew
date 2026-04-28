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

        private void Clear_Click(object sender, RoutedEventArgs e) { OutputBox.Clear(); }

        private async void Run_Click(object sender, RoutedEventArgs e) { await RunAsync(); }

        private async void ScriptBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await RunAsync();
            }
        }

        private async Task RunAsync()
        {
            var script = ScriptBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(script)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            RunButton.IsEnabled = false;
            StatusText.Text = "Running…";
            try
            {
                var item = EngineCombo.SelectedItem as ComboBoxItem;
                var engine = (item?.Content as string) ?? "PowerShell";
                ScriptResult result;
                if (engine == "Python")
                    result = await ScriptingHostService.Instance.RunPythonAsync(script, ct);
                else
                    result = await ScriptingHostService.Instance.RunPowerShellAsync(script, ct);

                Append($"--- {engine} (exit {result.ExitCode}) ---{Environment.NewLine}");
                if (!string.IsNullOrEmpty(result.StdOut)) Append(result.StdOut);
                if (!string.IsNullOrEmpty(result.StdErr))
                {
                    Append($"{Environment.NewLine}[stderr]{Environment.NewLine}{result.StdErr}");
                }
                Append(Environment.NewLine);
                StatusText.Text = result.Success ? "OK" : $"Exit {result.ExitCode}";
            }
            catch (Exception ex)
            {
                Append($"[error] {ex.Message}{Environment.NewLine}");
                StatusText.Text = "Error";
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        private void Append(string text)
        {
            OutputBox.AppendText(text);
            OutputBox.ScrollToEnd();
        }
    }
}
