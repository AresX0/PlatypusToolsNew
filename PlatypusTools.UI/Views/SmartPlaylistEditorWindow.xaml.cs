using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Smart Playlist Rules Editor dialog.
    /// </summary>
    public partial class SmartPlaylistEditorWindow : Window
    {
        private readonly ObservableCollection<RuleDisplayItem> _rules = new();

        /// <summary>
        /// The resulting rule set after save.
        /// </summary>
        public SmartPlaylistRuleSet? ResultRuleSet { get; private set; }

        /// <summary>
        /// The playlist name entered by the user.
        /// </summary>
        public string PlaylistName => PlaylistNameBox.Text?.Trim() ?? "Smart Playlist";

        /// <summary>
        /// Optional: provide tracks for preview evaluation.
        /// </summary>
        public List<AudioTrack>? PreviewTracks { get; set; }

        public SmartPlaylistEditorWindow()
        {
            InitializeComponent();
            RulesPanel.ItemsSource = _rules;
        }

        /// <summary>
        /// Load an existing rule set for editing.
        /// </summary>
        public void LoadRuleSet(string name, SmartPlaylistRuleSet ruleSet)
        {
            PlaylistNameBox.Text = name;
            MatchModeCombo.SelectedIndex = ruleSet.MatchMode == "Any" ? 1 : 0;

            _rules.Clear();
            foreach (var rule in ruleSet.Rules)
            {
                _rules.Add(new RuleDisplayItem
                {
                    Field = rule.Field,
                    Operator = rule.Operator,
                    Value = rule.Value
                });
            }

            // Sort
            if (!string.IsNullOrEmpty(ruleSet.SortBy))
            {
                for (int i = 0; i < SortByCombo.Items.Count; i++)
                {
                    if ((SortByCombo.Items[i] as ComboBoxItem)?.Content?.ToString() == ruleSet.SortBy)
                    {
                        SortByCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            SortDescCheck.IsChecked = ruleSet.SortDescending;
            MaxResultsBox.Text = ruleSet.MaxResults?.ToString() ?? "";
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            _rules.Add(new RuleDisplayItem { Field = "Artist", Operator = "Contains", Value = "" });
        }

        private void RemoveRule_Click(object sender, RoutedEventArgs e)
        {
            if (_rules.Count > 0)
                _rules.RemoveAt(_rules.Count - 1);
        }

        private void Field_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.Tag is RuleDisplayItem rule)
            {
                rule.RefreshOperators();
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var ruleSet = BuildRuleSet();
            if (ruleSet == null) return;

            if (PreviewTracks == null || PreviewTracks.Count == 0)
            {
                PreviewCountText.Text = "No tracks available for preview.";
                return;
            }

            var matches = SmartPlaylistService.Instance.EvaluateRules(ruleSet, PreviewTracks);
            PreviewCountText.Text = $"Matches {matches.Count} of {PreviewTracks.Count} tracks";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlaylistNameBox.Text))
            {
                MessageBox.Show("Please enter a playlist name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultRuleSet = BuildRuleSet();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private SmartPlaylistRuleSet BuildRuleSet()
        {
            var ruleSet = new SmartPlaylistRuleSet
            {
                MatchMode = MatchModeCombo.SelectedIndex == 1 ? "Any" : "All",
                Rules = _rules.Select(r => new SmartPlaylistRule
                {
                    Field = r.Field,
                    Operator = r.Operator,
                    Value = r.Value
                }).ToList()
            };

            var sortBy = (SortByCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (sortBy != "None" && !string.IsNullOrEmpty(sortBy))
                ruleSet.SortBy = sortBy;

            ruleSet.SortDescending = SortDescCheck.IsChecked == true;

            if (int.TryParse(MaxResultsBox.Text, out var max) && max > 0)
                ruleSet.MaxResults = max;

            return ruleSet;
        }
    }

    /// <summary>
    /// Display model for a single rule row in the editor.
    /// </summary>
    public class RuleDisplayItem : INotifyPropertyChanged
    {
        private string _field = "Title";
        private string _operator = "Contains";
        private string _value = "";

        public string Field
        {
            get => _field;
            set
            {
                _field = value;
                OnPropertyChanged();
                RefreshOperators();
            }
        }

        public string Operator
        {
            get => _operator;
            set { _operator = value; OnPropertyChanged(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public string[] AvailableFields => SmartPlaylistRule.AvailableFields;

        private string[] _availableOperators = SmartPlaylistRule.GetOperatorsForField("Title");
        public string[] AvailableOperators
        {
            get => _availableOperators;
            set { _availableOperators = value; OnPropertyChanged(); }
        }

        public void RefreshOperators()
        {
            AvailableOperators = SmartPlaylistRule.GetOperatorsForField(Field);
            if (!AvailableOperators.Contains(Operator))
                Operator = AvailableOperators.FirstOrDefault() ?? "Contains";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
