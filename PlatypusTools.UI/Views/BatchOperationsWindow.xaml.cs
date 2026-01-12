using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    public partial class BatchOperationsWindow : Window
    {
        private readonly ObservableCollection<BatchFileItem> _files = new();

        public BatchOperationsWindow()
        {
            InitializeComponent();
            FilesList.ItemsSource = _files;
            OperationCombo.SelectionChanged += OperationCombo_SelectionChanged;
        }

        private void OperationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateOptionsVisibility();
            UpdatePreview();
        }

        private void UpdateOptionsVisibility()
        {
            var index = OperationCombo.SelectedIndex;
            
            RenameOptionsPanel.Visibility = index == 0 || index == 7 ? Visibility.Visible : Visibility.Collapsed;
            DestinationPanel.Visibility = index == 1 || index == 2 ? Visibility.Visible : Visibility.Collapsed;
            PrefixSuffixPanel.Visibility = index == 5 || index == 6 ? Visibility.Visible : Visibility.Collapsed;
            SequencePanel.Visibility = index == 8 ? Visibility.Visible : Visibility.Collapsed;
            
            if (index == 5) PrefixSuffixLabel.Text = "Prefix:";
            else if (index == 6) PrefixSuffixLabel.Text = "Suffix:";
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select Files"
            };
            
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    _files.Add(new BatchFileItem { FullPath = file, OriginalName = Path.GetFileName(file) });
                }
                UpdatePreview();
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Folder" };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    foreach (var file in Directory.GetFiles(dialog.FolderName))
                    {
                        _files.Add(new BatchFileItem { FullPath = file, OriginalName = Path.GetFileName(file) });
                    }
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading folder: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FilesList.SelectedItems.Cast<BatchFileItem>().ToList();
            foreach (var item in selectedItems)
            {
                _files.Remove(item);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _files.Clear();
        }

        private void BrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Destination Folder" };
            if (dialog.ShowDialog() == true)
            {
                DestinationPath.Text = dialog.FolderName;
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var operation = OperationCombo.SelectedIndex;
            int counter = int.TryParse(SequenceStart.Text, out var start) ? start : 1;
            int digits = int.TryParse(SequenceDigits.Text, out var d) ? d : 3;

            foreach (var file in _files)
            {
                file.NewName = operation switch
                {
                    0 or 7 => file.OriginalName.Replace(FindText.Text, ReplaceText.Text),
                    1 or 2 => Path.Combine(DestinationPath.Text, file.OriginalName),
                    3 => "[DELETED]",
                    4 => Path.ChangeExtension(file.OriginalName, ReplaceText.Text),
                    5 => PrefixSuffixText.Text + file.OriginalName,
                    6 => Path.GetFileNameWithoutExtension(file.OriginalName) + PrefixSuffixText.Text + Path.GetExtension(file.OriginalName),
                    8 => $"{counter++.ToString().PadLeft(digits, '0')}_{file.OriginalName}",
                    _ => file.OriginalName
                };
                file.Status = "Pending";
            }

            FilesList.Items.Refresh();
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0)
            {
                MessageBox.Show("No files to process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var operation = OperationCombo.SelectedIndex;
            if ((operation == 1 || operation == 2) && string.IsNullOrWhiteSpace(DestinationPath.Text))
            {
                MessageBox.Show("Please select a destination folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (operation == 3)
            {
                var result = MessageBox.Show($"Are you sure you want to delete {_files.Count} files?", 
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            ExecuteButton.IsEnabled = false;
            ProgressBar.Maximum = _files.Count;
            ProgressBar.Value = 0;

            await Task.Run(() => ProcessFiles(operation));

            ExecuteButton.IsEnabled = true;
            MessageBox.Show("Batch operation completed.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ProcessFiles(int operation)
        {
            int processed = 0;
            
            foreach (var file in _files)
            {
                try
                {
                    switch (operation)
                    {
                        case 0: case 4: case 5: case 6: case 7: case 8:
                            var newPath = Path.Combine(Path.GetDirectoryName(file.FullPath) ?? "", file.NewName);
                            if (file.FullPath != newPath)
                                File.Move(file.FullPath, newPath);
                            file.Status = "Done";
                            break;
                        case 1:
                            File.Move(file.FullPath, file.NewName);
                            file.Status = "Moved";
                            break;
                        case 2:
                            File.Copy(file.FullPath, file.NewName, false);
                            file.Status = "Copied";
                            break;
                        case 3:
                            File.Delete(file.FullPath);
                            file.Status = "Deleted";
                            break;
                    }
                }
                catch (Exception ex)
                {
                    file.Status = $"Error: {ex.Message}";
                }

                processed++;
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = processed;
                    ProgressText.Text = $"{processed} / {_files.Count}";
                    FilesList.Items.Refresh();
                });
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class BatchFileItem
    {
        public string FullPath { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public string NewName { get; set; } = "";
        public string Status { get; set; } = "Pending";
    }
}
