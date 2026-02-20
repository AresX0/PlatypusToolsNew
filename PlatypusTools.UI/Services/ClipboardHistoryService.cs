using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for tracking clipboard history with pinning, search, and rich content preview.
    /// </summary>
    public class ClipboardHistoryService : BindableBase, IDisposable
    {
        private static ClipboardHistoryService? _instance;
        public static ClipboardHistoryService Instance => _instance ??= new ClipboardHistoryService();

        public class ClipboardEntry
        {
            public int Id { get; set; }
            public DateTime Timestamp { get; set; }
            public string ContentType { get; set; } = "Text"; // Text, Image, FileDrop, Html
            public string? TextContent { get; set; }
            public string? Preview { get; set; }
            public BitmapSource? ImageContent { get; set; }
            public string[]? FilePaths { get; set; }
            public bool IsPinned { get; set; }
            public int CharCount => TextContent?.Length ?? 0;
            public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
            public string TypeIcon => ContentType switch
            {
                "Image" => "🖼️",
                "FileDrop" => "📁",
                "Html" => "🌐",
                _ => "📝"
            };
        }

        private readonly DispatcherTimer _pollTimer;
        private string? _lastClipboardText;
        private int _nextId = 1;
        private int _maxEntries = 100;

        public ObservableCollection<ClipboardEntry> History { get; } = new();

        public int MaxEntries
        {
            get => _maxEntries;
            set { _maxEntries = value; OnPropertyChanged(); TrimHistory(); }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); if (value) _pollTimer.Start(); else _pollTimer.Stop(); }
        }

        private ClipboardHistoryService()
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _pollTimer.Tick += PollClipboard;
            // Don't auto-start; let the ViewModel start it
        }

        public void Start()
        {
            if (_isEnabled) _pollTimer.Start();
        }

        public void Stop()
        {
            _pollTimer.Stop();
        }

        private void PollClipboard(object? sender, EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (text != _lastClipboardText && !string.IsNullOrEmpty(text))
                    {
                        _lastClipboardText = text;
                        AddEntry(new ClipboardEntry
                        {
                            Id = _nextId++,
                            Timestamp = DateTime.Now,
                            ContentType = Clipboard.ContainsText(TextDataFormat.Html) ? "Html" : "Text",
                            TextContent = text,
                            Preview = text.Length > 200 ? text[..200] + "..." : text
                        });
                    }
                }
                else if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        AddEntry(new ClipboardEntry
                        {
                            Id = _nextId++,
                            Timestamp = DateTime.Now,
                            ContentType = "Image",
                            ImageContent = image,
                            Preview = $"Image {image.PixelWidth}x{image.PixelHeight}"
                        });
                        _lastClipboardText = null;
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    var paths = new string[files.Count];
                    files.CopyTo(paths, 0);
                    var preview = string.Join(", ", paths.Select(System.IO.Path.GetFileName));

                    AddEntry(new ClipboardEntry
                    {
                        Id = _nextId++,
                        Timestamp = DateTime.Now,
                        ContentType = "FileDrop",
                        FilePaths = paths,
                        TextContent = string.Join("\n", paths),
                        Preview = preview.Length > 200 ? preview[..200] + "..." : preview
                    });
                    _lastClipboardText = null;
                }
            }
            catch { } // Clipboard can throw if locked by another app
        }

        private void AddEntry(ClipboardEntry entry)
        {
            // Don't add duplicates back-to-back
            if (History.Count > 0 && History[0].Preview == entry.Preview && !History[0].IsPinned)
                return;

            History.Insert(0, entry);
            TrimHistory();
        }

        private void TrimHistory()
        {
            while (History.Count > _maxEntries)
            {
                var oldest = History.LastOrDefault(h => !h.IsPinned);
                if (oldest != null)
                    History.Remove(oldest);
                else
                    break;
            }
        }

        public void TogglePin(ClipboardEntry entry)
        {
            entry.IsPinned = !entry.IsPinned;
        }

        public void RestoreToClipboard(ClipboardEntry entry)
        {
            try
            {
                if (entry.ContentType == "Image" && entry.ImageContent != null)
                    Clipboard.SetImage(entry.ImageContent);
                else if (entry.ContentType == "FileDrop" && entry.FilePaths != null)
                {
                    var col = new System.Collections.Specialized.StringCollection();
                    col.AddRange(entry.FilePaths);
                    Clipboard.SetFileDropList(col);
                }
                else if (!string.IsNullOrEmpty(entry.TextContent))
                {
                    _lastClipboardText = entry.TextContent; // Prevent re-add
                    Clipboard.SetText(entry.TextContent);
                }
            }
            catch { }
        }

        public void ClearHistory()
        {
            var pinned = History.Where(h => h.IsPinned).ToList();
            History.Clear();
            foreach (var p in pinned)
                History.Add(p);
        }

        public void DeleteEntry(ClipboardEntry entry)
        {
            History.Remove(entry);
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            GC.SuppressFinalize(this);
        }
    }
}
