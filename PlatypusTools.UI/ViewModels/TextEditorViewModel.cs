using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Represents a single open document tab in the text editor.
    /// </summary>
    public class EditorDocumentViewModel : BindableBase
    {
        private string _fileName = "Untitled";
        public string FileName { get => _fileName; set { SetProperty(ref _fileName, value); RaisePropertyChanged(nameof(TabHeader)); } }

        private string _filePath = string.Empty;
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    IsModified = true;
                    UpdateLineCount();
                    UpdateCharCount();
                }
            }
        }

        private string _originalContent = string.Empty;
        public string OriginalContent { get => _originalContent; set => SetProperty(ref _originalContent, value); }

        private bool _isModified;
        public bool IsModified { get => _isModified; set { SetProperty(ref _isModified, value); RaisePropertyChanged(nameof(TabHeader)); } }

        private int _lineCount = 1;
        public int LineCount { get => _lineCount; set => SetProperty(ref _lineCount, value); }

        private int _charCount;
        public int CharCount { get => _charCount; set => SetProperty(ref _charCount, value); }

        private int _caretLine = 1;
        public int CaretLine { get => _caretLine; set => SetProperty(ref _caretLine, value); }

        private int _caretColumn = 1;
        public int CaretColumn { get => _caretColumn; set => SetProperty(ref _caretColumn, value); }

        private Encoding _encoding = Encoding.UTF8;
        public Encoding FileEncoding { get => _encoding; set => SetProperty(ref _encoding, value); }

        private string _language = "Plain Text";
        public string Language { get => _language; set => SetProperty(ref _language, value); }

        public string TabHeader => IsModified ? $"● {FileName}" : FileName;

        private void UpdateLineCount()
        {
            LineCount = string.IsNullOrEmpty(_content) ? 1 : _content.Split('\n').Length;
        }

        private void UpdateCharCount()
        {
            CharCount = _content?.Length ?? 0;
        }

        /// <summary>
        /// Detects the language based on file extension.
        /// </summary>
        public static string DetectLanguage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "Plain Text";
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".cs" => "C#",
                ".xaml" or ".xml" or ".csproj" or ".props" or ".targets" or ".resx" or ".config" or ".manifest" => "XML",
                ".json" => "JSON",
                ".js" or ".mjs" => "JavaScript",
                ".ts" or ".tsx" => "TypeScript",
                ".py" => "Python",
                ".ps1" or ".psm1" or ".psd1" => "PowerShell",
                ".bat" or ".cmd" => "Batch",
                ".sh" or ".bash" => "Shell",
                ".html" or ".htm" => "HTML",
                ".css" or ".scss" or ".less" => "CSS",
                ".sql" => "SQL",
                ".md" or ".markdown" => "Markdown",
                ".yaml" or ".yml" => "YAML",
                ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => "C++",
                ".c" => "C",
                ".java" => "Java",
                ".rb" => "Ruby",
                ".go" => "Go",
                ".rs" => "Rust",
                ".php" => "PHP",
                ".lua" => "Lua",
                ".r" => "R",
                ".swift" => "Swift",
                ".kt" or ".kts" => "Kotlin",
                ".ini" or ".cfg" => "INI",
                ".toml" => "TOML",
                ".log" or ".txt" => "Plain Text",
                ".csv" or ".tsv" => "CSV",
                ".reg" => "Registry",
                _ => "Plain Text"
            };
        }
    }

    /// <summary>
    /// ViewModel for the text editor (Notepad++ style) with multi-tab support, find/replace, and more.
    /// </summary>
    public class TextEditorViewModel : BindableBase
    {
        public TextEditorViewModel()
        {
            Documents = new ObservableCollection<EditorDocumentViewModel>();

            NewFileCommand = new RelayCommand(_ => NewFile());
            OpenFileCommand = new RelayCommand(_ => OpenFile());
            SaveFileCommand = new RelayCommand(_ => SaveFile(), _ => ActiveDocument != null);
            SaveAsCommand = new RelayCommand(_ => SaveAs(), _ => ActiveDocument != null);
            SaveAllCommand = new RelayCommand(_ => SaveAll(), _ => Documents.Any(d => d.IsModified));
            CloseTabCommand = new RelayCommand(param => CloseTab(param as EditorDocumentViewModel));
            CloseActiveTabCommand = new RelayCommand(_ => CloseTab(ActiveDocument), _ => ActiveDocument != null);

            FindNextCommand = new RelayCommand(_ => FindNext(), _ => !string.IsNullOrEmpty(SearchText));
            FindPreviousCommand = new RelayCommand(_ => FindPrevious(), _ => !string.IsNullOrEmpty(SearchText));
            ReplaceNextCommand = new RelayCommand(_ => ReplaceNext(), _ => !string.IsNullOrEmpty(SearchText));
            ReplaceAllCommand = new RelayCommand(_ => ReplaceAll(), _ => !string.IsNullOrEmpty(SearchText));

            GoToLineCommand = new RelayCommand(_ => GoToLine());
            ToggleWordWrapCommand = new RelayCommand(_ => WordWrap = !WordWrap);
            ToggleShowWhitespaceCommand = new RelayCommand(_ => ShowWhitespace = !ShowWhitespace);
            ToggleLineNumbersCommand = new RelayCommand(_ => ShowLineNumbers = !ShowLineNumbers);
            ToggleFindPanelCommand = new RelayCommand(_ => ShowFindPanel = !ShowFindPanel);

            ChangeEncodingCommand = new RelayCommand(param =>
            {
                if (param is string encName && ActiveDocument != null)
                {
                    ActiveDocument.FileEncoding = encName switch
                    {
                        "UTF-8" => Encoding.UTF8,
                        "UTF-8 BOM" => new UTF8Encoding(true),
                        "ASCII" => Encoding.ASCII,
                        "UTF-16 LE" => Encoding.Unicode,
                        "UTF-16 BE" => Encoding.BigEndianUnicode,
                        "Windows-1252" => Encoding.GetEncoding(1252),
                        _ => Encoding.UTF8
                    };
                    ActiveDocument.IsModified = true;
                    StatusMessage = $"Encoding changed to {encName}";
                }
            });

            IncreaseFontSizeCommand = new RelayCommand(_ => { if (FontSize < 72) FontSize += 2; });
            DecreaseFontSizeCommand = new RelayCommand(_ => { if (FontSize > 6) FontSize -= 2; });

            // Start with a new empty document
            NewFile();
        }

        public ObservableCollection<EditorDocumentViewModel> Documents { get; }

        private EditorDocumentViewModel? _activeDocument;
        public EditorDocumentViewModel? ActiveDocument
        {
            get => _activeDocument;
            set
            {
                SetProperty(ref _activeDocument, value);
                RaisePropertyChanged(nameof(StatusInfo));
                ((RelayCommand)SaveFileCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveAsCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CloseActiveTabCommand).RaiseCanExecuteChanged();
            }
        }

        private bool _wordWrap;
        public bool WordWrap { get => _wordWrap; set => SetProperty(ref _wordWrap, value); }

        private bool _showLineNumbers = true;
        public bool ShowLineNumbers { get => _showLineNumbers; set => SetProperty(ref _showLineNumbers, value); }

        private bool _showWhitespace;
        public bool ShowWhitespace { get => _showWhitespace; set => SetProperty(ref _showWhitespace, value); }

        private double _fontSize = 14;
        public double FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

        private bool _showFindPanel;
        public bool ShowFindPanel { get => _showFindPanel; set => SetProperty(ref _showFindPanel, value); }

        private bool _showReplaceRow;
        public bool ShowReplaceRow { get => _showReplaceRow; set => SetProperty(ref _showReplaceRow, value); }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                ((RelayCommand)FindNextCommand).RaiseCanExecuteChanged();
                ((RelayCommand)FindPreviousCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ReplaceNextCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ReplaceAllCommand).RaiseCanExecuteChanged();
                UpdateMatchCount();
            }
        }

        private string _replaceText = string.Empty;
        public string ReplaceText { get => _replaceText; set => SetProperty(ref _replaceText, value); }

        private bool _matchCase;
        public bool MatchCase { get => _matchCase; set { SetProperty(ref _matchCase, value); UpdateMatchCount(); } }

        private bool _useRegex;
        public bool UseRegex { get => _useRegex; set { SetProperty(ref _useRegex, value); UpdateMatchCount(); } }

        private bool _wholeWord;
        public bool WholeWord { get => _wholeWord; set { SetProperty(ref _wholeWord, value); UpdateMatchCount(); } }

        private int _matchCount;
        public int MatchCount { get => _matchCount; set => SetProperty(ref _matchCount, value); }

        private int _currentMatchIndex;
        public int CurrentMatchIndex { get => _currentMatchIndex; set => SetProperty(ref _currentMatchIndex, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _goToLineNumber = 1;
        public int GoToLineNumber { get => _goToLineNumber; set => SetProperty(ref _goToLineNumber, value); }

        public string StatusInfo => ActiveDocument != null
            ? $"Ln {ActiveDocument.CaretLine}, Col {ActiveDocument.CaretColumn} | {ActiveDocument.LineCount} lines | {ActiveDocument.CharCount} chars | {ActiveDocument.Language} | {ActiveDocument.FileEncoding.EncodingName}"
            : "No document";

        public void RefreshStatusInfo() => RaisePropertyChanged(nameof(StatusInfo));

        // Find/Replace state (these fire events to notify the view for selection)
        public event Action<int, int>? SelectionRequested; // startIndex, length
        public event Action<int>? GoToLineRequested; // lineNumber

        #region Commands
        public ICommand NewFileCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand SaveFileCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand CloseActiveTabCommand { get; }
        public ICommand FindNextCommand { get; }
        public ICommand FindPreviousCommand { get; }
        public ICommand ReplaceNextCommand { get; }
        public ICommand ReplaceAllCommand { get; }
        public ICommand GoToLineCommand { get; }
        public ICommand ToggleWordWrapCommand { get; }
        public ICommand ToggleShowWhitespaceCommand { get; }
        public ICommand ToggleLineNumbersCommand { get; }
        public ICommand ToggleFindPanelCommand { get; }
        public ICommand ChangeEncodingCommand { get; }
        public ICommand IncreaseFontSizeCommand { get; }
        public ICommand DecreaseFontSizeCommand { get; }
        #endregion

        #region File Operations

        public void NewFile()
        {
            var doc = new EditorDocumentViewModel
            {
                FileName = $"Untitled-{Documents.Count(d => d.FileName.StartsWith("Untitled")) + 1}",
                Content = string.Empty,
                OriginalContent = string.Empty,
                IsModified = false
            };
            Documents.Add(doc);
            ActiveDocument = doc;
            StatusMessage = "New file created";
        }

        public void OpenFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|Text Files (*.txt)|*.txt|C# Files (*.cs)|*.cs|XML/XAML Files (*.xml;*.xaml)|*.xml;*.xaml|JSON Files (*.json)|*.json|PowerShell (*.ps1)|*.ps1|Markdown (*.md)|*.md|HTML (*.html;*.htm)|*.html;*.htm|CSS (*.css)|*.css|JavaScript (*.js)|*.js|Python (*.py)|*.py|SQL (*.sql)|*.sql|Log Files (*.log)|*.log",
                Multiselect = true,
                Title = "Open File(s)"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var filePath in dlg.FileNames)
                {
                    OpenFilePath(filePath);
                }
            }
        }

        public void OpenFilePath(string filePath)
        {
            // Check if already open
            var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                StatusMessage = $"Switched to {existing.FileName}";
                return;
            }

            try
            {
                var encoding = DetectFileEncoding(filePath);
                var content = File.ReadAllText(filePath, encoding);

                var doc = new EditorDocumentViewModel
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    Content = content,
                    OriginalContent = content,
                    IsModified = false,
                    FileEncoding = encoding,
                    Language = EditorDocumentViewModel.DetectLanguage(filePath)
                };

                // If the only document is an untouched "Untitled", replace it
                if (Documents.Count == 1 && !Documents[0].IsModified && string.IsNullOrEmpty(Documents[0].FilePath) && string.IsNullOrEmpty(Documents[0].Content))
                {
                    Documents.RemoveAt(0);
                }

                Documents.Add(doc);
                ActiveDocument = doc;
                StatusMessage = $"Opened {doc.FileName} ({encoding.EncodingName})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening file: {ex.Message}";
                MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SaveFile()
        {
            if (ActiveDocument == null) return;

            if (string.IsNullOrEmpty(ActiveDocument.FilePath))
            {
                SaveAs();
                return;
            }

            try
            {
                File.WriteAllText(ActiveDocument.FilePath, ActiveDocument.Content, ActiveDocument.FileEncoding);
                ActiveDocument.OriginalContent = ActiveDocument.Content;
                ActiveDocument.IsModified = false;
                StatusMessage = $"Saved {ActiveDocument.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving: {ex.Message}";
                MessageBox.Show($"Failed to save file:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SaveAs()
        {
            if (ActiveDocument == null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "All Files (*.*)|*.*|Text Files (*.txt)|*.txt|C# Files (*.cs)|*.cs|XML Files (*.xml)|*.xml|JSON Files (*.json)|*.json",
                FileName = ActiveDocument.FileName,
                Title = "Save As"
            };

            if (dlg.ShowDialog() == true)
            {
                ActiveDocument.FilePath = dlg.FileName;
                ActiveDocument.FileName = Path.GetFileName(dlg.FileName);
                ActiveDocument.Language = EditorDocumentViewModel.DetectLanguage(dlg.FileName);
                SaveFile();
            }
        }

        public void SaveAll()
        {
            foreach (var doc in Documents.Where(d => d.IsModified))
            {
                var prev = ActiveDocument;
                ActiveDocument = doc;
                SaveFile();
                ActiveDocument = prev;
            }
            StatusMessage = "All files saved";
        }

        public void CloseTab(EditorDocumentViewModel? doc)
        {
            if (doc == null) return;

            if (doc.IsModified)
            {
                var result = MessageBox.Show($"Save changes to {doc.FileName}?", "Unsaved Changes",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                {
                    ActiveDocument = doc;
                    SaveFile();
                    if (doc.IsModified) return; // Save was cancelled
                }
            }

            var index = Documents.IndexOf(doc);
            Documents.Remove(doc);

            if (Documents.Count == 0)
            {
                NewFile();
            }
            else if (ActiveDocument == doc || ActiveDocument == null)
            {
                ActiveDocument = Documents[Math.Min(index, Documents.Count - 1)];
            }
        }

        #endregion

        #region Find/Replace

        private int _lastSearchIndex;

        private void UpdateMatchCount()
        {
            if (ActiveDocument == null || string.IsNullOrEmpty(SearchText))
            {
                MatchCount = 0;
                return;
            }

            try
            {
                var pattern = BuildSearchPattern();
                var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var matches = Regex.Matches(ActiveDocument.Content ?? string.Empty, pattern, options);
                MatchCount = matches.Count;
            }
            catch
            {
                MatchCount = 0;
            }
        }

        public void FindNext()
        {
            if (ActiveDocument == null || string.IsNullOrEmpty(SearchText)) return;

            try
            {
                var pattern = BuildSearchPattern();
                var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var content = ActiveDocument.Content ?? string.Empty;
                var match = Regex.Match(content, pattern, options);

                // Search from current position
                var startPos = Math.Min(_lastSearchIndex + 1, content.Length);
                match = Regex.Match(content.Substring(startPos), pattern, options);

                if (match.Success)
                {
                    _lastSearchIndex = startPos + match.Index;
                    SelectionRequested?.Invoke(_lastSearchIndex, match.Length);
                    CurrentMatchIndex++;
                    StatusMessage = $"Match {CurrentMatchIndex} of {MatchCount}";
                }
                else
                {
                    // Wrap around
                    match = Regex.Match(content, pattern, options);
                    if (match.Success)
                    {
                        _lastSearchIndex = match.Index;
                        CurrentMatchIndex = 1;
                        SelectionRequested?.Invoke(_lastSearchIndex, match.Length);
                        StatusMessage = $"Wrapped. Match {CurrentMatchIndex} of {MatchCount}";
                    }
                    else
                    {
                        StatusMessage = "No matches found";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search error: {ex.Message}";
            }
        }

        public void FindPrevious()
        {
            if (ActiveDocument == null || string.IsNullOrEmpty(SearchText)) return;

            try
            {
                var pattern = BuildSearchPattern();
                var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var content = ActiveDocument.Content ?? string.Empty;
                var matches = Regex.Matches(content, pattern, options);

                if (matches.Count == 0)
                {
                    StatusMessage = "No matches found";
                    return;
                }

                // Find the match before current position
                Match? prevMatch = null;
                foreach (Match m in matches)
                {
                    if (m.Index >= _lastSearchIndex) break;
                    prevMatch = m;
                }

                if (prevMatch == null)
                {
                    // Wrap to last match
                    prevMatch = matches[matches.Count - 1];
                    CurrentMatchIndex = MatchCount;
                }
                else
                {
                    CurrentMatchIndex--;
                    if (CurrentMatchIndex < 1) CurrentMatchIndex = MatchCount;
                }

                _lastSearchIndex = prevMatch.Index;
                SelectionRequested?.Invoke(prevMatch.Index, prevMatch.Length);
                StatusMessage = $"Match {CurrentMatchIndex} of {MatchCount}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search error: {ex.Message}";
            }
        }

        public void ReplaceNext()
        {
            if (ActiveDocument == null || string.IsNullOrEmpty(SearchText)) return;

            try
            {
                var pattern = BuildSearchPattern();
                var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var content = ActiveDocument.Content ?? string.Empty;

                var match = Regex.Match(content.Substring(Math.Min(_lastSearchIndex, content.Length)), pattern, options);
                if (match.Success)
                {
                    var absoluteIndex = _lastSearchIndex + match.Index;
                    var before = content.Substring(0, absoluteIndex);
                    var after = content.Substring(absoluteIndex + match.Length);
                    ActiveDocument.Content = before + (ReplaceText ?? string.Empty) + after;
                    _lastSearchIndex = absoluteIndex + (ReplaceText?.Length ?? 0);
                    UpdateMatchCount();
                    StatusMessage = $"Replaced. {MatchCount} matches remaining";
                    FindNext();
                }
                else
                {
                    StatusMessage = "No more matches";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Replace error: {ex.Message}";
            }
        }

        public void ReplaceAll()
        {
            if (ActiveDocument == null || string.IsNullOrEmpty(SearchText)) return;

            try
            {
                var pattern = BuildSearchPattern();
                var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var count = Regex.Matches(ActiveDocument.Content ?? string.Empty, pattern, options).Count;
                ActiveDocument.Content = Regex.Replace(ActiveDocument.Content ?? string.Empty, pattern, ReplaceText ?? string.Empty, options);
                UpdateMatchCount();
                StatusMessage = $"Replaced {count} occurrences";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Replace error: {ex.Message}";
            }
        }

        private string BuildSearchPattern()
        {
            var text = UseRegex ? SearchText : Regex.Escape(SearchText);
            if (WholeWord) text = $@"\b{text}\b";
            return text;
        }

        #endregion

        #region Navigation

        public void GoToLine()
        {
            if (ActiveDocument == null) return;
            var line = Math.Max(1, Math.Min(GoToLineNumber, ActiveDocument.LineCount));
            GoToLineRequested?.Invoke(line);
            StatusMessage = $"Moved to line {line}";
        }

        #endregion

        #region Helpers

        private static Encoding DetectFileEncoding(string filePath)
        {
            var bom = new byte[4];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(bom, 0, 4);
                if (bytesRead < 2) return Encoding.UTF8;
            }

            if (bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode; // UTF-16 LE
            if (bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode; // UTF-16 BE
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true); // UTF-8 BOM

            return Encoding.UTF8;
        }

        #endregion
    }
}
