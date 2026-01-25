using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using PlatypusTools.Core.Models.Video;
using PlatypusTools.Core.Services.Video;
using PlatypusTools.UI.Converters;
using PlatypusTools.UI.Utilities;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for VideoEditorView.xaml
    /// </summary>
    public partial class VideoEditorView : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDraggingPlayhead;
        private bool _isDraggingRuler;
        private string? _currentPreviewSource;
        private bool _useFFmpegPreview;
        private DispatcherTimer? _ffmpegPlaybackTimer;
        private DateTime _lastFrameUpdate = DateTime.MinValue;
        
        // LibVLC for universal video playback
        private static LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia; // Keep media reference alive
        private bool _vlcInitialized;

        // File extensions that WPF MediaElement can play natively (limited!)
        // AVI/MKV/WebM require codec packs - use FFmpeg extraction instead
        private static readonly HashSet<string> _nativePlayableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mp3", ".m4a", ".wav", ".wma", ".wmv"
        };

        public VideoEditorView()
        {
            InitializeComponent();
            
            // Initialize LibVLC for universal video playback
            InitializeLibVLC();
            
            // Subscribe to DataContext changes
            DataContextChanged += OnDataContextChanged;
            
            // Enable keyboard focus for shortcuts
            Focusable = true;
            Loaded += VideoEditorView_Loaded;
            Unloaded += VideoEditorView_Unloaded;
            
            // Setup keyboard shortcuts (Shotcut-style)
            PreviewKeyDown += VideoEditorView_PreviewKeyDown;
        }
        
        /// <summary>
        /// Initialize LibVLC for video playback - plays any format without external dependencies
        /// </summary>
        private void InitializeLibVLC()
        {
            System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] InitializeLibVLC called");
            try
            {
                // Initialize LibVLC core (only once per application)
                if (_libVLC == null)
                {
                    System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] Calling Core.Initialize()");
                    LibVLCSharp.Shared.Core.Initialize();
                    _libVLC = new LibVLC(
                        "--no-video-title-show",  // Don't show filename overlay
                        "--quiet",                 // Less verbose logging
                        "--no-snapshot-preview"    // No snapshot preview
                    );
                    System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] LibVLC instance created");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] LibVLC already exists (shared)");
                }
                
                // Create the media player with hardware decoding
                _mediaPlayer = new MediaPlayer(_libVLC)
                {
                    EnableHardwareDecoding = true
                };
                System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] MediaPlayer created");
                
                // DON'T wire up VideoView here - must wait for Loaded event
                // VlcVideoView.MediaPlayer = _mediaPlayer;
                
                // Handle time/position updates
                _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.Paused += MediaPlayer_Paused;
                _mediaPlayer.Stopped += MediaPlayer_Stopped;
                
                // Add error/buffer events for debugging
                _mediaPlayer.EncounteredError += (s, e) => System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] ERROR: MediaPlayer encountered error!");
                _mediaPlayer.Buffering += (s, e) => System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] Buffering: {e.Cache}%");
                _mediaPlayer.Opening += (s, e) => System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] Media opening...");
                _mediaPlayer.Vout += (s, e) => System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] Video output count: {e.Count}");
                
                _vlcInitialized = true;
                System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] LibVLC initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] FAILED to initialize LibVLC: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] Stack: {ex.StackTrace}");
                _vlcInitialized = false;
            }
        }
        
        private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is VideoEditorViewModel vm && vm.IsPlaying)
                {
                    // Update ViewModel's current time from VLC
                    var newTime = TimeSpan.FromMilliseconds(e.Time);
                    if (Math.Abs((newTime - vm.CurrentTime).TotalMilliseconds) > 50)
                    {
                        vm.CurrentTime = newTime;
                    }
                }
            });
        }
        
        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.IsPlaying = false;
                    // Loop or stop based on settings
                    if (vm.LoopPlayback)
                    {
                        vm.SkipPreviousCommand.Execute(null);
                        vm.PlayPauseCommand.Execute(null);
                    }
                }
            });
        }
        
        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.IsPlaying = true;
                }
            });
        }
        
        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.IsPlaying = false;
                }
            });
        }
        
        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.IsPlaying = false;
                }
            });
        }
        
        private void VideoEditorView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Just pause, don't dispose - we might come back to this tab
            _mediaPlayer?.Pause();
        }
        
        /// <summary>
        /// Handle loaded event - setup scroll synchronization and VLC
        /// </summary>
        private void VideoEditorView_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
            
            // Ensure we have a MediaPlayer (recreate if was disposed)
            if (_mediaPlayer == null && _libVLC != null)
            {
                _mediaPlayer = new MediaPlayer(_libVLC)
                {
                    EnableHardwareDecoding = true
                };
                _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.Paused += MediaPlayer_Paused;
                _mediaPlayer.Stopped += MediaPlayer_Stopped;
                _vlcInitialized = true;
            }
            
            // Ensure PropertyChanged is subscribed (may have been set before constructor subscription)
            if (DataContext is VideoEditorViewModel vm)
            {
                // Unsubscribe first to avoid double-subscription
                vm.PropertyChanged -= ViewModel_PropertyChanged;
                vm.PropertyChanged += ViewModel_PropertyChanged;
                System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] Ensured PropertyChanged subscription in Loaded");
                
                // If there's already a preview source, load it now
                if (!string.IsNullOrEmpty(vm.PreviewSource))
                {
                    System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] Found existing PreviewSource: {vm.PreviewSource}");
                    UpdatePreviewMedia(vm.PreviewSource);
                }
            }
            
            // Attach MediaPlayer to VideoView now that the control is loaded
            // This MUST be done after Loaded or the VideoView won't have an HWND
            System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] VideoEditorView_Loaded - vlcInitialized={_vlcInitialized}, mediaPlayer={((_mediaPlayer != null) ? "exists" : "null")}, videoView.MediaPlayer={(VlcVideoView.MediaPlayer != null ? "attached" : "null")}");
            System.Diagnostics.Debug.WriteLine($"[VLC-EDITOR] VlcVideoView size: {VlcVideoView.ActualWidth}x{VlcVideoView.ActualHeight}");
            
            if (_vlcInitialized && _mediaPlayer != null && VlcVideoView.MediaPlayer == null)
            {
                VlcVideoView.MediaPlayer = _mediaPlayer;
                System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] MediaPlayer attached to VideoView");
            }
            else if (VlcVideoView.MediaPlayer != null)
            {
                System.Diagnostics.Debug.WriteLine("[VLC-EDITOR] MediaPlayer was already attached");
            }
            
            // Synchronize vertical scroll between track headers and timeline content
            if (TimelineScrollViewer != null && TrackHeadersScrollViewer != null)
            {
                TimelineScrollViewer.ScrollChanged += (s, args) =>
                {
                    TrackHeadersScrollViewer.ScrollToVerticalOffset(args.VerticalOffset);
                };
            }
        }

        /// <summary>
        /// Handle keyboard shortcuts (J/K/L style transport, arrow keys, etc.)
        /// </summary>
        private void VideoEditorView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not VideoEditorViewModel vm) return;
            
            bool handled = true;
            
            switch (e.Key)
            {
                // J/K/L transport (industry standard)
                case Key.J:
                    vm.RewindCommand.Execute(null);
                    break;
                case Key.K:
                    vm.PlayPauseCommand.Execute(null);
                    break;
                case Key.L:
                    vm.FastForwardCommand.Execute(null);
                    break;
                    
                // Space for play/pause
                case Key.Space:
                    vm.PlayPauseCommand.Execute(null);
                    break;
                    
                // Arrow keys for frame navigation
                case Key.Left:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Ctrl+Left: Scroll timeline view left
                        ScrollTimelineLeft();
                    }
                    else if (Keyboard.Modifiers == ModifierKeys.Shift)
                        vm.SeekBackOneSecondCommand.Execute(null);
                    else
                        vm.PreviousFrameCommand.Execute(null);
                    break;
                case Key.Right:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        // Ctrl+Right: Scroll timeline view right
                        ScrollTimelineRight();
                    }
                    else if (Keyboard.Modifiers == ModifierKeys.Shift)
                        vm.SeekForwardOneSecondCommand.Execute(null);
                    else
                        vm.NextFrameCommand.Execute(null);
                    break;
                    
                // Home/End for skip
                case Key.Home:
                    vm.SkipPreviousCommand.Execute(null);
                    break;
                case Key.End:
                    vm.SkipNextCommand.Execute(null);
                    break;
                    
                // I/O for in/out points
                case Key.I:
                    vm.SetInPointCommand.Execute(null);
                    break;
                case Key.O:
                    vm.SetOutPointCommand.Execute(null);
                    break;
                    
                // M for marker
                case Key.M:
                    vm.AddMarkerCommand.Execute(null);
                    break;
                    
                // S for Split (Shotcut-style)
                case Key.S:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                        handled = false; // Let Ctrl+S pass through for save
                    else if (vm.SplitClipCommand.CanExecute(null))
                        vm.SplitClipCommand.Execute(null);
                    break;
                    
                // V for Insert at playhead (Shotcut-style ripple insert)
                case Key.V:
                    if (Keyboard.Modifiers != ModifierKeys.Control) // Don't interfere with Ctrl+V paste
                    {
                        if (vm.InsertAtPlayheadCommand.CanExecute(null))
                            vm.InsertAtPlayheadCommand.Execute(null);
                    }
                    else
                        handled = false;
                    break;
                    
                // B for Overwrite at playhead (Shotcut-style)
                case Key.B:
                    if (vm.SelectedAsset != null)
                        vm.OverwriteAtPlayhead(vm.SelectedAsset);
                    break;
                    
                // Delete for delete clip
                case Key.Delete:
                    if (vm.DeleteClipCommand.CanExecute(null))
                        vm.DeleteClipCommand.Execute(null);
                    break;
                    
                // X for ripple delete (delete and close gap)
                case Key.X:
                    ExecuteRippleDelete(vm);
                    break;
                    
                default:
                    handled = false;
                    break;
            }
            
            if (handled)
                e.Handled = true;
        }
        
        /// <summary>
        /// Shotcut-style ripple delete: delete clip and close the gap by shifting following clips left
        /// </summary>
        private void ExecuteRippleDelete(VideoEditorViewModel vm)
        {
            // Find clip to delete (selected or at playhead)
            TimelineClip? clipToDelete = vm.SelectedClip;
            TimelineTrack? clipTrack = null;
            
            if (clipToDelete != null)
            {
                clipTrack = vm.Tracks.FirstOrDefault(t => t.Clips.Contains(clipToDelete));
            }
            else
            {
                // Find clip at playhead
                foreach (var track in vm.Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (vm.CurrentTime >= clip.StartPosition && vm.CurrentTime < clip.EndPosition)
                        {
                            clipToDelete = clip;
                            clipTrack = track;
                            break;
                        }
                    }
                    if (clipToDelete != null) break;
                }
            }
            
            if (clipToDelete == null || clipTrack == null)
            {
                vm.StatusMessage = "No clip to delete";
                return;
            }
            
            var deletedDuration = clipToDelete.Duration;
            var deletedStart = clipToDelete.StartPosition;
            
            // Remove the clip
            clipTrack.Clips.Remove(clipToDelete);
            
            // Ripple: shift all clips after the deleted one to the left
            foreach (var clip in clipTrack.Clips)
            {
                if (clip.StartPosition > deletedStart)
                {
                    clip.StartPosition -= deletedDuration;
                    clip.StartTime = clip.StartPosition;
                }
            }
            
            vm.SelectedClip = null;
            vm.StatusMessage = $"Ripple deleted '{clipToDelete.Name}'";
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is VideoEditorViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }
            
            if (e.NewValue is VideoEditorViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not VideoEditorViewModel vm) return;

            switch (e.PropertyName)
            {
                case nameof(VideoEditorViewModel.PreviewSource):
                    vm.LogDebug($"[VIEW] PreviewSource changed to: {vm.PreviewSource}");
                    UpdatePreviewMedia(vm.PreviewSource);
                    break;
                
                case nameof(VideoEditorViewModel.OverlaySource):
                    vm.LogDebug($"[VIEW] OverlaySource changed to: {vm.OverlaySource}");
                    UpdateOverlayImage(vm.OverlaySource);
                    break;
                    
                case nameof(VideoEditorViewModel.IsPlaying):
                    vm.LogDebug($"[VIEW] IsPlaying changed to: {vm.IsPlaying}");
                    if (vm.IsPlaying)
                    {
                        // Use LibVLC for playback
                        if (_vlcInitialized && _mediaPlayer != null)
                        {
                            _mediaPlayer.Play();
                        }
                        else if (_useFFmpegPreview)
                        {
                            // Fallback to FFmpeg frame extraction
                            StartFFmpegPlaybackTimer();
                        }
                        else
                        {
                            PreviewMediaElement.Play();
                        }
                    }
                    else
                    {
                        StopFFmpegPlaybackTimer();
                        if (_vlcInitialized && _mediaPlayer != null)
                        {
                            _mediaPlayer.Pause();
                        }
                        PreviewMediaElement.Pause();
                    }
                    break;
                    
                case nameof(VideoEditorViewModel.CurrentTime):
                    // Seek VLC if initialized and not playing (scrubbing)
                    if (_vlcInitialized && _mediaPlayer != null && !vm.IsPlaying)
                    {
                        var targetMs = (long)vm.CurrentTime.TotalMilliseconds;
                        if (Math.Abs(_mediaPlayer.Time - targetMs) > 100)
                        {
                            _mediaPlayer.Time = targetMs;
                        }
                    }
                    // Update preview frame for FFmpeg mode
                    else if (_useFFmpegPreview && !vm.IsPlaying)
                    {
                        // Throttle frame updates to avoid overloading
                        if ((DateTime.Now - _lastFrameUpdate).TotalMilliseconds > 100)
                        {
                            _lastFrameUpdate = DateTime.Now;
                            _ = UpdateFFmpegFrameAsync();
                        }
                    }
                    // For native playback, seek if not playing
                    else if (!vm.IsPlaying && PreviewMediaElement.Source != null && !_useFFmpegPreview && !_vlcInitialized)
                    {
                        try
                        {
                            // Calculate position within the current clip
                            var clip = GetCurrentClipAtTime(vm);
                            if (clip != null)
                            {
                                var clipOffset = vm.CurrentTime - clip.StartTime;
                                if (clipOffset >= TimeSpan.Zero && clipOffset < clip.Duration)
                                {
                                    PreviewMediaElement.Position = clip.SourceStart + clipOffset;
                                }
                            }
                        }
                        catch { /* Ignore seek errors */ }
                    }
                    break;
            }
        }

        private TimelineClip? GetCurrentClipAtTime(VideoEditorViewModel vm)
        {
            foreach (var track in vm.Tracks)
            {
                if (track.Type != PlatypusTools.Core.Models.Video.TrackType.Video && track.Type != PlatypusTools.Core.Models.Video.TrackType.Overlay) continue;
                
                foreach (var clip in track.Clips)
                {
                    if (vm.CurrentTime >= clip.StartTime && vm.CurrentTime < clip.StartTime + clip.Duration)
                    {
                        return clip;
                    }
                }
            }
            return null;
        }

        private void StartFFmpegPlaybackTimer()
        {
            if (_ffmpegPlaybackTimer == null)
            {
                _ffmpegPlaybackTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200) // 5 FPS for FFmpeg preview
                };
                _ffmpegPlaybackTimer.Tick += async (s, e) => await UpdateFFmpegFrameAsync();
            }
            _ffmpegPlaybackTimer.Start();
        }

        private void StopFFmpegPlaybackTimer()
        {
            _ffmpegPlaybackTimer?.Stop();
        }

        private async Task UpdateFFmpegFrameAsync()
        {
            if (DataContext is not VideoEditorViewModel vm) return;
            if (string.IsNullOrEmpty(_currentPreviewSource)) return;

            try
            {
                // Use direct FFmpeg extraction for reliability
                await TryDirectFFmpegExtraction(_currentPreviewSource);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFMPEG] Frame update error: {ex.Message}");
            }
        }

        private async void UpdatePreviewMedia(string? sourcePath)
        {
            var vm = DataContext as VideoEditorViewModel;
            void Log(string msg) 
            {
                System.Diagnostics.Debug.WriteLine(msg);
                vm?.LogDebug(msg);
            }
            
            Log($"[PREVIEW] UpdatePreviewMedia called with: '{sourcePath}'");
            
            if (string.IsNullOrEmpty(sourcePath))
            {
                Log("[PREVIEW] Clearing preview (null/empty source)");
                ClearPreview();
                return;
            }

            // Only reload if source changed
            if (sourcePath == _currentPreviewSource)
            {
                Log("[PREVIEW] Source unchanged, skipping reload");
                return;
            }
            
            _currentPreviewSource = sourcePath;

            // Check if file exists
            if (!File.Exists(sourcePath))
            {
                Log($"[PREVIEW] ERROR: File does not exist: '{sourcePath}'");
                return;
            }

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            Log($"[PREVIEW] Loading file with extension: {ext}");

            // Check if this is an image file - handle separately from video
            var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
            };

            if (imageExtensions.Contains(ext))
            {
                Log($"[PREVIEW] Loading IMAGE file: {sourcePath}");
                
                // Stop VLC if it was playing
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Stop();
                }
                _currentMedia?.Dispose();
                _currentMedia = null;
                
                // Hide video players, show image
                _useFFmpegPreview = false;
                VlcVideoView.Visibility = Visibility.Collapsed;
                PreviewMediaElement.Visibility = Visibility.Collapsed;
                FFmpegWarningBorder.Visibility = Visibility.Collapsed;
                PreviewFrameImage.Visibility = Visibility.Visible;
                
                try
                {
                    var bitmap = ImageHelper.LoadFromFile(sourcePath);
                    
                    PreviewFrameImage.Source = bitmap;
                    Log($"[PREVIEW] Image loaded: {bitmap?.PixelWidth}x{bitmap?.PixelHeight}");
                    
                    // Set a default duration for images (5 seconds)
                    if (vm != null)
                    {
                        vm.Duration = TimeSpan.FromSeconds(5);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PREVIEW] Error loading image: {ex.Message}");
                }
                return;
            }

            try
            {
                // Use LibVLC for all video playback - it handles any format
                if (_vlcInitialized && _mediaPlayer != null && _libVLC != null)
                {
                    Log($"[PREVIEW] Using LibVLC for: {sourcePath}");
                    Log($"[PREVIEW] VlcVideoView.MediaPlayer = {(VlcVideoView.MediaPlayer != null ? "attached" : "null")}");
                    Log($"[PREVIEW] VlcVideoView size: {VlcVideoView.ActualWidth}x{VlcVideoView.ActualHeight}");
                    
                    // Ensure MediaPlayer is attached
                    if (VlcVideoView.MediaPlayer == null)
                    {
                        VlcVideoView.MediaPlayer = _mediaPlayer;
                        Log("[PREVIEW] Re-attached MediaPlayer to VideoView");
                    }
                    
                    _useFFmpegPreview = false;
                    PreviewFrameImage.Visibility = Visibility.Collapsed;
                    PreviewMediaElement.Visibility = Visibility.Collapsed;
                    FFmpegWarningBorder.Visibility = Visibility.Collapsed;
                    VlcVideoView.Visibility = Visibility.Visible;
                    
                    // Dispose any previous media
                    _currentMedia?.Dispose();
                    
                    // Create media from file path - DON'T use 'using', we need to keep it alive
                    Log($"[PREVIEW] Creating Media from: {sourcePath}");
                    _currentMedia = new Media(_libVLC, sourcePath, FromType.FromPath);
                    
                    // Parse the media to get duration
                    Log("[PREVIEW] Parsing media...");
                    await _currentMedia.Parse(MediaParseOptions.ParseLocal);
                    Log($"[PREVIEW] Media parsed, duration: {_currentMedia.Duration}ms");
                    
                    if (vm != null && _currentMedia.Duration > 0)
                    {
                        vm.Duration = TimeSpan.FromMilliseconds(_currentMedia.Duration);
                        Log($"[PREVIEW] Set VM duration: {vm.Duration}");
                    }
                    
                    // Set the media on the player
                    _mediaPlayer.Media = _currentMedia;
                    Log("[PREVIEW] Media assigned to player");
                    
                    // Start playback to show first frame
                    Log("[PREVIEW] Starting VLC playback");
                    _mediaPlayer.Play();
                    
                    // If we shouldn't be playing, pause after a short delay to show first frame
                    if (vm == null || !vm.IsPlaying)
                    {
                        await Task.Delay(500);
                        _mediaPlayer.Pause();
                        Log("[PREVIEW] Paused at first frame");
                    }
                }
                else
                {
                    Log($"[PREVIEW] VLC not available! vlcInit={_vlcInitialized}, mediaPlayer={(_mediaPlayer != null ? "exists" : "null")}, libVLC={(_libVLC != null ? "exists" : "null")}");
                    
                    // Fallback to FFmpeg frame extraction
                    _useFFmpegPreview = true;
                    PreviewMediaElement.Visibility = Visibility.Collapsed;
                    VlcVideoView.Visibility = Visibility.Collapsed;
                    PreviewFrameImage.Visibility = Visibility.Visible;
                    FFmpegWarningBorder.Visibility = Visibility.Collapsed;
                    
                    // Get duration from FFprobe
                    var ffprobePath = PlatypusTools.Core.Services.FFprobeService.FindFfprobe();
                    if (!string.IsNullOrEmpty(ffprobePath))
                    {
                        try
                        {
                            var durationSeconds = await PlatypusTools.Core.Services.FFprobeService.GetDurationSecondsAsync(sourcePath, ffprobePath);
                            if (vm != null && durationSeconds > 0)
                            {
                                vm.Duration = TimeSpan.FromSeconds(durationSeconds);
                                Log($"[PREVIEW] Duration from FFprobe: {vm.Duration}");
                            }
                        }
                        catch (Exception dex)
                        {
                            Log($"[PREVIEW] Could not get duration: {dex.Message}");
                        }
                    }
                    
                    // Extract preview frame
                    await TryDirectFFmpegExtraction(sourcePath);
                }
            }
            catch (Exception ex)
            {
                Log($"[PREVIEW] ERROR loading preview: {ex.Message}");
                Log($"[PREVIEW] Stack trace: {ex.StackTrace}");
                
                // Show FFmpeg warning if extraction failed
                FFmpegWarningBorder.Visibility = Visibility.Visible;
            }
        }
        
        private void ClearPreview()
        {
            _currentPreviewSource = null;
            _useFFmpegPreview = false;
            
            // Stop VLC and dispose media
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
            }
            
            _currentMedia?.Dispose();
            _currentMedia = null;
            
            // Clear all preview elements
            PreviewMediaElement.Source = null;
            PreviewFrameImage.Source = null;
            PreviewFrameImage.Visibility = Visibility.Collapsed;
            OverlayImage.Source = null;
            OverlayImage.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Updates the overlay image displayed on top of the video preview.
        /// </summary>
        private void UpdateOverlayImage(string? overlayPath)
        {
            var vm = DataContext as VideoEditorViewModel;
            void Log(string msg) 
            {
                System.Diagnostics.Debug.WriteLine(msg);
                vm?.LogDebug(msg);
            }
            
            if (string.IsNullOrEmpty(overlayPath))
            {
                // Clear overlay
                OverlayImage.Source = null;
                OverlayImage.Visibility = Visibility.Collapsed;
                Log("[OVERLAY] Cleared overlay");
                return;
            }
            
            if (!File.Exists(overlayPath))
            {
                Log($"[OVERLAY] File not found: {overlayPath}");
                OverlayImage.Source = null;
                OverlayImage.Visibility = Visibility.Collapsed;
                return;
            }
            
            try
            {
                Log($"[OVERLAY] Loading overlay image: {overlayPath}");
                
                // Load the image
                var bitmap = ImageHelper.LoadFromFile(overlayPath);
                
                OverlayImage.Source = bitmap;
                OverlayImage.Visibility = Visibility.Visible;
                
                Log($"[OVERLAY] Overlay displayed: {bitmap?.PixelWidth}x{bitmap?.PixelHeight}");
            }
            catch (Exception ex)
            {
                Log($"[OVERLAY] Error loading overlay: {ex.Message}");
                OverlayImage.Source = null;
                OverlayImage.Visibility = Visibility.Collapsed;
            }
        }
        
        /// <summary>
        /// Extracts and displays a frame from the video using FFmpeg.
        /// </summary>
        private async Task ExtractAndShowFrameAsync(string videoPath)
        {
            var vm = DataContext as VideoEditorViewModel;
            void Log(string msg) 
            {
                System.Diagnostics.Debug.WriteLine(msg);
                vm?.LogDebug(msg);
            }
            
            if (vm == null)
            {
                Log("[FRAME] ERROR: ViewModel is null");
                return;
            }
            
            try
            {
                // Get current time offset within the clip
                var clip = GetCurrentClipAtTime(vm);
                var seekTime = TimeSpan.Zero;
                
                if (clip != null)
                {
                    var clipOffset = vm.CurrentTime - clip.StartTime;
                    if (clipOffset >= TimeSpan.Zero)
                    {
                        seekTime = clip.SourceStart + clipOffset;
                    }
                    Log($"[FRAME] Found clip at playhead: {clip.Name}, seeking to {seekTime}");
                }
                else
                {
                    Log("[FRAME] No clip at playhead, using time zero");
                }
                
                Log($"[FRAME] Calling EditorService.ExtractPreviewFrameAsync for: {videoPath}");
                
                // Use the VideoEditorService to extract a frame
                var framePath = await vm.EditorService.ExtractPreviewFrameAsync(videoPath, seekTime);
                
                Log($"[FRAME] EditorService returned: {framePath ?? "null"}");
                
                if (!string.IsNullOrEmpty(framePath) && File.Exists(framePath))
                {
                    Log($"[FRAME] Frame file exists, loading image: {framePath}");
                    
                    // Load the frame image
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var bitmap = ImageHelper.LoadUncached(framePath);
                            
                            PreviewFrameImage.Source = bitmap;
                            PreviewFrameImage.Visibility = Visibility.Visible;
                            PreviewMediaElement.Visibility = Visibility.Collapsed;
                            
                            Log($"[FRAME] SUCCESS - Frame loaded and displayed: {bitmap?.PixelWidth}x{bitmap?.PixelHeight}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[FRAME] ERROR displaying frame: {ex.Message}");
                        }
                    });
                }
                else
                {
                    Log("[FRAME] EditorService extraction failed - trying direct FFmpeg");
                    await TryDirectFFmpegExtraction(videoPath);
                }
            }
            catch (Exception ex)
            {
                Log($"[FRAME] Exception in extraction: {ex.Message}");
                await TryDirectFFmpegExtraction(videoPath);
            }
        }
        
        /// <summary>
        /// Directly extracts a frame using FFmpeg without going through the service layer.
        /// This is the ultimate fallback for preview.
        /// </summary>
        private async Task TryDirectFFmpegExtraction(string videoPath)
        {
            var vm = DataContext as VideoEditorViewModel;
            void Log(string msg) 
            {
                System.Diagnostics.Debug.WriteLine(msg);
                vm?.LogDebug(msg);
            }
            
            try
            {
                // Find FFmpeg in PATH
                var ffmpegPath = PlatypusTools.Core.Services.FFmpegService.FindFfmpeg();
                
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    Log("[DIRECT FFMPEG] ERROR: FFmpeg not found! Install FFmpeg and add to PATH.");
                    // Show the warning in the UI
                    await Dispatcher.InvokeAsync(() =>
                    {
                        FFmpegWarningBorder.Visibility = Visibility.Visible;
                    });
                    return;
                }
                
                // Hide warning if FFmpeg is found
                await Dispatcher.InvokeAsync(() =>
                {
                    FFmpegWarningBorder.Visibility = Visibility.Collapsed;
                });

                // Calculate seek time based on current playhead position
                double seekSeconds = 0;
                if (vm != null)
                {
                    var clip = GetCurrentClipAtTime(vm);
                    if (clip != null)
                    {
                        var clipOffset = vm.CurrentTime - clip.StartTime;
                        if (clipOffset >= TimeSpan.Zero)
                        {
                            seekSeconds = (clip.SourceStart + clipOffset).TotalSeconds;
                        }
                    }
                }

                // Create output path - include seek time in hash for caching
                var hash = $"{videoPath}_{seekSeconds:F1}".GetHashCode();
                var tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "Preview");
                Directory.CreateDirectory(tempDir);
                var outputPath = Path.Combine(tempDir, $"preview_{hash:X8}.jpg");
                
                Log($"[DIRECT FFMPEG] Extracting frame at {seekSeconds:F2}s to: {outputPath}");

                // Extract frame at current time
                if (!File.Exists(outputPath))
                {
                    // Use simpler FFmpeg command that works with more formats
                    var args = $"-y -ss {seekSeconds:F3} -i \"{videoPath}\" -vframes 1 -q:v 2 \"{outputPath}\"";
                    
                    var result = await PlatypusTools.Core.Services.FFmpegService.RunAsync(args, ffmpegPath);
                    
                    if (!result.Success)
                    {
                        Log($"[DIRECT FFMPEG] Failed! ExitCode={result.ExitCode}, Error: {result.StdErr}");
                        return;
                    }
                }

                // Load and display the frame
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    Log($"[DIRECT FFMPEG] Frame file size: {fileInfo.Length} bytes");
                    
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var bitmap = ImageHelper.LoadUncached(outputPath);

                            PreviewFrameImage.Source = bitmap;
                            PreviewFrameImage.Visibility = Visibility.Visible;
                            PreviewMediaElement.Visibility = Visibility.Collapsed;

                            Log($"[DIRECT FFMPEG] SUCCESS - Frame displayed: {bitmap?.PixelWidth}x{bitmap?.PixelHeight}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[DIRECT FFMPEG] ERROR displaying: {ex.Message}");
                        }
                    });
                }
                else
                {
                    Log($"[DIRECT FFMPEG] Output file not created at: {outputPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"[DIRECT FFMPEG] Exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Fallback to FFmpeg when MediaElement fails.
        /// </summary>
        private async Task TryFFmpegFallback(string sourcePath)
        {
            var vm = DataContext as VideoEditorViewModel;
            vm?.LogDebug("[FALLBACK] MediaElement failed, trying FFmpeg fallback");
            
            _useFFmpegPreview = true;
            PreviewMediaElement.Source = null;
            PreviewMediaElement.Visibility = Visibility.Collapsed;
            PreviewFrameImage.Visibility = Visibility.Visible;
            await ExtractAndShowFrameAsync(sourcePath);
        }

        /// <summary>
        /// Handles drag start from media library items.
        /// </summary>
        private void MediaAsset_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement element)
            {
                var currentPos = e.GetPosition(null);
                
                // Check if we've moved enough to start a drag
                if (System.Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    System.Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (element.DataContext is MediaAsset asset)
                    {
                        // Execute the drop command directly since WPF drag-drop can be complex
                        if (DataContext is VideoEditorViewModel vm)
                        {
                            vm.DropMediaOnTimelineCommand.Execute(asset);
                        }
                    }
                }
            }
            else
            {
                _dragStartPoint = e.GetPosition(null);
            }
        }

        /// <summary>
        /// Handles speed preset button clicks.
        /// </summary>
        private void SpeedPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagValue && 
                double.TryParse(tagValue, out var speed) &&
                DataContext is VideoEditorViewModel vm && 
                vm.SelectedClip != null)
            {
                vm.SelectedClip.Speed = speed;
            }
        }

        /// <summary>
        /// Handles volume preset button clicks.
        /// </summary>
        private void VolumePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagValue && 
                double.TryParse(tagValue, out var volume) &&
                DataContext is VideoEditorViewModel vm && 
                vm.SelectedClip != null)
            {
                vm.SelectedClip.Volume = volume;
            }
        }

        #region Time Ruler Click-to-Seek

        /// <summary>
        /// Handles mouse down on the time ruler to start seeking.
        /// </summary>
        private void TimeRuler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas ruler)
            {
                _isDraggingRuler = true;
                ruler.CaptureMouse();
                SeekToMousePosition(e.GetPosition(ruler).X);
            }
        }

        /// <summary>
        /// Handles mouse move on the time ruler for continuous seeking.
        /// </summary>
        private void TimeRuler_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingRuler && sender is Canvas ruler && e.LeftButton == MouseButtonState.Pressed)
            {
                SeekToMousePosition(e.GetPosition(ruler).X);
            }
        }

        /// <summary>
        /// Handles mouse up on the time ruler to stop seeking.
        /// </summary>
        private void TimeRuler_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingRuler && sender is Canvas ruler)
            {
                _isDraggingRuler = false;
                ruler.ReleaseMouseCapture();
            }
        }

        #endregion

        #region Playhead Drag

        /// <summary>
        /// Handles mouse down on the playhead to start dragging.
        /// </summary>
        private void Playhead_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas playheadCanvas)
            {
                _isDraggingPlayhead = true;
                playheadCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles mouse move for dragging the playhead.
        /// </summary>
        private void Playhead_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingPlayhead && sender is Canvas playheadCanvas && e.LeftButton == MouseButtonState.Pressed)
            {
                SeekToMousePosition(e.GetPosition(playheadCanvas).X);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles mouse up to stop dragging the playhead.
        /// </summary>
        private void Playhead_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingPlayhead && sender is Canvas playheadCanvas)
            {
                _isDraggingPlayhead = false;
                playheadCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        #endregion

        /// <summary>
        /// Seeks to the specified pixel position on the timeline.
        /// </summary>
        private void SeekToMousePosition(double pixelX)
        {
            if (DataContext is VideoEditorViewModel vm)
            {
                // Convert pixel position to time using the converter's settings
                var pixelsPerSecond = TimeToPixelConverter.PixelsPerSecond * TimeToPixelConverter.ZoomLevel;
                var seconds = System.Math.Max(0, pixelX / pixelsPerSecond);
                var newTime = System.TimeSpan.FromSeconds(seconds);
                
                // Clamp to duration if set
                if (vm.Duration > System.TimeSpan.Zero && newTime > vm.Duration)
                {
                    newTime = vm.Duration;
                }
                
                vm.CurrentTime = newTime;
            }
        }

        #region Preview Media Handling

        /// <summary>
        /// Handles when a media file is loaded in the preview.
        /// </summary>
        private void PreviewMediaElement_MediaOpened(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[UI Preview] MediaOpened! Source: {PreviewMediaElement.Source}");
            System.Diagnostics.Debug.WriteLine($"[UI Preview] Natural duration: {PreviewMediaElement.NaturalDuration}");
            System.Diagnostics.Debug.WriteLine($"[UI Preview] Natural size: {PreviewMediaElement.NaturalVideoWidth}x{PreviewMediaElement.NaturalVideoHeight}");
            
            if (DataContext is VideoEditorViewModel vm)
            {
                // Calculate initial position within the clip
                var clip = GetCurrentClipAtTime(vm);
                if (clip != null)
                {
                    var clipOffset = vm.CurrentTime - clip.StartTime;
                    System.Diagnostics.Debug.WriteLine($"[UI Preview] Seeking to clip offset: {clipOffset}");
                    if (clipOffset >= TimeSpan.Zero)
                    {
                        try
                        {
                            PreviewMediaElement.Position = clip.SourceStart + clipOffset;
                        }
                        catch (Exception ex) 
                        { 
                            System.Diagnostics.Debug.WriteLine($"[UI Preview] Seek error (may be image): {ex.Message}");
                        }
                    }
                }
                
                // Start or pause based on current state
                if (vm.IsPlaying)
                {
                    System.Diagnostics.Debug.WriteLine("[UI Preview] Starting playback");
                    PreviewMediaElement.Play();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[UI Preview] Pausing to show first frame");
                    // Show first frame
                    PreviewMediaElement.Pause();
                }
            }
        }

        /// <summary>
        /// Handles when media playback ends.
        /// </summary>
        private void PreviewMediaElement_MediaEnded(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[UI Preview] MediaEnded");
            // The timer in ViewModel will handle advancing to next clip
        }

        /// <summary>
        /// Handle media failed to load - fall back to FFmpeg frame extraction.
        /// </summary>
        private async void PreviewMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[UI Preview] MediaFailed! Error: {e.ErrorException?.Message}");
            System.Diagnostics.Debug.WriteLine($"[UI Preview] Source was: {PreviewMediaElement.Source}");
            if (e.ErrorException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[UI Preview] Stack trace: {e.ErrorException.StackTrace}");
            }
            
            // Fall back to FFmpeg frame extraction
            if (!string.IsNullOrEmpty(_currentPreviewSource))
            {
                await TryFFmpegFallback(_currentPreviewSource);
            }
        }

        #endregion

        #region Clip Interaction (Shotcut-style drag/drop/selection)

        private TimelineClip? _draggingClip;
        private Point _clipDragStartPoint;
        private TimeSpan _clipOriginalStartPosition;
        private bool _isDraggingClip;

        // Trim state
        private bool _isTrimmingIn;
        private bool _isTrimmingOut;
        private TimelineClip? _trimmingClip;
        private TimeSpan _trimOriginalStart;
        private TimeSpan _trimOriginalDuration;
        private TimeSpan _trimOriginalInPoint;

        /// <summary>
        /// Handle clip mouse down - select clip and prepare for drag
        /// </summary>
        private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border clipBorder && clipBorder.Tag is TimelineClip clip)
            {
                // Select this clip
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.SelectedClip = clip;
                    
                    // Seek to clip start
                    vm.CurrentTime = clip.StartPosition;
                    
                    // Update preview immediately
                    if (!string.IsNullOrEmpty(clip.SourcePath) && System.IO.File.Exists(clip.SourcePath))
                    {
                        vm.PreviewSource = clip.SourcePath;
                    }
                }

                // Prepare for drag
                _draggingClip = clip;
                _clipDragStartPoint = e.GetPosition(this);
                _clipOriginalStartPosition = clip.StartPosition;
                _isDraggingClip = false;
                
                clipBorder.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle clip mouse move - drag clip to new position
        /// </summary>
        private void Clip_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingClip != null && e.LeftButton == MouseButtonState.Pressed && sender is Border clipBorder)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos.X - _clipDragStartPoint.X;

                // Start drag if moved enough
                if (!_isDraggingClip && System.Math.Abs(delta) > SystemParameters.MinimumHorizontalDragDistance)
                {
                    _isDraggingClip = true;
                    clipBorder.Opacity = 0.7; // Visual feedback
                }

                if (_isDraggingClip)
                {
                    // Convert pixel delta to time delta
                    var pixelsPerSecond = TimeToPixelConverter.PixelsPerSecond * TimeToPixelConverter.ZoomLevel;
                    var timeDelta = TimeSpan.FromSeconds(delta / pixelsPerSecond);
                    
                    // Calculate new position (don't go negative)
                    var newPosition = _clipOriginalStartPosition + timeDelta;
                    if (newPosition < TimeSpan.Zero)
                        newPosition = TimeSpan.Zero;
                    
                    // Update clip position
                    _draggingClip.StartPosition = newPosition;
                    _draggingClip.StartTime = newPosition;
                    
                    // Update status
                    if (DataContext is VideoEditorViewModel vm)
                    {
                        vm.StatusMessage = $"Moving clip to {FormatTimeSpan(newPosition)}";
                    }
                }

                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle clip mouse up - finish drag
        /// </summary>
        private void Clip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingClip != null && sender is Border clipBorder)
            {
                clipBorder.ReleaseMouseCapture();
                clipBorder.Opacity = 1.0; // Reset visual

                if (_isDraggingClip && DataContext is VideoEditorViewModel vm)
                {
                    vm.StatusMessage = $"Moved {_draggingClip.Name} to {FormatTimeSpan(_draggingClip.StartPosition)}";
                }

                _draggingClip = null;
                _isDraggingClip = false;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle trim in (left edge drag)
        /// </summary>
        private void TrimIn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TimelineClip clip)
            {
                _trimmingClip = clip;
                _isTrimmingIn = true;
                _isTrimmingOut = false;
                _clipDragStartPoint = e.GetPosition(this);
                _trimOriginalStart = clip.StartPosition;
                _trimOriginalDuration = clip.Duration;
                _trimOriginalInPoint = clip.TrimIn;

                element.CaptureMouse();
                
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.SelectedClip = clip;
                    vm.StatusMessage = "Trimming in point...";
                }
            }
            e.Handled = true; // Prevent clip drag
        }

        /// <summary>
        /// Handle trim out (right edge drag)
        /// </summary>
        private void TrimOut_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TimelineClip clip)
            {
                _trimmingClip = clip;
                _isTrimmingIn = false;
                _isTrimmingOut = true;
                _clipDragStartPoint = e.GetPosition(this);
                _trimOriginalStart = clip.StartPosition;
                _trimOriginalDuration = clip.Duration;

                element.CaptureMouse();
                
                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.SelectedClip = clip;
                    vm.StatusMessage = "Trimming out point...";
                }
            }
            e.Handled = true; // Prevent clip drag
        }

        /// <summary>
        /// Handle trim drag movement
        /// </summary>
        private void Trim_MouseMove(object sender, MouseEventArgs e)
        {
            if (_trimmingClip == null || (!_isTrimmingIn && !_isTrimmingOut)) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (DataContext is not VideoEditorViewModel vm) return;

            var currentPos = e.GetPosition(this);
            var deltaX = currentPos.X - _clipDragStartPoint.X;
            var deltaTime = TimeSpan.FromSeconds(deltaX / vm.PixelsPerSecond);

            if (_isTrimmingIn)
            {
                // Trim in: move start position forward, reduce duration, increase in-point
                var newStart = _trimOriginalStart + deltaTime;
                var newDuration = _trimOriginalDuration - deltaTime;
                var newInPoint = _trimOriginalInPoint + deltaTime;

                // Clamp values
                if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
                if (newDuration < TimeSpan.FromSeconds(0.1)) newDuration = TimeSpan.FromSeconds(0.1);
                if (newInPoint < TimeSpan.Zero) newInPoint = TimeSpan.Zero;

                _trimmingClip.StartPosition = newStart;
                _trimmingClip.Duration = newDuration;
                _trimmingClip.TrimIn = newInPoint;
            }
            else if (_isTrimmingOut)
            {
                // Trim out: keep start position, adjust duration
                var newDuration = _trimOriginalDuration + deltaTime;

                // Clamp duration
                if (newDuration < TimeSpan.FromSeconds(0.1)) newDuration = TimeSpan.FromSeconds(0.1);

                // Don't let clip extend past source duration (if known)
                if (_trimmingClip.SourceDuration > TimeSpan.Zero)
                {
                    var maxDuration = _trimmingClip.SourceDuration - _trimmingClip.TrimIn;
                    if (newDuration > maxDuration) newDuration = maxDuration;
                }

                _trimmingClip.Duration = newDuration;
            }

            vm.StatusMessage = $"Trim: {FormatTimeSpan(_trimmingClip.StartPosition)} - {FormatTimeSpan(_trimmingClip.StartPosition + _trimmingClip.Duration)}";
        }

        /// <summary>
        /// Handle trim drag end
        /// </summary>
        private void Trim_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_trimmingClip != null && (sender is FrameworkElement element))
            {
                element.ReleaseMouseCapture();

                if (DataContext is VideoEditorViewModel vm)
                {
                    vm.StatusMessage = $"Trimmed {_trimmingClip.Name}";
                }
            }

            _trimmingClip = null;
            _isTrimmingIn = false;
            _isTrimmingOut = false;
            e.Handled = true;
        }

        /// <summary>
        /// Context menu: Set preview to this clip
        /// </summary>
        private void SetPreviewToClip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && 
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is Border clipBorder &&
                clipBorder.Tag is TimelineClip clip &&
                DataContext is VideoEditorViewModel vm)
            {
                vm.CurrentTime = clip.StartPosition;
                if (!string.IsNullOrEmpty(clip.SourcePath))
                {
                    vm.PreviewSource = clip.SourcePath;
                }
            }
        }

        /// <summary>
        /// Handle drag over track (for reordering/moving between tracks)
        /// </summary>
        private void Track_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        /// <summary>
        /// Handle drop on track
        /// </summary>
        private void Track_Drop(object sender, DragEventArgs e)
        {
            // TODO: Handle clip drop between tracks
            e.Handled = true;
        }

        private static string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
        }

        #endregion

        #region Timeline Horizontal Scrolling

        private const double TimelineScrollStep = 100; // Pixels per scroll action

        /// <summary>
        /// Scrolls the timeline view left.
        /// </summary>
        private void ScrollTimelineLeft()
        {
            var vm = DataContext as VideoEditorViewModel;
            if (TimelineScrollViewer != null)
            {
                var oldOffset = TimelineScrollViewer.HorizontalOffset;
                var newOffset = Math.Max(0, TimelineScrollViewer.HorizontalOffset - TimelineScrollStep);
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                vm?.LogDebug($"[SCROLL] Left: {oldOffset:F0} -> {newOffset:F0}");
            }
            else
            {
                vm?.LogDebug("[SCROLL] ERROR: TimelineScrollViewer is null!");
            }
        }

        /// <summary>
        /// Scrolls the timeline view right.
        /// </summary>
        private void ScrollTimelineRight()
        {
            var vm = DataContext as VideoEditorViewModel;
            if (TimelineScrollViewer != null)
            {
                var oldOffset = TimelineScrollViewer.HorizontalOffset;
                var newOffset = Math.Min(
                    TimelineScrollViewer.ScrollableWidth,
                    TimelineScrollViewer.HorizontalOffset + TimelineScrollStep);
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                vm?.LogDebug($"[SCROLL] Right: {oldOffset:F0} -> {newOffset:F0} (Max: {TimelineScrollViewer.ScrollableWidth:F0})");
            }
            else
            {
                vm?.LogDebug("[SCROLL] ERROR: TimelineScrollViewer is null!");
            }
        }

        /// <summary>
        /// Scrolls the timeline to center on the playhead.
        /// </summary>
        private void ScrollTimelineToPlayhead()
        {
            if (DataContext is not VideoEditorViewModel vm) return;
            if (TimelineScrollViewer == null) return;

            // Calculate playhead position in pixels
            var playheadPixels = vm.CurrentTime.TotalSeconds * vm.PixelsPerSecond * vm.ZoomLevel;
            
            // Center the viewport on the playhead
            var viewportCenter = TimelineScrollViewer.ViewportWidth / 2;
            var targetOffset = playheadPixels - viewportCenter;
            
            TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetOffset));
        }

        /// <summary>
        /// Handles mouse wheel for horizontal scrolling on the timeline.
        /// Shift+Wheel = horizontal scroll, Ctrl+Wheel = zoom
        /// </summary>
        private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (TimelineScrollViewer == null) return;

            // Shift+Wheel: Horizontal scroll
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var delta = -e.Delta; // Reverse for natural scrolling
                var newOffset = TimelineScrollViewer.HorizontalOffset + delta;
                newOffset = Math.Max(0, Math.Min(TimelineScrollViewer.ScrollableWidth, newOffset));
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                e.Handled = true;
            }
            // Ctrl+Wheel: Zoom
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is VideoEditorViewModel vm)
                {
                    if (e.Delta > 0)
                        vm.ZoomInCommand.Execute(null);
                    else
                        vm.ZoomOutCommand.Execute(null);
                }
                e.Handled = true;
            }
            // Regular wheel on timeline area: also horizontal scroll (more intuitive for timeline)
            else if (sender == TimelineScrollViewer || 
                     (e.OriginalSource is FrameworkElement fe && IsChildOfTimeline(fe)))
            {
                var delta = -e.Delta;
                var newOffset = TimelineScrollViewer.HorizontalOffset + delta;
                newOffset = Math.Max(0, Math.Min(TimelineScrollViewer.ScrollableWidth, newOffset));
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                e.Handled = true;
            }
        }

        private static bool IsChildOfTimeline(FrameworkElement element)
        {
            var parent = element.Parent as FrameworkElement;
            while (parent != null)
            {
                if (parent.Name == "TimelineScrollViewer")
                    return true;
                parent = parent.Parent as FrameworkElement;
            }
            return false;
        }

        #endregion
    }
}
