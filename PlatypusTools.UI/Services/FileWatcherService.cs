using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Monitors file system changes and raises events for file operations.
    /// Supports debouncing and batching of events.
    /// </summary>
    public class FileWatcherService : IDisposable
    {
        private static FileWatcherService? _instance;
        public static FileWatcherService Instance => _instance ??= new FileWatcherService();

        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly ConcurrentQueue<FileChangeEvent> _pendingChanges = new();
        private readonly System.Timers.Timer _debounceTimer;
        private readonly object _lock = new();
        private bool _isProcessing;

        public event EventHandler<FileChangeEvent>? FileChanged;
        public event EventHandler<FileChangeEvent>? FileCreated;
        public event EventHandler<FileChangeEvent>? FileDeleted;
        public event EventHandler<FileChangeEvent>? FileRenamed;
        public event EventHandler<FileChangeEventBatch>? ChangesBatched;

        public FileWatcherService()
        {
            _debounceTimer = new System.Timers.Timer(300);
            _debounceTimer.Elapsed += OnDebounceElapsed;
            _debounceTimer.AutoReset = false;
        }

        /// <summary>
        /// Starts watching a directory for changes.
        /// </summary>
        public void Watch(string directoryPath, string filter = "*.*", bool includeSubdirectories = true)
        {
            if (_watchers.ContainsKey(directoryPath)) return;
            if (!Directory.Exists(directoryPath)) return;

            var watcher = new FileSystemWatcher(directoryPath)
            {
                Filter = filter,
                IncludeSubdirectories = includeSubdirectories,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                              NotifyFilters.LastWrite | NotifyFilters.Size
            };

            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            _watchers[directoryPath] = watcher;
        }

        /// <summary>
        /// Stops watching a directory.
        /// </summary>
        public void StopWatching(string directoryPath)
        {
            if (_watchers.TryRemove(directoryPath, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        /// <summary>
        /// Stops watching all directories.
        /// </summary>
        public void StopAllWatching()
        {
            foreach (var path in _watchers.Keys.ToArray())
            {
                StopWatching(path);
            }
        }

        /// <summary>
        /// Pauses file watching temporarily.
        /// </summary>
        public void Pause()
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
            }
        }

        /// <summary>
        /// Resumes file watching.
        /// </summary>
        public void Resume()
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = true;
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            EnqueueChange(new FileChangeEvent
            {
                ChangeType = FileChangeType.Modified,
                FullPath = e.FullPath,
                Name = e.Name ?? Path.GetFileName(e.FullPath)
            });
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            EnqueueChange(new FileChangeEvent
            {
                ChangeType = FileChangeType.Created,
                FullPath = e.FullPath,
                Name = e.Name ?? Path.GetFileName(e.FullPath)
            });
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            EnqueueChange(new FileChangeEvent
            {
                ChangeType = FileChangeType.Deleted,
                FullPath = e.FullPath,
                Name = e.Name ?? Path.GetFileName(e.FullPath)
            });
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            EnqueueChange(new FileChangeEvent
            {
                ChangeType = FileChangeType.Renamed,
                FullPath = e.FullPath,
                Name = e.Name ?? Path.GetFileName(e.FullPath),
                OldPath = e.OldFullPath,
                OldName = e.OldName
            });
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FileWatcher error: {e.GetException().Message}");
        }

        private void EnqueueChange(FileChangeEvent change)
        {
            change.Timestamp = DateTime.Now;
            _pendingChanges.Enqueue(change);
            
            lock (_lock)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                var changes = new System.Collections.Generic.List<FileChangeEvent>();
                while (_pendingChanges.TryDequeue(out var change))
                {
                    changes.Add(change);
                }

                if (changes.Count > 0)
                {
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (var change in changes)
                        {
                            RaiseEvent(change);
                        }

                        if (changes.Count > 1)
                        {
                            ChangesBatched?.Invoke(this, new FileChangeEventBatch { Changes = changes });
                        }
                    });
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void RaiseEvent(FileChangeEvent change)
        {
            switch (change.ChangeType)
            {
                case FileChangeType.Created:
                    FileCreated?.Invoke(this, change);
                    break;
                case FileChangeType.Modified:
                    FileChanged?.Invoke(this, change);
                    break;
                case FileChangeType.Deleted:
                    FileDeleted?.Invoke(this, change);
                    break;
                case FileChangeType.Renamed:
                    FileRenamed?.Invoke(this, change);
                    break;
            }
        }

        public void Dispose()
        {
            StopAllWatching();
            _debounceTimer.Dispose();
        }
    }

    public class FileChangeEvent
    {
        public FileChangeType ChangeType { get; set; }
        public string FullPath { get; set; } = "";
        public string Name { get; set; } = "";
        public string? OldPath { get; set; }
        public string? OldName { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class FileChangeEventBatch
    {
        public System.Collections.Generic.List<FileChangeEvent> Changes { get; set; } = new();
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted,
        Renamed
    }
}
