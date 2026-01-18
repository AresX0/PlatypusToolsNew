using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Enhanced clipboard service supporting file operations, history, and multi-format data.
    /// </summary>
    public class ClipboardService
    {
        private static ClipboardService? _instance;
        public static ClipboardService Instance => _instance ??= new ClipboardService();

        private readonly List<ClipboardEntry> _history = new();
        private const int MaxHistorySize = 50;

        public event EventHandler<ClipboardEntry>? ClipboardChanged;

        /// <summary>
        /// Copies files to clipboard.
        /// </summary>
        public void CopyFiles(IEnumerable<string> filePaths)
        {
            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var path in filePaths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                    paths.Add(path);
            }

            if (paths.Count > 0)
            {
                Clipboard.SetFileDropList(paths);
                AddToHistory(new ClipboardEntry
                {
                    Type = ClipboardEntryType.Files,
                    FilePaths = paths.Cast<string>().ToList(),
                    Timestamp = DateTime.Now,
                    IsCut = false
                });
            }
        }

        /// <summary>
        /// Cuts files to clipboard (marks for move operation).
        /// </summary>
        public void CutFiles(IEnumerable<string> filePaths)
        {
            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var path in filePaths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                    paths.Add(path);
            }

            if (paths.Count > 0)
            {
                var data = new DataObject();
                data.SetFileDropList(paths);
                data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 2, 0, 0, 0 }));
                Clipboard.SetDataObject(data, true);

                AddToHistory(new ClipboardEntry
                {
                    Type = ClipboardEntryType.Files,
                    FilePaths = paths.Cast<string>().ToList(),
                    Timestamp = DateTime.Now,
                    IsCut = true
                });
            }
        }

        /// <summary>
        /// Copies text to clipboard.
        /// </summary>
        public void CopyText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                AddToHistory(new ClipboardEntry
                {
                    Type = ClipboardEntryType.Text,
                    Text = text,
                    Timestamp = DateTime.Now
                });
            }
        }

        /// <summary>
        /// Gets files from clipboard.
        /// </summary>
        public List<string> GetFiles()
        {
            var result = new List<string>();
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(file))
                        result.Add(file);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets text from clipboard.
        /// </summary>
        public string? GetText()
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }

        /// <summary>
        /// Checks if clipboard contains files.
        /// </summary>
        public bool ContainsFiles() => Clipboard.ContainsFileDropList();

        /// <summary>
        /// Checks if clipboard operation is a cut (move) operation.
        /// </summary>
        public bool IsCutOperation()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data?.GetDataPresent("Preferred DropEffect") == true)
                {
                    if (data.GetData("Preferred DropEffect") is MemoryStream stream && stream.Length >= 4)
                    {
                        var bytes = new byte[4];
                        stream.Position = 0;
                        stream.Read(bytes, 0, 4);
                        return bytes[0] == 2;
                    }
                }
            }
            catch { }
            return false;
        }

        public IReadOnlyList<ClipboardEntry> History => _history.AsReadOnly();

        public void ClearHistory() => _history.Clear();

        public void RestoreFromHistory(ClipboardEntry entry)
        {
            switch (entry.Type)
            {
                case ClipboardEntryType.Files when entry.FilePaths != null:
                    if (entry.IsCut) CutFiles(entry.FilePaths);
                    else CopyFiles(entry.FilePaths);
                    break;
                case ClipboardEntryType.Text when entry.Text != null:
                    CopyText(entry.Text);
                    break;
            }
        }

        private void AddToHistory(ClipboardEntry entry)
        {
            _history.Insert(0, entry);
            while (_history.Count > MaxHistorySize)
                _history.RemoveAt(_history.Count - 1);
            ClipboardChanged?.Invoke(this, entry);
        }
    }

    public class ClipboardEntry
    {
        public ClipboardEntryType Type { get; set; }
        public List<string>? FilePaths { get; set; }
        public string? Text { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCut { get; set; }
    }

    public enum ClipboardEntryType { Files, Text, Image }
}
