using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Views
{
    public partial class CredentialManagerWindow : Window
    {
        private readonly CredentialManagerService _credentialService;
        private List<StoredCredential> _allCredentials = new();
        private StoredCredential? _selectedCredential;
        private bool _showPassword;
        private bool _isNewCredential;

        public CredentialManagerWindow()
        {
            InitializeComponent();
            _credentialService = CredentialManagerService.Instance;
            LoadCredentials();
        }

        private void LoadCredentials()
        {
            _allCredentials = _credentialService.GetAllCredentials().ToList();
            ApplyFilter();
            UpdateStatus();
        }

        private void ApplyFilter()
        {
            var filtered = _allCredentials.AsEnumerable();
            
            // Apply type filter
            if (TypeFilter.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string typeTag)
            {
                if (Enum.TryParse<CredentialType>(typeTag, out var type))
                {
                    filtered = filtered.Where(c => c.Type == type);
                }
            }
            
            // Apply search filter
            var searchText = SearchBox.Text?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(c =>
                    (c.Key?.ToLowerInvariant().Contains(searchText) ?? false) ||
                    (c.Username?.ToLowerInvariant().Contains(searchText) ?? false) ||
                    (c.Description?.ToLowerInvariant().Contains(searchText) ?? false));
            }
            
            CredentialsGrid.ItemsSource = filtered.ToList();
        }

        private void UpdateStatus()
        {
            StatusText.Text = $"{_allCredentials.Count} credential(s) stored";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                ApplyFilter();
            }
        }

        private void CredentialsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CredentialsGrid.SelectedItem is StoredCredential cred)
            {
                _selectedCredential = cred;
                _isNewCredential = false;
                LoadCredentialToForm(cred);
            }
        }

        private void LoadCredentialToForm(StoredCredential cred)
        {
            KeyBox.Text = cred.Key;
            UsernameBox.Text = cred.Username;
            DescriptionBox.Text = cred.Description;
            
            // Get decrypted password
            var password = _credentialService.GetPassword(cred.Key);
            PasswordBox.Password = password ?? "";
            PasswordTextBox.Text = password ?? "";
            
            // Set type combo
            foreach (ComboBoxItem item in TypeCombo.Items)
            {
                if (item.Tag is string tag && tag == cred.Type.ToString())
                {
                    TypeCombo.SelectedItem = item;
                    break;
                }
            }
            
            KeyBox.IsEnabled = false; // Can't change key of existing credential
        }

        private void ClearForm()
        {
            KeyBox.Text = "";
            UsernameBox.Text = "";
            PasswordBox.Password = "";
            PasswordTextBox.Text = "";
            DescriptionBox.Text = "";
            TypeCombo.SelectedIndex = 0;
            KeyBox.IsEnabled = true;
            _selectedCredential = null;
            _isNewCredential = true;
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            CredentialsGrid.SelectedItem = null;
            KeyBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyBox.Text?.Trim();
            var username = UsernameBox.Text?.Trim();
            var password = _showPassword ? PasswordTextBox.Text : PasswordBox.Password;
            var description = DescriptionBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Please enter a name/key for this credential.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                KeyBox.Focus();
                return;
            }
            
            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Please enter a username.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UsernameBox.Focus();
                return;
            }
            
            if (_isNewCredential && _credentialService.HasCredential(key))
            {
                var result = MessageBox.Show($"A credential with key '{key}' already exists. Overwrite it?",
                    "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            var type = CredentialType.Generic;
            if (TypeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string typeTag)
            {
                Enum.TryParse(typeTag, out type);
            }
            
            _credentialService.SaveCredential(key, username, password, description, type);
            
            LoadCredentials();
            ClearForm();
            
            StatusText.Text = $"Credential '{key}' saved successfully.";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCredential == null)
            {
                MessageBox.Show("Please select a credential to delete.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var result = MessageBox.Show($"Are you sure you want to delete '{_selectedCredential.Key}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                _credentialService.DeleteCredential(_selectedCredential.Key);
                LoadCredentials();
                ClearForm();
                StatusText.Text = "Credential deleted.";
            }
        }

        private void ShowPassword_Click(object sender, RoutedEventArgs e)
        {
            _showPassword = !_showPassword;
            
            if (_showPassword)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordTextBox.Visibility = Visibility.Visible;
                ShowPasswordBtn.Content = "\uED1A"; // Hide icon
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                ShowPasswordBtn.Content = "\uE7B3"; // View icon
            }
        }

        private void GeneratePassword_Click(object sender, RoutedEventArgs e)
        {
            // Toggle password options panel visibility
            if (PasswordOptionsPanel.Visibility == Visibility.Collapsed)
            {
                PasswordOptionsPanel.Visibility = Visibility.Visible;
            }
            
            // Generate password
            var password = GenerateSecurePassword();
            
            PasswordBox.Password = password;
            PasswordTextBox.Text = password;
            
            // Show the password so user can see what was generated
            if (!_showPassword)
            {
                ShowPassword_Click(sender, e);
            }
            
            StatusText.Text = "Secure password generated.";
        }

        private string GenerateSecurePassword()
        {
            if (!int.TryParse(PasswordLengthBox.Text, out var length) || length < 4)
                length = 16;
            if (length > 128)
                length = 128;

            var charSets = new StringBuilder();
            
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string numbers = "0123456789";
            const string symbols = "!@#$%^&*()_+-=[]{}|;:,.<>?";

            if (IncludeUppercase.IsChecked == true) charSets.Append(uppercase);
            if (IncludeLowercase.IsChecked == true) charSets.Append(lowercase);
            if (IncludeNumbers.IsChecked == true) charSets.Append(numbers);
            if (IncludeSymbols.IsChecked == true) charSets.Append(symbols);

            if (charSets.Length == 0)
            {
                // Default to all if nothing selected
                charSets.Append(uppercase).Append(lowercase).Append(numbers).Append(symbols);
            }

            var chars = charSets.ToString();
            var result = new char[length];
            
            // Use cryptographic random number generator
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[length * 4]; // 4 bytes per random number for better distribution
            rng.GetBytes(buffer);

            for (int i = 0; i < length; i++)
            {
                uint randomValue = BitConverter.ToUInt32(buffer, i * 4);
                result[i] = chars[(int)(randomValue % (uint)chars.Length)];
            }

            // Ensure at least one character from each selected set
            int pos = 0;
            if (IncludeUppercase.IsChecked == true && length > pos)
            {
                result[pos++] = uppercase[GetSecureRandomInt(uppercase.Length)];
            }
            if (IncludeLowercase.IsChecked == true && length > pos)
            {
                result[pos++] = lowercase[GetSecureRandomInt(lowercase.Length)];
            }
            if (IncludeNumbers.IsChecked == true && length > pos)
            {
                result[pos++] = numbers[GetSecureRandomInt(numbers.Length)];
            }
            if (IncludeSymbols.IsChecked == true && length > pos)
            {
                result[pos++] = symbols[GetSecureRandomInt(symbols.Length)];
            }

            // Shuffle the result to randomize required character positions
            ShuffleArray(result);

            return new string(result);
        }

        private static int GetSecureRandomInt(int max)
        {
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[4];
            rng.GetBytes(buffer);
            return (int)(BitConverter.ToUInt32(buffer, 0) % (uint)max);
        }

        private static void ShuffleArray(char[] array)
        {
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[4];
            
            for (int i = array.Length - 1; i > 0; i--)
            {
                rng.GetBytes(buffer);
                int j = (int)(BitConverter.ToUInt32(buffer, 0) % (uint)(i + 1));
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        private void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCredential == null)
            {
                MessageBox.Show("Please select a credential first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var password = _credentialService.GetPassword(_selectedCredential.Key);
            if (!string.IsNullOrEmpty(password))
            {
                Clipboard.SetText(password);
                StatusText.Text = "Password copied to clipboard.";
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Credential Export Files|*.ptcred|All Files|*.*",
                Title = "Import Credentials"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var passwordWindow = new PromptPasswordWindow("Enter the password used to encrypt this backup:");
                passwordWindow.Owner = this;
                if (passwordWindow.ShowDialog() == true)
                {
                    try
                    {
                        var count = _credentialService.ImportCredentials(dialog.FileName, passwordWindow.EnteredPassword, true);
                        LoadCredentials();
                        MessageBox.Show($"Successfully imported {count} credential(s).", "Import Complete",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to import credentials: {ex.Message}", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_allCredentials.Count == 0)
            {
                MessageBox.Show("No credentials to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Filter = "Credential Export Files|*.ptcred|All Files|*.*",
                Title = "Export Credentials",
                FileName = $"credentials_backup_{DateTime.Now:yyyyMMdd}.ptcred"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var passwordWindow = new PromptPasswordWindow("Enter a password to encrypt this backup:");
                passwordWindow.Owner = this;
                if (passwordWindow.ShowDialog() == true)
                {
                    try
                    {
                        _credentialService.ExportCredentials(dialog.FileName, passwordWindow.EnteredPassword);
                        MessageBox.Show($"Successfully exported {_allCredentials.Count} credential(s).", "Export Complete",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to export credentials: {ex.Message}", "Export Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        
        private void OpenWindowsCredentialManager(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("control", "/name Microsoft.CredentialManager") 
                { 
                    UseShellExecute = true 
                });
            }
            catch
            {
                MessageBox.Show("Could not open Windows Credential Manager.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}