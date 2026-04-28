using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PlatypusTools.UI.Services.Workflow;

namespace PlatypusTools.UI.Views
{
    public partial class WorkflowDesignerWindow : Window
    {
        private readonly WorkflowEngine _engine = new();
        private WorkflowEngine.Workflow _flow = new();
        private WorkflowEngine.WorkflowNode? _selected;
        private bool _suppressEvents;

        public WorkflowDesignerWindow()
        {
            InitializeComponent();
            foreach (var t in WorkflowEngine.SupportedNodeTypes)
                TypeBox.Items.Add(t);
            RebindList();
        }

        private void RebindList()
        {
            _suppressEvents = true;
            StepsList.Items.Clear();
            foreach (var n in _flow.Nodes)
                StepsList.Items.Add($"[{n.Type}] {n.Name}");
            NameBox.Text = _flow.Name;
            _suppressEvents = false;
        }

        private void RefreshSelected()
        {
            _suppressEvents = true;
            if (_selected == null)
            {
                TypeBox.SelectedIndex = -1;
                StepNameBox.Text = "";
                InputsBox.Text = "";
            }
            else
            {
                TypeBox.SelectedItem = _selected.Type;
                StepNameBox.Text = _selected.Name;
                InputsBox.Text = string.Join(Environment.NewLine,
                    _selected.Inputs.Select(kv => $"{kv.Key}={kv.Value}"));
            }
            _suppressEvents = false;
        }

        private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = StepsList.SelectedIndex;
            _selected = idx >= 0 && idx < _flow.Nodes.Count ? _flow.Nodes[idx] : null;
            RefreshSelected();
        }

        private void AddStep_Click(object sender, RoutedEventArgs e)
        {
            _flow.Nodes.Add(new WorkflowEngine.WorkflowNode { Type = "log", Name = $"Step {_flow.Nodes.Count + 1}", Inputs = new Dictionary<string, string> { ["message"] = "Hello" } });
            RebindList();
            StepsList.SelectedIndex = _flow.Nodes.Count - 1;
        }

        private void RemoveStep_Click(object sender, RoutedEventArgs e)
        {
            var idx = StepsList.SelectedIndex;
            if (idx < 0) return;
            _flow.Nodes.RemoveAt(idx);
            RebindList();
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
        private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);
        private void Move(int delta)
        {
            var idx = StepsList.SelectedIndex;
            if (idx < 0) return;
            var ni = idx + delta;
            if (ni < 0 || ni >= _flow.Nodes.Count) return;
            (_flow.Nodes[idx], _flow.Nodes[ni]) = (_flow.Nodes[ni], _flow.Nodes[idx]);
            RebindList();
            StepsList.SelectedIndex = ni;
        }

        private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents || _selected == null || TypeBox.SelectedItem is not string t) return;
            _selected.Type = t;
            RebindList();
            StepsList.SelectedIndex = _flow.Nodes.IndexOf(_selected);
        }

        private void StepNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _selected == null) return;
            _selected.Name = StepNameBox.Text;
            var idx = _flow.Nodes.IndexOf(_selected);
            if (idx >= 0) StepsList.Items[idx] = $"[{_selected.Type}] {_selected.Name}";
        }

        private void InputsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents || _selected == null) return;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in InputsBox.Text.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                var eq = l.IndexOf('=');
                if (eq <= 0) continue;
                dict[l.Substring(0, eq).Trim()] = l.Substring(eq + 1);
            }
            _selected.Inputs = dict;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Platypus Workflow (*.platypusflow;*.json)|*.platypusflow;*.json|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _flow = WorkflowEngine.Deserialize(File.ReadAllText(dlg.FileName));
                RebindList();
            }
            catch (Exception ex) { MessageBox.Show("Open failed: " + ex.Message, "Workflow"); }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            _flow.Name = NameBox.Text;
            var dlg = new SaveFileDialog
            {
                Filter = "Platypus Workflow (*.platypusflow)|*.platypusflow|JSON (*.json)|*.json",
                FileName = (_flow.Name ?? "workflow") + ".platypusflow"
            };
            if (dlg.ShowDialog() != true) return;
            try { File.WriteAllText(dlg.FileName, WorkflowEngine.Serialize(_flow)); }
            catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message, "Workflow"); }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            _flow.Name = NameBox.Text;
            if (_flow.Nodes.Count == 0)
            {
                MessageBox.Show("No steps to run.", "Workflow");
                return;
            }
            OutputBox.Text = "";
            var sb = new StringBuilder();
            try
            {
                _engine.StepCompleted += OnStep;
                var results = await _engine.RunAsync(_flow);
                _engine.StepCompleted -= OnStep;
                sb.AppendLine();
                sb.AppendLine($"=== Done — {results.Count(r => r.Success)}/{results.Count} succeeded ===");
                OutputBox.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"FATAL: {ex.Message}");
                OutputBox.Text = sb.ToString();
            }

            void OnStep(WorkflowEngine.StepResult r)
            {
                Dispatcher.Invoke(() =>
                {
                    sb.AppendLine($"--- {r.Node} ({r.Duration.TotalMilliseconds:F0}ms) {(r.Success ? "OK" : "FAIL")} ---");
                    sb.AppendLine(r.Success ? r.Output : ("ERROR: " + r.Error));
                    OutputBox.Text = sb.ToString();
                    OutputBox.ScrollToEnd();
                });
            }
        }
    }
}
