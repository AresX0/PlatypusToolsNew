using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Local command terminal with full PowerShell/CMD/Bash support.
    /// </summary>
    public partial class LocalTerminalView : UserControl
    {
        private Process? _shellProcess;
        private StreamWriter? _shellInput;
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private CancellationTokenSource? _cts;
        private string _currentShell = "powershell";
        private string _workingDirectory;
        private bool _isExecuting;

        public LocalTerminalView()
        {
            InitializeComponent();
            _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            WorkingDirBox.Text = _workingDirectory;
        }

        public string WorkingDirectory
        {
            get => _workingDirectory;
            set
            {
                if (Directory.Exists(value))
                {
                    _workingDirectory = value;
                    UpdatePrompt();
                }
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            StartShellProcess();
            CommandInput.Focus();
            AppendOutput($"PlatypusTools Terminal v1.0\r\n", Brushes.Cyan);
            AppendOutput($"Type commands below. Use Up/Down arrows for command history.\r\n", Brushes.Gray);
            AppendOutput($"Shell: {GetShellName(_currentShell)}\r\n\r\n", Brushes.Gray);
        }

        private string GetShellName(string shell)
        {
            return shell switch
            {
                "powershell" => "PowerShell (pwsh)",
                "powershell_legacy" => "Windows PowerShell",
                "cmd" => "Command Prompt",
                "bash" => "Git Bash",
                "wsl" => "WSL (Ubuntu)",
                _ => shell
            };
        }

        private string GetShellExecutable(string shell)
        {
            return shell switch
            {
                "powershell" => "pwsh.exe",
                "powershell_legacy" => "powershell.exe",
                "cmd" => "cmd.exe",
                "bash" => GetGitBashPath(),
                "wsl" => "wsl.exe",
                _ => "powershell.exe"
            };
        }

        private string GetGitBashPath()
        {
            var paths = new[]
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\bin\bash.exe")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
            return "bash.exe"; // Try PATH
        }

        private void StartShellProcess()
        {
            try
            {
                StopShellProcess();

                var executable = GetShellExecutable(_currentShell);
                var args = _currentShell switch
                {
                    "powershell" => "-NoLogo -NoProfile -NonInteractive",
                    "powershell_legacy" => "-NoLogo -NoProfile -NonInteractive",
                    "cmd" => "/Q",
                    "bash" => "--login -i",
                    "wsl" => "",
                    _ => ""
                };

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // Set environment for proper Unicode support
                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                _shellProcess = Process.Start(startInfo);
                if (_shellProcess == null)
                {
                    AppendOutput($"Failed to start {GetShellName(_currentShell)}\r\n", Brushes.Red);
                    return;
                }

                _shellInput = _shellProcess.StandardInput;
                _cts = new CancellationTokenSource();

                // Start async readers for output and error
                Task.Run(() => ReadOutputAsync(_shellProcess.StandardOutput, Brushes.LightGreen, _cts.Token));
                Task.Run(() => ReadOutputAsync(_shellProcess.StandardError, Brushes.OrangeRed, _cts.Token));

                UpdatePrompt();
            }
            catch (Exception ex)
            {
                AppendOutput($"Error starting shell: {ex.Message}\r\n", Brushes.Red);
            }
        }

        private void StopShellProcess()
        {
            _cts?.Cancel();
            
            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                try
                {
                    _shellProcess.Kill(true);
                }
                catch { }
            }
            
            _shellProcess?.Dispose();
            _shellProcess = null;
            _shellInput = null;
        }

        private async Task ReadOutputAsync(StreamReader reader, Brush color, CancellationToken token)
        {
            var buffer = new char[4096];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var count = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (count == 0) break;

                    var text = new string(buffer, 0, count);
                    await Dispatcher.InvokeAsync(() => AppendOutput(text, color));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => 
                    AppendOutput($"\r\n[Stream error: {ex.Message}]\r\n", Brushes.Red));
            }
        }

        private void AppendOutput(string text, Brush? color = null)
        {
            var doc = OutputTextBox.Document;
            var paragraph = doc.Blocks.LastBlock as Paragraph ?? new Paragraph();
            
            if (doc.Blocks.Count == 0)
                doc.Blocks.Add(paragraph);

            var run = new Run(text)
            {
                Foreground = color ?? Brushes.LightGreen
            };
            paragraph.Inlines.Add(run);

            // Auto-scroll to bottom
            OutputTextBox.ScrollToEnd();
            OutputScroller.ScrollToEnd();

            // Limit buffer size (keep last 100KB)
            var allText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            if (allText.Length > 100000)
            {
                var range = new TextRange(doc.ContentStart, doc.ContentStart.GetPositionAtOffset(50000));
                range.Text = "";
            }
        }

        private void UpdatePrompt()
        {
            var prompt = _currentShell switch
            {
                "powershell" => "PS>",
                "powershell_legacy" => "PS>",
                "cmd" => ">",
                "bash" => "$",
                "wsl" => "$",
                _ => ">"
            };
            PromptText.Text = prompt;
        }

        private async void ExecuteCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || _shellInput == null || _isExecuting)
                return;

            _isExecuting = true;
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                // Add to history
                if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
                {
                    _commandHistory.Add(command);
                }
                _historyIndex = _commandHistory.Count;

                // Show command in output
                AppendOutput($"\r\n{PromptText.Text} {command}\r\n", Brushes.Cyan);

                // Handle built-in commands
                if (command.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                    command.Equals("cls", StringComparison.OrdinalIgnoreCase))
                {
                    OutputTextBox.Document.Blocks.Clear();
                    CommandInput.Clear();
                    _isExecuting = false;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    var newDir = command.Substring(3).Trim().Trim('"');
                    if (!Path.IsPathRooted(newDir))
                    {
                        newDir = Path.Combine(_workingDirectory, newDir);
                    }
                    newDir = Path.GetFullPath(newDir);
                    
                    if (Directory.Exists(newDir))
                    {
                        _workingDirectory = newDir;
                        WorkingDirBox.Text = _workingDirectory;
                        AppendOutput($"Changed directory to: {_workingDirectory}\r\n", Brushes.Gray);
                    }
                    else
                    {
                        AppendOutput($"Directory not found: {newDir}\r\n", Brushes.Red);
                    }
                    CommandInput.Clear();
                    _isExecuting = false;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                // Execute command via shell
                await _shellInput.WriteLineAsync(command);
                await _shellInput.FlushAsync();
                
                // Small delay to let output stream
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}\r\n", Brushes.Red);
            }
            finally
            {
                CommandInput.Clear();
                _isExecuting = false;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void CommandInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExecuteCommand(CommandInput.Text);
                e.Handled = true;
            }
        }

        private void CommandInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Command history navigation
            if (e.Key == Key.Up)
            {
                if (_commandHistory.Count > 0 && _historyIndex > 0)
                {
                    _historyIndex--;
                    CommandInput.Text = _commandHistory[_historyIndex];
                    CommandInput.CaretIndex = CommandInput.Text.Length;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                if (_historyIndex < _commandHistory.Count - 1)
                {
                    _historyIndex++;
                    CommandInput.Text = _commandHistory[_historyIndex];
                    CommandInput.CaretIndex = CommandInput.Text.Length;
                }
                else
                {
                    _historyIndex = _commandHistory.Count;
                    CommandInput.Clear();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                // Tab completion - run Get-ChildItem for partial match
                var text = CommandInput.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    // Simple tab completion - just insert a tab or space
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+C to cancel
                if (_isExecuting)
                {
                    AppendOutput("\r\n^C\r\n", Brushes.Yellow);
                    StartShellProcess(); // Restart shell to cancel
                }
                e.Handled = true;
            }
        }

        private void ShellCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShellCombo.SelectedItem is ComboBoxItem item && item.Tag is string shell)
            {
                _currentShell = shell;
                if (IsLoaded)
                {
                    AppendOutput($"\r\n--- Switching to {GetShellName(shell)} ---\r\n", Brushes.Cyan);
                    StartShellProcess();
                }
            }
        }

        private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Working Directory",
                SelectedPath = _workingDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _workingDirectory = dialog.SelectedPath;
                WorkingDirBox.Text = _workingDirectory;
                
                // Change directory in shell
                if (_shellInput != null)
                {
                    var cdCommand = _currentShell == "cmd" ? $"cd /d \"{_workingDirectory}\"" : $"cd \"{_workingDirectory}\"";
                    _shellInput.WriteLine(cdCommand);
                    _shellInput.Flush();
                }
            }
        }

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBox.Document.Blocks.Clear();
            _commandHistory.Clear();
            _historyIndex = -1;
            StartShellProcess();
            AppendOutput($"New terminal session started.\r\n", Brushes.Cyan);
            AppendOutput($"Shell: {GetShellName(_currentShell)}\r\n\r\n", Brushes.Gray);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBox.Document.Blocks.Clear();
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            var doc = OutputTextBox.Document;
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            Clipboard.SetText(range.Text);
            AppendOutput("\r\n[Output copied to clipboard]\r\n", Brushes.Gray);
        }

        private void RunQuickCommand_Click(object sender, RoutedEventArgs e)
        {
            if (QuickCommandsCombo.SelectedItem is ComboBoxItem item && item.Tag is string cmd)
            {
                ExecuteCommand(cmd);
                QuickCommandsCombo.SelectedIndex = 0;
            }
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCommand(CommandInput.Text);
        }

        private void OutputScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3);
                e.Handled = true;
            }
        }

        // Cleanup when control is unloaded
        public void Dispose()
        {
            StopShellProcess();
            _cts?.Dispose();
        }
    }
}
