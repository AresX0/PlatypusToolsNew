using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PlatypusTools.Core.Services.Video;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Models.VideoEditor;
using Filter = PlatypusTools.Core.Models.Video.Filter;
using MediaType = PlatypusTools.UI.Models.VideoEditor.MediaType;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Native Shotcut-style video editor view.
    /// Combines playlist, player, properties, and timeline panels.
    /// </summary>
    public partial class ShotcutNativeEditorView : UserControl, INotifyPropertyChanged
    {
        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public TimelineModel TimelineModel { get; } = new();
        
        // Filter collections
        public ObservableCollection<Filter> ShotcutFilters { get; } = new();
        public ObservableCollection<Filter> FilteredShotcutFilters { get; } = new();
        
        // Filter commands
        public ICommand ToggleFilterFavoriteCommand { get; }
        public ICommand ApplyShotcutFilterCommand { get; }
        public ICommand RemoveShotcutFilterCommand { get; }
        
        // Filter search
        private string _filterSearchText = string.Empty;
        public string FilterSearchText
        {
            get => _filterSearchText;
            set
            {
                _filterSearchText = value;
                OnPropertyChanged();
                FilterFilters();
            }
        }
        
        // Selected clip for filter application
        public TimelineClip? SelectedClip => _selectedTimelineClip;
        public bool HasSelectedClip => _selectedTimelineClip != null;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private PlaylistItem? _selectedPlaylistItem;
        private TimelineClip? _selectedTimelineClip;
        private Point _dragStartPoint;
        private bool _isDragging;
        
        // Timeline playback
        private readonly DispatcherTimer _timelinePlaybackTimer;
        private bool _isTimelinePlaying;
        private TimeSpan _timelinePosition;
        private TimelineClip? _currentPlayingClip;

        public ShotcutNativeEditorView()
        {
            // Initialize filter commands before InitializeComponent
            ToggleFilterFavoriteCommand = new RelayCommand(param => ExecuteToggleFilterFavorite(param as Filter));
            ApplyShotcutFilterCommand = new RelayCommand(param => ExecuteApplyFilter(param as Filter), _ => HasSelectedClip);
            RemoveShotcutFilterCommand = new RelayCommand(param => ExecuteRemoveFilter(param as Filter), _ => HasSelectedClip);
            
            InitializeComponent();
            DataContext = this;
            PlaylistBox.ItemsSource = PlaylistItems;
            
            // Load filters
            InitializeFilters();
            
            // Initialize timeline playback timer
            _timelinePlaybackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30fps
            };
            _timelinePlaybackTimer.Tick += TimelinePlaybackTimer_Tick;
            
            // Subscribe to timeline events
            Timeline.InsertClipRequested += Timeline_InsertClipRequested;
            Timeline.AppendToTrackRequested += Timeline_AppendToTrackRequested;
            Timeline.PositionChanged += Timeline_PositionChanged;
            Timeline.ClipSelected += Timeline_ClipSelected;
            
            // Subscribe to video player events
            VideoPlayer.Seeked += VideoPlayer_Seeked;
            VideoPlayer.Played += VideoPlayer_Played;
            VideoPlayer.Paused += VideoPlayer_Paused;
        }
        
        private void InitializeFilters()
        {
            var allFilters = FilterLibrary.GetAllFilters();
            foreach (var filter in allFilters)
            {
                ShotcutFilters.Add(filter);
                FilteredShotcutFilters.Add(filter);
            }
        }
        
        private void FilterFilters()
        {
            FilteredShotcutFilters.Clear();
            var query = FilterSearchText?.ToLowerInvariant() ?? "";
            
            foreach (var filter in ShotcutFilters)
            {
                var matchesSearch = string.IsNullOrEmpty(query) || 
                                    filter.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    filter.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (matchesSearch)
                {
                    FilteredShotcutFilters.Add(filter);
                }
            }
        }
        
        private void ExecuteToggleFilterFavorite(Filter? filter)
        {
            if (filter == null) return;
            filter.IsFavorite = !filter.IsFavorite;
        }
        
        private void ExecuteApplyFilter(Filter? filter)
        {
            if (filter == null || _selectedTimelineClip == null) return;
            var newFilter = filter.Clone();
            _selectedTimelineClip.Filters.Add(newFilter);
        }
        
        private void ExecuteRemoveFilter(Filter? filter)
        {
            if (filter == null || _selectedTimelineClip == null) return;
            _selectedTimelineClip.Filters.Remove(filter);
        }
        
        private void TimelinePlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isTimelinePlaying) return;
            
            // Advance timeline position
            _timelinePosition += TimeSpan.FromMilliseconds(33);
            Timeline.Position = _timelinePosition;
            
            // Update the video player with the current clip
            var clipAtPosition = GetClipAtPosition(_timelinePosition);
            if (clipAtPosition != null)
            {
                // If we're on a different clip, load it
                if (_currentPlayingClip != clipAtPosition)
                {
                    _currentPlayingClip = clipAtPosition;
                    VideoPlayer.LoadMedia(clipAtPosition.SourcePath);
                    
                    // Calculate offset within the clip
                    var offsetInClip = _timelinePosition - clipAtPosition.StartTime;
                    var sourcePosition = clipAtPosition.InPoint + offsetInClip;
                    VideoPlayer.SeekTo(sourcePosition);
                    VideoPlayer.Play();
                }
            }
            else
            {
                // No clip at this position - show black or pause
                _currentPlayingClip = null;
            }
            
            // Stop at end of timeline
            if (_timelinePosition >= TimelineModel.Duration)
            {
                StopTimelinePlayback();
            }
        }
        
        private void VideoPlayer_Played(object? sender, EventArgs e)
        {
            // When player is played, start timeline playback if we have clips
            if (!_isTimelinePlaying)
            {
                StartTimelinePlayback();
            }
        }
        
        private void VideoPlayer_Paused(object? sender, EventArgs e)
        {
            // Pause timeline playback when player pauses
            if (_isTimelinePlaying)
            {
                PauseTimelinePlayback();
            }
        }
        
        private void StartTimelinePlayback()
        {
            _isTimelinePlaying = true;
            _timelinePosition = Timeline.Position;
            _timelinePlaybackTimer.Start();
        }
        
        private void PauseTimelinePlayback()
        {
            _isTimelinePlaying = false;
            _timelinePlaybackTimer.Stop();
        }
        
        private void StopTimelinePlayback()
        {
            _isTimelinePlaying = false;
            _timelinePlaybackTimer.Stop();
            _timelinePosition = TimeSpan.Zero;
            Timeline.Position = TimeSpan.Zero;
            _currentPlayingClip = null;
        }
        
        private void Timeline_PositionChanged(object? sender, TimeSpan position)
        {
            // When timeline position changes (via seeking), update video preview
            _timelinePosition = position;
            
            var clipAtPosition = GetClipAtPosition(position);
            if (clipAtPosition != null)
            {
                // Load the clip's source if not already loaded
                var currentSource = VideoPlayer.Source?.LocalPath;
                if (currentSource != clipAtPosition.SourcePath)
                {
                    VideoPlayer.LoadMedia(clipAtPosition.SourcePath);
                }
                
                // Calculate the position within the source file
                var clipStartOnTimeline = clipAtPosition.StartTime;
                var offsetInClip = position - clipStartOnTimeline;
                var sourcePosition = clipAtPosition.InPoint + offsetInClip;
                
                // Seek to the correct position in the source
                VideoPlayer.SeekTo(sourcePosition);
            }
        }
        
        private void Timeline_ClipSelected(object? sender, TimelineClip clip)
        {
            _selectedTimelineClip = clip;
            OnPropertyChanged(nameof(SelectedClip));
            OnPropertyChanged(nameof(HasSelectedClip));
            UpdatePropertiesPanel(clip);
            
            // Load clip into player for preview
            if (!string.IsNullOrEmpty(clip.SourcePath))
            {
                VideoPlayer.LoadMedia(clip.SourcePath);
                VideoPlayer.InPoint = clip.InPoint;
                VideoPlayer.OutPoint = clip.OutPoint;
                VideoPlayer.SeekTo(clip.InPoint);
            }
        }
        
        private void VideoPlayer_Seeked(object? sender, TimeSpan position)
        {
            // Optionally update timeline position when seeking in the player
        }
        
        private TimelineClip? GetClipAtPosition(TimeSpan position)
        {
            // Find the topmost visible clip at the given position
            foreach (var track in TimelineModel.Tracks.Where(t => !t.IsHidden))
            {
                var clip = track.Clips.FirstOrDefault(c => 
                    position >= c.StartTime && position < c.EndTime);
                if (clip != null)
                    return clip;
            }
            return null;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize with sample timeline duration
            TimelineModel.Duration = TimeSpan.FromMinutes(5);
            
            // Initialize default tracks if none exist
            if (TimelineModel.Tracks.Count == 0)
            {
                TimelineModel.Tracks.Add(new TimelineTrack { Name = "V1", Type = TrackType.Video });
                TimelineModel.Tracks.Add(new TimelineTrack { Name = "A1", Type = TrackType.Audio });
            }
            
            // Explicitly set the timeline model to ensure bindings refresh
            Timeline.TimelineModel = TimelineModel;
        }

        #region File Operations

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Media File",
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                         "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg|" +
                         "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddFileToPlaylist(file);
                }
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Project",
                Filter = "PlatypusTools Project|*.ptproj|MLT XML|*.mlt",
                DefaultExt = ".ptproj"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SaveProject(dialog.FileName);
                    MessageBox.Show($"Project saved to:\n{dialog.FileName}", "Saved",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save project:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Save project to a file
        /// </summary>
        private void SaveProject(string filePath)
        {
            var project = new ProjectData
            {
                Version = "1.0",
                SavedAt = DateTime.Now,
                Duration = TimelineModel.Duration,
                FrameRate = TimelineModel.FrameRate
            };

            // Save playlist items
            foreach (var item in PlaylistItems)
            {
                project.PlaylistItems.Add(new ProjectPlaylistItem
                {
                    FilePath = item.FilePath,
                    Name = item.Name,
                    Duration = item.Duration,
                    IsVideo = item.Type == MediaType.Video || item.Type == MediaType.Image
                });
            }

            // Save timeline tracks and clips
            foreach (var track in TimelineModel.Tracks)
            {
                var projectTrack = new ProjectTrack
                {
                    Name = track.Name,
                    Type = track.Type.ToString(),
                    IsHidden = track.IsHidden,
                    IsMuted = track.IsMuted,
                    IsLocked = track.IsLocked,
                    Volume = 1.0 // Default - track-level volume not supported yet
                };

                foreach (var clip in track.Clips)
                {
                    projectTrack.Clips.Add(new ProjectClip
                    {
                        Name = clip.Name,
                        SourcePath = clip.SourcePath,
                        StartPosition = clip.StartTime, // Use StartTime property
                        Duration = clip.Duration,
                        InPoint = clip.InPoint,
                        OutPoint = clip.OutPoint,
                        Speed = 1.0, // Default - speed not supported yet
                        Volume = clip.Gain, // Map Gain to Volume
                        Opacity = 1.0 // Default - opacity not supported yet
                    });
                }

                project.Tracks.Add(projectTrack);
            }

            // Serialize to JSON
            var json = JsonSerializer.Serialize(project, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Load a project file
        /// </summary>
        public void LoadProject(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var project = JsonSerializer.Deserialize<ProjectData>(json);

                if (project == null)
                {
                    MessageBox.Show("Invalid project file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Clear current state
                PlaylistItems.Clear();
                TimelineModel.Tracks.Clear();

                // Restore playlist
                foreach (var item in project.PlaylistItems)
                {
                    if (File.Exists(item.FilePath))
                    {
                        PlaylistItems.Add(new PlaylistItem
                        {
                            FilePath = item.FilePath,
                            Name = item.Name,
                            Duration = item.Duration,
                            Type = item.IsVideo ? MediaType.Video : MediaType.Audio
                        });
                    }
                }

                // Restore timeline tracks
                foreach (var trackData in project.Tracks)
                {
                    var trackType = Enum.TryParse<TrackType>(trackData.Type, out var t) ? t : TrackType.Video;
                    var track = new TimelineTrack
                    {
                        Name = trackData.Name,
                        Type = trackType,
                        IsHidden = trackData.IsHidden,
                        IsMuted = trackData.IsMuted,
                        IsLocked = trackData.IsLocked
                        // Volume is stored but not used yet (no track-level volume)
                    };

                    foreach (var clipData in trackData.Clips)
                    {
                        track.Clips.Add(new TimelineClip
                        {
                            Name = clipData.Name,
                            SourcePath = clipData.SourcePath,
                            StartTime = clipData.StartPosition, // Map StartPosition to StartTime
                            Duration = clipData.Duration,
                            InPoint = clipData.InPoint,
                            OutPoint = clipData.OutPoint,
                            Gain = clipData.Volume // Map Volume to Gain
                            // Speed and Opacity stored but not used yet
                        });
                    }

                    TimelineModel.Tracks.Add(track);
                }

                TimelineModel.Duration = project.Duration;
                Timeline.TimelineModel = TimelineModel;
                Timeline.InvalidateVisual(); // Refresh the timeline display

                MessageBox.Show($"Project loaded: {Path.GetFileName(filePath)}", "Loaded",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load project:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            // Check if timeline has any clips
            var hasClips = TimelineModel.Tracks.Any(t => t.Clips.Count > 0);
            if (!hasClips)
            {
                MessageBox.Show("No clips on timeline to export.\n\nAdd clips from the Playlist to the Timeline first.",
                    "No Clips", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Video",
                Filter = "MP4 Video|*.mp4|MKV Video|*.mkv|WebM Video|*.webm",
                DefaultExt = ".mp4"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                // Convert UI models to Core models for export
                var coreTracks = ConvertToCoreTracks();
                
                // Create export settings based on output format
                var settings = CreateExportSettings(dialog.FileName);
                
                // Create and show progress window
                var progressWindow = new Window
                {
                    Title = "Exporting Video",
                    Width = 450,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.NoResize
                };
                
                var progressGrid = new Grid { Margin = new Thickness(20) };
                progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                progressGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                var phaseLabel = new TextBlock { Text = "Preparing...", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
                Grid.SetRow(phaseLabel, 0);
                
                var progressBar = new ProgressBar { Height = 25, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 10) };
                Grid.SetRow(progressBar, 1);
                
                var messageLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray };
                Grid.SetRow(messageLabel, 2);
                
                var cancelButton = new Button { Content = "Cancel", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
                Grid.SetRow(cancelButton, 3);
                
                progressGrid.Children.Add(phaseLabel);
                progressGrid.Children.Add(progressBar);
                progressGrid.Children.Add(messageLabel);
                progressGrid.Children.Add(cancelButton);
                progressWindow.Content = progressGrid;
                
                using var cts = new System.Threading.CancellationTokenSource();
                cancelButton.Click += (s, args) => cts.Cancel();
                progressWindow.Closing += (s, args) => { if (!cts.IsCancellationRequested) cts.Cancel(); };
                
                // Progress reporting
                var progress = new Progress<Core.Services.Video.ExportProgress>(p =>
                {
                    phaseLabel.Text = p.Phase;
                    progressBar.Value = p.Percent;
                    messageLabel.Text = p.Message;
                });
                
                progressWindow.Show();
                
                // Run export
                var exporter = new Core.Services.Video.SimpleVideoExporter();
                var result = await exporter.ExportAsync(coreTracks, dialog.FileName, settings, progress, cts.Token);
                
                progressWindow.Close();
                
                if (result.Success)
                {
                    var fileSizeMb = result.FileSize / (1024.0 * 1024.0);
                    MessageBox.Show($"Export complete!\n\nFile: {dialog.FileName}\nSize: {fileSizeMb:F1} MB",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Export failed:\n\n{result.ErrorMessage}\n\nSee log for details:\n{result.Log?.Substring(0, Math.Min(500, result.Log?.Length ?? 0))}",
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Export was cancelled.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export error:\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Core.Models.Video.TimelineTrack> ConvertToCoreTracks()
        {
            var coreTracks = new List<Core.Models.Video.TimelineTrack>();
            
            foreach (var uiTrack in TimelineModel.Tracks)
            {
                var coreTrack = new Core.Models.Video.TimelineTrack
                {
                    Name = uiTrack.Name,
                    Type = uiTrack.Type == TrackType.Video 
                        ? Core.Models.Video.TrackType.Video 
                        : Core.Models.Video.TrackType.Audio,
                    IsVisible = !uiTrack.IsHidden,
                    IsMuted = uiTrack.IsMuted,
                    IsLocked = uiTrack.IsLocked,
                    Opacity = uiTrack.Opacity,
                    BlendMode = uiTrack.BlendMode
                };
                
                foreach (var uiClip in uiTrack.Clips)
                {
                    var coreClip = new Core.Models.Video.TimelineClip
                    {
                        Name = uiClip.Name,
                        SourcePath = uiClip.SourcePath,
                        StartPosition = uiClip.StartTime,
                        Duration = uiClip.Duration,
                        SourceStart = uiClip.InPoint,
                        SourceEnd = uiClip.OutPoint,
                        Volume = uiClip.Gain,
                        Type = Core.Models.Video.ClipType.Video
                    };
                    
                    // Copy filters to the core clip
                    foreach (var filter in uiClip.Filters)
                    {
                        coreClip.Filters.Add(filter);
                    }
                    
                    coreTrack.Clips.Add(coreClip);
                }
                
                coreTracks.Add(coreTrack);
            }
            
            return coreTracks;
        }

        private Core.Services.Video.ExportSettings CreateExportSettings(string outputPath)
        {
            var ext = System.IO.Path.GetExtension(outputPath).ToLowerInvariant();
            
            return ext switch
            {
                ".mkv" => new Core.Services.Video.ExportSettings
                {
                    Width = 1920,
                    Height = 1080,
                    FrameRate = 30,
                    VideoCodec = "libx264",
                    AudioCodec = "aac",
                    AudioBitrate = 192,
                    Preset = "medium",
                    Crf = 18,
                    PixelFormat = "yuv420p",
                    Container = "mkv"
                },
                ".webm" => new Core.Services.Video.ExportSettings
                {
                    Width = 1920,
                    Height = 1080,
                    FrameRate = 30,
                    VideoCodec = "libvpx-vp9",
                    AudioCodec = "libopus",
                    AudioBitrate = 128,
                    Preset = "medium",
                    Crf = 23,
                    PixelFormat = "yuv420p",
                    Container = "webm"
                },
                _ => new Core.Services.Video.ExportSettings
                {
                    Width = 1920,
                    Height = 1080,
                    FrameRate = 30,
                    VideoCodec = "libx264",
                    AudioCodec = "aac",
                    AudioBitrate = 192,
                    Preset = "medium",
                    Crf = 18,
                    PixelFormat = "yuv420p",
                    Container = "mp4"
                }
            };
        }

        #endregion

        #region Playlist

        private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            OpenFile_Click(sender, e);
        }

        private void RemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylistItem != null)
            {
                PlaylistItems.Remove(_selectedPlaylistItem);
                _selectedPlaylistItem = null;
            }
        }

        private void AddToTimeline_Click(object sender, RoutedEventArgs e)
        {
            // Get selected item from listbox if not already set
            var itemToAdd = _selectedPlaylistItem ?? PlaylistBox.SelectedItem as PlaylistItem;
            if (itemToAdd == null)
                return;
            
            _selectedPlaylistItem = itemToAdd;

            // Add to the first video track by default
            var track = TimelineModel.Tracks.FirstOrDefault(t => t.Type == TrackType.Video);
            if (track == null)
            {
                // Create a video track if none exists
                track = new TimelineTrack { Name = "V1", Type = TrackType.Video };
                TimelineModel.Tracks.Add(track);
            }

            // Find the end of the timeline for insertion
            var insertTime = TimeSpan.Zero;
            if (track.Clips.Count > 0)
            {
                var lastClip = track.Clips.OrderByDescending(c => c.EndTime).First();
                insertTime = lastClip.EndTime;
            }

            // Image files get a default duration (e.g., 5 seconds)
            var duration = _selectedPlaylistItem.Duration;
            if (_selectedPlaylistItem.Type == MediaType.Image)
            {
                duration = TimeSpan.FromSeconds(5);
            }

            var clip = new TimelineClip
            {
                Name = _selectedPlaylistItem.Name,
                SourcePath = _selectedPlaylistItem.FilePath,
                StartTime = insertTime,
                Duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(5),
                InPoint = TimeSpan.Zero,
                OutPoint = duration,
                SourceDuration = duration,
                Thumbnail = _selectedPlaylistItem.Thumbnail
            };

            track.Clips.Add(clip);
            TimelineModel.RecalculateDuration();
            
            // Force timeline to redraw
            Timeline.TimelineModel = TimelineModel;
        }

        private void AddVideoToTimeline_Click(object sender, RoutedEventArgs e)
        {
            // Get selected item from listbox
            var itemToAdd = _selectedPlaylistItem ?? PlaylistBox.SelectedItem as PlaylistItem;
            if (itemToAdd == null || itemToAdd.Type != MediaType.Video)
                return;

            // Add specifically to video track V1
            var videoTrack = TimelineModel.Tracks.FirstOrDefault(t => t.Type == TrackType.Video);
            if (videoTrack == null)
            {
                videoTrack = new TimelineTrack { Name = "V1", Type = TrackType.Video };
                TimelineModel.Tracks.Insert(0, videoTrack);
            }

            var insertTime = videoTrack.Clips.Count > 0 
                ? videoTrack.Clips.Max(c => c.EndTime) 
                : TimeSpan.Zero;

            var clip = new TimelineClip
            {
                Name = itemToAdd.Name,
                SourcePath = itemToAdd.FilePath,
                StartTime = insertTime,
                Duration = itemToAdd.Duration > TimeSpan.Zero ? itemToAdd.Duration : TimeSpan.FromSeconds(10),
                InPoint = TimeSpan.Zero,
                OutPoint = itemToAdd.Duration,
                SourceDuration = itemToAdd.Duration,
                Thumbnail = itemToAdd.Thumbnail
            };

            videoTrack.Clips.Add(clip);
            TimelineModel.RecalculateDuration();
            Timeline.TimelineModel = TimelineModel;
        }

        private void ExtractAudioToTimeline_Click(object sender, RoutedEventArgs e)
        {
            // Get selected item from listbox
            var itemToAdd = _selectedPlaylistItem ?? PlaylistBox.SelectedItem as PlaylistItem;
            if (itemToAdd == null || itemToAdd.Type != MediaType.Video)
                return;

            // Add audio representation to audio track A1
            var audioTrack = TimelineModel.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio);
            if (audioTrack == null)
            {
                audioTrack = new TimelineTrack { Name = "A1", Type = TrackType.Audio };
                TimelineModel.Tracks.Add(audioTrack);
            }

            var insertTime = audioTrack.Clips.Count > 0 
                ? audioTrack.Clips.Max(c => c.EndTime) 
                : TimeSpan.Zero;

            var clip = new TimelineClip
            {
                Name = $"{itemToAdd.Name} (Audio)",
                SourcePath = itemToAdd.FilePath,
                StartTime = insertTime,
                Duration = itemToAdd.Duration > TimeSpan.Zero ? itemToAdd.Duration : TimeSpan.FromSeconds(10),
                InPoint = TimeSpan.Zero,
                OutPoint = itemToAdd.Duration,
                SourceDuration = itemToAdd.Duration,
                IsAudioOnly = true
            };

            audioTrack.Clips.Add(clip);
            TimelineModel.RecalculateDuration();
            Timeline.TimelineModel = TimelineModel;
        }

        private async void AddFileToPlaylist(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mediaType = GetMediaType(extension);

            var item = new PlaylistItem
            {
                Name = Path.GetFileName(filePath),
                FilePath = filePath,
                Type = mediaType,
                FileSize = new FileInfo(filePath).Length
            };

            // Get actual duration using FFprobe
            if (mediaType == MediaType.Video || mediaType == MediaType.Audio)
            {
                try
                {
                    var durationSeconds = await PlatypusTools.Core.Services.FFprobeService.GetDurationSecondsAsync(filePath);
                    if (durationSeconds > 0)
                    {
                        item.Duration = TimeSpan.FromSeconds(durationSeconds);
                    }
                    else
                    {
                        // Fallback - try to get duration from MediaElement
                        item.Duration = TimeSpan.FromSeconds(30); // Default fallback
                    }
                }
                catch
                {
                    item.Duration = TimeSpan.FromSeconds(30); // Default fallback
                }
            }
            else if (mediaType == MediaType.Image)
            {
                item.Duration = TimeSpan.FromSeconds(5); // Default image duration
            }
            else
            {
                item.Duration = TimeSpan.FromSeconds(30);
            }

            PlaylistItems.Add(item);
        }

        private Models.VideoEditor.MediaType GetMediaType(string extension)
        {
            return extension switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" => Models.VideoEditor.MediaType.Video,
                ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" => Models.VideoEditor.MediaType.Audio,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => Models.VideoEditor.MediaType.Image,
                _ => Models.VideoEditor.MediaType.Video
            };
        }

        private void PlaylistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistBox.SelectedItem is PlaylistItem item)
            {
                _selectedPlaylistItem = item;
                UpdatePropertiesPanel(item);
            }
        }

        private void PlaylistBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedInPlayer();
        }

        private void OpenInPlayer_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedInPlayer();
        }

        private void OpenSelectedInPlayer()
        {
            if (_selectedPlaylistItem != null)
            {
                // Load into player
                VideoPlayer.LoadMedia(_selectedPlaylistItem.FilePath);
            }
        }

        private void PlaylistBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    AddFileToPlaylist(file);
                }
            }
        }

        private void PlaylistBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void PlaylistBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_selectedPlaylistItem != null && !_isDragging)
                {
                    _isDragging = true;
                    var data = new DataObject(typeof(PlaylistItem), _selectedPlaylistItem);
                    DragDrop.DoDragDrop(PlaylistBox, data, DragDropEffects.Copy);
                    _isDragging = false;
                }
            }
        }

        #endregion

        #region Timeline

        private void Timeline_InsertClipRequested(object? sender, EventArgs e)
        {
            // Open file dialog to insert a clip at playhead position
            var dialog = new OpenFileDialog
            {
                Title = "Insert Clip",
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                         "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg|" +
                         "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "All Files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                InsertClipAtPlayhead(dialog.FileName);
            }
        }

        private void Timeline_AppendToTrackRequested(object? sender, EventArgs e)
        {
            // If we have a selected playlist item, add it
            if (_selectedPlaylistItem != null)
            {
                AddToTimeline_Click(sender, new RoutedEventArgs());
            }
            else
            {
                // Open file dialog
                var dialog = new OpenFileDialog
                {
                    Title = "Append Clip to Track",
                    Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                             "Audio Files|*.mp3;*.wav;*.aac;*.flac;*.ogg|" +
                             "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                             "All Files|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    AddFileToPlaylist(dialog.FileName);
                    _selectedPlaylistItem = PlaylistItems.LastOrDefault();
                    AddToTimeline_Click(sender, new RoutedEventArgs());
                }
            }
        }

        private void InsertClipAtPlayhead(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mediaType = GetMediaType(extension);
            var isImage = mediaType == MediaType.Image;
            
            // Get current playhead position
            var insertTime = Timeline.Position;

            // Get or create the appropriate track
            var track = TimelineModel.Tracks.FirstOrDefault(t => t.Type == 
                (mediaType == MediaType.Audio ? TrackType.Audio : TrackType.Video));
            
            if (track == null)
            {
                track = new TimelineTrack 
                { 
                    Name = mediaType == MediaType.Audio ? "A1" : "V1",
                    Type = mediaType == MediaType.Audio ? TrackType.Audio : TrackType.Video
                };
                TimelineModel.Tracks.Add(track);
            }

            // Image files get a default duration (e.g., 5 seconds)
            var duration = isImage ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10);

            var clip = new TimelineClip
            {
                Name = Path.GetFileName(filePath),
                SourcePath = filePath,
                StartTime = insertTime,
                Duration = duration,
                InPoint = TimeSpan.Zero,
                OutPoint = duration,
                SourceDuration = duration
            };

            track.Clips.Add(clip);
            TimelineModel.RecalculateDuration();
            
            // Force timeline to redraw
            Timeline.TimelineModel = TimelineModel;
        }

        #endregion

        #region Advanced Edit Operations (Rolling, Slip, Slide)

        /// <summary>
        /// Rolling edit: Adjusts the edit point between two adjacent clips on the same track.
        /// The out-point of the left clip and in-point of the right clip move together,
        /// maintaining the total duration of both clips combined.
        /// </summary>
        private void RollingEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null) return;
            
            // Find the track and adjacent clip
            foreach (var track in TimelineModel.Tracks)
            {
                var idx = track.Clips.IndexOf(_selectedTimelineClip);
                if (idx < 0) continue;
                
                // Need a clip to the right
                if (idx + 1 >= track.Clips.Count)
                {
                    MessageBox.Show("Rolling edit requires an adjacent clip to the right.", "Rolling Edit", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                var leftClip = _selectedTimelineClip;
                var rightClip = track.Clips[idx + 1];
                
                // Shift edit point by 1 second (can be adjusted with a dialog later)
                var shift = TimeSpan.FromSeconds(1);
                
                // Check if left clip has room to extend
                var leftCanExtend = leftClip.InPoint + leftClip.Duration + shift <= leftClip.SourceDuration;
                var rightCanShrink = rightClip.Duration - shift > TimeSpan.Zero;
                
                if (!leftCanExtend || !rightCanShrink)
                {
                    MessageBox.Show("Not enough source media to perform rolling edit.", "Rolling Edit",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                leftClip.Duration += shift;
                leftClip.OutPoint += shift;
                rightClip.StartTime += shift;
                rightClip.Duration -= shift;
                rightClip.InPoint += shift;
                
                TimelineModel.RecalculateDuration();
                Timeline.TimelineModel = TimelineModel;
                return;
            }
        }

        /// <summary>
        /// Slip edit: Moves the source in/out points of a clip without changing its position 
        /// or duration on the timeline. Like sliding the source media under the clip window.
        /// </summary>
        private void SlipEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null) return;
            
            var clip = _selectedTimelineClip;
            var shift = TimeSpan.FromSeconds(1);
            
            // Check if we can slip forward (more source media after out point)
            if (clip.OutPoint + shift <= clip.SourceDuration)
            {
                clip.InPoint += shift;
                clip.OutPoint += shift;
                
                // Update player preview
                VideoPlayer.InPoint = clip.InPoint;
                VideoPlayer.OutPoint = clip.OutPoint;
                VideoPlayer.SeekTo(clip.InPoint);
                UpdatePropertiesPanel(clip);
            }
            else
            {
                MessageBox.Show("Cannot slip further — end of source media reached.", "Slip Edit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Slide edit: Moves a clip along the timeline while adjusting the durations of 
        /// adjacent clips to fill the gaps. The clip's content stays the same.
        /// </summary>
        private void SlideEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null) return;
            
            foreach (var track in TimelineModel.Tracks)
            {
                var idx = track.Clips.IndexOf(_selectedTimelineClip);
                if (idx < 0) continue;
                
                var shift = TimeSpan.FromSeconds(1);
                var clip = _selectedTimelineClip;
                
                // Need clips on both sides for a true slide edit
                TimelineClip? leftClip = idx > 0 ? track.Clips[idx - 1] : null;
                TimelineClip? rightClip = idx + 1 < track.Clips.Count ? track.Clips[idx + 1] : null;
                
                if (rightClip != null)
                {
                    // Slide right: extend left neighbor, shrink right neighbor
                    clip.StartTime += shift;
                    
                    if (leftClip != null)
                    {
                        leftClip.Duration += shift;
                        leftClip.OutPoint += shift;
                    }
                    
                    rightClip.StartTime += shift;
                    rightClip.Duration -= shift;
                    rightClip.InPoint += shift;
                }
                else if (leftClip != null)
                {
                    // Can only slide left
                    clip.StartTime -= shift;
                    leftClip.Duration -= shift;
                    leftClip.OutPoint -= shift;
                }
                else
                {
                    MessageBox.Show("Slide edit requires adjacent clips.", "Slide Edit",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                TimelineModel.RecalculateDuration();
                Timeline.TimelineModel = TimelineModel;
                return;
            }
        }

        #endregion

        #region Speed Ramping

        private void SpeedRamping_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null)
            {
                MessageBox.Show("Select a clip to adjust speed.", "Speed Ramping", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var clip = _selectedTimelineClip;
            
            var dialog = new Window
            {
                Title = "Speed Ramping",
                Width = 400, Height = 340,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E))
            };
            
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Speed Ramping", FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 15) });
            
            stack.Children.Add(new TextBlock { Text = $"Clip: {clip.Name}", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) });
            
            // Speed slider
            var speedPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            speedPanel.Children.Add(new TextBlock { Text = "Speed:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 60 });
            var speedSlider = new Slider { Width = 200, Minimum = 0.1, Maximum = 8, Value = clip.Speed, TickFrequency = 0.1 };
            var speedLabel = new TextBlock { Text = $"{clip.Speed:F2}x", Foreground = System.Windows.Media.Brushes.Gray, 
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 50 };
            speedSlider.ValueChanged += (s, args) => speedLabel.Text = $"{args.NewValue:F2}x";
            speedPanel.Children.Add(speedSlider);
            speedPanel.Children.Add(speedLabel);
            stack.Children.Add(speedPanel);
            
            // Preset buttons
            stack.Children.Add(new TextBlock { Text = "Presets:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 10, 0, 5) });
            var presetPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var (label, speed) in new[] { ("0.25x", 0.25), ("0.5x", 0.5), ("1x", 1.0), ("1.5x", 1.5), ("2x", 2.0), ("4x", 4.0), ("8x", 8.0) })
            {
                var btn = new Button { Content = label, Width = 44, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(4, 2, 4, 2) };
                var s = speed;
                btn.Click += (_, _) => { speedSlider.Value = s; };
                presetPanel.Children.Add(btn);
            }
            stack.Children.Add(presetPanel);
            
            // Preserve pitch
            var pitchCheck = new CheckBox { Content = "Preserve Audio Pitch", Foreground = System.Windows.Media.Brushes.White, 
                IsChecked = clip.PreservePitch, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(pitchCheck);
            
            // Buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(8, 4, 8, 4) };
            cancelBtn.Click += (_, _) => dialog.Close();
            var applyBtn = new Button { Content = "Apply", Width = 80, Padding = new Thickness(8, 4, 8, 4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            applyBtn.Click += (_, _) =>
            {
                clip.Speed = speedSlider.Value;
                clip.PreservePitch = pitchCheck.IsChecked == true;
                UpdatePropertiesPanel(clip);
                dialog.Close();
            };
            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(applyBtn);
            stack.Children.Add(btnPanel);
            
            dialog.Content = stack;
            dialog.ShowDialog();
        }

        #endregion

        #region Freeze Frame

        private void FreezeFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null)
            {
                MessageBox.Show("Select a clip to insert a freeze frame.", "Freeze Frame", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var clip = _selectedTimelineClip;
            var freezePoint = Timeline.Position;
            
            // Validate freeze point is within the clip
            if (freezePoint < clip.StartTime || freezePoint > clip.EndTime)
            {
                MessageBox.Show("Move the playhead to a position within the selected clip.", "Freeze Frame",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Find the track containing this clip
            foreach (var track in TimelineModel.Tracks)
            {
                var idx = track.Clips.IndexOf(clip);
                if (idx < 0) continue;
                
                // Split the clip at freeze point and insert a freeze clip
                var freezeDuration = TimeSpan.FromSeconds(2); // Default 2s freeze
                var offsetInClip = freezePoint - clip.StartTime;
                
                // Create the freeze frame clip
                var freezeClip = new TimelineClip
                {
                    Name = $"Freeze: {clip.Name}",
                    SourcePath = clip.SourcePath,
                    StartTime = freezePoint,
                    Duration = freezeDuration,
                    InPoint = clip.InPoint + offsetInClip,
                    OutPoint = clip.InPoint + offsetInClip + TimeSpan.FromMilliseconds(33), // Single frame
                    SourceDuration = clip.SourceDuration,
                    IsFreezeFrame = true,
                    FreezeAt = clip.InPoint + offsetInClip,
                    Color = System.Windows.Media.Colors.LightBlue
                };
                
                // Add freeze filter
                freezeClip.Filters.Add(new Filter
                {
                    Name = "freeze",
                    DisplayName = "Freeze Frame",
                    Category = Core.Models.Video.FilterCategory.Time,
                    FFmpegFilterName = "freeze",
                    Icon = "❄",
                    Description = "Holds a single frame",
                    IsEnabled = true
                });
                
                // Shift all subsequent clips
                for (int i = idx + 1; i < track.Clips.Count; i++)
                    track.Clips[i].StartTime += freezeDuration;
                
                // If freeze point splits the clip, trim the original and add remainder after freeze
                if (freezePoint > clip.StartTime && freezePoint < clip.EndTime)
                {
                    var origEnd = clip.EndTime;
                    var origOutPoint = clip.OutPoint;
                    
                    // Trim original clip to freeze point
                    clip.Duration = freezePoint - clip.StartTime;
                    clip.OutPoint = clip.InPoint + clip.Duration;
                    
                    // Insert freeze clip
                    track.Clips.Insert(idx + 1, freezeClip);
                    
                    // Create remainder clip
                    var remainder = new TimelineClip
                    {
                        Name = clip.Name,
                        SourcePath = clip.SourcePath,
                        StartTime = freezePoint + freezeDuration,
                        Duration = origEnd - freezePoint,
                        InPoint = clip.InPoint + (freezePoint - clip.StartTime),
                        OutPoint = origOutPoint,
                        SourceDuration = clip.SourceDuration,
                        Color = clip.Color
                    };
                    track.Clips.Insert(idx + 2, remainder);
                }
                else
                {
                    track.Clips.Insert(idx + 1, freezeClip);
                }
                
                TimelineModel.RecalculateDuration();
                Timeline.TimelineModel = TimelineModel;
                
                MessageBox.Show($"Inserted {freezeDuration.TotalSeconds}s freeze frame at {freezePoint:hh\\:mm\\:ss\\.ff}", 
                    "Freeze Frame", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        #endregion

        #region Keyframe Snapping

        /// <summary>
        /// Snaps the playhead to the nearest clip boundary, marker, or edit point.
        /// </summary>
        private void SnapToNearestEdit_Click(object sender, RoutedEventArgs e)
        {
            var position = Timeline.Position;
            var snapPoints = new List<TimeSpan>();
            
            // Collect all snap points from clip boundaries
            foreach (var track in TimelineModel.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    snapPoints.Add(clip.StartTime);
                    snapPoints.Add(clip.EndTime);
                }
            }
            
            // Add chapter markers as snap points
            foreach (var marker in ChapterMarkers)
                snapPoints.Add(marker.Position);
            
            // Add in/out points
            if (TimelineModel.InPoint != TimeSpan.Zero) snapPoints.Add(TimelineModel.InPoint);
            if (TimelineModel.OutPoint != TimeSpan.Zero) snapPoints.Add(TimelineModel.OutPoint);
            
            if (snapPoints.Count == 0) return;
            
            // Find nearest point
            var nearest = snapPoints.OrderBy(p => Math.Abs((p - position).TotalMilliseconds)).First();
            Timeline.Position = nearest;
        }

        /// <summary>
        /// Snaps playhead to next edit point.
        /// </summary>
        private void SnapToNextEdit()
        {
            var position = Timeline.Position;
            var nextPoints = new List<TimeSpan>();
            
            foreach (var track in TimelineModel.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.StartTime > position) nextPoints.Add(clip.StartTime);
                    if (clip.EndTime > position) nextPoints.Add(clip.EndTime);
                }
            }
            
            if (nextPoints.Count > 0)
                Timeline.Position = nextPoints.Min();
        }

        /// <summary>
        /// Snaps playhead to previous edit point.
        /// </summary>
        private void SnapToPreviousEdit()
        {
            var position = Timeline.Position;
            var prevPoints = new List<TimeSpan>();
            
            foreach (var track in TimelineModel.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.StartTime < position) prevPoints.Add(clip.StartTime);
                    if (clip.EndTime < position) prevPoints.Add(clip.EndTime);
                }
            }
            
            if (prevPoints.Count > 0)
                Timeline.Position = prevPoints.Max();
        }

        #endregion

        #region Text/Title Generator

        /// <summary>
        /// Text/Title generator collections
        /// </summary>
        public ObservableCollection<string> TextPresetNames { get; } = new()
        {
            "Title", "Subtitle", "Lower Third", "Callout", "Social Handle", "Countdown", "Glitch", "Typewriter"
        };

        private void AddTextTitle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "Text / Title Generator",
                Width = 500,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E))
            };

            var stack = new StackPanel { Margin = new Thickness(20) };

            var titleLabel = new TextBlock { Text = "Text / Title Generator", FontSize = 18, FontWeight = FontWeights.Bold, 
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(titleLabel);

            // Preset selector
            stack.Children.Add(new TextBlock { Text = "Preset:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var presetCombo = new ComboBox { ItemsSource = TextPresetNames, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(presetCombo);

            // Text input
            stack.Children.Add(new TextBlock { Text = "Text Content:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var textBox = new TextBox { Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, 
                Text = "Your Title Here", FontSize = 16, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(textBox);

            // Font family
            stack.Children.Add(new TextBlock { Text = "Font:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var fontCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var family in new[] { "Arial", "Arial Black", "Segoe UI", "Impact", "Consolas", "Courier New", 
                "Georgia", "Times New Roman", "Verdana", "Comic Sans MS", "Trebuchet MS" })
                fontCombo.Items.Add(family);
            fontCombo.SelectedIndex = 0;
            stack.Children.Add(fontCombo);

            // Font size
            var sizePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            sizePanel.Children.Add(new TextBlock { Text = "Size:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var sizeSlider = new Slider { Width = 200, Minimum = 12, Maximum = 200, Value = 72 };
            sizePanel.Children.Add(sizeSlider);
            var sizeLabel = new TextBlock { Text = "72", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            sizeSlider.ValueChanged += (s, args) => sizeLabel.Text = ((int)args.NewValue).ToString();
            sizePanel.Children.Add(sizeLabel);
            stack.Children.Add(sizePanel);

            // Color
            var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            colorPanel.Children.Add(new TextBlock { Text = "Color:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var colorTextBox = new TextBox { Text = "#FFFFFF", Width = 100 };
            colorPanel.Children.Add(colorTextBox);
            stack.Children.Add(colorPanel);

            // Shadow
            var shadowCheck = new CheckBox { Content = "Drop Shadow", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            stack.Children.Add(shadowCheck);

            // Outline
            var outlineCheck = new CheckBox { Content = "Text Outline", Foreground = System.Windows.Media.Brushes.White, IsChecked = true, Margin = new Thickness(0, 0, 0, 5) };
            stack.Children.Add(outlineCheck);

            // Scrolling credits option
            var scrollCheck = new CheckBox { Content = "Scrolling Credits (vertical scroll)", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            stack.Children.Add(scrollCheck);
            
            // Scroll speed (visible when scrolling is checked)
            var scrollSpeedPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 5), Visibility = Visibility.Collapsed };
            scrollSpeedPanel.Children.Add(new TextBlock { Text = "Scroll Speed:", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var scrollSpeedSlider = new Slider { Width = 140, Minimum = 10, Maximum = 200, Value = 60 };
            var scrollSpeedLabel = new TextBlock { Text = "60 px/s", Foreground = System.Windows.Media.Brushes.Gray, 
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            scrollSpeedSlider.ValueChanged += (s, args) => scrollSpeedLabel.Text = $"{(int)args.NewValue} px/s";
            scrollSpeedPanel.Children.Add(scrollSpeedSlider);
            scrollSpeedPanel.Children.Add(scrollSpeedLabel);
            stack.Children.Add(scrollSpeedPanel);
            scrollCheck.Checked += (_, _) => scrollSpeedPanel.Visibility = Visibility.Visible;
            scrollCheck.Unchecked += (_, _) => scrollSpeedPanel.Visibility = Visibility.Collapsed;

            // 3D Perspective options
            var perspectiveExpander = new Expander
            {
                Header = new TextBlock { Text = "3D Text / Perspective", Foreground = System.Windows.Media.Brushes.White },
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 5, 0, 5),
                IsExpanded = false
            };
            var perspStack = new StackPanel { Margin = new Thickness(20, 5, 0, 0) };
            var perspXPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            perspXPanel.Children.Add(new TextBlock { Text = "X Rotation:", Foreground = System.Windows.Media.Brushes.Gray, Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var perspXSlider = new Slider { Width = 140, Minimum = -60, Maximum = 60, Value = 0 };
            var perspXLabel = new TextBlock { Text = "0°", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            perspXSlider.ValueChanged += (s, args) => perspXLabel.Text = $"{(int)args.NewValue}°";
            perspXPanel.Children.Add(perspXSlider);
            perspXPanel.Children.Add(perspXLabel);
            perspStack.Children.Add(perspXPanel);
            var perspYPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            perspYPanel.Children.Add(new TextBlock { Text = "Y Rotation:", Foreground = System.Windows.Media.Brushes.Gray, Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var perspYSlider = new Slider { Width = 140, Minimum = -60, Maximum = 60, Value = 0 };
            var perspYLabel = new TextBlock { Text = "0°", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            perspYSlider.ValueChanged += (s, args) => perspYLabel.Text = $"{(int)args.NewValue}°";
            perspYPanel.Children.Add(perspYSlider);
            perspYPanel.Children.Add(perspYLabel);
            perspStack.Children.Add(perspYPanel);
            perspectiveExpander.Content = perspStack;
            stack.Children.Add(perspectiveExpander);

            // Duration
            var durPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
            durPanel.Children.Add(new TextBlock { Text = "Duration (sec):", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var durBox = new TextBox { Text = "5", Width = 60 };
            durPanel.Children.Add(durBox);
            stack.Children.Add(durPanel);

            // Animation preset — expanded with more options (TASK-359)
            stack.Children.Add(new TextBlock { Text = "Animation:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var animCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 5) };
            foreach (var anim in new[] { "None", "Fade In", "Fade Out", "Slide Up", "Slide Down", "Slide Left", "Slide Right",
                "Zoom In", "Zoom Out", "Spin In", "Bounce In", "Typewriter", "Glitch", "Flicker", "Blur In", "Wave" })
                animCombo.Items.Add(anim);
            animCombo.SelectedIndex = 1;
            stack.Children.Add(animCombo);
            
            // Out animation
            stack.Children.Add(new TextBlock { Text = "Exit Animation:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var outAnimCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 15) };
            foreach (var anim in new[] { "None", "Fade Out", "Slide Down", "Slide Right", "Zoom Out", "Spin Out", "Bounce Out", "Blur Out" })
                outAnimCombo.Items.Add(anim);
            outAnimCombo.SelectedIndex = 0;
            stack.Children.Add(outAnimCombo);

            // Buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Add to Timeline", Width = 120, Padding = new Thickness(8, 4, 8, 4), 
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            var cancelButton = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(8, 4, 8, 4) };
            cancelButton.Click += (s, args) => dialog.Close();
            okButton.Click += (s, args) =>
            {
                var presetName = presetCombo.SelectedItem?.ToString() ?? "Title";
                var textElement = presetName switch
                {
                    "Subtitle" => PlatypusTools.Core.Models.Video.TextPresets.CreateSubtitle(),
                    "Lower Third" => PlatypusTools.Core.Models.Video.TextPresets.CreateLowerThird(),
                    "Callout" => PlatypusTools.Core.Models.Video.TextPresets.CreateCallout(),
                    "Social Handle" => PlatypusTools.Core.Models.Video.TextPresets.CreateSocialHandle(),
                    "Countdown" => PlatypusTools.Core.Models.Video.TextPresets.CreateCountdown(),
                    "Glitch" => PlatypusTools.Core.Models.Video.TextPresets.CreateGlitchTitle(),
                    "Typewriter" => PlatypusTools.Core.Models.Video.TextPresets.CreateTypewriter(),
                    _ => PlatypusTools.Core.Models.Video.TextPresets.CreateTitle()
                };

                textElement.Text = textBox.Text;
                textElement.FontFamily = fontCombo.SelectedItem?.ToString() ?? "Arial";
                textElement.FontSize = (int)sizeSlider.Value;
                textElement.Color = colorTextBox.Text;
                
                if (shadowCheck.IsChecked == true)
                {
                    textElement.ShadowColor = "#80000000";
                    textElement.ShadowOffsetX = 3;
                    textElement.ShadowOffsetY = 3;
                    textElement.ShadowBlur = 5;
                }
                
                if (outlineCheck.IsChecked != true)
                {
                    textElement.OutlineColor = null;
                    textElement.OutlineWidth = 0;
                }

                var animName = animCombo.SelectedItem?.ToString() ?? "None";
                textElement.InAnimation = animName switch
                {
                    "Fade In" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.FadeIn },
                    "Fade Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.FadeOut },
                    "Slide Up" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideUp },
                    "Slide Down" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideDown },
                    "Slide Left" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideLeft },
                    "Slide Right" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideRight },
                    "Zoom In" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.ZoomIn },
                    "Zoom Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.ZoomOut },
                    "Spin In" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SpinIn },
                    "Bounce In" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.BounceIn },
                    "Typewriter" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.Typewriter },
                    "Glitch" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.GlitchIn },
                    "Flicker" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.FlickerIn },
                    "Blur In" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.BlurIn },
                    "Wave" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.WaveIn },
                    _ => null
                };
                
                // Exit animation (TASK-359)
                var outAnimName = outAnimCombo.SelectedItem?.ToString() ?? "None";
                textElement.OutAnimation = outAnimName switch
                {
                    "Fade Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.FadeOut },
                    "Slide Down" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideDown },
                    "Slide Right" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SlideRight },
                    "Zoom Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.ZoomOut },
                    "Spin Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.SpinOut },
                    "Bounce Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.BounceOut },
                    "Blur Out" => new Core.Models.Video.TextAnimation { Type = Core.Models.Video.TextAnimationType.BlurOut },
                    _ => null
                };

                // Create a title clip on the timeline
                var durationSec = double.TryParse(durBox.Text, out var d) ? d : 5;
                var titleTrack = TimelineModel.Tracks.FirstOrDefault(t => t.Type == TrackType.Video)
                    ?? TimelineModel.AddVideoTrack("V-Title");
                    
                var insertTime = Timeline.Position;
                var titleClip = new TimelineClip
                {
                    Name = $"Title: {textElement.Text.Substring(0, Math.Min(20, textElement.Text.Length))}",
                    SourcePath = string.Empty, // Text clips have no source file
                    StartTime = insertTime,
                    Duration = TimeSpan.FromSeconds(durationSec),
                    InPoint = TimeSpan.Zero,
                    OutPoint = TimeSpan.FromSeconds(durationSec),
                    SourceDuration = TimeSpan.FromSeconds(durationSec),
                    Color = System.Windows.Media.Colors.MediumPurple
                };
                
                // Store the text element data as a filter for the clip
                var textFilter = new Filter
                {
                    Name = "text_overlay",
                    DisplayName = $"Text: {textElement.PresetName ?? "Custom"}",
                    Category = Core.Models.Video.FilterCategory.Overlay,
                    FFmpegFilterName = "drawtext",
                    Icon = "🔤",
                    Description = textElement.Text,
                    IsEnabled = true
                };
                
                // Store text rendering parameters for FFmpeg drawtext export (TASK-360)
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter 
                { 
                    Name = "text", DisplayName = "Text", Type = Core.Models.Video.FilterParameterType.String, 
                    Value = textElement.Text 
                });
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter 
                { 
                    Name = "fontfile", DisplayName = "Font", Type = Core.Models.Video.FilterParameterType.String, 
                    Value = textElement.FontFamily 
                });
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter 
                { 
                    Name = "fontsize", DisplayName = "Font Size", Type = Core.Models.Video.FilterParameterType.Integer, 
                    Value = (int)textElement.FontSize 
                });
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter 
                { 
                    Name = "fontcolor", DisplayName = "Color", Type = Core.Models.Video.FilterParameterType.Color, 
                    Value = textElement.Color.TrimStart('#') 
                });
                
                // Drop shadow parameters (TASK-360)
                if (shadowCheck.IsChecked == true)
                {
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "shadowcolor", DisplayName = "Shadow Color", Type = Core.Models.Video.FilterParameterType.Color,
                        Value = (textElement.ShadowColor ?? "#80000000").TrimStart('#')
                    });
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "shadowx", DisplayName = "Shadow X", Type = Core.Models.Video.FilterParameterType.Integer,
                        Value = (int)textElement.ShadowOffsetX
                    });
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "shadowy", DisplayName = "Shadow Y", Type = Core.Models.Video.FilterParameterType.Integer,
                        Value = (int)textElement.ShadowOffsetY
                    });
                }
                
                // Outline/border parameters (TASK-360)
                if (outlineCheck.IsChecked == true && textElement.OutlineColor != null)
                {
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "borderw", DisplayName = "Outline Width", Type = Core.Models.Video.FilterParameterType.Integer,
                        Value = (int)textElement.OutlineWidth
                    });
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "bordercolor", DisplayName = "Outline Color", Type = Core.Models.Video.FilterParameterType.Color,
                        Value = textElement.OutlineColor.TrimStart('#')
                    });
                }
                
                // Position parameters
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                {
                    Name = "x", DisplayName = "X Position", Type = Core.Models.Video.FilterParameterType.String,
                    Value = $"(w-text_w)*{textElement.PositionX:F2}"
                });
                textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                {
                    Name = "y", DisplayName = "Y Position", Type = Core.Models.Video.FilterParameterType.String,
                    Value = $"(h-text_h)*{textElement.PositionY:F2}"
                });
                
                // Scrolling text support (TASK-357)
                if (scrollCheck.IsChecked == true)
                {
                    var scrollSpeed = (int)scrollSpeedSlider.Value;
                    // Override Y position to create vertical scroll: y starts at bottom, scrolls to top
                    textFilter.Parameters.RemoveAll(p => p.Name == "y");
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "y", DisplayName = "Y Position (Scroll)", Type = Core.Models.Video.FilterParameterType.String,
                        Value = $"h-{scrollSpeed}*t"
                    });
                }
                
                // 3D perspective parameters (TASK-358) — stored as metadata for ASS/drawtext overlay
                var pxRot = (int)perspXSlider.Value;
                var pyRot = (int)perspYSlider.Value;
                if (pxRot != 0 || pyRot != 0)
                {
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "perspX", DisplayName = "3D X Rotation", Type = Core.Models.Video.FilterParameterType.Integer,
                        Value = pxRot
                    });
                    textFilter.Parameters.Add(new Core.Models.Video.FilterParameter
                    {
                        Name = "perspY", DisplayName = "3D Y Rotation", Type = Core.Models.Video.FilterParameterType.Integer,
                        Value = pyRot
                    });
                    // Apply perspective via rotate filter with angle expression
                    textElement.Rotation = pxRot; // Store primary rotation
                }
                
                titleClip.Filters.Add(textFilter);
                
                titleTrack.Clips.Add(titleClip);
                TimelineModel.RecalculateDuration();
                Timeline.TimelineModel = TimelineModel;
                
                dialog.Close();
            };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);
            stack.Children.Add(buttonPanel);

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
            dialog.Content = scrollViewer;
            dialog.ShowDialog();
        }

        #endregion

        #region Reverse Playback & Speed

        private void ReversePlayback_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null)
            {
                MessageBox.Show("Select a clip to reverse.", "Reverse", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Add reverse filter to the clip
            var reverseFilter = new Filter
            {
                Name = "reverse",
                DisplayName = "Reverse Playback",
                Category = Core.Models.Video.FilterCategory.Time,
                FFmpegFilterName = "reverse",
                Icon = "⏪",
                Description = "Plays clip in reverse",
                IsEnabled = true
            };
            
            // Check if already reversed
            var existing = _selectedTimelineClip.Filters.FirstOrDefault(f => f.Name == "reverse");
            if (existing != null)
            {
                _selectedTimelineClip.Filters.Remove(existing);
                MessageBox.Show("Reverse removed — clip plays forward.", "Reverse", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _selectedTimelineClip.Filters.Add(reverseFilter);
                MessageBox.Show("Clip set to reverse playback.", "Reverse", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            
            OnPropertyChanged(nameof(SelectedClip));
        }

        #endregion

        #region Keyframe Copy/Paste

        private List<Filter>? _copiedFilters;
        
        private void CopyKeyframes_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null || _selectedTimelineClip.Filters.Count == 0)
            {
                MessageBox.Show("Select a clip with filters to copy.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _copiedFilters = _selectedTimelineClip.Filters.Select(f => f.Clone()).ToList();
            MessageBox.Show($"Copied {_copiedFilters.Count} filter(s).", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void PasteKeyframes_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null)
            {
                MessageBox.Show("Select a target clip to paste filters.", "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_copiedFilters == null || _copiedFilters.Count == 0)
            {
                MessageBox.Show("No filters copied. Copy filters from a clip first.", "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            foreach (var filter in _copiedFilters)
            {
                _selectedTimelineClip.Filters.Add(filter.Clone());
            }
            
            OnPropertyChanged(nameof(SelectedClip));
            MessageBox.Show($"Pasted {_copiedFilters.Count} filter(s) to clip.", "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Voice-Over Recording

        private NAudio.Wave.WaveInEvent? _voiceOverWaveIn;
        private NAudio.Wave.WaveFileWriter? _voiceOverWriter;
        private string? _voiceOverFilePath;
        private bool _isRecordingVoiceOver;

        private void VoiceOverRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecordingVoiceOver)
            {
                StopVoiceOverRecording();
                return;
            }

            try
            {
                // Create temp file for recording
                var tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "voiceover");
                Directory.CreateDirectory(tempDir);
                _voiceOverFilePath = Path.Combine(tempDir, $"voiceover_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

                _voiceOverWaveIn = new NAudio.Wave.WaveInEvent
                {
                    WaveFormat = new NAudio.Wave.WaveFormat(44100, 16, 1)
                };

                _voiceOverWriter = new NAudio.Wave.WaveFileWriter(_voiceOverFilePath, _voiceOverWaveIn.WaveFormat);

                _voiceOverWaveIn.DataAvailable += (s, args) =>
                {
                    _voiceOverWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                };

                _voiceOverWaveIn.RecordingStopped += (s, args) =>
                {
                    _voiceOverWriter?.Dispose();
                    _voiceOverWriter = null;
                    _voiceOverWaveIn?.Dispose();
                    _voiceOverWaveIn = null;
                };

                _voiceOverWaveIn.StartRecording();
                _isRecordingVoiceOver = true;

                // Also start timeline playback so user can record along
                if (!_isTimelinePlaying)
                    StartTimelinePlayback();

                MessageBox.Show("🎙️ Recording voice-over...\nClick the mic button again to stop.", "Voice-Over",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start recording:\n{ex.Message}", "Voice-Over Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopVoiceOverRecording()
        {
            if (!_isRecordingVoiceOver) return;

            _isRecordingVoiceOver = false;
            _voiceOverWaveIn?.StopRecording();

            if (_isTimelinePlaying)
                PauseTimelinePlayback();

            // Ask user if they want to add to timeline
            if (_voiceOverFilePath != null && File.Exists(_voiceOverFilePath))
            {
                var result = MessageBox.Show(
                    $"Voice-over recorded to:\n{_voiceOverFilePath}\n\nAdd to timeline audio track?",
                    "Voice-Over Complete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var audioTrack = TimelineModel.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio)
                        ?? TimelineModel.AddAudioTrack("A-VO");

                    var insertTime = Timeline.Position;
                    var clip = new TimelineClip
                    {
                        Name = "Voice-Over",
                        SourcePath = _voiceOverFilePath,
                        StartTime = insertTime,
                        Duration = TimeSpan.FromSeconds(10), // Will be corrected when loaded
                        InPoint = TimeSpan.Zero,
                        OutPoint = TimeSpan.FromSeconds(10),
                        SourceDuration = TimeSpan.FromSeconds(10),
                        IsAudioOnly = true,
                        Color = System.Windows.Media.Colors.OrangeRed
                    };

                    audioTrack.Clips.Add(clip);
                    TimelineModel.RecalculateDuration();
                    Timeline.TimelineModel = TimelineModel;
                }
            }
        }

        #endregion

        #region Proxy Editing

        private Core.Models.Video.ProxySettings _proxySettings = new();
        
        public bool IsProxyEnabled
        {
            get => _proxySettings.IsEnabled;
            set { _proxySettings.IsEnabled = value; OnPropertyChanged(); }
        }

        private async void GenerateProxy_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylistItem == null)
            {
                MessageBox.Show("Select a media file from the playlist to generate proxy.", "Proxy", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var proxyDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "proxies");
            Directory.CreateDirectory(proxyDir);
            
            var proxyFile = Path.Combine(proxyDir, 
                Path.GetFileNameWithoutExtension(_selectedPlaylistItem.FilePath) + "_proxy.mp4");

            try
            {
                var (width, height) = _proxySettings.Resolution switch
                {
                    Core.Models.Video.ProxyResolution.SD480 => (854, 480),
                    Core.Models.Video.ProxyResolution.HD720 => (1280, 720),
                    Core.Models.Video.ProxyResolution.HD1080 => (1920, 1080),
                    _ => (1280, 720)
                };

                var ffmpegPath = Core.Services.FFmpegService.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    MessageBox.Show("FFmpeg not found. Proxy generation requires FFmpeg.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var args = $"-i \"{_selectedPlaylistItem.FilePath}\" -vf \"scale={width}:{height}\" " +
                           $"-c:v libx264 -preset ultrafast -crf 28 -c:a aac -b:a 128k \"{proxyFile}\" -y";

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    }
                };

                MessageBox.Show($"Generating proxy ({width}x{height})...\nThis may take a moment.", "Proxy Generation",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && File.Exists(proxyFile))
                {
                    MessageBox.Show($"Proxy generated:\n{proxyFile}", "Proxy Ready",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    MessageBox.Show($"Proxy generation failed:\n{error}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Proxy error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Preview Scaling

        private Core.Models.Video.PreviewResolution _previewResolution = Core.Models.Video.PreviewResolution.Full;

        private void SetPreviewScale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu && menu.Tag is string scale)
            {
                _previewResolution = scale switch
                {
                    "Full" => Core.Models.Video.PreviewResolution.Full,
                    "Half" => Core.Models.Video.PreviewResolution.Half,
                    "Quarter" => Core.Models.Video.PreviewResolution.Quarter,
                    "Eighth" => Core.Models.Video.PreviewResolution.Eighth,
                    _ => Core.Models.Video.PreviewResolution.Auto
                };
                OnPropertyChanged(nameof(PreviewScaleText));
            }
        }

        public string PreviewScaleText => _previewResolution switch
        {
            Core.Models.Video.PreviewResolution.Full => "1:1",
            Core.Models.Video.PreviewResolution.Half => "1:2",
            Core.Models.Video.PreviewResolution.Quarter => "1:4",
            Core.Models.Video.PreviewResolution.Eighth => "1:8",
            _ => "Auto"
        };

        #endregion

        #region Shuttle/Jog & Loop Region

        private double _shuttleSpeed = 0;
        
        private void ShuttleReverse_Click(object sender, RoutedEventArgs e)
        {
            // JKL-style shuttle: J = reverse
            _shuttleSpeed = _shuttleSpeed switch
            {
                <= -8 => -8,
                < 0 => _shuttleSpeed * 2,
                _ => -1
            };
            
            VideoPlayer.Play(_shuttleSpeed);
        }

        private void ShuttlePause_Click(object sender, RoutedEventArgs e)
        {
            // JKL-style shuttle: K = pause/stop
            _shuttleSpeed = 0;
            VideoPlayer.Pause();
        }
        
        private void ShuttleForward_Click(object sender, RoutedEventArgs e)
        {
            // JKL-style shuttle: L = forward
            _shuttleSpeed = _shuttleSpeed switch
            {
                >= 8 => 8,
                > 0 => _shuttleSpeed * 2,
                _ => 1
            };
            
            VideoPlayer.Play(_shuttleSpeed);
        }

        private void JogBackward_Click(object sender, RoutedEventArgs e)
        {
            // Step back one frame
            VideoPlayer.SeekByFrames(-1);
        }

        private void JogForward_Click(object sender, RoutedEventArgs e)
        {
            // Step forward one frame
            VideoPlayer.SeekByFrames(1);
        }

        private void SetLoopRegion_Click(object sender, RoutedEventArgs e)
        {
            if (TimelineModel.InPoint == TimeSpan.Zero && TimelineModel.OutPoint == TimeSpan.Zero)
            {
                MessageBox.Show("Set In and Out points first to define the loop region.", "Loop Region",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            TimelineModel.LoopStart = TimelineModel.InPoint;
            TimelineModel.LoopEnd = TimelineModel.OutPoint;
            TimelineModel.IsLooping = !TimelineModel.IsLooping;
            
            OnPropertyChanged(nameof(IsLoopingText));
        }

        public string IsLoopingText => TimelineModel.IsLooping ? "Loop: ON" : "Loop: OFF";

        #endregion

        #region Chapter Markers

        public ObservableCollection<Core.Models.Video.TimelineMarker> ChapterMarkers { get; } = new();
        
        private void AddChapterMarker_Click(object sender, RoutedEventArgs e)
        {
            var position = Timeline.Position;
            
            var marker = new Core.Models.Video.TimelineMarker
            {
                Position = position,
                Name = $"Chapter {ChapterMarkers.Count + 1}",
                Type = Core.Models.Video.MarkerType.Chapter,
                IsChapter = true,
                Color = "#FF9800"
            };
            
            ChapterMarkers.Add(marker);
            OnPropertyChanged(nameof(ChapterMarkers));
            
            MessageBox.Show($"Chapter marker added at {marker.FormattedTime}", "Chapter Marker",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportChapterMarkers_Click(object sender, RoutedEventArgs e)
        {
            if (ChapterMarkers.Count == 0)
            {
                MessageBox.Show("No chapter markers to export.", "Export Chapters", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Title = "Export Chapter Markers",
                Filter = "FFmpeg Metadata|*.txt|WebVTT Chapters|*.vtt",
                DefaultExt = ".txt"
            };
            
            if (dialog.ShowDialog() != true) return;
            
            try
            {
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                var lines = new List<string>();
                
                if (ext == ".vtt")
                {
                    // WebVTT format
                    lines.Add("WEBVTT");
                    lines.Add("");
                    foreach (var marker in ChapterMarkers.OrderBy(m => m.Position))
                    {
                        lines.Add($"{marker.Position:hh\\:mm\\:ss\\.fff} --> {marker.Position:hh\\:mm\\:ss\\.fff}");
                        lines.Add(marker.Name);
                        lines.Add("");
                    }
                }
                else
                {
                    // FFmpeg metadata format
                    lines.Add(";FFMETADATA1");
                    foreach (var (marker, index) in ChapterMarkers.OrderBy(m => m.Position).Select((m, i) => (m, i)))
                    {
                        var next = index + 1 < ChapterMarkers.Count 
                            ? ChapterMarkers.OrderBy(m => m.Position).ElementAt(index + 1).Position 
                            : TimelineModel.Duration;
                        
                        lines.Add("[CHAPTER]");
                        lines.Add("TIMEBASE=1/1000");
                        lines.Add($"START={(long)marker.Position.TotalMilliseconds}");
                        lines.Add($"END={(long)next.TotalMilliseconds}");
                        lines.Add($"title={marker.Name}");
                    }
                }
                
                File.WriteAllLines(dialog.FileName, lines);
                MessageBox.Show($"Exported {ChapterMarkers.Count} chapters to:\n{dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Render Preview

        private async void RenderPreview_Click(object sender, RoutedEventArgs e)
        {
            var hasClips = TimelineModel.Tracks.Any(t => t.Clips.Count > 0);
            if (!hasClips)
            {
                MessageBox.Show("No clips on timeline to preview.", "Render Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "preview");
            Directory.CreateDirectory(tempDir);
            var previewFile = Path.Combine(tempDir, $"preview_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            try
            {
                var coreTracks = ConvertToCoreTracks();
                var settings = new Core.Services.Video.ExportSettings
                {
                    Width = 960,
                    Height = 540,
                    FrameRate = 30,
                    VideoCodec = "libx264",
                    AudioCodec = "aac",
                    AudioBitrate = 128,
                    Preset = "ultrafast",
                    Crf = 28,
                    PixelFormat = "yuv420p",
                    Container = "mp4"
                };

                var progress = new Progress<Core.Services.Video.ExportProgress>(p =>
                {
                    // Could show progress bar
                });

                var exporter = new Core.Services.Video.SimpleVideoExporter();
                var result = await exporter.ExportAsync(coreTracks, previewFile, settings, progress, CancellationToken.None);

                if (result.Success && File.Exists(previewFile))
                {
                    VideoPlayer.LoadMedia(previewFile);
                    VideoPlayer.Play();
                }
                else
                {
                    MessageBox.Show($"Preview render failed: {result.ErrorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Preview error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Project Templates, EDL Export, Notes, Archive

        private string _projectNotes = string.Empty;
        public string ProjectNotes
        {
            get => _projectNotes;
            set { _projectNotes = value; OnPropertyChanged(); }
        }

        private void NewFromTemplate_Click(object sender, RoutedEventArgs e)
        {
            var templates = new[]
            {
                ("YouTube 1080p", Core.Models.Video.VideoMode.HD1080p30, 16, 9),
                ("YouTube 4K", Core.Models.Video.VideoMode.UHD4K30, 16, 9),
                ("Instagram Reel", Core.Models.Video.VideoMode.Vertical1080x1920p30, 9, 16),
                ("TikTok", Core.Models.Video.VideoMode.Vertical1080x1920p60, 9, 16),
                ("Square Social", Core.Models.Video.VideoMode.Square1080p30, 1, 1),
                ("Film 24fps", Core.Models.Video.VideoMode.HD1080p24, 16, 9),
                ("Broadcast PAL", Core.Models.Video.VideoMode.SD576p25, 4, 3)
            };

            var dialog = new Window
            {
                Title = "New Project from Template",
                Width = 400, Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E))
            };

            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Select Project Template", FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 15) });

            var listBox = new ListBox { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
            foreach (var (name, mode, w, h) in templates)
            {
                var (width, height, fps) = Core.Models.Video.VideoModeHelper.GetModeSettings(mode);
                listBox.Items.Add(new TextBlock
                {
                    Text = $"{name}  ({width}×{height} @ {fps}fps)",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(4)
                });
            }
            listBox.SelectedIndex = 0;
            stack.Children.Add(listBox);

            var okBtn = new Button { Content = "Create Project", Width = 120, Margin = new Thickness(0, 15, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4) };
            okBtn.Click += (s, args) =>
            {
                var idx = listBox.SelectedIndex;
                if (idx >= 0 && idx < templates.Length)
                {
                    var (name, mode, _, _) = templates[idx];
                    var (width, height, fps) = Core.Models.Video.VideoModeHelper.GetModeSettings(mode);

                    // Reset timeline
                    PlaylistItems.Clear();
                    TimelineModel.Tracks.Clear();
                    TimelineModel.FrameRate = fps;
                    TimelineModel.Duration = TimeSpan.FromMinutes(5);
                    TimelineModel.AddVideoTrack("V1");
                    TimelineModel.AddAudioTrack("A1");
                    Timeline.TimelineModel = TimelineModel;

                    MessageBox.Show($"Project created: {name}\n{width}×{height} @ {fps}fps", "Template Applied",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                dialog.Close();
            };
            stack.Children.Add(okBtn);
            dialog.Content = stack;
            dialog.ShowDialog();
        }

        private void ExportEDL_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export EDL",
                Filter = "EDL File|*.edl|XML Timeline|*.xml|FCPXML|*.fcpxml",
                DefaultExt = ".edl"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var lines = new List<string>();
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                
                if (ext == ".xml" || ext == ".fcpxml")
                {
                    // Simple XML export
                    lines.Add("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    lines.Add("<timeline>");
                    lines.Add($"  <duration>{TimelineModel.Duration.TotalSeconds:F3}</duration>");
                    lines.Add($"  <framerate>{TimelineModel.FrameRate}</framerate>");
                    foreach (var track in TimelineModel.Tracks)
                    {
                        lines.Add($"  <track name=\"{track.Name}\" type=\"{track.Type}\">");
                        foreach (var clip in track.Clips)
                        {
                            lines.Add($"    <clip name=\"{clip.Name}\" source=\"{clip.SourcePath}\"");
                            lines.Add($"          start=\"{clip.StartTime.TotalSeconds:F3}\" duration=\"{clip.Duration.TotalSeconds:F3}\"");
                            lines.Add($"          in=\"{clip.InPoint.TotalSeconds:F3}\" out=\"{clip.OutPoint.TotalSeconds:F3}\"/>");
                        }
                        lines.Add("  </track>");
                    }
                    lines.Add("</timeline>");
                }
                else
                {
                    // CMX 3600 EDL format
                    lines.Add("TITLE: PlatypusTools Export");
                    lines.Add("FCM: NON-DROP FRAME");
                    lines.Add("");
                    
                    int editNum = 1;
                    foreach (var track in TimelineModel.Tracks)
                    {
                        foreach (var clip in track.Clips.OrderBy(c => c.StartTime))
                        {
                            var srcIn = FormatTimecode(clip.InPoint, TimelineModel.FrameRate);
                            var srcOut = FormatTimecode(clip.OutPoint, TimelineModel.FrameRate);
                            var recIn = FormatTimecode(clip.StartTime, TimelineModel.FrameRate);
                            var recOut = FormatTimecode(clip.EndTime, TimelineModel.FrameRate);
                            
                            lines.Add($"{editNum:D3}  AX       V     C        {srcIn} {srcOut} {recIn} {recOut}");
                            lines.Add($"* FROM CLIP NAME: {clip.Name}");
                            lines.Add($"* SOURCE FILE: {clip.SourcePath}");
                            lines.Add("");
                            editNum++;
                        }
                    }
                }

                File.WriteAllLines(dialog.FileName, lines);
                MessageBox.Show($"EDL exported to:\n{dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"EDL export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatTimecode(TimeSpan time, double fps)
        {
            int frames = (int)(time.Milliseconds / (1000.0 / fps));
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}:{frames:D2}";
        }

        private void ProjectNotes_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "Project Notes",
                Width = 500, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E))
            };

            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Project Notes", FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10) });

            var textBox = new TextBox { Text = ProjectNotes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Height = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            stack.Children.Add(textBox);

            var saveBtn = new Button { Content = "Save Notes", Width = 100, Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4) };
            saveBtn.Click += (s, args) => { ProjectNotes = textBox.Text; dialog.Close(); };
            stack.Children.Add(saveBtn);
            dialog.Content = stack;
            dialog.ShowDialog();
        }

        private void ArchiveProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Archive Project",
                Filter = "ZIP Archive|*.zip",
                DefaultExt = ".zip",
                FileName = $"Project_Archive_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "archive", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Save project file
                var projectFile = Path.Combine(tempDir, "project.ptproj");
                SaveProject(projectFile);

                // Copy all source media
                var mediaDir = Path.Combine(tempDir, "media");
                Directory.CreateDirectory(mediaDir);
                
                foreach (var track in TimelineModel.Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (!string.IsNullOrEmpty(clip.SourcePath) && File.Exists(clip.SourcePath))
                        {
                            var destFile = Path.Combine(mediaDir, Path.GetFileName(clip.SourcePath));
                            if (!File.Exists(destFile))
                                File.Copy(clip.SourcePath, destFile);
                        }
                    }
                }

                // Save project notes
                if (!string.IsNullOrWhiteSpace(ProjectNotes))
                    File.WriteAllText(Path.Combine(tempDir, "notes.txt"), ProjectNotes);

                // Create ZIP
                if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, dialog.FileName);

                // Cleanup temp
                Directory.Delete(tempDir, true);

                var fileSize = new FileInfo(dialog.FileName).Length / (1024.0 * 1024.0);
                MessageBox.Show($"Project archived to:\n{dialog.FileName}\nSize: {fileSize:F1} MB", "Archive Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Archive failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Undo/Redo

        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();

        private void SaveUndoState()
        {
            try
            {
                var state = JsonSerializer.Serialize(new ProjectData
                {
                    Duration = TimelineModel.Duration,
                    FrameRate = TimelineModel.FrameRate,
                    Tracks = TimelineModel.Tracks.Select(t => new ProjectTrack
                    {
                        Name = t.Name,
                        Type = t.Type.ToString(),
                        Clips = t.Clips.Select(c => new ProjectClip
                        {
                            Name = c.Name,
                            SourcePath = c.SourcePath,
                            StartPosition = c.StartTime,
                            Duration = c.Duration,
                            InPoint = c.InPoint,
                            OutPoint = c.OutPoint,
                            Volume = c.Gain
                        }).ToList()
                    }).ToList()
                });
                _undoStack.Push(state);
                _redoStack.Clear();
            }
            catch { }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0) return;
            
            // Save current state for redo
            try
            {
                var currentState = JsonSerializer.Serialize(new ProjectData
                {
                    Duration = TimelineModel.Duration,
                    Tracks = TimelineModel.Tracks.Select(t => new ProjectTrack
                    {
                        Name = t.Name,
                        Type = t.Type.ToString(),
                        Clips = t.Clips.Select(c => new ProjectClip
                        {
                            Name = c.Name,
                            SourcePath = c.SourcePath,
                            StartPosition = c.StartTime,
                            Duration = c.Duration,
                            InPoint = c.InPoint,
                            OutPoint = c.OutPoint,
                            Volume = c.Gain
                        }).ToList()
                    }).ToList()
                });
                _redoStack.Push(currentState);
            }
            catch { }
            
            RestoreState(_undoStack.Pop());
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count == 0) return;
            
            SaveUndoState();
            RestoreState(_redoStack.Pop());
        }

        private void RestoreState(string stateJson)
        {
            try
            {
                var project = JsonSerializer.Deserialize<ProjectData>(stateJson);
                if (project == null) return;

                TimelineModel.Tracks.Clear();

                foreach (var trackData in project.Tracks)
                {
                    var trackType = Enum.TryParse<TrackType>(trackData.Type, out var t) ? t : TrackType.Video;
                    var track = new TimelineTrack
                    {
                        Name = trackData.Name,
                        Type = trackType
                    };

                    foreach (var clipData in trackData.Clips)
                    {
                        track.Clips.Add(new TimelineClip
                        {
                            Name = clipData.Name,
                            SourcePath = clipData.SourcePath,
                            StartTime = clipData.StartPosition,
                            Duration = clipData.Duration,
                            InPoint = clipData.InPoint,
                            OutPoint = clipData.OutPoint,
                            Gain = clipData.Volume
                        });
                    }

                    TimelineModel.Tracks.Add(track);
                }

                TimelineModel.Duration = project.Duration;
                Timeline.TimelineModel = TimelineModel;
            }
            catch { }
        }

        #endregion

        private void UpdatePropertiesPanel(PlaylistItem item)
        {
            PropertiesPanel.Children.Clear();
            
            AddPropertyRow("Name", item.Name);
            AddPropertyRow("Type", item.Type.ToString());
            AddPropertyRow("Duration", item.DurationText);
            AddPropertyRow("Resolution", item.Resolution);
            AddPropertyRow("File Size", item.FileSizeText);
            AddPropertyRow("Path", item.FilePath);
        }

        private void UpdatePropertiesPanel(TimelineClip clip)
        {
            PropertiesPanel.Children.Clear();
            
            AddPropertyRow("Clip Name", clip.Name);
            AddPropertyRow("Start Time", clip.StartTime.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Duration", clip.Duration.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("In Point", clip.InPoint.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Out Point", clip.OutPoint.ToString(@"hh\:mm\:ss\.ff"));
            AddPropertyRow("Gain", $"{clip.Gain:F2}");
            AddPropertyRow("Speed", $"{clip.Speed:F2}x");
            AddPropertyRow("Source", clip.SourcePath);
            
            // Add edit controls
            AddPropertySlider("Gain", clip.Gain, 0, 2, value => clip.Gain = value);
            AddPropertySlider("Speed", clip.Speed, 0.1, 4.0, value =>
            {
                clip.Speed = value;
                // Adjust clip duration based on speed
            });
            
            // Freeze frame toggle
            AddPropertyToggle("Freeze Frame", clip.IsFreezeFrame, isOn =>
            {
                clip.IsFreezeFrame = isOn;
                if (isOn)
                {
                    clip.FreezeAt = Timeline.Position > clip.StartTime && Timeline.Position < clip.EndTime
                        ? Timeline.Position - clip.StartTime 
                        : TimeSpan.Zero;
                }
            });
            
            // Track opacity for the track containing this clip
            foreach (var track in TimelineModel.Tracks)
            {
                if (track.Clips.Contains(clip))
                {
                    AddPropertySlider($"Track Opacity ({track.Name})", track.Opacity, 0, 1, value => track.Opacity = value);
                    AddPropertyCombo($"Blend Mode ({track.Name})", TimelineTrack.AvailableBlendModes, track.BlendMode, value => track.BlendMode = value);
                    break;
                }
            }
        }

        private void AddPropertyRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label + ":",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = value
            };
            Grid.SetColumn(valueBlock, 1);

            row.Children.Add(labelBlock);
            row.Children.Add(valueBlock);
            PropertiesPanel.Children.Add(row);
        }

        private void AddPropertySlider(string label, double value, double min, double max, Action<double> onChanged)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11
            };

            var valueBlock = new TextBlock
            {
                Text = value.ToString("F2"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11
            };
            Grid.SetColumn(valueBlock, 1);

            header.Children.Add(labelBlock);
            header.Children.Add(valueBlock);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Margin = new Thickness(0, 4, 0, 0)
            };
            slider.ValueChanged += (s, e) =>
            {
                valueBlock.Text = e.NewValue.ToString("F2");
                onChanged(e.NewValue);
            };

            row.Children.Add(header);
            row.Children.Add(slider);
            PropertiesPanel.Children.Add(row);
        }

        private void AddPropertyToggle(string label, bool value, Action<bool> onChanged)
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            };
            check.Checked += (s, e) => onChanged(true);
            check.Unchecked += (s, e) => onChanged(false);
            PropertiesPanel.Children.Add(check);
        }

        private void AddPropertyCombo(string label, string[] options, string selected, Action<string> onChanged)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11
            });
            var combo = new ComboBox { Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
            foreach (var opt in options) combo.Items.Add(opt);
            combo.SelectedItem = selected;
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is string val) onChanged(val);
            };
            row.Children.Add(combo);
            PropertiesPanel.Children.Add(row);
        }

        #region Customizable Layouts
        
        private bool _leftPanelVisible = true;
        private bool _rightPanelVisible = true;
        private bool _timelinePanelVisible = true;
        
        private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
        {
            _leftPanelVisible = !_leftPanelVisible;
            LeftPanel.Visibility = _leftPanelVisible ? Visibility.Visible : Visibility.Collapsed;
            LeftSplitter.Visibility = _leftPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
        {
            _rightPanelVisible = !_rightPanelVisible;
            RightPanel.Visibility = _rightPanelVisible ? Visibility.Visible : Visibility.Collapsed;
            RightSplitter.Visibility = _rightPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private void ToggleTimeline_Click(object sender, RoutedEventArgs e)
        {
            _timelinePanelVisible = !_timelinePanelVisible;
            TimelineRow.Height = _timelinePanelVisible ? new GridLength(200, GridUnitType.Star) : new GridLength(0);
            TimelineSplitter.Visibility = _timelinePanelVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        
        private void LayoutPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string preset)
            {
                switch (preset)
                {
                    case "Default":
                        SetPanelVisibility(true, true, true);
                        break;
                    case "EditOnly":
                        SetPanelVisibility(false, false, true);
                        break;
                    case "Preview":
                        SetPanelVisibility(false, false, false);
                        break;
                    case "Trim":
                        SetPanelVisibility(true, true, false);
                        break;
                }
            }
        }
        
        private void SetPanelVisibility(bool left, bool right, bool timeline)
        {
            _leftPanelVisible = left;
            _rightPanelVisible = right;
            _timelinePanelVisible = timeline;
            
            LeftPanel.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            LeftSplitter.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            RightPanel.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            RightSplitter.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
            TimelineRow.Height = timeline ? new GridLength(200, GridUnitType.Star) : new GridLength(0);
            TimelineSplitter.Visibility = timeline ? Visibility.Visible : Visibility.Collapsed;
        }
        
        #endregion

        #region Time Remap (TASK-351)

        /// <summary>
        /// Opens a time remap/speed curve editor dialog for the selected clip.
        /// Allows defining keyframeable speed changes over the clip duration.
        /// </summary>
        private void TimeRemap_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTimelineClip == null)
            {
                MessageBox.Show("Select a clip to apply time remapping.", "Time Remap", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var clip = _selectedTimelineClip;
            
            var dialog = new Window
            {
                Title = "Time Remap / Speed Curves",
                Width = 600, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E))
            };
            
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Time Remap / Speed Curves", FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10) });
            stack.Children.Add(new TextBlock { Text = $"Clip: {clip.Name}  |  Duration: {clip.Duration:hh\\:mm\\:ss\\.ff}", 
                Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 15) });
            
            // Speed curve preset selector
            stack.Children.Add(new TextBlock { Text = "Speed Curve Preset:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            var curvePresetCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var preset in new[] { "Constant", "Ease In (Speed Up)", "Ease Out (Slow Down)", "Ease In-Out", 
                "Speed Ramp In", "Speed Ramp Out", "Slow Motion Center", "Reverse Ramp" })
                curvePresetCombo.Items.Add(preset);
            curvePresetCombo.SelectedIndex = 0;
            stack.Children.Add(curvePresetCombo);
            
            // Speed range
            var rangePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            rangePanel.Children.Add(new TextBlock { Text = "Min Speed:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 80 });
            var minSpeedSlider = new Slider { Width = 160, Minimum = 0.1, Maximum = 4, Value = 0.25 };
            var minSpeedLabel = new TextBlock { Text = "0.25x", Foreground = System.Windows.Media.Brushes.Gray, 
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            minSpeedSlider.ValueChanged += (s, args) => minSpeedLabel.Text = $"{args.NewValue:F2}x";
            rangePanel.Children.Add(minSpeedSlider);
            rangePanel.Children.Add(minSpeedLabel);
            stack.Children.Add(rangePanel);
            
            var maxPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            maxPanel.Children.Add(new TextBlock { Text = "Max Speed:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 80 });
            var maxSpeedSlider = new Slider { Width = 160, Minimum = 0.5, Maximum = 8, Value = 2.0 };
            var maxSpeedLabel = new TextBlock { Text = "2.00x", Foreground = System.Windows.Media.Brushes.Gray, 
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            maxSpeedSlider.ValueChanged += (s, args) => maxSpeedLabel.Text = $"{args.NewValue:F2}x";
            maxPanel.Children.Add(maxSpeedSlider);
            maxPanel.Children.Add(maxSpeedLabel);
            stack.Children.Add(maxPanel);
            
            // Preserve pitch
            var pitchCheck = new CheckBox { Content = "Preserve Audio Pitch", Foreground = System.Windows.Media.Brushes.White, 
                IsChecked = clip.PreservePitch, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(pitchCheck);
            
            // Speed curve visual (simplified representation)
            var curveCanvas = new Canvas { Width = 540, Height = 120, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(0, 0, 0, 10) };
            // Draw baseline at 1x speed
            var baseLine = new System.Windows.Shapes.Line 
            { 
                X1 = 0, Y1 = 60, X2 = 540, Y2 = 60, 
                Stroke = System.Windows.Media.Brushes.Gray, 
                StrokeDashArray = new System.Windows.Media.DoubleCollection(new[] { 4.0, 4.0 }) 
            };
            curveCanvas.Children.Add(baseLine);
            var baseLabel = new TextBlock { Text = "1.0x", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 10 };
            Canvas.SetLeft(baseLabel, 2); Canvas.SetTop(baseLabel, 45);
            curveCanvas.Children.Add(baseLabel);
            stack.Children.Add(curveCanvas);
            
            // Draw curve based on preset selection
            void UpdateCurveVisual()
            {
                // Remove previous curve lines
                var toRemove = curveCanvas.Children.OfType<System.Windows.Shapes.Polyline>().ToList();
                foreach (var r in toRemove) curveCanvas.Children.Remove(r);
                
                var points = new System.Windows.Media.PointCollection();
                var preset = curvePresetCombo.SelectedItem?.ToString() ?? "Constant";
                var minS = minSpeedSlider.Value;
                var maxS = maxSpeedSlider.Value;
                
                for (int i = 0; i <= 100; i++)
                {
                    double t = i / 100.0;
                    double speed = preset switch
                    {
                        "Ease In (Speed Up)" => minS + (maxS - minS) * t * t,
                        "Ease Out (Slow Down)" => maxS + (minS - maxS) * t * t,
                        "Ease In-Out" => minS + (maxS - minS) * (t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2),
                        "Speed Ramp In" => t < 0.3 ? minS : minS + (maxS - minS) * ((t - 0.3) / 0.7),
                        "Speed Ramp Out" => t < 0.7 ? maxS + (minS - maxS) * (t / 0.7) : minS,
                        "Slow Motion Center" => t < 0.25 ? 1.0 : (t > 0.75 ? 1.0 : minS),
                        "Reverse Ramp" => maxS - (maxS - minS) * t,
                        _ => 1.0 // Constant
                    };
                    double x = t * 540;
                    double y = 120 - (speed / Math.Max(maxS, 4)) * 100;
                    points.Add(new System.Windows.Point(x, Math.Clamp(y, 5, 115)));
                }
                
                var polyline = new System.Windows.Shapes.Polyline 
                { 
                    Points = points, 
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                    StrokeThickness = 2 
                };
                curveCanvas.Children.Add(polyline);
            }
            
            curvePresetCombo.SelectionChanged += (_, _) => UpdateCurveVisual();
            minSpeedSlider.ValueChanged += (_, _) => UpdateCurveVisual();
            maxSpeedSlider.ValueChanged += (_, _) => UpdateCurveVisual();
            UpdateCurveVisual();
            
            // Buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(8, 4, 8, 4) };
            cancelBtn.Click += (_, _) => dialog.Close();
            var applyBtn = new Button { Content = "Apply Curve", Width = 100, Padding = new Thickness(8, 4, 8, 4),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x88, 0xFF)),
                Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            applyBtn.Click += (_, _) =>
            {
                var selectedPreset = curvePresetCombo.SelectedItem?.ToString() ?? "Constant";
                clip.PreservePitch = pitchCheck.IsChecked == true;
                
                // Apply the average speed from the curve preset
                var avgSpeed = selectedPreset switch
                {
                    "Ease In (Speed Up)" => (minSpeedSlider.Value + maxSpeedSlider.Value) / 2.0,
                    "Ease Out (Slow Down)" => (maxSpeedSlider.Value + minSpeedSlider.Value) / 2.0,
                    "Ease In-Out" => (minSpeedSlider.Value + maxSpeedSlider.Value) / 2.0,
                    "Speed Ramp In" => minSpeedSlider.Value * 0.3 + maxSpeedSlider.Value * 0.7,
                    "Speed Ramp Out" => maxSpeedSlider.Value * 0.7 + minSpeedSlider.Value * 0.3,
                    "Slow Motion Center" => (1.0 * 0.5 + minSpeedSlider.Value * 0.5),
                    "Reverse Ramp" => (maxSpeedSlider.Value + minSpeedSlider.Value) / 2.0,
                    _ => 1.0
                };
                clip.Speed = avgSpeed;
                
                // Add time remap filter with curve data
                var remapFilter = clip.Filters.FirstOrDefault(f => f.Name == "time_remap");
                if (remapFilter != null) clip.Filters.Remove(remapFilter);
                
                remapFilter = new Filter
                {
                    Name = "time_remap",
                    DisplayName = $"Time Remap: {selectedPreset}",
                    Category = Core.Models.Video.FilterCategory.Time,
                    FFmpegFilterName = "setpts",
                    Icon = "⏱",
                    Description = $"Speed curve: {selectedPreset} ({minSpeedSlider.Value:F1}x → {maxSpeedSlider.Value:F1}x)",
                    IsEnabled = true
                };
                clip.Filters.Add(remapFilter);
                
                UpdatePropertiesPanel(clip);
                dialog.Close();
                MessageBox.Show($"Applied time remap: {selectedPreset}\nAvg speed: {avgSpeed:F2}x", "Time Remap", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            };
            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(applyBtn);
            stack.Children.Add(btnPanel);
            
            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
            dialog.Content = scrollViewer;
            dialog.ShowDialog();
        }

        #endregion

        #region External Monitor (TASK-370)

        private Window? _externalMonitorWindow;

        /// <summary>
        /// Opens or toggles an external monitor preview window.
        /// Shows the current frame/preview on a separate window that can be moved to a second monitor.
        /// </summary>
        private void ExternalMonitor_Click(object sender, RoutedEventArgs e)
        {
            if (_externalMonitorWindow != null && _externalMonitorWindow.IsVisible)
            {
                _externalMonitorWindow.Close();
                _externalMonitorWindow = null;
                return;
            }
            
            _externalMonitorWindow = new Window
            {
                Title = "PlatypusTools — External Preview",
                Width = 960, Height = 540,
                Background = System.Windows.Media.Brushes.Black,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.CanResize
            };
            
            var grid = new Grid();
            
            // Preview image (mirrors the main preview)
            var previewImage = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(previewImage);
            
            // Status bar
            var statusBar = new TextBlock
            {
                Text = "External Preview — drag to second monitor | Ctrl+F = fullscreen",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            };
            grid.Children.Add(statusBar);
            
            // Fullscreen toggle on Ctrl+F
            _externalMonitorWindow.KeyDown += (s, args) =>
            {
                if (args.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
                {
                    if (_externalMonitorWindow.WindowStyle == WindowStyle.None)
                    {
                        _externalMonitorWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                        _externalMonitorWindow.WindowState = WindowState.Normal;
                        statusBar.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _externalMonitorWindow.WindowStyle = WindowStyle.None;
                        _externalMonitorWindow.WindowState = WindowState.Maximized;
                        statusBar.Visibility = Visibility.Collapsed;
                    }
                }
                else if (args.Key == System.Windows.Input.Key.Escape)
                {
                    if (_externalMonitorWindow.WindowStyle == WindowStyle.None)
                    {
                        _externalMonitorWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                        _externalMonitorWindow.WindowState = WindowState.Normal;
                        statusBar.Visibility = Visibility.Visible;
                    }
                }
            };
            
            // Mirror the main preview frame info
            var statusText = "Drag this window to a second monitor for full-screen preview.";
            statusBar.Text = $"External Preview — {statusText}";
            
            // Update external monitor status periodically
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, args) =>
            {
                if (_externalMonitorWindow == null || !_externalMonitorWindow.IsVisible)
                {
                    timer.Stop();
                    return;
                }
                statusBar.Text = $"External Preview — {Timeline.Position:hh\\:mm\\:ss\\.ff}";
            };
            timer.Start();
            
            _externalMonitorWindow.Closed += (s, args) =>
            {
                timer.Stop();
                _externalMonitorWindow = null;
            };
            
            _externalMonitorWindow.Content = grid;
            _externalMonitorWindow.Show();
        }

        #endregion
    }

    #region Project Serialization Models

    /// <summary>
    /// Project data for saving/loading
    /// </summary>
    public class ProjectData
    {
        public string Version { get; set; } = "1.0";
        public DateTime SavedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public double FrameRate { get; set; } = 30;
        public List<ProjectPlaylistItem> PlaylistItems { get; set; } = new();
        public List<ProjectTrack> Tracks { get; set; } = new();
    }

    public class ProjectPlaylistItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool IsVideo { get; set; }
    }

    public class ProjectTrack
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Video";
        public bool IsHidden { get; set; }
        public bool IsMuted { get; set; }
        public bool IsLocked { get; set; }
        public double Volume { get; set; } = 1.0;
        public List<ProjectClip> Clips { get; set; } = new();
    }

    public class ProjectClip
    {
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public TimeSpan StartPosition { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan InPoint { get; set; }
        public TimeSpan OutPoint { get; set; }
        public double Speed { get; set; } = 1.0;
        public double Volume { get; set; } = 1.0;
        public double Opacity { get; set; } = 1.0;
    }

    #endregion
}
