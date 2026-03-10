using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class ArchivedScriptRunnerWindow : Window
    {
        private readonly string _archivedDir;
        public ArchivedScriptRunnerWindow()
        {
            InitializeComponent();
            var cur = Directory.GetCurrentDirectory();
            string dir = string.Empty;
            while (!string.IsNullOrEmpty(cur))
            {
                var candidate = Path.Combine(cur, "ArchivedScripts");
                if (Directory.Exists(candidate)) { dir = candidate; break; }
                var parent = Directory.GetParent(cur); cur = parent?.FullName;
            }
            _archivedDir = dir;
            if (!string.IsNullOrEmpty(_archivedDir))
            {
                var scripts = Directory.GetFiles(_archivedDir, "*.ps1").OrderBy(x => x);
                foreach (var s in scripts) ScriptCombo.Items.Add(s);
                if (ScriptCombo.Items.Count > 0) ScriptCombo.SelectedIndex = 0;
            }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptCombo.SelectedItem == null) { OutputBox.Text = "No script selected."; return; }
            var script = ScriptCombo.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(script)) { OutputBox.Text = "No script selected."; return; }
            var cmd = CmdBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cmd)) { OutputBox.Text = "No command entered."; return; }

            // Sanitize: only allow alphanumeric, spaces, hyphens, dots, and common parameter chars
            // Block semicolons, pipes, ampersands, backticks which enable command chaining
            var dangerousChars = new[] { ';', '|', '&', '`', '$', '(', ')', '{', '}' };
            if (cmd.IndexOfAny(dangerousChars) >= 0)
            {
                OutputBox.Text = "Error: Command contains disallowed characters (; | & ` $ ( ) { }). Please use only simple commands.";
                return;
            }

            // Escape single quotes in path to prevent injection via script path
            var escapedScript = script.Replace("'", "''");
            var ps = $". '{escapedScript}'; {cmd}";
            OutputBox.Text = "Running...";
            var res = await PlatypusTools.UI.Services.PowerShellRunner.RunScriptAsync(ps, timeoutMs: 90000);
            OutputBox.Text = res.StdOut + "\n" + res.StdErr + $"\nExitCode: {res.ExitCode}";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}