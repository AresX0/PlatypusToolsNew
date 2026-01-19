using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PlatypusTools.Core.Models.Video;
using PlatypusTools.Core.Services.AI;
using PlatypusTools.Core.Services.Video;

// Use Video namespace versions of FFmpeg services
using FFmpegService = PlatypusTools.Core.Services.Video.FFmpegService;
using FFprobeService = PlatypusTools.Core.Services.Video.FFprobeService;
using Filter = PlatypusTools.Core.Models.Video.Filter;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the CapCut-class video editor.
    /// Supports multi-track timeline, keyframes, beat sync, AI tools, and export.
    /// </summary>
    public class VideoEditorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly VideoEditorService _editorService;
        private readonly FFmpegService _ffmpeg;
        private readonly FFprobeService _ffprobe;
        private readonly BeatDetectionService _beatDetection;
        private readonly KeyframeInterpolator _keyframeInterpolator;
        private readonly DispatcherTimer _playbackTimer;
        
        // Shotcut-inspired services
        private readonly TimelineOperationsService _timelineOps;
        private readonly ProxyService _proxyService;
        private readonly WaveformService _waveformService;
        
        private CancellationTokenSource? _operationCts;
        private bool _disposed;
        
        /// <summary>
        /// Default duration for image overlays in seconds.
        /// </summary>
        public const double DefaultImageOverlayDuration = 3.0;
        
        /// <summary>
        /// Exposes the VideoEditorService for UI operations like frame extraction.
        /// </summary>
        public VideoEditorService EditorService => _editorService;

        #region Properties

        private VideoProject? _project;
        public VideoProject? Project
        {
            get => _project;
            set => SetProperty(ref _project, value);
        }

        private MediaAsset? _selectedAsset;
        public MediaAsset? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (SetProperty(ref _selectedAsset, value) && value != null)
                {
                    // Like Shotcut: selecting a media library item shows it in preview
                    if (!string.IsNullOrEmpty(value.FilePath) && System.IO.File.Exists(value.FilePath))
                    {
                        var ext = System.IO.Path.GetExtension(value.FilePath).ToLowerInvariant();
                        if (IsVideoExtension(ext) || IsImageExtension(ext))
                        {
                            LogDebug($"[ASSET] Selected asset preview: {value.FileName}");
                            PreviewSource = value.FilePath;
                        }
                    }
                }
            }
        }

        private TimelineClip? _selectedClip;
        public TimelineClip? SelectedClip
        {
            get => _selectedClip;
            set
            {
                if (SetProperty(ref _selectedClip, value))
                {
                    OnPropertyChanged(nameof(HasSelectedClip));
                    OnPropertyChanged(nameof(SelectedClipTransform));
                    OnPropertyChanged(nameof(SelectedClipColorGrading));
                }
            }
        }

        private TimelineTrack? _selectedTrack;
        public TimelineTrack? SelectedTrack
        {
            get => _selectedTrack;
            set => SetProperty(ref _selectedTrack, value);
        }

        private TimeSpan _currentTime;
        public TimeSpan CurrentTime
        {
            get => _currentTime;
            set
            {
                if (SetProperty(ref _currentTime, value))
                {
                    OnPropertyChanged(nameof(CurrentTimeText));
                    UpdatePreviewFrame();
                }
            }
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (SetProperty(ref _duration, value))
                {
                    OnPropertyChanged(nameof(DurationText));
                    OnPropertyChanged(nameof(TimelineWidth));
                    UpdateTimelineRuler();
                }
            }
        }

        private double _zoomLevel = 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (SetProperty(ref _zoomLevel, Math.Clamp(value, 0.1, 10.0)))
                {
                    OnPropertyChanged(nameof(TimelineWidth));
                    UpdateTimelineRuler();
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        private bool _loopPlayback;
        public bool LoopPlayback
        {
            get => _loopPlayback;
            set => SetProperty(ref _loopPlayback, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _debugLog = "";
        public string DebugLog
        {
            get => _debugLog;
            set => SetProperty(ref _debugLog, value);
        }
        
        private bool _showDebugLog = false;
        public bool ShowDebugLog
        {
            get => _showDebugLog;
            set => SetProperty(ref _showDebugLog, value);
        }

        /// <summary>
        /// Appends a message to the debug log with timestamp.
        /// </summary>
        public void LogDebug(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] {message}";
            System.Diagnostics.Debug.WriteLine(line);
            DebugLog = DebugLog + line + Environment.NewLine;
            
            // Trim if too long (keep last 100 lines)
            var lines = DebugLog.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 100)
            {
                DebugLog = string.Join(Environment.NewLine, lines.Skip(lines.Length - 100)) + Environment.NewLine;
            }
        }

        private double _masterVolume = 1.0;
        public double MasterVolume
        {
            get => _masterVolume;
            set => SetProperty(ref _masterVolume, Math.Clamp(value, 0, 1.0));
        }

        private bool _showBeatMarkers = true;
        public bool ShowBeatMarkers
        {
            get => _showBeatMarkers;
            set => SetProperty(ref _showBeatMarkers, value);
        }

        private bool _showWaveform = true;
        public bool ShowWaveform
        {
            get => _showWaveform;
            set => SetProperty(ref _showWaveform, value);
        }

        private bool _snapToBeats = true;
        public bool SnapToBeats
        {
            get => _snapToBeats;
            set => SetProperty(ref _snapToBeats, value);
        }

        private bool _snapToClips = true;
        public bool SnapToClips
        {
            get => _snapToClips;
            set => SetProperty(ref _snapToClips, value);
        }

        private bool _isLooping;
        public bool IsLooping
        {
            get => _isLooping;
            set => SetProperty(ref _isLooping, value);
        }

        private TimeSpan _inPoint = TimeSpan.Zero;
        public TimeSpan InPoint
        {
            get => _inPoint;
            set => SetProperty(ref _inPoint, value);
        }

        private TimeSpan _outPoint = TimeSpan.Zero;
        public TimeSpan OutPoint
        {
            get => _outPoint;
            set => SetProperty(ref _outPoint, value);
        }

        private bool _rippleEdit;
        public bool RippleEdit
        {
            get => _rippleEdit;
            set => SetProperty(ref _rippleEdit, value);
        }

        private double _playbackSpeed = 1.0;
        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                if (SetProperty(ref _playbackSpeed, Math.Clamp(value, 0.25, 4.0)))
                {
                    OnPropertyChanged(nameof(PlaybackSpeedText));
                }
            }
        }

        public string PlaybackSpeedText => PlaybackSpeed == 1.0 ? "1x" : $"{PlaybackSpeed:F2}x";

        // Markers collection
        public ObservableCollection<TimelineMarker> Markers { get; } = new();

        private TimelineMarker? _selectedMarker;
        public TimelineMarker? SelectedMarker
        {
            get => _selectedMarker;
            set => SetProperty(ref _selectedMarker, value);
        }

        private double _pixelsPerSecond = 100.0;
        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set
            {
                if (SetProperty(ref _pixelsPerSecond, Math.Clamp(value, 10, 500)))
                {
                    OnPropertyChanged(nameof(TimelineWidth));
                    UpdateTimelineRuler();
                }
            }
        }

        private string? _previewSource;
        public string? PreviewSource
        {
            get => _previewSource;
            set => SetProperty(ref _previewSource, value);
        }

        private string? _overlaySource;
        /// <summary>
        /// Current overlay image path to display on top of the video preview.
        /// </summary>
        public string? OverlaySource
        {
            get => _overlaySource;
            set => SetProperty(ref _overlaySource, value);
        }

        // Computed properties
        public bool HasSelectedClip => SelectedClip != null;
        public string CurrentTimeText => FormatTime(CurrentTime);
        public string DurationText => FormatTime(Duration);

        /// <summary>
        /// Total timeline width in pixels based on duration and zoom.
        /// </summary>
        public double TimelineWidth => Math.Max(Duration.TotalSeconds * PixelsPerSecond * ZoomLevel, 1000);

        public ClipTransform? SelectedClipTransform => SelectedClip != null
            ? _keyframeInterpolator.InterpolateTransform(SelectedClip.TransformKeyframes, (CurrentTime - SelectedClip.StartTime).TotalSeconds)
            : null;

        public ColorGradingSettings? SelectedClipColorGrading => SelectedClip?.ColorGrading;

        // Collections
        public ObservableCollection<MediaAsset> MediaLibrary { get; } = new();
        public ObservableCollection<TimelineTrack> Tracks { get; } = new();
        public ObservableCollection<BeatMarker> BeatMarkers { get; } = new();
        public ObservableCollection<Caption> Captions { get; } = new();
        public ObservableCollection<ExportProfile> ExportProfiles { get; } = new();
        public ObservableCollection<Transition> AvailableTransitions { get; } = new();
        public ObservableCollection<VideoFilter> AvailableFilters { get; } = new();
        public ObservableCollection<TimelineRulerMarker> TimelineRulerMarkers { get; } = new();

        // Shotcut-inspired collections
        /// <summary>
        /// All available filters from FilterLibrary (100+ Shotcut-style filters).
        /// </summary>
        public ObservableCollection<Filter> ShotcutFilters { get; } = new();
        
        /// <summary>
        /// Filters grouped by category for display.
        /// </summary>
        public ObservableCollection<FilterCategoryGroup> FiltersByCategory { get; } = new();
        
        /// <summary>
        /// All available export presets (YouTube, TikTok, Instagram, etc.).
        /// </summary>
        public ObservableCollection<ExportPreset> AvailableExportPresets { get; } = new();
        
        /// <summary>
        /// Export presets grouped by category for display.
        /// </summary>
        public ObservableCollection<ExportCategoryGroup> ExportPresetsByCategory { get; } = new();

        private ExportPreset? _selectedExportPreset;
        /// <summary>
        /// Currently selected export preset.
        /// </summary>
        public ExportPreset? SelectedExportPreset
        {
            get => _selectedExportPreset;
            set => SetProperty(ref _selectedExportPreset, value);
        }

        private Filter? _selectedFilter;
        /// <summary>
        /// Currently selected filter to apply.
        /// </summary>
        public Filter? SelectedFilter
        {
            get => _selectedFilter;
            set => SetProperty(ref _selectedFilter, value);
        }

        private bool _useProxyEditing = true;
        /// <summary>
        /// Whether to use proxy files for high-resolution footage.
        /// </summary>
        public bool UseProxyEditing
        {
            get => _useProxyEditing;
            set => SetProperty(ref _useProxyEditing, value);
        }

        private bool _snappingEnabled = true;
        /// <summary>
        /// Whether timeline snapping is enabled.
        /// </summary>
        public bool SnappingEnabled
        {
            get => _snappingEnabled;
            set
            {
                if (SetProperty(ref _snappingEnabled, value))
                {
                    // Snapping state is stored locally
                }
            }
        }

        private bool _rippleAllTracks = false;
        /// <summary>
        /// Whether ripple operations affect all tracks or just the current track.
        /// </summary>
        public bool RippleAllTracks
        {
            get => _rippleAllTracks;
            set => SetProperty(ref _rippleAllTracks, value);
        }

        private string _filterSearchText = "";
        /// <summary>
        /// Search text for filtering the filter list.
        /// </summary>
        public string FilterSearchText
        {
            get => _filterSearchText;
            set
            {
                if (SetProperty(ref _filterSearchText, value))
                {
                    FilterFilters();
                }
            }
        }

        private FilterCategory? _selectedFilterCategory;
        /// <summary>
        /// Selected filter category for filtering.
        /// </summary>
        public FilterCategory? SelectedFilterCategory
        {
            get => _selectedFilterCategory;
            set
            {
                if (SetProperty(ref _selectedFilterCategory, value))
                {
                    FilterFilters();
                }
            }
        }

        /// <summary>
        /// Filtered list of Shotcut filters based on search and category.
        /// </summary>
        public ObservableCollection<Filter> FilteredShotcutFilters { get; } = new();

        #endregion

        #region Commands

        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand ImportMediaCommand { get; }
        public ICommand ImportBackgroundCommand { get; }
        public ICommand AddToTimelineCommand { get; }
        public ICommand AddAsOverlayCommand { get; }
        public ICommand AddTrackCommand { get; }
        public ICommand RemoveBackgroundCommand { get; }
        public ICommand DeleteClipCommand { get; }
        public ICommand SplitClipCommand { get; }
        public ICommand DuplicateClipCommand { get; }
        public ICommand DetectBeatsCommand { get; }
        public ICommand ApplyBeatSyncCommand { get; }
        public ICommand GenerateCaptionsCommand { get; }
        public ICommand ExportCaptionsCommand { get; }
        public ICommand ApplyTransitionCommand { get; }
        public ICommand ApplyFilterCommand { get; }
        public ICommand RemoveFilterCommand { get; }
        public ICommand AddKeyframeCommand { get; }
        public ICommand RemoveKeyframeCommand { get; }
        public ICommand ExportVideoCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand CancelOperationCommand { get; }
        public ICommand DropMediaOnTimelineCommand { get; }
        public ICommand SeekToPositionCommand { get; }
        public ICommand InsertOverlayAtPlayheadCommand { get; }
        public ICommand ExtendClipCommand { get; }
        public ICommand TrimClipCommand { get; }
        public ICommand InsertAtPlayheadCommand { get; }
        public ICommand InstallWhisperCommand { get; }
        public ICommand DownloadWhisperModelCommand { get; }
        public ICommand ClearDebugLogCommand { get; }
        
        // Shotcut-inspired transport controls
        public ICommand SkipPreviousCommand { get; }
        public ICommand SkipNextCommand { get; }
        public ICommand PreviousFrameCommand { get; }
        public ICommand NextFrameCommand { get; }
        public ICommand RewindCommand { get; }
        public ICommand FastForwardCommand { get; }
        public ICommand SeekBackOneSecondCommand { get; }
        public ICommand SeekForwardOneSecondCommand { get; }
        public ICommand ToggleLoopCommand { get; }
        public ICommand SetInPointCommand { get; }
        public ICommand SetOutPointCommand { get; }
        public ICommand AddMarkerCommand { get; }
        public ICommand GoToMarkerCommand { get; }
        public ICommand ToggleRippleCommand { get; }

        // Shotcut-inspired commands
        public ICommand CopyClipCommand { get; }
        public ICommand CutClipCommand { get; }
        public ICommand PasteClipCommand { get; }
        public ICommand RippleTrimInCommand { get; }
        public ICommand RippleTrimOutCommand { get; }
        public ICommand SlipEditCommand { get; }
        public ICommand SlideEditCommand { get; }
        public ICommand ApplyShotcutFilterCommand { get; }
        public ICommand RemoveShotcutFilterCommand { get; }
        public ICommand ToggleFilterEnabledCommand { get; }
        public ICommand MoveFilterUpCommand { get; }
        public ICommand MoveFilterDownCommand { get; }
        public ICommand GenerateProxyCommand { get; }
        public ICommand AddTextClipCommand { get; }
        public ICommand ExportWithPresetCommand { get; }
        public ICommand RippleDeleteCommand { get; }
        public ICommand LiftCommand { get; }
        public ICommand InsertCommand { get; }
        public ICommand OverwriteCommand { get; }
        public ICommand AppendCommand { get; }
        public ICommand RollEditCommand { get; }
        public ICommand UndoTimelineCommand { get; }
        public ICommand RedoTimelineCommand { get; }

        #endregion
        
        #region Whisper Status
        
        private string _whisperStatus = "Checking Whisper availability...";
        public string WhisperStatus
        {
            get => _whisperStatus;
            set => SetProperty(ref _whisperStatus, value);
        }
        
        private bool _isWhisperAvailable;
        public bool IsWhisperAvailable
        {
            get => _isWhisperAvailable;
            set => SetProperty(ref _isWhisperAvailable, value);
        }
        
        #endregion

        #region Constructor

        public VideoEditorViewModel()
        {
            _ffmpeg = new FFmpegService();
            _ffprobe = new FFprobeService();
            _editorService = new VideoEditorService(_ffmpeg, _ffprobe);
            _beatDetection = new BeatDetectionService();
            _keyframeInterpolator = new KeyframeInterpolator();
            
            // Initialize Shotcut-inspired services
            _timelineOps = new TimelineOperationsService();
            _proxyService = new ProxyService(_ffmpeg, _ffprobe);
            _waveformService = new WaveformService(_ffmpeg);

            // Initialize commands
            NewProjectCommand = new RelayCommand(_ => ExecuteNewProject());
            OpenProjectCommand = new RelayCommand(async _ => await ExecuteOpenProjectAsync());
            SaveProjectCommand = new RelayCommand(async _ => await ExecuteSaveProjectAsync(), _ => Project != null);
            ImportMediaCommand = new RelayCommand(async _ => await ExecuteImportMediaAsync());
            ImportBackgroundCommand = new RelayCommand(async _ => await ExecuteImportBackgroundAsync());
            AddToTimelineCommand = new RelayCommand(_ => ExecuteAddToTimeline(), _ => SelectedAsset != null);
            AddAsOverlayCommand = new RelayCommand(async _ => await ExecuteAddAsOverlayAsync());  // Always enabled - opens file picker if no asset selected
            AddTrackCommand = new RelayCommand(param => ExecuteAddTrack(param?.ToString()));
            RemoveBackgroundCommand = new RelayCommand(_ => ExecuteRemoveBackground(), _ => SelectedClip != null);
            DeleteClipCommand = new RelayCommand(_ => ExecuteDeleteClip()); // Works on selected clip or clip at playhead
            SplitClipCommand = new RelayCommand(_ => ExecuteSplitClip()); // Works on clip at playhead or selected clip
            DuplicateClipCommand = new RelayCommand(_ => ExecuteDuplicateClip()); // Works on selected or clip at playhead
            DetectBeatsCommand = new RelayCommand(async _ => await ExecuteDetectBeatsAsync(), _ => SelectedAsset?.Type == MediaType.Audio);
            ApplyBeatSyncCommand = new RelayCommand(async _ => await ExecuteApplyBeatSyncAsync(), _ => BeatMarkers.Count > 0 && Tracks.Count > 0);
            GenerateCaptionsCommand = new RelayCommand(async _ => await ExecuteGenerateCaptionsAsync(), _ => Project != null);
            ExportCaptionsCommand = new RelayCommand(async _ => await ExecuteExportCaptionsAsync(), _ => Captions.Count > 0);
            ApplyTransitionCommand = new RelayCommand(param => ExecuteApplyTransition(param as Transition), _ => SelectedClip != null);
            ApplyFilterCommand = new RelayCommand(param => ExecuteApplyFilter(param as VideoFilter), _ => SelectedClip != null);
            RemoveFilterCommand = new RelayCommand(param => ExecuteRemoveFilter(param as ClipEffect), _ => SelectedClip != null);
            AddKeyframeCommand = new RelayCommand(_ => ExecuteAddKeyframe(), _ => SelectedClip != null);
            RemoveKeyframeCommand = new RelayCommand(_ => ExecuteRemoveKeyframe(), _ => SelectedClip != null);
            ExportVideoCommand = new RelayCommand(async _ => await ExecuteExportVideoAsync(), _ => Project != null && Tracks.Count > 0);
            PlayPauseCommand = new RelayCommand(_ => ExecutePlayPause());
            StopCommand = new RelayCommand(_ => ExecuteStop());
            ZoomInCommand = new RelayCommand(_ => ZoomLevel *= 1.5);
            ZoomOutCommand = new RelayCommand(_ => ZoomLevel /= 1.5);
            UndoCommand = new RelayCommand(_ => ExecuteUndo()); // TODO: Implement proper undo stack
            RedoCommand = new RelayCommand(_ => ExecuteRedo()); // TODO: Implement proper redo stack
            CancelOperationCommand = new RelayCommand(_ => ExecuteCancelOperation(), _ => IsBusy);
            DropMediaOnTimelineCommand = new RelayCommand(param => ExecuteDropMediaOnTimeline(param));
            SeekToPositionCommand = new RelayCommand(param => ExecuteSeekToPosition(param));
            InsertOverlayAtPlayheadCommand = new RelayCommand(async _ => await ExecuteInsertOverlayAtPlayheadAsync());
            ExtendClipCommand = new RelayCommand(param => ExecuteExtendClip(param), _ => SelectedClip != null);
            TrimClipCommand = new RelayCommand(param => ExecuteTrimClip(param), _ => SelectedClip != null);
            InsertAtPlayheadCommand = new RelayCommand(async _ => await ExecuteInsertAtPlayheadAsync()); // Always enabled - opens file picker if no asset
            InstallWhisperCommand = new RelayCommand(async _ => await ExecuteInstallWhisperAsync(), _ => !IsWhisperAvailable && !IsBusy);
            DownloadWhisperModelCommand = new RelayCommand(async _ => await ExecuteDownloadWhisperModelAsync(), _ => !IsBusy);
            ClearDebugLogCommand = new RelayCommand(_ => DebugLog = "");

            // Shotcut-inspired transport controls
            SkipPreviousCommand = new RelayCommand(_ => ExecuteSkipPrevious());
            SkipNextCommand = new RelayCommand(_ => ExecuteSkipNext());
            PreviousFrameCommand = new RelayCommand(_ => ExecutePreviousFrame());
            NextFrameCommand = new RelayCommand(_ => ExecuteNextFrame());
            RewindCommand = new RelayCommand(_ => ExecuteRewind());
            FastForwardCommand = new RelayCommand(_ => ExecuteFastForward());
            SeekBackOneSecondCommand = new RelayCommand(_ => ExecuteSeekBySeconds(-1));
            SeekForwardOneSecondCommand = new RelayCommand(_ => ExecuteSeekBySeconds(1));
            ToggleLoopCommand = new RelayCommand(_ => IsLooping = !IsLooping);
            SetInPointCommand = new RelayCommand(_ => ExecuteSetInPoint());
            SetOutPointCommand = new RelayCommand(_ => ExecuteSetOutPoint());
            AddMarkerCommand = new RelayCommand(_ => ExecuteAddMarker());
            GoToMarkerCommand = new RelayCommand(param => ExecuteGoToMarker(param));
            ToggleRippleCommand = new RelayCommand(_ => RippleEdit = !RippleEdit);
            
            // Shotcut-inspired editing commands
            CopyClipCommand = new RelayCommand(_ => ExecuteCopyClip(), _ => SelectedClip != null);
            CutClipCommand = new RelayCommand(_ => ExecuteCutClip(), _ => SelectedClip != null);
            PasteClipCommand = new RelayCommand(_ => ExecutePasteClip(), _ => _timelineOps.HasClipboard);
            RippleTrimInCommand = new RelayCommand(_ => ExecuteRippleTrimIn(), _ => SelectedClip != null);
            RippleTrimOutCommand = new RelayCommand(_ => ExecuteRippleTrimOut(), _ => SelectedClip != null);
            SlipEditCommand = new RelayCommand(param => ExecuteSlipEdit(param), _ => SelectedClip != null);
            SlideEditCommand = new RelayCommand(param => ExecuteSlideEdit(param), _ => SelectedClip != null);
            ApplyShotcutFilterCommand = new RelayCommand(param => ExecuteApplyShotcutFilter(param as Filter), _ => SelectedClip != null);
            RemoveShotcutFilterCommand = new RelayCommand(param => ExecuteRemoveShotcutFilter(param as Filter), _ => SelectedClip != null);
            ToggleFilterEnabledCommand = new RelayCommand(param => ExecuteToggleFilterEnabled(param as Filter), _ => SelectedClip != null);
            MoveFilterUpCommand = new RelayCommand(param => ExecuteMoveFilterUp(param as Filter), _ => SelectedClip != null);
            MoveFilterDownCommand = new RelayCommand(param => ExecuteMoveFilterDown(param as Filter), _ => SelectedClip != null);
            GenerateProxyCommand = new RelayCommand(async _ => await ExecuteGenerateProxyAsync(), _ => SelectedAsset != null);
            AddTextClipCommand = new RelayCommand(_ => ExecuteAddTextClip());
            ExportWithPresetCommand = new RelayCommand(async _ => await ExecuteExportWithPresetAsync(), _ => SelectedExportPreset != null && Project != null);
            RippleDeleteCommand = new RelayCommand(_ => ExecuteRippleDelete(), _ => SelectedClip != null);
            LiftCommand = new RelayCommand(_ => ExecuteLift(), _ => InPoint < OutPoint);
            InsertCommand = new RelayCommand(_ => ExecuteInsert(), _ => SelectedAsset != null);
            OverwriteCommand = new RelayCommand(_ => ExecuteOverwrite(), _ => SelectedAsset != null);
            AppendCommand = new RelayCommand(_ => ExecuteAppend(), _ => SelectedAsset != null);
            RollEditCommand = new RelayCommand(param => ExecuteRollEdit(param), _ => SelectedClip != null);
            UndoTimelineCommand = new RelayCommand(_ => ExecuteUndoTimeline(), _ => _timelineOps.CanUndo);
            RedoTimelineCommand = new RelayCommand(_ => ExecuteRedoTimeline(), _ => _timelineOps.CanRedo);

            // Initialize playback timer
            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33.33) // ~30 FPS
            };
            _playbackTimer.Tick += PlaybackTimer_Tick;

            // Initialize export profiles
            InitializeExportProfiles();
            InitializeTransitions();
            InitializeFilters();
            InitializeShotcutFilters();
            InitializeExportPresets();

            // Create default project
            ExecuteNewProject();
            
            // Initialize timeline ruler
            UpdateTimelineRuler();
            
            // Check Whisper availability
            CheckWhisperAvailability();
        }

        #endregion

        #region Command Implementations

        private void ExecuteNewProject()
        {
            Project = _editorService.CreateProject("New Project");
            MediaLibrary.Clear();
            Tracks.Clear();
            BeatMarkers.Clear();
            Captions.Clear();
            Markers.Clear();
            
            // Add default video tracks (top to bottom: overlays on top)
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Video 3", Type = TrackType.Overlay });
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Video 2", Type = TrackType.Overlay });
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Video 1", Type = TrackType.Video });
            
            // Add default audio tracks
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Audio 1", Type = TrackType.Audio });
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Audio 2", Type = TrackType.Audio });
            Tracks.Add(new TimelineTrack { Id = Guid.NewGuid(), Name = "Audio 3", Type = TrackType.Audio });
            
            CurrentTime = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
            InPoint = TimeSpan.Zero;
            OutPoint = TimeSpan.Zero;
            StatusMessage = "New project created with 6 tracks";
            
            System.Diagnostics.Debug.WriteLine($"[VideoEditor] New project created with {Tracks.Count} tracks");
        }

        private async Task ExecuteImportBackgroundAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image/Video Files|*.png;*.jpg;*.jpeg;*.mp4;*.mov;*.avi|All Files|*.*",
                Title = "Import Background"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Importing background...";

                    var asset = await _editorService.ImportMediaAsync(
                        dialog.FileName,
                        new Progress<double>(p => Progress = p),
                        _operationCts?.Token ?? CancellationToken.None);

                    // Add to library
                    MediaLibrary.Add(asset);
                    Project?.Assets.Add(asset);

                    // Create a background track if not exists
                    var bgTrack = Tracks.FirstOrDefault(t => t.Name == "Background");
                    if (bgTrack == null)
                    {
                        bgTrack = new TimelineTrack { Id = Guid.NewGuid(), Name = "Background", Type = TrackType.Video };
                        Tracks.Insert(0, bgTrack);
                    }

                    // Add as full-duration clip
                    var clip = new TimelineClip
                    {
                        Id = Guid.NewGuid(),
                        Name = Path.GetFileName(dialog.FileName),
                        SourcePath = dialog.FileName,
                        StartPosition = TimeSpan.Zero,
                        Duration = asset.Duration > TimeSpan.Zero ? asset.Duration : TimeSpan.FromSeconds(30),
                        SourceStart = TimeSpan.Zero,
                        SourceEnd = asset.Duration,
                        Color = "#2E7D32" // Green for background
                    };

                    bgTrack.Clips.Add(clip);
                    UpdateDuration();
                    StatusMessage = $"Background added: {asset.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Import failed: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                    Progress = 0;
                }
            }
        }

        private async Task ExecuteAddAsOverlayAsync()
        {
            MediaAsset? assetToUse = SelectedAsset;
            
            // If no asset selected, open file picker
            if (assetToUse == null)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Media for Overlay",
                    Filter = "Media Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                             "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                             "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                             "All Files|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    IsBusy = true;
                    _operationCts = new CancellationTokenSource();
                    StatusMessage = "Loading overlay media...";
                    
                    var filePath = dialog.FileName;
                    assetToUse = await _editorService.ImportMediaAsync(
                        filePath,
                        new Progress<double>(p => Progress = p),
                        _operationCts.Token);
                    
                    // For images, set a default duration
                    if (assetToUse.Type == MediaType.Image)
                    {
                        assetToUse.Duration = TimeSpan.FromSeconds(DefaultImageOverlayDuration);
                    }
                    
                    MediaLibrary.Add(assetToUse);
                    Project?.Assets.Add(assetToUse);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to load media: {ex.Message}";
                    IsBusy = false;
                    return;
                }
                finally
                {
                    IsBusy = false;
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }

            if (Project == null) return;

            // Find or create overlay track
            var overlayTrack = Tracks.FirstOrDefault(t => t.Type == TrackType.Overlay);
            if (overlayTrack == null)
            {
                var overlayNum = Tracks.Count(t => t.Type == TrackType.Overlay) + 1;
                overlayTrack = new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Name = $"Overlay {overlayNum}",
                    Type = TrackType.Overlay
                };
                // Insert at top for overlays
                Tracks.Insert(0, overlayTrack);
            }

            // Determine duration - images default to 3 seconds
            var duration = assetToUse.Type == MediaType.Image
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration)
                : (assetToUse.Duration > TimeSpan.Zero ? assetToUse.Duration : TimeSpan.FromSeconds(5));

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = assetToUse.FileName,
                SourcePath = assetToUse.FilePath,
                StartPosition = CurrentTime, // Insert at current playhead position
                StartTime = CurrentTime,
                Duration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = duration,
                SourceDuration = duration,
                Color = "#9C27B0", // Purple for overlay
                Opacity = 1.0,
                OverlaySettings = new OverlaySettings
                {
                    BlendMode = BlendMode.Normal,
                    Opacity = 1.0
                }
            };

            overlayTrack.Clips.Add(clip);
            SelectedClip = clip;
            UpdateDuration();
            StatusMessage = $"Added '{assetToUse.FileName}' as overlay at {FormatTime(CurrentTime)}";
        }

        private void ExecuteAddTrack(string? trackType)
        {
            var type = trackType?.ToLower() switch
            {
                "audio" => TrackType.Audio,
                "overlay" => TrackType.Video,
                _ => TrackType.Video
            };

            var prefix = trackType?.ToLower() == "audio" ? "Audio" : 
                        (trackType?.ToLower() == "overlay" ? "Overlay" : "Video");
            var count = Tracks.Count(t => t.Name.StartsWith(prefix)) + 1;

            var track = new TimelineTrack
            {
                Id = Guid.NewGuid(),
                Name = $"{prefix} {count}",
                Type = type
            };

            if (type == TrackType.Audio)
                Tracks.Add(track);
            else
            {
                var insertIdx = Tracks.Count(t => t.Type == TrackType.Video);
                Tracks.Insert(insertIdx, track);
            }

            StatusMessage = $"Added {track.Name} track";
        }

        private void ExecuteRemoveBackground()
        {
            if (SelectedClip == null) return;

            // Initialize overlay settings if needed
            SelectedClip.OverlaySettings ??= new OverlaySettings();
            SelectedClip.OverlaySettings.RemoveBackground = true;
            SelectedClip.OverlaySettings.RemovalMethod = BackgroundRemovalMethod.AIMatting;

            StatusMessage = "Background removal enabled (will process on export)";
            OnPropertyChanged(nameof(SelectedClip));
        }

        private void ExecuteDropMediaOnTimeline(object? param)
        {
            if (param is not MediaAsset asset) return;

            // Find appropriate track by type
            var trackIndex = FindTrackIndexForMediaType(asset.Type);
            if (trackIndex < 0 || trackIndex >= Tracks.Count)
            {
                StatusMessage = "No suitable track found";
                return;
            }

            var track = Tracks[trackIndex];
            var startTime = track.Clips.Count > 0
                ? track.Clips.Max(c => c.EndPosition)
                : TimeSpan.Zero;

            var duration = asset.Type == MediaType.Image 
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration)
                : (asset.Duration > TimeSpan.Zero ? asset.Duration : TimeSpan.FromSeconds(5));

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = asset.FileName,
                SourcePath = asset.FilePath,
                Type = asset.Type == MediaType.Audio ? ClipType.Audio : 
                       asset.Type == MediaType.Image ? ClipType.Image : ClipType.Video,
                StartPosition = startTime,
                StartTime = startTime, // Set both for compatibility
                Duration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = duration,
                SourceDuration = duration,
                Color = asset.Type == MediaType.Audio ? "#FF9800" : 
                        asset.Type == MediaType.Image ? "#4CAF50" : "#2196F3"
            };

            track.Clips.Add(clip);
            SelectedClip = clip;
            UpdateDuration();
            
            LogDebug($"[ADD CLIP] Added {clip.Name} to {track.Name}. Duration={clip.Duration}, SourcePath={clip.SourcePath}");
            
            // Seek to clip start and set preview directly for immediate feedback
            CurrentTime = clip.StartPosition;
            if (!string.IsNullOrEmpty(clip.SourcePath) && System.IO.File.Exists(clip.SourcePath))
            {
                LogDebug($"[ADD CLIP] Setting PreviewSource to: {clip.SourcePath}");
                PreviewSource = clip.SourcePath;
            }
            else
            {
                LogDebug($"[ADD CLIP] WARNING: SourcePath is null/empty or file doesn't exist: {clip.SourcePath}");
            }
            
            StatusMessage = $"Added {asset.FileName} to {track.Name}";
        }

        private void ExecuteApplyFilter(VideoFilter? filter)
        {
            if (SelectedClip == null || filter == null) return;

            var effect = new ClipEffect
            {
                Id = Guid.NewGuid().ToString(),
                Name = filter.Name,
                EffectType = filter.Type,
                IsEnabled = true,
                Parameters = new Dictionary<string, object>(filter.DefaultParameters)
            };

            SelectedClip.Effects.Add(effect);
            OnPropertyChanged(nameof(SelectedClip));
            StatusMessage = $"Applied {filter.Name} filter";
        }

        private void ExecuteRemoveFilter(ClipEffect? effect)
        {
            if (SelectedClip == null || effect == null) return;

            SelectedClip.Effects.Remove(effect);
            OnPropertyChanged(nameof(SelectedClip));
            StatusMessage = $"Removed {effect.Name} filter";
        }

        private async Task ExecuteOpenProjectAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PlatypusTools Project|*.ptp|All Files|*.*",
                Title = "Open Project"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    StatusMessage = "Loading project...";
                    
                    Project = await _editorService.LoadProjectAsync(dialog.FileName);
                    
                    // Sync collections
                    SyncProjectToUI();
                    
                    StatusMessage = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to load: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task ExecuteSaveProjectAsync()
        {
            if (Project == null) return;

            var path = Project.FilePath;
            
            if (string.IsNullOrEmpty(path))
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PlatypusTools Project|*.ptp",
                    FileName = $"{Project.Name}.ptp",
                    Title = "Save Project"
                };

                if (dialog.ShowDialog() != true)
                    return;
                    
                path = dialog.FileName;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Saving project...";
                
                SyncUIToProject();
                await _editorService.SaveProjectAsync(Project, path);
                
                StatusMessage = $"Saved: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to save: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteImportMediaAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media Files|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.aac;*.flac;*.png;*.jpg;*.jpeg;*.gif|All Files|*.*",
                Multiselect = true,
                Title = "Import Media"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                _operationCts = new CancellationTokenSource();

                try
                {
                    var total = dialog.FileNames.Length;
                    var current = 0;

                    foreach (var file in dialog.FileNames)
                    {
                        StatusMessage = $"Importing {Path.GetFileName(file)}...";
                        Progress = (double)current / total;

                        var asset = await _editorService.ImportMediaAsync(
                            file,
                            new Progress<double>(p => Progress = (current + p) / total),
                            _operationCts.Token);

                        MediaLibrary.Add(asset);
                        Project?.Assets.Add(asset);

                        current++;
                    }

                    StatusMessage = $"Imported {total} file(s)";
                    Progress = 0;
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Import cancelled";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Import failed: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }
        }

        private void ExecuteAddToTimeline()
        {
            if (SelectedAsset == null || Project == null) return;

            // Find appropriate track by type (not hardcoded index)
            var trackIndex = FindTrackIndexForMediaType(SelectedAsset.Type);
            if (trackIndex < 0 || trackIndex >= Tracks.Count)
            {
                StatusMessage = "No suitable track found";
                return;
            }
            
            // Find end of last clip on the track
            var track = Tracks[trackIndex];
            var startTime = track.Clips.Count > 0 
                ? track.Clips.Max(c => c.EndPosition) 
                : TimeSpan.Zero;

            var clip = _editorService.AddClipToTimeline(Project, SelectedAsset, trackIndex, startTime);
            track.Clips.Add(clip);

            // Update duration
            UpdateDuration();
            
            StatusMessage = $"Added {SelectedAsset.FileName} to timeline";
        }

        private void ExecuteDeleteClip()
        {
            // Try selected clip first, then fall back to clip under playhead
            TimelineClip? clipToDelete = SelectedClip;
            
            if (clipToDelete == null)
            {
                // Find clip at playhead position
                var (foundClip, _) = FindClipAtPlayhead();
                clipToDelete = foundClip;
            }
            
            if (clipToDelete == null)
            {
                StatusMessage = "No clip selected or at playhead";
                return;
            }

            foreach (var track in Tracks)
            {
                if (track.Clips.Remove(clipToDelete))
                {
                    StatusMessage = "Clip deleted";
                    SelectedClip = null;
                    UpdateDuration();
                    UpdatePreviewSource();
                    break;
                }
            }
        }

        /// <summary>
        /// Checks if there's a clip at the playhead position that can be split.
        /// </summary>
        private bool CanSplitAtPlayhead()
        {
            var (clip, _) = FindClipAtPlayhead();
            return clip != null;
        }

        /// <summary>
        /// Finds the clip at the current playhead position.
        /// </summary>
        private (TimelineClip? clip, TimelineTrack? track) FindClipAtPlayhead()
        {
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    // Check if playhead is within this clip (not at the very edges)
                    if (CurrentTime > clip.StartPosition && CurrentTime < clip.EndPosition)
                    {
                        return (clip, track);
                    }
                }
            }
            return (null, null);
        }

        private void ExecuteSplitClip()
        {
            System.Diagnostics.Debug.WriteLine($"[SPLIT] ExecuteSplitClip called. CurrentTime={CurrentTime}, SelectedClip={(SelectedClip?.Name ?? "null")}");

            // Find clip at playhead (Shotcut-style: split clip under playhead, not just selected)
            var (clipToSplit, track) = FindClipAtPlayhead();
            
            // Fall back to selected clip if no clip at playhead
            if (clipToSplit == null && SelectedClip != null)
            {
                clipToSplit = SelectedClip;
                track = Tracks.FirstOrDefault(t => t.Clips.Contains(SelectedClip));
                System.Diagnostics.Debug.WriteLine($"[SPLIT] Using selected clip: {clipToSplit?.Name}");
            }

            if (clipToSplit == null || track == null)
            {
                System.Diagnostics.Debug.WriteLine("[SPLIT] No clip found to split");
                StatusMessage = "Position playhead over a clip to split";
                return;
            }

            var splitTime = CurrentTime;
            System.Diagnostics.Debug.WriteLine($"[SPLIT] Splitting clip {clipToSplit.Name} at {splitTime}. Clip range: {clipToSplit.StartPosition} - {clipToSplit.EndPosition}");
            
            if (splitTime <= clipToSplit.StartPosition || splitTime >= clipToSplit.EndPosition)
            {
                System.Diagnostics.Debug.WriteLine($"[SPLIT] Invalid split position: {splitTime} not within {clipToSplit.StartPosition}-{clipToSplit.EndPosition}");
                StatusMessage = "Playhead must be within the clip to split";
                return;
            }

            // Create second half
            var secondClip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = clipToSplit.Name + " (2)",
                SourcePath = clipToSplit.SourcePath,
                StartPosition = splitTime,
                Duration = clipToSplit.EndPosition - splitTime,
                SourceStart = clipToSplit.SourceStart + (splitTime - clipToSplit.StartPosition),
                SourceEnd = clipToSplit.SourceEnd,
                Speed = clipToSplit.Speed,
                Volume = clipToSplit.Volume,
                Opacity = clipToSplit.Opacity,
                Color = clipToSplit.Color
            };

            System.Diagnostics.Debug.WriteLine($"[SPLIT] Created second clip: {secondClip.Name}, StartPos={secondClip.StartPosition}, Duration={secondClip.Duration}");

            // Trim first half
            var originalDuration = clipToSplit.Duration;
            clipToSplit.Duration = splitTime - clipToSplit.StartPosition;
            clipToSplit.SourceEnd = clipToSplit.SourceStart + clipToSplit.Duration;
            
            System.Diagnostics.Debug.WriteLine($"[SPLIT] Trimmed first clip: Duration={clipToSplit.Duration} (was {originalDuration})");

            // Add second clip after the first
            var insertIndex = track.Clips.IndexOf(clipToSplit) + 1;
            track.Clips.Insert(insertIndex, secondClip);
            
            System.Diagnostics.Debug.WriteLine($"[SPLIT] Inserted second clip at index {insertIndex}. Total clips in track: {track.Clips.Count}");

            StatusMessage = "Clip split";
        }

        private void ExecuteDuplicateClip()
        {
            if (SelectedClip == null) return;

            var track = Tracks.FirstOrDefault(t => t.Clips.Contains(SelectedClip));
            if (track == null) return;

            var duplicate = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = SelectedClip.Name + " (copy)",
                SourcePath = SelectedClip.SourcePath,
                StartPosition = SelectedClip.EndPosition,
                Duration = SelectedClip.Duration,
                SourceStart = SelectedClip.SourceStart,
                SourceEnd = SelectedClip.SourceEnd,
                Speed = SelectedClip.Speed,
                Volume = SelectedClip.Volume,
                Opacity = SelectedClip.Opacity,
                Color = SelectedClip.Color,
                ColorGrading = SelectedClip.ColorGrading,
                TransformKeyframes = new List<KeyframeTrack>(SelectedClip.TransformKeyframes)
            };

            track.Clips.Add(duplicate);
            UpdateDuration();
            
            StatusMessage = "Clip duplicated";
        }

        private async Task ExecuteDetectBeatsAsync()
        {
            if (SelectedAsset?.Type != MediaType.Audio) return;

            IsBusy = true;
            _operationCts = new CancellationTokenSource();
            StatusMessage = "Detecting beats...";

            try
            {
                var result = await _beatDetection.DetectBeatsAsync(
                    SelectedAsset.FilePath,
                    null,
                    new Progress<double>(p => Progress = p),
                    _operationCts.Token);

                if (result.Success)
                {
                    BeatMarkers.Clear();
                    foreach (var beat in result.BeatMarkers)
                    {
                        BeatMarkers.Add(beat);
                    }
                    
                    Project!.BeatMarkers = result.BeatMarkers;
                    Project.Bpm = result.Bpm;
                    
                    StatusMessage = $"Detected {result.BeatMarkers.Count} beats ({result.Bpm:F1} BPM)";
                }
                else
                {
                    StatusMessage = $"Beat detection failed: {result.Error}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Beat detection cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Beat detection failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        private async Task ExecuteApplyBeatSyncAsync()
        {
            if (Project == null || BeatMarkers.Count == 0) return;

            var audioAsset = MediaLibrary.FirstOrDefault(a => a.Type == MediaType.Audio);
            if (audioAsset == null) return;

            IsBusy = true;
            StatusMessage = "Applying beat sync...";

            try
            {
                await _editorService.ApplyBeatSyncAsync(
                    Project,
                    0, // Video track
                    audioAsset,
                    null,
                    new Progress<double>(p => Progress = p),
                    _operationCts?.Token ?? CancellationToken.None);

                SyncProjectToUI();
                StatusMessage = "Beat sync applied";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Beat sync failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
            }
        }

        private async Task ExecuteGenerateCaptionsAsync()
        {
            if (Project == null) return;

            // Find audio source: first check Media Library, then check timeline clips
            string? audioPath = null;
            
            // Check Media Library first
            var audioAsset = MediaLibrary.FirstOrDefault(a => a.HasAudio || a.Type == MediaType.Audio);
            if (audioAsset != null)
            {
                audioPath = audioAsset.FilePath;
                LogDebug($"[CAPTION] Using audio from Media Library: {audioPath}");
            }
            else
            {
                // Check timeline clips for video/audio files
                foreach (var track in Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (!string.IsNullOrEmpty(clip.SourcePath) && System.IO.File.Exists(clip.SourcePath))
                        {
                            var ext = System.IO.Path.GetExtension(clip.SourcePath).ToLowerInvariant();
                            if (IsVideoExtension(ext) || ext is ".mp3" or ".wav" or ".aac" or ".m4a" or ".flac" or ".ogg")
                            {
                                audioPath = clip.SourcePath;
                                LogDebug($"[CAPTION] Using audio from timeline clip: {audioPath}");
                                break;
                            }
                        }
                    }
                    if (audioPath != null) break;
                }
            }
            
            if (string.IsNullOrEmpty(audioPath))
            {
                StatusMessage = "No audio/video found for caption generation. Add media to timeline or library first.";
                return;
            }

            IsBusy = true;
            _operationCts = new CancellationTokenSource();
            StatusMessage = "Generating captions with AI...";

            try
            {
                var whisper = new LocalWhisperService();
                
                if (!whisper.IsAvailable)
                {
                    StatusMessage = $"Whisper not available: {whisper.InstallationStatus}";
                    LogDebug($"[CAPTION] Whisper status: {whisper.InstallationStatus}");
                    return;
                }
                
                LogDebug($"[CAPTION] Starting transcription of: {audioPath}");

                var captions = await whisper.TranscribeAsync(
                    audioPath,
                    "auto",
                    new Progress<double>(p => Progress = p),
                    _operationCts.Token);

                Captions.Clear();
                foreach (var caption in captions)
                {
                    Captions.Add(caption);
                }

                // Add to project
                var captionTrack = new CaptionTrack { Language = "en" };
                captionTrack.Captions.AddRange(captions);
                Project.CaptionTracks.Add(captionTrack);

                StatusMessage = $"Generated {captions.Count} captions";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Caption generation cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Caption generation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        private async Task ExecuteExportCaptionsAsync()
        {
            if (Captions.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SRT Subtitles|*.srt|VTT Subtitles|*.vtt|Text File|*.txt",
                FileName = $"{Project?.Name ?? "captions"}.srt",
                Title = "Export Captions"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = SrtHelper.Export(Captions.ToList());
                    await File.WriteAllTextAsync(dialog.FileName, content);
                    StatusMessage = $"Exported captions to {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        private void ExecuteApplyTransition(Transition? transition)
        {
            if (SelectedClip == null || transition == null) return;

            // Find next clip
            var track = Tracks.FirstOrDefault(t => t.Clips.Contains(SelectedClip));
            if (track == null) return;

            var clips = track.Clips.OrderBy(c => c.StartPosition).ToList();
            var index = clips.IndexOf(SelectedClip);
            
            if (index < clips.Count - 1)
            {
                var nextClip = clips[index + 1];
                _editorService.ApplyTransition(SelectedClip, nextClip, transition);
                StatusMessage = $"Applied {transition.Name} transition";
            }
            else
            {
                SelectedClip.TransitionOut = transition;
                StatusMessage = $"Added {transition.Name} as out transition";
            }
        }

        private void ExecuteAddKeyframe()
        {
            if (SelectedClip == null) return;

            var clipTime = (CurrentTime - SelectedClip.StartTime).TotalSeconds;
            if (clipTime < 0 || clipTime > SelectedClip.Duration.TotalSeconds)
            {
                StatusMessage = "Playhead must be within the clip";
                return;
            }

            // Add transform keyframe at current time
            var positionTrack = SelectedClip.TransformKeyframes.FirstOrDefault(t => t.Property == "Position")
                ?? new KeyframeTrack { Property = "Position" };

            if (!SelectedClip.TransformKeyframes.Contains(positionTrack))
            {
                SelectedClip.TransformKeyframes.Add(positionTrack);
            }

            var keyframe = new AnimatedKeyframe
            {
                Time = TimeSpan.FromSeconds(clipTime),
                Value = 0,
                Easing = KeyframeEasing.EaseInOut
            };

            positionTrack.Keyframes.Add(keyframe);
            StatusMessage = $"Added keyframe at {clipTime:F2}s";
        }

        private void ExecuteRemoveKeyframe()
        {
            if (SelectedClip == null) return;
            
            var clipTime = (CurrentTime - SelectedClip.StartTime).TotalSeconds;
            
            foreach (var track in SelectedClip.TransformKeyframes)
            {
                var keyframe = track.Keyframes.FirstOrDefault(k => Math.Abs(k.Time.TotalSeconds - clipTime) < 0.1);
                if (keyframe != null)
                {
                    track.Keyframes.Remove(keyframe);
                    StatusMessage = "Keyframe removed";
                    return;
                }
            }

            StatusMessage = "No keyframe at playhead position";
        }

        private async Task ExecuteExportVideoAsync()
        {
            if (Project == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "MP4 Video|*.mp4|MOV Video|*.mov|WebM Video|*.webm|All Files|*.*",
                FileName = $"{Project.Name}.mp4",
                Title = "Export Video"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                _operationCts = new CancellationTokenSource();
                StatusMessage = "Exporting video...";

                try
                {
                    SyncUIToProject();

                    var profile = ExportProfiles.FirstOrDefault() ?? Core.Models.Video.ExportProfiles.HD1080p30;

                    await _editorService.ExportAsync(
                        Project,
                        dialog.FileName,
                        profile,
                        new Progress<double>(p =>
                        {
                            Progress = p;
                            StatusMessage = $"Exporting... {p * 100:F0}%";
                        }),
                        _operationCts.Token);

                    StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
                }
                catch (OperationCanceledException)
                {
                    StatusMessage = "Export cancelled";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                    Progress = 0;
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }
        }

        private void ExecutePlayPause()
        {
            IsPlaying = !IsPlaying;
            
            if (IsPlaying)
            {
                // If at end, restart from beginning
                if (CurrentTime >= Duration && Duration > TimeSpan.Zero)
                {
                    CurrentTime = TimeSpan.Zero;
                }
                
                _playbackTimer.Start();
                UpdatePreviewSource();
                StatusMessage = "Playing...";
            }
            else
            {
                _playbackTimer.Stop();
                StatusMessage = "Paused";
            }
        }

        private void ExecuteStop()
        {
            IsPlaying = false;
            _playbackTimer.Stop();
            CurrentTime = TimeSpan.Zero;
            StatusMessage = "Stopped";
        }
        
        #region Shotcut-Inspired Transport Controls
        
        /// <summary>
        /// Assumed frame rate for frame navigation (30 FPS).
        /// </summary>
        private const double FrameRate = 30.0;
        
        private void ExecuteSkipPrevious()
        {
            // Skip to previous clip start or timeline start
            var allClipStarts = Tracks.SelectMany(t => t.Clips)
                                       .Select(c => c.StartPosition)
                                       .Where(t => t < CurrentTime)
                                       .OrderByDescending(t => t)
                                       .ToList();
            
            if (allClipStarts.Count > 0)
            {
                CurrentTime = allClipStarts[0];
            }
            else
            {
                CurrentTime = TimeSpan.Zero;
            }
            UpdatePreviewSource();
            StatusMessage = $"Skipped to {FormatTime(CurrentTime)}";
        }
        
        private void ExecuteSkipNext()
        {
            // Skip to next clip start or timeline end
            var allClipStarts = Tracks.SelectMany(t => t.Clips)
                                       .Select(c => c.StartPosition)
                                       .Where(t => t > CurrentTime)
                                       .OrderBy(t => t)
                                       .ToList();
            
            if (allClipStarts.Count > 0)
            {
                CurrentTime = allClipStarts[0];
            }
            else if (Duration > TimeSpan.Zero)
            {
                CurrentTime = Duration;
            }
            UpdatePreviewSource();
            StatusMessage = $"Skipped to {FormatTime(CurrentTime)}";
        }
        
        private void ExecutePreviousFrame()
        {
            var frameTime = TimeSpan.FromSeconds(1.0 / FrameRate);
            CurrentTime = CurrentTime > frameTime ? CurrentTime - frameTime : TimeSpan.Zero;
            UpdatePreviewSource();
        }
        
        private void ExecuteNextFrame()
        {
            var frameTime = TimeSpan.FromSeconds(1.0 / FrameRate);
            var newTime = CurrentTime + frameTime;
            CurrentTime = Duration > TimeSpan.Zero && newTime > Duration ? Duration : newTime;
            UpdatePreviewSource();
        }
        
        private void ExecuteRewind()
        {
            // Toggle or increase rewind speed (2x, 4x, 8x)
            if (PlaybackSpeed >= 0)
            {
                PlaybackSpeed = -2.0;
            }
            else if (PlaybackSpeed > -8.0)
            {
                PlaybackSpeed *= 2;
            }
            
            if (!IsPlaying)
            {
                IsPlaying = true;
                _playbackTimer.Start();
            }
            StatusMessage = $"Rewinding {Math.Abs(PlaybackSpeed)}x";
        }
        
        private void ExecuteFastForward()
        {
            // Toggle or increase fast forward speed (2x, 4x, 8x)
            if (PlaybackSpeed <= 1.0)
            {
                PlaybackSpeed = 2.0;
            }
            else if (PlaybackSpeed < 8.0)
            {
                PlaybackSpeed *= 2;
            }
            
            if (!IsPlaying)
            {
                IsPlaying = true;
                _playbackTimer.Start();
            }
            StatusMessage = $"Fast forward {PlaybackSpeed}x";
        }
        
        private void ExecuteSeekBySeconds(double seconds)
        {
            var newTime = CurrentTime + TimeSpan.FromSeconds(seconds);
            if (newTime < TimeSpan.Zero) newTime = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && newTime > Duration) newTime = Duration;
            CurrentTime = newTime;
            UpdatePreviewSource();
        }
        
        private void ExecuteSetInPoint()
        {
            InPoint = CurrentTime;
            if (OutPoint < InPoint) OutPoint = Duration;
            StatusMessage = $"In point set at {FormatTime(InPoint)}";
        }
        
        private void ExecuteSetOutPoint()
        {
            OutPoint = CurrentTime;
            if (InPoint > OutPoint) InPoint = TimeSpan.Zero;
            StatusMessage = $"Out point set at {FormatTime(OutPoint)}";
        }
        
        private void ExecuteAddMarker()
        {
            var marker = new TimelineMarker
            {
                Id = Guid.NewGuid(),
                Position = CurrentTime,
                Name = $"Marker {Markers.Count + 1}",
                Type = MarkerType.Standard
            };
            Markers.Add(marker);
            StatusMessage = $"Marker added at {FormatTime(CurrentTime)}";
        }
        
        private void ExecuteGoToMarker(object? param)
        {
            if (param is TimelineMarker marker)
            {
                CurrentTime = marker.Position;
                UpdatePreviewSource();
                StatusMessage = $"Jumped to marker: {marker.Name}";
            }
            else if (param is int index && index >= 0 && index < Markers.Count)
            {
                CurrentTime = Markers[index].Position;
                UpdatePreviewSource();
                StatusMessage = $"Jumped to marker {index + 1}";
            }
        }
        
        #endregion
        
        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsPlaying) return;
            
            // Calculate time delta based on playback speed
            var baseInterval = _playbackTimer.Interval;
            var scaledInterval = TimeSpan.FromTicks((long)(baseInterval.Ticks * PlaybackSpeed));
            var newTime = CurrentTime + scaledInterval;
            
            // Handle boundaries based on loop mode and in/out points
            var effectiveStart = IsLooping && InPoint > TimeSpan.Zero ? InPoint : TimeSpan.Zero;
            var effectiveEnd = IsLooping && OutPoint > TimeSpan.Zero ? OutPoint : Duration;
            
            if (PlaybackSpeed < 0)
            {
                // Rewinding
                if (newTime <= effectiveStart)
                {
                    if (IsLooping && effectiveEnd > TimeSpan.Zero)
                    {
                        CurrentTime = effectiveEnd;
                    }
                    else
                    {
                        CurrentTime = TimeSpan.Zero;
                        IsPlaying = false;
                        _playbackTimer.Stop();
                        PlaybackSpeed = 1.0;
                        StatusMessage = "Reached start";
                    }
                }
                else
                {
                    CurrentTime = newTime;
                }
            }
            else
            {
                // Playing forward
                if (newTime >= effectiveEnd && effectiveEnd > TimeSpan.Zero)
                {
                    if (IsLooping)
                    {
                        CurrentTime = effectiveStart;
                        StatusMessage = "Looping...";
                    }
                    else
                    {
                        CurrentTime = effectiveEnd;
                        IsPlaying = false;
                        _playbackTimer.Stop();
                        PlaybackSpeed = 1.0;
                        StatusMessage = "Playback complete";
                    }
                }
                else
                {
                    CurrentTime = newTime;
                }
            }
            
            UpdatePreviewSource();
        }
        
        private void UpdatePreviewSource()
        {
            System.Diagnostics.Debug.WriteLine($"[Preview] UpdatePreviewSource called. CurrentTime={CurrentTime}, Tracks={Tracks.Count}");
            
            // Simple Shotcut-style approach:
            // 1. Find ANY clip at current playhead position (check all tracks)
            // 2. Set preview to that clip's source
            // 3. Handle overlays separately
            
            string? videoSource = null;
            string? overlaySource = null;
            
            // Check all tracks for clips at current time
            foreach (var track in Tracks)
            {
                var clip = FindClipAtCurrentTime(track);
                if (clip == null || string.IsNullOrEmpty(clip.SourcePath)) continue;
                
                var ext = System.IO.Path.GetExtension(clip.SourcePath).ToLowerInvariant();
                
                if (track.Type == TrackType.Overlay)
                {
                    // Overlay track - could be image or video
                    if (IsImageExtension(ext))
                    {
                        // Image overlay - display on top of base video
                        if (overlaySource == null)
                            overlaySource = clip.SourcePath;
                    }
                    else if (IsVideoExtension(ext) && videoSource == null)
                    {
                        // Video on overlay track - use if no main video
                        videoSource = clip.SourcePath;
                    }
                }
                else if (track.Type == TrackType.Video)
                {
                    // Main video track - highest priority for video source
                    if (IsVideoExtension(ext) || IsImageExtension(ext))
                    {
                        videoSource = clip.SourcePath;
                        System.Diagnostics.Debug.WriteLine($"[Preview] Found video/image on {track.Name}: {clip.Name}");
                    }
                }
                else if (track.Type == TrackType.Audio)
                {
                    // Audio track - check if it has video (shouldn't but handle it)
                    if (IsVideoExtension(ext) && videoSource == null)
                    {
                        videoSource = clip.SourcePath;
                    }
                }
            }
            
            // Update overlay (image on top of video)
            OverlaySource = overlaySource;
            
            // Update main video source
            if (videoSource != null)
            {
                if (PreviewSource != videoSource)
                {
                    System.Diagnostics.Debug.WriteLine($"[Preview] Setting PreviewSource to: {videoSource}");
                    PreviewSource = videoSource;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] No clip at current time, keeping current preview");
                // Don't clear - keep showing last frame like Shotcut does
            }
        }
        
        private static bool IsImageExtension(string ext)
        {
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".tif";
        }
        
        private TimelineClip? FindClipAtCurrentTime(TimelineTrack track)
        {
            foreach (var c in track.Clips)
            {
                var clipStart = c.StartTime > TimeSpan.Zero ? c.StartTime : c.StartPosition;
                var clipEnd = clipStart + c.Duration;
                
                if (CurrentTime >= clipStart && CurrentTime < clipEnd)
                {
                    System.Diagnostics.Debug.WriteLine($"[Preview] Found clip '{c.Name}' at {clipStart} in {track.Name}");
                    return c;
                }
            }
            return null;
        }
        
        private bool TrySetPreviewSource(TimelineClip clip)
        {
            if (string.IsNullOrEmpty(clip.SourcePath))
                return false;
                
            // Check if file exists
            if (!System.IO.File.Exists(clip.SourcePath))
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] WARNING: File does not exist: '{clip.SourcePath}'");
                StatusMessage = $"Preview file not found: {clip.SourcePath}";
                return false;
            }
            
            if (PreviewSource != clip.SourcePath)
            {
                System.Diagnostics.Debug.WriteLine($"[Preview] Setting PreviewSource to '{clip.SourcePath}'");
                PreviewSource = clip.SourcePath;
            }
            return true;
        }
        
        private static bool IsVideoExtension(string ext)
        {
            return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".wmv" 
                or ".flv" or ".m4v" or ".mpg" or ".mpeg" or ".ts" or ".mts" or ".m2ts" or ".3gp";
        }
        
        private void ExecuteSeekToPosition(object? param)
        {
            if (param is double pixelPosition)
            {
                // Convert pixel position to time
                var seconds = pixelPosition / (PixelsPerSecond * ZoomLevel);
                CurrentTime = TimeSpan.FromSeconds(Math.Max(0, seconds));
                UpdatePreviewSource();
                StatusMessage = $"Seeked to {FormatTime(CurrentTime)}";
            }
            else if (param is TimeSpan time)
            {
                CurrentTime = time;
                UpdatePreviewSource();
                StatusMessage = $"Seeked to {FormatTime(CurrentTime)}";
            }
        }
        
        private async Task ExecuteInsertOverlayAtPlayheadAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Media for Overlay",
                Filter = "Media Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.webm|" +
                         "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp|" +
                         "All Files|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsBusy = true;
                    _operationCts = new CancellationTokenSource();
                    StatusMessage = "Importing overlay media...";

                    var filePath = dialog.FileName;
                    var asset = await _editorService.ImportMediaAsync(
                        filePath,
                        new Progress<double>(p => Progress = p),
                        _operationCts.Token);
                    
                    MediaLibrary.Add(asset);
                    Project?.Assets.Add(asset);

                    // Find or create overlay track
                    var overlayTrack = Tracks.FirstOrDefault(t => t.Type == TrackType.Overlay);
                    if (overlayTrack == null)
                    {
                        overlayTrack = new TimelineTrack 
                        { 
                            Id = Guid.NewGuid(), 
                            Name = $"Overlay {Tracks.Count(t => t.Type == TrackType.Overlay) + 1}", 
                            Type = TrackType.Overlay 
                        };
                        Tracks.Insert(0, overlayTrack); // Overlays on top
                    }

                    // Determine duration - images get default 3 seconds
                    var duration = asset.Type == MediaType.Image 
                        ? TimeSpan.FromSeconds(DefaultImageOverlayDuration) 
                        : asset.Duration;

                    var clip = new TimelineClip
                    {
                        Id = Guid.NewGuid(),
                        Name = Path.GetFileName(filePath),
                        SourcePath = filePath,
                        StartTime = CurrentTime, // Insert at playhead
                        StartPosition = CurrentTime,
                        Duration = duration,
                        SourceDuration = duration,
                        SourceStart = TimeSpan.Zero,
                        SourceEnd = duration,
                        Volume = 1.0,
                        Speed = 1.0,
                        Opacity = 1.0,
                        Color = "#9C27B0" // Purple for overlay
                    };

                    overlayTrack.Clips.Add(clip);
                    SelectedClip = clip;
                    UpdateDuration();

                    StatusMessage = $"Added overlay '{clip.Name}' at {FormatTime(CurrentTime)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to add overlay: {ex.Message}";
                }
                finally
                {
                    IsBusy = false;
                    _operationCts?.Dispose();
                    _operationCts = null;
                }
            }
        }
        
        private void ExecuteExtendClip(object? param)
        {
            if (SelectedClip == null) return;
            
            double extensionSeconds = 1.0;
            if (param is double seconds)
            {
                extensionSeconds = seconds;
            }
            else if (param is string str && double.TryParse(str, out var parsed))
            {
                extensionSeconds = parsed;
            }
            
            SelectedClip.Duration += TimeSpan.FromSeconds(extensionSeconds);
            UpdateDuration();
            OnPropertyChanged(nameof(SelectedClip));
            StatusMessage = $"Extended clip by {extensionSeconds}s to {FormatTime(SelectedClip.Duration)}";
        }
        
        private void ExecuteTrimClip(object? param)
        {
            if (SelectedClip == null) return;
            
            double trimSeconds = 1.0;
            if (param is double seconds)
            {
                trimSeconds = seconds;
            }
            else if (param is string str && double.TryParse(str, out var parsed))
            {
                trimSeconds = parsed;
            }
            
            var newDuration = SelectedClip.Duration - TimeSpan.FromSeconds(trimSeconds);
            if (newDuration > TimeSpan.Zero)
            {
                SelectedClip.Duration = newDuration;
                UpdateDuration();
                OnPropertyChanged(nameof(SelectedClip));
                StatusMessage = $"Trimmed clip by {trimSeconds}s to {FormatTime(SelectedClip.Duration)}";
            }
            else
            {
                StatusMessage = "Cannot trim below 0 seconds";
            }
        }
        
        /// <summary>
        /// Shotcut-style Insert at Playhead: inserts clip and pushes all clips at/after playhead to the right
        /// </summary>
        private async Task ExecuteInsertAtPlayheadAsync()
        {
            var asset = SelectedAsset;
            
            // If no asset selected, open file picker
            if (asset == null)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select media file to insert",
                    Filter = "Media Files|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.mp3;*.wav;*.aac;*.flac;*.m4a;*.ogg;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files|*.*",
                    Multiselect = false
                };
                
                if (dialog.ShowDialog() == true)
                {
                    var filePath = dialog.FileName;
                    // Quick add to media library and select
                    asset = await _editorService.ImportMediaAsync(
                        filePath,
                        new Progress<double>(p => Progress = p),
                        CancellationToken.None);
                    MediaLibrary.Add(asset);
                    Project?.Assets.Add(asset);
                }
            }
            
            if (asset == null) return;
            
            // Find appropriate track
            var track = asset.Type == MediaType.Audio
                ? Tracks.FirstOrDefault(t => t.Type == TrackType.Audio)
                : Tracks.FirstOrDefault(t => t.Type == TrackType.Video);

            if (track == null)
            {
                StatusMessage = "No suitable track found";
                return;
            }

            var duration = asset.Type == MediaType.Image 
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration) 
                : asset.Duration;

            var insertTime = CurrentTime;
            
            // Ripple: push all clips at or after insert point to the right
            foreach (var existingClip in track.Clips)
            {
                if (existingClip.StartPosition >= insertTime)
                {
                    existingClip.StartPosition += duration;
                    existingClip.StartTime = existingClip.StartPosition;
                    LogDebug($"[INSERT] Pushed '{existingClip.Name}' to {existingClip.StartPosition}");
                }
                else if (existingClip.EndPosition > insertTime)
                {
                    // Clip straddles the insert point - we need to split it
                    // For simplicity, just push the whole clip
                    existingClip.StartPosition += duration;
                    existingClip.StartTime = existingClip.StartPosition;
                    LogDebug($"[INSERT] Pushed overlapping '{existingClip.Name}' to {existingClip.StartPosition}");
                }
            }

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = asset.FileName,
                SourcePath = asset.FilePath,
                StartTime = insertTime,
                StartPosition = insertTime,
                Duration = duration,
                SourceDuration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = duration,
                Volume = 1.0,
                Speed = 1.0,
                Opacity = 1.0
            };

            track.Clips.Add(clip);
            SelectedClip = clip;
            
            // Set preview to new clip
            if (!string.IsNullOrEmpty(clip.SourcePath))
            {
                PreviewSource = clip.SourcePath;
            }
            
            UpdateDuration();

            StatusMessage = $"Inserted '{clip.Name}' at {FormatTime(insertTime)} (ripple)";
        }
        
        /// <summary>
        /// Overwrite at Playhead: inserts clip without pushing other clips (overwrites)
        /// </summary>
        public void OverwriteAtPlayhead(MediaAsset asset)
        {
            if (asset == null) return;
            
            var track = asset.Type == MediaType.Audio
                ? Tracks.FirstOrDefault(t => t.Type == TrackType.Audio)
                : Tracks.FirstOrDefault(t => t.Type == TrackType.Video);

            if (track == null) return;

            var duration = asset.Type == MediaType.Image 
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration) 
                : asset.Duration;

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = asset.FileName,
                SourcePath = asset.FilePath,
                StartTime = CurrentTime,
                StartPosition = CurrentTime,
                Duration = duration,
                SourceDuration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = duration,
                Volume = 1.0,
                Speed = 1.0,
                Opacity = 1.0
            };

            track.Clips.Add(clip);
            SelectedClip = clip;
            
            if (!string.IsNullOrEmpty(clip.SourcePath))
            {
                PreviewSource = clip.SourcePath;
            }
            
            UpdateDuration();
            StatusMessage = $"Added '{clip.Name}' at {FormatTime(CurrentTime)} (overwrite)";
        }
        
        /// <summary>
        /// Insert media at a specific time position with ripple (Shotcut V key behavior)
        /// </summary>
        public void InsertAtPosition(MediaAsset asset, TimeSpan position, TimelineTrack? targetTrack = null)
        {
            if (asset == null) return;
            
            var track = targetTrack ?? (asset.Type == MediaType.Audio
                ? Tracks.FirstOrDefault(t => t.Type == TrackType.Audio)
                : Tracks.FirstOrDefault(t => t.Type == TrackType.Video));

            if (track == null) return;

            var duration = asset.Type == MediaType.Image 
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration) 
                : asset.Duration;

            // Ripple: push all clips at or after insert point to the right
            foreach (var existingClip in track.Clips)
            {
                if (existingClip.StartPosition >= position)
                {
                    existingClip.StartPosition += duration;
                    existingClip.StartTime = existingClip.StartPosition;
                }
            }

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = asset.FileName,
                SourcePath = asset.FilePath,
                StartTime = position,
                StartPosition = position,
                Duration = duration,
                SourceDuration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = duration,
                Volume = 1.0,
                Speed = 1.0,
                Opacity = 1.0
            };

            track.Clips.Add(clip);
            SelectedClip = clip;
            CurrentTime = position;
            
            if (!string.IsNullOrEmpty(clip.SourcePath))
            {
                PreviewSource = clip.SourcePath;
            }
            
            UpdateDuration();
            StatusMessage = $"Inserted '{clip.Name}' at {FormatTime(position)}";
        }
        
        #endregion
        
        #region Whisper Management
        
        private LocalWhisperService? _whisperService;
        
        private void CheckWhisperAvailability()
        {
            try
            {
                _whisperService = new LocalWhisperService();
                IsWhisperAvailable = _whisperService.IsAvailable;
                WhisperStatus = _whisperService.InstallationStatus;
            }
            catch (Exception ex)
            {
                IsWhisperAvailable = false;
                WhisperStatus = $"Whisper check failed: {ex.Message}";
            }
        }
        
        private async Task ExecuteInstallWhisperAsync()
        {
            if (_whisperService == null)
            {
                _whisperService = new LocalWhisperService();
            }
            
            IsBusy = true;
            _operationCts = new CancellationTokenSource();
            
            try
            {
                StatusMessage = "Downloading whisper.cpp...";
                WhisperStatus = "Downloading whisper.cpp executable...";
                
                var success = await _whisperService.InstallWhisperAsync(
                    new Progress<double>(p =>
                    {
                        Progress = p;
                        StatusMessage = $"Downloading whisper.cpp... {p * 100:F0}%";
                    }),
                    _operationCts.Token);
                
                if (success)
                {
                    StatusMessage = "Whisper installed. Now downloading model...";
                    WhisperStatus = "Downloading base model...";
                    
                    // Also download base model
                    await _whisperService.DownloadModelAsync(
                        "base",
                        new Progress<double>(p =>
                        {
                            Progress = p;
                            StatusMessage = $"Downloading base model... {p * 100:F0}%";
                        }),
                        _operationCts.Token);
                    
                    IsWhisperAvailable = _whisperService.IsAvailable;
                    WhisperStatus = _whisperService.InstallationStatus;
                    StatusMessage = IsWhisperAvailable ? "Whisper is ready!" : "Whisper installation completed but needs model.";
                }
                else
                {
                    StatusMessage = "Failed to install Whisper. See Output window for manual instructions.";
                    WhisperStatus = "Installation failed. Click for manual setup.";
                    ShowWhisperManualInstructions();
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Installation cancelled";
                WhisperStatus = "Installation cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Installation failed: {ex.Message}";
                WhisperStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }
        
        private void ShowWhisperManualInstructions()
        {
            var instructions = @"
================================================================================
MANUAL WHISPER INSTALLATION INSTRUCTIONS
================================================================================

Whisper auto-download failed. Please install manually:

OPTION 1: Download Pre-built Binary (Recommended for Windows)
---------------------------------------------------------------
1. Go to: https://github.com/ggerganov/whisper.cpp/releases
2. Download: whisper-bin-x64.zip (or whisper-cublas for GPU)
3. Extract to: %APPDATA%\PlatypusTools\whisper\
4. Ensure 'main.exe' (or 'whisper.exe') is in that folder

OPTION 2: Build from Source
---------------------------------------------------------------
1. Install Visual Studio 2022 with C++ workload
2. Clone: git clone https://github.com/ggerganov/whisper.cpp
3. Build: cmake -B build && cmake --build build --config Release
4. Copy main.exe to: %APPDATA%\PlatypusTools\whisper\

DOWNLOAD MODELS:
---------------------------------------------------------------
1. Go to: https://huggingface.co/ggerganov/whisper.cpp/tree/main
2. Download: ggml-base.bin (or ggml-small.bin for better quality)
3. Save to: %APPDATA%\PlatypusTools\whisper\models\

After installation, restart PlatypusTools.
================================================================================
";
            System.Diagnostics.Debug.WriteLine(instructions);
            
            // Also try to open the whisper folder
            try
            {
                var whisperPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "whisper");
                    
                if (!Directory.Exists(whisperPath))
                    Directory.CreateDirectory(whisperPath);
                    
                System.Diagnostics.Process.Start("explorer.exe", whisperPath);
            }
            catch { /* Ignore if can't open folder */ }
        }
        
        private async Task ExecuteDownloadWhisperModelAsync()
        {
            if (_whisperService == null)
            {
                _whisperService = new LocalWhisperService();
            }
            
            IsBusy = true;
            _operationCts = new CancellationTokenSource();
            
            try
            {
                StatusMessage = "Downloading Whisper model (base)...";
                WhisperStatus = "Downloading base model (~142MB)...";
                
                var success = await _whisperService.DownloadModelAsync(
                    "base",
                    new Progress<double>(p =>
                    {
                        Progress = p;
                        StatusMessage = $"Downloading model... {p * 100:F0}%";
                    }),
                    _operationCts.Token);
                
                if (success)
                {
                    IsWhisperAvailable = _whisperService.IsAvailable;
                    WhisperStatus = _whisperService.InstallationStatus;
                    StatusMessage = IsWhisperAvailable ? "Whisper model downloaded!" : "Model downloaded but executable missing.";
                }
                else
                {
                    StatusMessage = "Failed to download model.";
                    WhisperStatus = "Model download failed.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }
        
        #endregion
        
        #region Timeline Ruler
        
        private void UpdateTimelineRuler()
        {
            TimelineRulerMarkers.Clear();
            
            // Calculate visible duration and appropriate interval
            var visibleDuration = Math.Max(Duration.TotalSeconds, 60); // At least 60 seconds
            var pixelsPerSecondScaled = PixelsPerSecond * ZoomLevel;
            
            // Determine marker interval based on zoom level
            double interval;
            if (pixelsPerSecondScaled >= 200)
                interval = 0.5;  // Every half second
            else if (pixelsPerSecondScaled >= 100)
                interval = 1;    // Every second
            else if (pixelsPerSecondScaled >= 50)
                interval = 2;    // Every 2 seconds
            else if (pixelsPerSecondScaled >= 25)
                interval = 5;    // Every 5 seconds
            else if (pixelsPerSecondScaled >= 10)
                interval = 10;   // Every 10 seconds
            else
                interval = 30;   // Every 30 seconds
            
            for (double seconds = 0; seconds <= visibleDuration; seconds += interval)
            {
                var position = seconds * pixelsPerSecondScaled;
                var isMajor = seconds % (interval * 2) == 0 || interval <= 1;
                
                TimelineRulerMarkers.Add(new TimelineRulerMarker
                {
                    Time = TimeSpan.FromSeconds(seconds),
                    Position = position,
                    Label = FormatTime(TimeSpan.FromSeconds(seconds)),
                    IsMajor = isMajor
                });
            }
            
            OnPropertyChanged(nameof(TimelineWidth));
        }

        private void ExecuteUndo()
        {
            // TODO: Implement undo stack
        }

        private bool CanUndo() => false; // TODO

        private void ExecuteRedo()
        {
            // TODO: Implement redo stack
        }

        private bool CanRedo() => false; // TODO

        private void ExecuteCancelOperation()
        {
            _operationCts?.Cancel();
        }

        #endregion

        #region Helpers

        private void InitializeExportProfiles()
        {
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.HD1080p30);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.HD1080p60);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.UHD4K30);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.UHD4K60);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.UHD4K60HDR);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.Instagram);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.TikTok);
            ExportProfiles.Add(Core.Models.Video.ExportProfiles.YouTube);
        }

        private void InitializeTransitions()
        {
            AvailableTransitions.Add(new Transition { Type = TransitionType.FadeIn, Name = "Fade" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.CrossDissolve, Name = "Cross Dissolve" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.SlideLeft, Name = "Slide Left" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.SlideRight, Name = "Slide Right" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.SlideUp, Name = "Slide Up" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.SlideDown, Name = "Slide Down" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.ZoomIn, Name = "Zoom" });
            AvailableTransitions.Add(new Transition { Type = TransitionType.WipeLeft, Name = "Wipe" });
        }

        private void InitializeFilters()
        {
            // Blur filters
            AvailableFilters.Add(new VideoFilter { Name = "Blur", Type = "blur", Category = "Blur", 
                DefaultParameters = new Dictionary<string, object> { ["radius"] = 5.0 } });
            AvailableFilters.Add(new VideoFilter { Name = "Gaussian Blur", Type = "gblur", Category = "Blur",
                DefaultParameters = new Dictionary<string, object> { ["sigma"] = 2.0 } });
            AvailableFilters.Add(new VideoFilter { Name = "Motion Blur", Type = "avgblur", Category = "Blur",
                DefaultParameters = new Dictionary<string, object> { ["sizeX"] = 5, ["sizeY"] = 5 } });

            // Color filters
            AvailableFilters.Add(new VideoFilter { Name = "Grayscale", Type = "colorchannelmixer", Category = "Color",
                DefaultParameters = new Dictionary<string, object> { ["preset"] = "grayscale" } });
            AvailableFilters.Add(new VideoFilter { Name = "Sepia", Type = "colorchannelmixer", Category = "Color",
                DefaultParameters = new Dictionary<string, object> { ["preset"] = "sepia" } });
            AvailableFilters.Add(new VideoFilter { Name = "Negative", Type = "negate", Category = "Color",
                DefaultParameters = new Dictionary<string, object>() });
            AvailableFilters.Add(new VideoFilter { Name = "Vintage", Type = "curves", Category = "Color",
                DefaultParameters = new Dictionary<string, object> { ["preset"] = "vintage" } });
            AvailableFilters.Add(new VideoFilter { Name = "Cool Tone", Type = "colorbalance", Category = "Color",
                DefaultParameters = new Dictionary<string, object> { ["bs"] = 0.2, ["bm"] = 0.1 } });
            AvailableFilters.Add(new VideoFilter { Name = "Warm Tone", Type = "colorbalance", Category = "Color",
                DefaultParameters = new Dictionary<string, object> { ["rs"] = 0.2, ["rm"] = 0.1 } });

            // Stylize filters
            AvailableFilters.Add(new VideoFilter { Name = "Sharpen", Type = "unsharp", Category = "Stylize",
                DefaultParameters = new Dictionary<string, object> { ["luma_msize_x"] = 5, ["luma_amount"] = 1.0 } });
            AvailableFilters.Add(new VideoFilter { Name = "Edge Detect", Type = "edgedetect", Category = "Stylize",
                DefaultParameters = new Dictionary<string, object>() });
            AvailableFilters.Add(new VideoFilter { Name = "Posterize", Type = "eq", Category = "Stylize",
                DefaultParameters = new Dictionary<string, object> { ["contrast"] = 2.0, ["saturation"] = 1.5 } });
            AvailableFilters.Add(new VideoFilter { Name = "Vignette", Type = "vignette", Category = "Stylize",
                DefaultParameters = new Dictionary<string, object> { ["angle"] = 0.5 } });

            // Distort filters
            AvailableFilters.Add(new VideoFilter { Name = "Mirror", Type = "hflip", Category = "Distort",
                DefaultParameters = new Dictionary<string, object>() });
            AvailableFilters.Add(new VideoFilter { Name = "Flip Vertical", Type = "vflip", Category = "Distort",
                DefaultParameters = new Dictionary<string, object>() });
            AvailableFilters.Add(new VideoFilter { Name = "Rotate 90°", Type = "transpose", Category = "Distort",
                DefaultParameters = new Dictionary<string, object> { ["dir"] = 1 } });
        }

        private void InitializeShotcutFilters()
        {
            // Load all filters from FilterLibrary
            var allFilters = FilterLibrary.GetAllFilters();
            foreach (var filter in allFilters)
            {
                ShotcutFilters.Add(filter);
                FilteredShotcutFilters.Add(filter);
            }
            
            // Group by category
            var categories = Enum.GetValues<FilterCategory>();
            foreach (var category in categories)
            {
                var filtersInCategory = FilterLibrary.GetFiltersByCategory(category).ToList();
                if (filtersInCategory.Count > 0)
                {
                    FiltersByCategory.Add(new FilterCategoryGroup
                    {
                        Category = category,
                        CategoryName = category.ToString(),
                        Filters = new ObservableCollection<Filter>(filtersInCategory)
                    });
                }
            }
            
            StatusMessage = $"Loaded {ShotcutFilters.Count} filters in {FiltersByCategory.Count} categories";
        }

        private void InitializeExportPresets()
        {
            // Load all export presets from ExportPresets library
            var allPresets = ExportPresets.GetAll();
            foreach (var preset in allPresets)
            {
                AvailableExportPresets.Add(preset);
            }
            
            // Group by category
            var categories = Enum.GetValues<ExportCategory>();
            foreach (var category in categories)
            {
                var presetsInCategory = ExportPresets.GetByCategory(category).ToList();
                if (presetsInCategory.Count > 0)
                {
                    ExportPresetsByCategory.Add(new ExportCategoryGroup
                    {
                        Category = category,
                        CategoryName = category.ToString(),
                        Presets = new ObservableCollection<ExportPreset>(presetsInCategory)
                    });
                }
            }
            
            // Select default preset (YouTube HD)
            SelectedExportPreset = AvailableExportPresets.FirstOrDefault(p => p.Name == "YouTube HD");
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
                                    
                var matchesCategory = !SelectedFilterCategory.HasValue || 
                                      filter.Category == SelectedFilterCategory.Value;
                
                if (matchesSearch && matchesCategory)
                {
                    FilteredShotcutFilters.Add(filter);
                }
            }
        }

        #region Shotcut-Inspired Command Implementations

        private void ExecuteCopyClip()
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            _timelineOps.Copy(new[] { SelectedClip });
            StatusMessage = $"Copied clip: {SelectedClip.Name}";
        }

        private void ExecuteCutClip()
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            _timelineOps.Cut(SelectedTrack, new[] { SelectedClip });
            SelectedClip = null;
            UpdateDuration();
            StatusMessage = "Cut clip to clipboard";
        }

        private void ExecutePasteClip()
        {
            if (!_timelineOps.HasClipboard || SelectedTrack == null) return;
            _timelineOps.Paste(SelectedTrack, CurrentTime);
            UpdateDuration();
            StatusMessage = "Pasted clip from clipboard";
            OnPropertyChanged(nameof(Tracks));
        }

        private void ExecuteRippleTrimIn()
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            var trimAmount = TimeSpan.FromSeconds(1); // Or get from UI/parameter
            _timelineOps.RippleTrim(SelectedTrack, SelectedClip, trimAmount, true);
            UpdateDuration();
            StatusMessage = "Ripple trimmed clip start";
        }

        private void ExecuteRippleTrimOut()
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            var trimAmount = TimeSpan.FromSeconds(1);
            _timelineOps.RippleTrim(SelectedTrack, SelectedClip, trimAmount, false);
            UpdateDuration();
            StatusMessage = "Ripple trimmed clip end";
        }

        private void ExecuteSlipEdit(object? param)
        {
            if (SelectedClip == null) return;
            var offset = param is double d ? TimeSpan.FromSeconds(d) : TimeSpan.FromSeconds(0.5);
            _timelineOps.SlipEdit(SelectedClip, offset);
            StatusMessage = "Slip edited clip";
        }

        private void ExecuteSlideEdit(object? param)
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            var offset = param is double d ? TimeSpan.FromSeconds(d) : TimeSpan.FromSeconds(0.5);
            _timelineOps.SlideEdit(SelectedTrack, SelectedClip, offset);
            UpdateDuration();
            StatusMessage = "Slide edited clip";
        }

        private void ExecuteApplyShotcutFilter(Filter? filter)
        {
            if (filter == null || SelectedClip == null) return;
            
            // Clone the filter for this clip instance
            var clonedFilter = filter.Clone();
            SelectedClip.Filters.Add(clonedFilter);
            
            StatusMessage = $"Applied filter: {filter.Name}";
            OnPropertyChanged(nameof(SelectedClip));
        }

        private void ExecuteRemoveShotcutFilter(Filter? filter)
        {
            if (filter == null || SelectedClip == null) return;
            SelectedClip.Filters.Remove(filter);
            StatusMessage = $"Removed filter: {filter.Name}";
            OnPropertyChanged(nameof(SelectedClip));
        }

        private void ExecuteToggleFilterEnabled(Filter? filter)
        {
            if (filter == null) return;
            filter.IsEnabled = !filter.IsEnabled;
            StatusMessage = filter.IsEnabled ? $"Enabled filter: {filter.Name}" : $"Disabled filter: {filter.Name}";
            OnPropertyChanged(nameof(SelectedClip));
        }

        private void ExecuteMoveFilterUp(Filter? filter)
        {
            if (filter == null || SelectedClip == null) return;
            var index = SelectedClip.Filters.IndexOf(filter);
            if (index > 0)
            {
                SelectedClip.Filters.RemoveAt(index);
                SelectedClip.Filters.Insert(index - 1, filter);
                StatusMessage = $"Moved filter up: {filter.Name}";
            }
        }

        private void ExecuteMoveFilterDown(Filter? filter)
        {
            if (filter == null || SelectedClip == null) return;
            var index = SelectedClip.Filters.IndexOf(filter);
            if (index < SelectedClip.Filters.Count - 1)
            {
                SelectedClip.Filters.RemoveAt(index);
                SelectedClip.Filters.Insert(index + 1, filter);
                StatusMessage = $"Moved filter down: {filter.Name}";
            }
        }

        private async Task ExecuteGenerateProxyAsync()
        {
            if (SelectedAsset == null) return;
            
            try
            {
                IsBusy = true;
                StatusMessage = $"Generating proxy for {SelectedAsset.FileName}...";
                
                var proxySettings = new ProxySettings { IsEnabled = true };
                var proxyFile = await _proxyService.GenerateProxyAsync(
                    SelectedAsset.FilePath,
                    proxySettings,
                    new Progress<double>(p => Progress = p),
                    _operationCts?.Token ?? CancellationToken.None);
                
                StatusMessage = $"Proxy generated: {Path.GetFileName(proxyFile.ProxyPath)}";
                
                // Update any clips using this asset
                foreach (var track in Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (clip.SourcePath == SelectedAsset.FilePath)
                        {
                            clip.ProxyPath = proxyFile.ProxyPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Proxy generation failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
            }
        }

        private void ExecuteAddTextClip()
        {
            // Find or create text track
            var textTrack = Tracks.FirstOrDefault(t => t.Name.Contains("Text") || t.Type == TrackType.Overlay);
            if (textTrack == null)
            {
                textTrack = new TimelineTrack
                {
                    Id = Guid.NewGuid(),
                    Name = "Text 1",
                    Type = TrackType.Overlay
                };
                Tracks.Insert(0, textTrack);
            }
            
            // Create a new text clip with default title preset
            var textElement = new TextElement
            {
                Text = "Title Text",
                FontFamily = "Arial Black",
                FontSize = 96,
                FontWeight = 700,
                Color = "#FFFFFF",
                OutlineColor = "#000000",
                OutlineWidth = 2,
                ShadowColor = "#80000000",
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            
            var textClip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = "New Title",
                Type = ClipType.Title,
                StartPosition = CurrentTime,
                Duration = TimeSpan.FromSeconds(5),
                Color = "#E91E63", // Pink for text clips
                TextOverlay = textElement
            };
            
            textTrack.Clips.Add(textClip);
            SelectedClip = textClip;
            UpdateDuration();
            StatusMessage = "Added text clip - double-click to edit";
        }

        private async Task ExecuteExportWithPresetAsync()
        {
            if (SelectedExportPreset == null || Project == null) return;
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Video",
                Filter = $"{SelectedExportPreset.Format} Files|*.{SelectedExportPreset.Extension}|All Files|*.*",
                FileName = $"{Project.Name}_{SelectedExportPreset.Name}",
                DefaultExt = SelectedExportPreset.Extension
            };
            
            if (dialog.ShowDialog() != true) return;
            
            try
            {
                IsBusy = true;
                _operationCts = new CancellationTokenSource();
                
                StatusMessage = $"Exporting with {SelectedExportPreset.Name} preset...";
                
                // Use the existing VideoExporter through EditorService
                await _editorService.ExportAsync(
                    Project,
                    dialog.FileName,
                    null, // Use default profile
                    new Progress<double>(p => Progress = p),
                    _operationCts.Token);
                
                StatusMessage = $"Export complete: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _operationCts?.Dispose();
                _operationCts = null;
            }
        }

        private void ExecuteRippleDelete()
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            _timelineOps.Ripple(SelectedTrack, SelectedClip.StartPosition, -SelectedClip.Duration);
            SelectedTrack.Clips.Remove(SelectedClip);
            SelectedClip = null;
            UpdateDuration();
            StatusMessage = "Ripple deleted clip";
        }

        private void ExecuteLift()
        {
            // Lift (remove) content between in and out points, leaving gap
            foreach (var track in Tracks)
            {
                var clipsToRemove = track.Clips
                    .Where(c => c.StartPosition >= InPoint && c.EndPosition <= OutPoint)
                    .ToList();
                
                foreach (var clip in clipsToRemove)
                {
                    track.Clips.Remove(clip);
                }
            }
            UpdateDuration();
            StatusMessage = $"Lifted content from {FormatTime(InPoint)} to {FormatTime(OutPoint)}";
        }

        private void ExecuteInsert()
        {
            if (SelectedAsset == null) return;
            
            // Insert at playhead, rippling content after
            var targetTrack = Tracks.FirstOrDefault(t => 
                (SelectedAsset.Type == MediaType.Audio && t.Type == TrackType.Audio) ||
                (SelectedAsset.Type != MediaType.Audio && t.Type == TrackType.Video));
            
            if (targetTrack == null) return;
            
            // Ripple existing content
            var insertDuration = SelectedAsset.Duration;
            _timelineOps.Ripple(targetTrack, CurrentTime, insertDuration);
            
            // Add the clip
            var clip = CreateClipFromAsset(SelectedAsset, CurrentTime);
            targetTrack.Clips.Add(clip);
            
            UpdateDuration();
            StatusMessage = $"Inserted {SelectedAsset.FileName} at playhead";
        }

        private void ExecuteOverwrite()
        {
            if (SelectedAsset == null) return;
            
            var targetTrack = Tracks.FirstOrDefault(t => 
                (SelectedAsset.Type == MediaType.Audio && t.Type == TrackType.Audio) ||
                (SelectedAsset.Type != MediaType.Audio && t.Type == TrackType.Video));
            
            if (targetTrack == null) return;
            
            // Remove clips in the overwrite range
            var endPos = CurrentTime + SelectedAsset.Duration;
            var clipsToRemove = targetTrack.Clips
                .Where(c => c.StartPosition < endPos && c.EndPosition > CurrentTime)
                .ToList();
            
            foreach (var clip in clipsToRemove)
            {
                // Trim or remove based on overlap
                if (clip.StartPosition >= CurrentTime && clip.EndPosition <= endPos)
                {
                    targetTrack.Clips.Remove(clip);
                }
                else if (clip.StartPosition < CurrentTime)
                {
                    clip.Duration = CurrentTime - clip.StartPosition;
                }
                else if (clip.EndPosition > endPos)
                {
                    var trimAmount = endPos - clip.StartPosition;
                    clip.SourceStart += trimAmount;
                    clip.StartPosition = endPos;
                    clip.Duration -= trimAmount;
                }
            }
            
            // Add the new clip
            var newClip = CreateClipFromAsset(SelectedAsset, CurrentTime);
            targetTrack.Clips.Add(newClip);
            
            UpdateDuration();
            StatusMessage = $"Overwrote with {SelectedAsset.FileName}";
        }

        private void ExecuteAppend()
        {
            if (SelectedAsset == null) return;
            
            var targetTrack = Tracks.FirstOrDefault(t => 
                (SelectedAsset.Type == MediaType.Audio && t.Type == TrackType.Audio) ||
                (SelectedAsset.Type != MediaType.Audio && t.Type == TrackType.Video));
            
            if (targetTrack == null) return;
            
            // Find the end of the track
            var trackEnd = targetTrack.Clips.Count > 0 
                ? targetTrack.Clips.Max(c => c.EndPosition) 
                : TimeSpan.Zero;
            
            var clip = CreateClipFromAsset(SelectedAsset, trackEnd);
            targetTrack.Clips.Add(clip);
            
            UpdateDuration();
            StatusMessage = $"Appended {SelectedAsset.FileName} to end of track";
        }

        private void ExecuteRollEdit(object? param)
        {
            if (SelectedClip == null || SelectedTrack == null) return;
            var offset = param is double d ? TimeSpan.FromSeconds(d) : TimeSpan.FromSeconds(0.5);
            
            // Find adjacent clip to roll with
            var adjacentClip = SelectedTrack.Clips
                .Where(c => c != SelectedClip && c.StartPosition == SelectedClip.EndPosition)
                .FirstOrDefault();
            
            if (adjacentClip != null)
            {
                _timelineOps.RollEdit(SelectedClip, adjacentClip, offset);
                StatusMessage = "Roll edited clip boundary";
            }
            else
            {
                StatusMessage = "No adjacent clip for roll edit";
            }
        }

        private void ExecuteUndoTimeline()
        {
            if (SelectedTrack == null) return;
            _timelineOps.Undo(SelectedTrack);
            StatusMessage = "Undone timeline operation";
            OnPropertyChanged(nameof(Tracks));
        }

        private void ExecuteRedoTimeline()
        {
            if (SelectedTrack == null) return;
            _timelineOps.Redo(SelectedTrack);
            StatusMessage = "Redone timeline operation";
            OnPropertyChanged(nameof(Tracks));
        }

        private TimelineClip CreateClipFromAsset(MediaAsset asset, TimeSpan startPosition)
        {
            var duration = asset.Type == MediaType.Image 
                ? TimeSpan.FromSeconds(DefaultImageOverlayDuration) 
                : asset.Duration;
            
            return new TimelineClip
            {
                Id = Guid.NewGuid(),
                Name = asset.FileName,
                SourcePath = asset.FilePath,
                StartPosition = startPosition,
                Duration = duration,
                SourceStart = TimeSpan.Zero,
                SourceEnd = asset.Duration,
                Type = asset.Type == MediaType.Audio ? ClipType.Audio 
                     : asset.Type == MediaType.Image ? ClipType.Image 
                     : ClipType.Video,
                Color = asset.Type == MediaType.Audio ? "#4CAF50" : "#5B9BD5"
            };
        }

        #endregion

        private void SyncProjectToUI()
        {
            if (Project == null) return;

            MediaLibrary.Clear();
            foreach (var asset in Project.Assets)
            {
                MediaLibrary.Add(asset);
            }

            Tracks.Clear();
            foreach (var track in Project.Tracks)
            {
                Tracks.Add(track);
            }

            BeatMarkers.Clear();
            foreach (var beat in Project.BeatMarkers)
            {
                BeatMarkers.Add(beat);
            }

            Captions.Clear();
            foreach (var captionTrack in Project.CaptionTracks)
            {
                foreach (var caption in captionTrack.Captions)
                {
                    Captions.Add(caption);
                }
            }

            UpdateDuration();
        }

        private void SyncUIToProject()
        {
            if (Project == null) return;

            Project.Assets = MediaLibrary.ToList();
            Project.Tracks = Tracks.ToList();
            Project.BeatMarkers = BeatMarkers.ToList();
        }

        private void UpdateDuration()
        {
            var maxEnd = TimeSpan.Zero;
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (clip.EndPosition > maxEnd)
                        maxEnd = clip.EndPosition;
                }
            }
            Duration = maxEnd;
        }
        
        /// <summary>
        /// Finds the appropriate track index for the given media type.
        /// Video/Image goes to first Video track, Audio goes to first Audio track.
        /// </summary>
        private int FindTrackIndexForMediaType(MediaType mediaType)
        {
            if (mediaType == MediaType.Audio)
            {
                // Find first audio track
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (Tracks[i].Type == TrackType.Audio)
                        return i;
                }
            }
            else
            {
                // Video or Image - find first Video track (not Overlay for main content)
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (Tracks[i].Type == TrackType.Video)
                        return i;
                }
                // Fallback to Overlay if no Video track
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (Tracks[i].Type == TrackType.Overlay)
                        return i;
                }
            }
            return 0; // Fallback
        }

        private void UpdatePreviewFrame()
        {
            // TODO: Update preview frame at current time
            OnPropertyChanged(nameof(SelectedClipTransform));
        }

        private static string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds / 10:D2}";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _playbackTimer.Stop();
            _playbackTimer.Tick -= PlaybackTimer_Tick;
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _editorService.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// Represents a video filter that can be applied to clips.
    /// </summary>
    public class VideoFilter
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Icon { get; set; } = "🎨";
        public Dictionary<string, object> DefaultParameters { get; set; } = new();
    }
    
    /// <summary>
    /// Represents a time marker on the timeline ruler.
    /// </summary>
    public class TimelineRulerMarker
    {
        /// <summary>
        /// The time this marker represents.
        /// </summary>
        public TimeSpan Time { get; set; }
        
        /// <summary>
        /// The horizontal pixel position of the marker.
        /// </summary>
        public double Position { get; set; }
        
        /// <summary>
        /// The display label (e.g., "00:01:30.00").
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether this is a major marker (shows label) vs minor (tick only).
        /// </summary>
        public bool IsMajor { get; set; }
    }

    /// <summary>
    /// Groups filters by category for UI display.
    /// </summary>
    public class FilterCategoryGroup
    {
        public FilterCategory Category { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public ObservableCollection<Filter> Filters { get; set; } = new();
        public string Icon => Category switch
        {
            FilterCategory.ColorCorrection => "🎨",
            FilterCategory.ColorGrading => "🌈",
            FilterCategory.Blur => "💨",
            FilterCategory.Sharpen => "🔍",
            FilterCategory.Distort => "🌀",
            FilterCategory.Stylize => "✨",
            FilterCategory.Generate => "🎬",
            FilterCategory.Time => "⏱️",
            FilterCategory.Transform => "📐",
            FilterCategory.Overlay => "🔲",
            FilterCategory.AudioLevel => "🔊",
            FilterCategory.AudioEQ => "🎛️",
            FilterCategory.AudioEffect => "🔈",
            FilterCategory.AudioDynamics => "📊",
            FilterCategory.AudioSpatial => "🎧",
            _ => "🎬"
        };
    }

    /// <summary>
    /// Groups export presets by category for UI display.
    /// </summary>
    public class ExportCategoryGroup
    {
        public ExportCategory Category { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public ObservableCollection<ExportPreset> Presets { get; set; } = new();
        public string Icon => Category switch
        {
            ExportCategory.Stock => "📦",
            ExportCategory.Web => "🌐",
            ExportCategory.H264 => "🎬",
            ExportCategory.H265 => "🎥",
            ExportCategory.AV1 => "📹",
            ExportCategory.WebM => "🕸️",
            ExportCategory.ProRes => "🎞️",
            ExportCategory.DNx => "📽️",
            ExportCategory.Lossless => "💎",
            ExportCategory.Hardware => "⚡",
            ExportCategory.Animated => "🎭",
            ExportCategory.AudioOnly => "🔊",
            ExportCategory.ImageSequence => "🖼️",
            _ => "📁"
        };
    }
}
