using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PlatypusTools.UI.Models.VideoEditor;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Native Shotcut-style video editor view.
    /// Combines playlist, player, properties, and timeline panels.
    /// </summary>
    public partial class ShotcutNativeEditorView : UserControl
    {
        public ObservableCollection<PlaylistItem> PlaylistItems { get; } = new();
        public TimelineModel TimelineModel { get; } = new();

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
            InitializeComponent();
            DataContext = this;
            PlaylistBox.ItemsSource = PlaylistItems;
            
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
                    IsLocked = uiTrack.IsLocked
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

        private MediaType GetMediaType(string extension)
        {
            return extension switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" => MediaType.Video,
                ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" => MediaType.Audio,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => MediaType.Image,
                _ => MediaType.Video
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

        #region Properties Panel

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
            AddPropertyRow("Source", clip.SourcePath);
            
            // Add edit controls
            AddPropertySlider("Gain", clip.Gain, 0, 2, value => clip.Gain = value);
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
