using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the video editor timeline.
    /// Manages tracks, clips, playhead position, and timeline operations.
    /// Uses async initialization to defer transition loading.
    /// </summary>
    public class TimelineViewModel : AsyncBindableBase
    {
        private int _nextTrackNumber = 1;

        public TimelineViewModel()
        {
            Tracks = new ObservableCollection<TimelineTrack>();
            AvailableTransitions = new ObservableCollection<Transition>();
            
            // Commands
            AddVideoTrackCommand = new RelayCommand(_ => AddTrack(TrackType.Video));
            AddAudioTrackCommand = new RelayCommand(_ => AddTrack(TrackType.Audio));
            AddTitleTrackCommand = new RelayCommand(_ => AddTrack(TrackType.Title));
            RemoveTrackCommand = new RelayCommand(p => RemoveTrack(p as TimelineTrack), _ => true);
            
            PlayCommand = new RelayCommand(_ => Play(), _ => !IsPlaying);
            PauseCommand = new RelayCommand(_ => Pause(), _ => IsPlaying);
            StopCommand = new RelayCommand(_ => Stop());
            
            SplitClipCommand = new RelayCommand(_ => SplitSelectedClip(), _ => SelectedClip != null);
            DeleteClipCommand = new RelayCommand(_ => DeleteSelectedClip(), _ => SelectedClip != null);
            
            ZoomInCommand = new RelayCommand(_ => ZoomIn(), _ => ZoomLevel < MaxZoom);
            ZoomOutCommand = new RelayCommand(_ => ZoomOut(), _ => ZoomLevel > MinZoom);
            FitToWindowCommand = new RelayCommand(_ => FitToWindow());
            
            UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
            RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
            
            // Deferred initialization - transitions and tracks loaded when view is shown
        }

        /// <summary>
        /// Async initialization - loads transitions and default tracks.
        /// </summary>
        protected override Task OnInitializeAsync()
        {
            LoadTransitions();
            AddTrack(TrackType.Video);
            AddTrack(TrackType.Audio);
            return Task.CompletedTask;
        }

        #region Properties

        /// <summary>
        /// Collection of timeline tracks.
        /// </summary>
        public ObservableCollection<TimelineTrack> Tracks { get; }

        /// <summary>
        /// Available transitions for the library.
        /// </summary>
        public ObservableCollection<Transition> AvailableTransitions { get; }

        private TimelineTrack? _selectedTrack;
        /// <summary>
        /// Currently selected track.
        /// </summary>
        public TimelineTrack? SelectedTrack
        {
            get => _selectedTrack;
            set => SetProperty(ref _selectedTrack, value);
        }

        private TimelineClip? _selectedClip;
        /// <summary>
        /// Currently selected clip.
        /// </summary>
        public TimelineClip? SelectedClip
        {
            get => _selectedClip;
            set
            {
                // Deselect previous
                if (_selectedClip != null)
                    _selectedClip.IsSelected = false;
                
                SetProperty(ref _selectedClip, value);
                
                // Select new
                if (_selectedClip != null)
                    _selectedClip.IsSelected = true;
                
                RaiseCommandsCanExecuteChanged();
            }
        }

        private TimeSpan _duration = TimeSpan.FromMinutes(5);
        /// <summary>
        /// Total duration of the timeline.
        /// </summary>
        public TimeSpan Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        private TimeSpan _playheadPosition;
        /// <summary>
        /// Current playhead position.
        /// </summary>
        public TimeSpan PlayheadPosition
        {
            get => _playheadPosition;
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > Duration) value = Duration;
                SetProperty(ref _playheadPosition, value);
            }
        }

        private double _zoomLevel = 1.0;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 10.0;
        private const double BasePixelsPerSecond = 50.0;
        
        /// <summary>
        /// Zoom level (1.0 = default, higher = more zoomed in).
        /// </summary>
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                value = Math.Clamp(value, MinZoom, MaxZoom);
                SetProperty(ref _zoomLevel, value);
                OnPropertyChanged(nameof(PixelsPerSecond));
            }
        }

        /// <summary>
        /// Computed pixels per second based on zoom level.
        /// Used for clip positioning and width calculations.
        /// </summary>
        public double PixelsPerSecond => BasePixelsPerSecond * ZoomLevel;

        private bool _isPlaying;
        /// <summary>
        /// Whether playback is active.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                SetProperty(ref _isPlaying, value);
                RaiseCommandsCanExecuteChanged();
            }
        }

        private bool _snapToGrid = true;
        /// <summary>
        /// Whether clips snap to grid/markers.
        /// </summary>
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => SetProperty(ref _snapToGrid, value);
        }

        private bool _rippleEdit;
        /// <summary>
        /// Whether edits ripple to subsequent clips.
        /// </summary>
        public bool RippleEdit
        {
            get => _rippleEdit;
            set => SetProperty(ref _rippleEdit, value);
        }

        private TimeSpan _gridInterval = TimeSpan.FromSeconds(1);
        /// <summary>
        /// Grid interval for snapping.
        /// </summary>
        public TimeSpan GridInterval
        {
            get => _gridInterval;
            set => SetProperty(ref _gridInterval, value);
        }

        #endregion

        #region Commands

        public ICommand AddVideoTrackCommand { get; }
        public ICommand AddAudioTrackCommand { get; }
        public ICommand AddTitleTrackCommand { get; }
        public ICommand RemoveTrackCommand { get; }
        
        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        
        public ICommand SplitClipCommand { get; }
        public ICommand DeleteClipCommand { get; }
        
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand FitToWindowCommand { get; }
        
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        #endregion

        #region Track Operations

        /// <summary>
        /// Adds a new track of the specified type.
        /// </summary>
        public void AddTrack(TrackType type)
        {
            var track = new TimelineTrack
            {
                Type = type,
                Name = $"{type} {_nextTrackNumber++}",
                Order = Tracks.Count,
                Color = GetTrackColor(type)
            };
            Tracks.Add(track);
            SelectedTrack = track;
        }

        /// <summary>
        /// Removes a track from the timeline.
        /// </summary>
        public void RemoveTrack(TimelineTrack? track)
        {
            if (track == null) return;
            
            SaveUndoState();
            Tracks.Remove(track);
            
            // Reorder remaining tracks
            for (int i = 0; i < Tracks.Count; i++)
                Tracks[i].Order = i;
            
            if (SelectedTrack == track)
                SelectedTrack = Tracks.FirstOrDefault();
        }

        /// <summary>
        /// Moves a track to a new position.
        /// </summary>
        public void MoveTrack(TimelineTrack track, int newIndex)
        {
            SaveUndoState();
            var oldIndex = Tracks.IndexOf(track);
            if (oldIndex < 0 || newIndex < 0 || newIndex >= Tracks.Count) return;
            
            Tracks.Move(oldIndex, newIndex);
            
            // Update order
            for (int i = 0; i < Tracks.Count; i++)
                Tracks[i].Order = i;
        }

        private string GetTrackColor(TrackType type) => type switch
        {
            TrackType.Video => "#4A90D9",
            TrackType.Audio => "#50C878",
            TrackType.Title => "#9B59B6",
            TrackType.Effects => "#F39C12",
            TrackType.Adjustment => "#E74C3C",
            _ => "#95A5A6"
        };

        #endregion

        #region Clip Operations

        /// <summary>
        /// Adds a clip to a track at the specified position.
        /// </summary>
        public void AddClip(TimelineTrack track, string filePath, TimeSpan position, TimeSpan duration)
        {
            SaveUndoState();
            
            var clip = new TimelineClip
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                SourcePath = filePath,
                StartPosition = SnapToGrid ? SnapTime(position) : position,
                Duration = duration,
                SourceDuration = duration,
                SourceEnd = duration,
                Type = GetClipType(track.Type)
            };
            
            track.Clips.Add(clip);
            SelectedClip = clip;
            
            // Extend timeline if needed
            if (clip.EndPosition > Duration)
                Duration = clip.EndPosition + TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Splits the selected clip at the playhead position.
        /// </summary>
        public void SplitSelectedClip()
        {
            if (SelectedClip == null) return;
            SplitClip(SelectedClip, PlayheadPosition);
        }

        /// <summary>
        /// Splits a clip at the specified position.
        /// </summary>
        public void SplitClip(TimelineClip clip, TimeSpan position)
        {
            if (position <= clip.StartPosition || position >= clip.EndPosition)
                return;
            
            SaveUndoState();
            
            // Find the track containing this clip
            var track = Tracks.FirstOrDefault(t => t.Clips.Contains(clip));
            if (track == null) return;
            
            var splitPoint = position - clip.StartPosition;
            
            // Create second half
            var newClip = new TimelineClip
            {
                Name = clip.Name + " (2)",
                SourcePath = clip.SourcePath,
                Type = clip.Type,
                StartPosition = position,
                Duration = clip.Duration - splitPoint,
                SourceStart = clip.SourceStart + splitPoint,
                SourceEnd = clip.SourceEnd,
                SourceDuration = clip.SourceDuration,
                Volume = clip.Volume,
                Opacity = clip.Opacity,
                Speed = clip.Speed,
                Color = clip.Color
            };
            
            // Trim first half
            clip.Duration = splitPoint;
            clip.SourceEnd = clip.SourceStart + splitPoint;
            
            track.Clips.Add(newClip);
        }

        /// <summary>
        /// Deletes the selected clip.
        /// </summary>
        public void DeleteSelectedClip()
        {
            if (SelectedClip == null) return;
            
            SaveUndoState();
            
            var track = Tracks.FirstOrDefault(t => t.Clips.Contains(SelectedClip));
            track?.Clips.Remove(SelectedClip);
            SelectedClip = null;
        }

        /// <summary>
        /// Trims a clip's in/out points.
        /// </summary>
        public void TrimClip(TimelineClip clip, TimeSpan newStart, TimeSpan newDuration)
        {
            SaveUndoState();
            
            var deltaStart = newStart - clip.StartPosition;
            clip.StartPosition = SnapToGrid ? SnapTime(newStart) : newStart;
            clip.SourceStart += deltaStart;
            clip.Duration = newDuration;
            clip.SourceEnd = clip.SourceStart + newDuration;
        }

        private ClipType GetClipType(TrackType trackType) => trackType switch
        {
            TrackType.Video => ClipType.Video,
            TrackType.Audio => ClipType.Audio,
            TrackType.Title => ClipType.Title,
            _ => ClipType.Video
        };

        #endregion

        #region Playback

        public void Play()
        {
            IsPlaying = true;
            // Playback timer would be implemented here
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        public void Stop()
        {
            IsPlaying = false;
            PlayheadPosition = TimeSpan.Zero;
        }

        #endregion

        #region Zoom & View

        public void ZoomIn()
        {
            ZoomLevel *= 1.25;
        }

        public void ZoomOut()
        {
            ZoomLevel /= 1.25;
        }

        public void FitToWindow()
        {
            // Would calculate based on window width
            ZoomLevel = 1.0;
        }

        #endregion

        #region Undo/Redo

        private readonly System.Collections.Generic.Stack<TimelineState> _undoStack = new();
        private readonly System.Collections.Generic.Stack<TimelineState> _redoStack = new();

        private void SaveUndoState()
        {
            // Simplified state saving - would serialize full timeline state
            _undoStack.Push(new TimelineState { Timestamp = DateTime.Now });
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(_undoStack.Pop());
            RaiseCommandsCanExecuteChanged();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(_redoStack.Pop());
            RaiseCommandsCanExecuteChanged();
        }

        private class TimelineState
        {
            public DateTime Timestamp { get; set; }
            // Would contain serialized tracks/clips
        }

        #endregion

        #region Transitions

        private void LoadTransitions()
        {
            var allTransitions = TransitionFactory.GetAllTransitions();
            foreach (var category in allTransitions.Values)
            {
                foreach (var transition in category)
                {
                    AvailableTransitions.Add(transition);
                }
            }
        }

        /// <summary>
        /// Applies a transition between two clips.
        /// </summary>
        public void ApplyTransition(TimelineClip clipA, TimelineClip clipB, Transition transition)
        {
            SaveUndoState();
            clipA.TransitionOut = transition;
            clipB.TransitionIn = transition;
        }

        #endregion

        #region Helpers

        private TimeSpan SnapTime(TimeSpan time)
        {
            var ticks = time.Ticks;
            var intervalTicks = GridInterval.Ticks;
            var snapped = (ticks / intervalTicks) * intervalTicks;
            return TimeSpan.FromTicks(snapped);
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (PlayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PauseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SplitClipCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteClipCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ZoomInCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ZoomOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion
    }
}
