using PlatypusTools.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// A simple credential picker dialog for selecting saved credentials.
    /// </summary>
    public partial class CredentialPickerWindow : Window
    {
        private List<CredentialDisplayItem> _allCredentials = new();
        private CredentialType? _filterType;

        public StoredCredential? SelectedCredential { get; private set; }
        public string? DecryptedPassword { get; private set; }

        public CredentialPickerWindow(CredentialType? defaultType = null)
        {
            InitializeComponent();
            _filterType = defaultType;

            // Set default filter if provided
            if (defaultType.HasValue)
            {
                var index = GetComboIndexForType(defaultType.Value);
                if (index >= 0 && index < TypeFilter.Items.Count)
                {
                    TypeFilter.SelectedIndex = index;
                }
            }

            LoadCredentials();
        }

        private void LoadCredentials()
        {
            try
            {
                var service = CredentialManagerService.Instance;
                var credentials = service.GetAllCredentials();

                _allCredentials = credentials.Select(c => new CredentialDisplayItem
                {
                    Credential = c,
                    Key = c.Key,
                    Username = c.Username,
                    Description = c.Description,
                    Type = c.Type,
                    TypeDisplay = c.Type.ToString(),
                    DisplayInfo = $"{c.Username} - {c.Description}",
                    LastUsed = c.LastUsed,
                    LastUsedDisplay = FormatLastUsed(c.LastUsed)
                }).ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading credentials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            var typeItem = TypeFilter.SelectedItem as ComboBoxItem;
            var typeText = typeItem?.Content?.ToString() ?? "All Types";

            var filtered = _allCredentials.AsEnumerable();

            // Filter by type
            if (typeText != "All Types" && Enum.TryParse<CredentialType>(typeText, out var type))
            {
                filtered = filtered.Where(c => c.Type == type);
            }

            // Filter by search text
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(c =>
                    c.Key.ToLowerInvariant().Contains(query) ||
                    c.Username.ToLowerInvariant().Contains(query) ||
                    (c.Description?.ToLowerInvariant().Contains(query) ?? false));
            }

            CredentialsList.ItemsSource = filtered.OrderBy(c => c.Key).ToList();
        }

        private static string FormatLastUsed(DateTime lastUsed)
        {
            var diff = DateTime.UtcNow - lastUsed;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}d ago";
            return lastUsed.ToString("MMM d, yyyy");
        }

        private static int GetComboIndexForType(CredentialType type)
        {
            return type switch
            {
                CredentialType.SSH => 1,
                CredentialType.FTP => 2,
                CredentialType.SFTP => 3,
                CredentialType.Database => 4,
                CredentialType.Generic => 5,
                _ => 0
            };
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CredentialsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCredential();
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            SelectCredential();
        }

        private void SelectCredential()
        {
            if (CredentialsList.SelectedItem is CredentialDisplayItem item)
            {
                SelectedCredential = item.Credential;

                // Get the decrypted password
                try
                {
                    DecryptedPassword = CredentialManagerService.Instance.GetPassword(item.Key);
                }
                catch
                {
                    DecryptedPassword = null;
                }

                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a credential.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OpenCredentialManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new CredentialManagerWindow
            {
                Owner = this
            };
            window.ShowDialog();

            // Refresh credentials after potentially adding new ones
            LoadCredentials();
        }

        private class CredentialDisplayItem
        {
            public StoredCredential Credential { get; set; } = null!;
            public string Key { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string? Description { get; set; }
            public CredentialType Type { get; set; }
            public string TypeDisplay { get; set; } = string.Empty;
            public string DisplayInfo { get; set; } = string.Empty;
            public DateTime LastUsed { get; set; }
            public string LastUsedDisplay { get; set; } = string.Empty;
        }
    }
}
