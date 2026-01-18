using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    public partial class SearchWindow : Window
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly List<FileInfo> _results = new();

        public SearchWindow()
        {
            InitializeComponent();
            SearchPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Search Folder" };
            if (dialog.ShowDialog() == true)
            {
                SearchPath.Text = dialog.FolderName;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchPath.Text) || !Directory.Exists(SearchPath.Text))
            {
                MessageBox.Show("Please select a valid folder to search.", "Invalid Path", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _results.Clear();
            ResultsList.ItemsSource = null;
            SearchButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ResultCount.Text = "Searching...";

            try
            {
                var options = BuildSearchOptions();
                await Task.Run(() => PerformSearch(SearchPath.Text, options, _cancellationTokenSource.Token));
                
                ResultsList.ItemsSource = _results;
                ResultCount.Text = $"{_results.Count:N0} files found";
            }
            catch (OperationCanceledException)
            {
                ResultCount.Text = $"Search cancelled. {_results.Count:N0} files found.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Search error: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SearchButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        private SearchOptions BuildSearchOptions()
        {
            return new SearchOptions
            {
                Pattern = SearchPattern.Text,
                UseRegex = UseRegex.IsChecked == true,
                ContentPattern = ContentPattern.Text,
                ContentRegex = ContentRegex.IsChecked == true,
                IncludeSubfolders = IncludeSubfolders.IsChecked == true,
                CaseSensitive = CaseSensitive.IsChecked == true,
                IncludeHidden = IncludeHidden.IsChecked == true,
                MinSize = ParseSize(MinSize.Text, MinSizeUnit.SelectedIndex),
                MaxSize = ParseSize(MaxSize.Text, MaxSizeUnit.SelectedIndex),
                DateFrom = DateFrom.SelectedDate,
                DateTo = DateTo.SelectedDate
            };
        }

        private static long? ParseSize(string text, int unitIndex)
        {
            if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out var value))
                return null;
            
            return unitIndex switch
            {
                0 => value,
                1 => value * 1024,
                2 => value * 1024 * 1024,
                3 => value * 1024 * 1024 * 1024,
                _ => value
            };
        }

        private void PerformSearch(string rootPath, SearchOptions options, CancellationToken token)
        {
            var searchOption = options.IncludeSubfolders 
                ? SearchOption.AllDirectories 
                : SearchOption.TopDirectoryOnly;
            
            Regex? nameRegex = null;
            Regex? contentRegex = null;
            
            if (options.UseRegex && !string.IsNullOrEmpty(options.Pattern))
            {
                var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                nameRegex = new Regex(options.Pattern, regexOptions);
            }
            
            if (options.ContentRegex && !string.IsNullOrEmpty(options.ContentPattern))
            {
                var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                contentRegex = new Regex(options.ContentPattern, regexOptions);
            }

            IEnumerable<string> files;
            try
            {
                var pattern = options.UseRegex ? "*.*" : options.Pattern;
                files = Directory.EnumerateFiles(rootPath, pattern, searchOption);
            }
            catch
            {
                return;
            }

            foreach (var filePath in files)
            {
                token.ThrowIfCancellationRequested();
                
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    
                    if (!options.IncludeHidden && (fileInfo.Attributes & FileAttributes.Hidden) != 0)
                        continue;
                    
                    if (nameRegex != null && !nameRegex.IsMatch(fileInfo.Name))
                        continue;
                    
                    if (options.MinSize.HasValue && fileInfo.Length < options.MinSize.Value)
                        continue;
                    
                    if (options.MaxSize.HasValue && fileInfo.Length > options.MaxSize.Value)
                        continue;
                    
                    if (options.DateFrom.HasValue && fileInfo.LastWriteTime < options.DateFrom.Value)
                        continue;
                    
                    if (options.DateTo.HasValue && fileInfo.LastWriteTime > options.DateTo.Value.AddDays(1))
                        continue;
                    
                    if (!string.IsNullOrEmpty(options.ContentPattern))
                    {
                        try
                        {
                            var content = File.ReadAllText(filePath);
                            bool matches = contentRegex != null 
                                ? contentRegex.IsMatch(content)
                                : content.Contains(options.ContentPattern, 
                                    options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                            
                            if (!matches) continue;
                        }
                        catch { continue; }
                    }
                    
                    _results.Add(fileInfo);
                    
                    if (_results.Count % 100 == 0)
                    {
                        Dispatcher.Invoke(() => ResultCount.Text = $"Found {_results.Count:N0} files...");
                    }
                }
                catch { /* Skip inaccessible files */ }
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        private void OpenSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsList.SelectedItem is FileInfo fileInfo)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(fileInfo.FullName) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open file: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private class SearchOptions
        {
            public string Pattern { get; set; } = "*.*";
            public bool UseRegex { get; set; }
            public string? ContentPattern { get; set; }
            public bool ContentRegex { get; set; }
            public bool IncludeSubfolders { get; set; } = true;
            public bool CaseSensitive { get; set; }
            public bool IncludeHidden { get; set; }
            public long? MinSize { get; set; }
            public long? MaxSize { get; set; }
            public DateTime? DateFrom { get; set; }
            public DateTime? DateTo { get; set; }
        }
    }
}
