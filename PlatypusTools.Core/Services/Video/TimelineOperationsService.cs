using System;
using System.Collections.Generic;
using System.Linq;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for timeline editing operations like Shotcut.
    /// Handles clip manipulation, ripple edits, clipboard, and snapping.
    /// </summary>
    public class TimelineOperationsService
    {
        private readonly List<TimelineClip> _clipboard = new();
        private readonly Stack<TimelineEdit> _undoStack = new();
        private readonly Stack<TimelineEdit> _redoStack = new();

        public event EventHandler? ClipboardChanged;
        public event EventHandler? HistoryChanged;

        public bool HasClipboard => _clipboard.Count > 0;
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        #region Clipboard Operations

        /// <summary>
        /// Copies clips to clipboard.
        /// </summary>
        public void Copy(IEnumerable<TimelineClip> clips)
        {
            _clipboard.Clear();
            foreach (var clip in clips)
            {
                _clipboard.Add(CloneClip(clip));
            }
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Cuts clips to clipboard.
        /// </summary>
        public void Cut(TimelineTrack track, IEnumerable<TimelineClip> clips, bool ripple = false)
        {
            var clipList = clips.ToList();
            RecordEdit(new TimelineEdit
            {
                Type = EditType.Cut,
                TrackId = track.StringId,
                ClipsBefore = clipList.Select(CloneClip).ToList()
            });

            _clipboard.Clear();
            foreach (var clip in clipList)
            {
                _clipboard.Add(CloneClip(clip));
                track.Clips.Remove(clip);
            }

            if (ripple)
            {
                RippleAfterDelete(track, clipList);
            }

            ClipboardChanged?.Invoke(this, EventArgs.Empty);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Pastes clips from clipboard.
        /// </summary>
        public List<TimelineClip> Paste(TimelineTrack track, TimeSpan insertTime, bool ripple = false)
        {
            if (_clipboard.Count == 0)
                return new List<TimelineClip>();

            // Calculate offset from first clip in clipboard
            var firstClipStart = _clipboard.Min(c => c.StartTime);
            var offset = insertTime - firstClipStart;

            var newClips = new List<TimelineClip>();

            // Ripple existing clips if needed
            if (ripple)
            {
                var totalDuration = _clipboard.Max(c => c.EndTime) - firstClipStart;
                RippleForInsert(track, insertTime, totalDuration);
            }

            foreach (var clipToCopy in _clipboard)
            {
                var newClip = CloneClip(clipToCopy);
                newClip.Id = Guid.NewGuid();
                newClip.StartPosition = clipToCopy.StartPosition + offset;
                track.Clips.Add(newClip);
                newClips.Add(newClip);
            }

            RecordEdit(new TimelineEdit
            {
                Type = EditType.Paste,
                TrackId = track.StringId,
                ClipsAfter = newClips.Select(CloneClip).ToList()
            });

            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return newClips;
        }

        /// <summary>
        /// Deletes clips from track.
        /// </summary>
        public void Delete(TimelineTrack track, IEnumerable<TimelineClip> clips, bool ripple = false)
        {
            var clipList = clips.ToList();
            RecordEdit(new TimelineEdit
            {
                Type = EditType.Delete,
                TrackId = track.StringId,
                ClipsBefore = clipList.Select(CloneClip).ToList()
            });

            foreach (var clip in clipList)
            {
                track.Clips.Remove(clip);
            }

            if (ripple)
            {
                RippleAfterDelete(track, clipList);
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Split Operations

        /// <summary>
        /// Splits a clip at the specified time.
        /// </summary>
        public (TimelineClip Left, TimelineClip Right)? SplitClip(TimelineTrack track, TimelineClip clip, TimeSpan splitTime)
        {
            if (splitTime <= clip.StartTime || splitTime >= clip.EndTime)
                return null;

            RecordEdit(new TimelineEdit
            {
                Type = EditType.Split,
                TrackId = track.StringId,
                ClipsBefore = new List<TimelineClip> { CloneClip(clip) }
            });

            // Calculate the split point within the source
            var clipProgress = (splitTime - clip.StartTime).TotalSeconds / clip.Duration.TotalSeconds;
            var sourceSplitTime = clip.SourceStart + TimeSpan.FromSeconds(clipProgress * (clip.SourceEnd - clip.SourceStart).TotalSeconds);

            // Create right portion
            var rightClip = CloneClip(clip);
            rightClip.Id = Guid.NewGuid();
            rightClip.StartPosition = splitTime;
            rightClip.SourceStart = sourceSplitTime;
            rightClip.Duration = clip.EndTime - splitTime;
            rightClip.Name = clip.Name + " (2)";

            // Modify left portion
            clip.Duration = splitTime - clip.StartTime;
            clip.SourceEnd = sourceSplitTime;

            track.Clips.Add(rightClip);

            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return (clip, rightClip);
        }

        /// <summary>
        /// Splits all clips at the playhead position.
        /// </summary>
        public List<(TimelineClip Left, TimelineClip Right)> SplitAllAtPlayhead(
            IEnumerable<TimelineTrack> tracks, TimeSpan playhead)
        {
            var results = new List<(TimelineClip Left, TimelineClip Right)>();

            foreach (var track in tracks)
            {
                var clipAtPlayhead = track.Clips
                    .FirstOrDefault(c => playhead > c.StartTime && playhead < c.EndTime);

                if (clipAtPlayhead != null)
                {
                    var result = SplitClip(track, clipAtPlayhead, playhead);
                    if (result.HasValue)
                    {
                        results.Add(result.Value);
                    }
                }
            }

            return results;
        }

        #endregion

        #region Trim Operations

        /// <summary>
        /// Trims the in-point of a clip.
        /// </summary>
        public void TrimIn(TimelineClip clip, TimeSpan newStartTime, bool ripple = false)
        {
            var delta = newStartTime - clip.StartTime;
            
            // Adjust source in point
            clip.SourceStart += delta;
            clip.StartPosition = newStartTime;
            clip.Duration -= delta;

            // Clamp to valid range
            if (clip.SourceStart < TimeSpan.Zero)
                clip.SourceStart = TimeSpan.Zero;
            if (clip.Duration < TimeSpan.FromMilliseconds(100))
                clip.Duration = TimeSpan.FromMilliseconds(100);
        }

        /// <summary>
        /// Trims the out-point of a clip.
        /// </summary>
        public void TrimOut(TimelineClip clip, TimeSpan newEndTime, bool ripple = false)
        {
            var newDuration = newEndTime - clip.StartTime;
            
            // Calculate new source out point
            var sourceDuration = (clip.SourceEnd - clip.SourceStart);
            var durationRatio = newDuration.TotalSeconds / clip.Duration.TotalSeconds;
            clip.SourceEnd = clip.SourceStart + TimeSpan.FromSeconds(sourceDuration.TotalSeconds * durationRatio);
            clip.Duration = newDuration;

            // Clamp to valid range
            if (clip.SourceEnd > clip.SourceDuration)
                clip.SourceEnd = clip.SourceDuration;
            if (clip.Duration < TimeSpan.FromMilliseconds(100))
                clip.Duration = TimeSpan.FromMilliseconds(100);
        }

        /// <summary>
        /// Performs a slip edit (changes source in/out while keeping timeline position).
        /// </summary>
        public void SlipEdit(TimelineClip clip, TimeSpan slipAmount)
        {
            var newSourceStart = clip.SourceStart + slipAmount;
            var newSourceEnd = clip.SourceEnd + slipAmount;

            // Clamp to source boundaries
            if (newSourceStart < TimeSpan.Zero)
            {
                var adjust = TimeSpan.Zero - newSourceStart;
                newSourceStart = TimeSpan.Zero;
                newSourceEnd += adjust;
            }
            if (newSourceEnd > clip.SourceDuration)
            {
                var adjust = newSourceEnd - clip.SourceDuration;
                newSourceEnd = clip.SourceDuration;
                newSourceStart -= adjust;
            }

            clip.SourceStart = TimeSpan.FromSeconds(Math.Max(0, newSourceStart.TotalSeconds));
            clip.SourceEnd = TimeSpan.FromSeconds(Math.Min(clip.SourceDuration.TotalSeconds, newSourceEnd.TotalSeconds));
        }

        /// <summary>
        /// Performs a slide edit (moves clip without affecting source, ripples neighbors).
        /// </summary>
        public void SlideEdit(TimelineTrack track, TimelineClip clip, TimeSpan slideAmount)
        {
            var newStart = clip.StartTime + slideAmount;
            var newEnd = clip.EndTime + slideAmount;

            // Find neighboring clips
            var prevClip = track.Clips
                .Where(c => c.EndTime <= clip.StartTime)
                .OrderByDescending(c => c.EndTime)
                .FirstOrDefault();

            var nextClip = track.Clips
                .Where(c => c.StartTime >= clip.EndTime)
                .OrderBy(c => c.StartTime)
                .FirstOrDefault();

            // Adjust neighbors
            if (prevClip != null && slideAmount < TimeSpan.Zero)
            {
                // Sliding left - extend previous clip
                prevClip.Duration = newStart - prevClip.StartTime;
                var trimAmount = prevClip.EndTime - prevClip.StartTime - prevClip.Duration;
                prevClip.SourceEnd -= trimAmount;
            }

            if (nextClip != null && slideAmount > TimeSpan.Zero)
            {
                // Sliding right - trim next clip
                var trimAmount = slideAmount;
                nextClip.StartPosition += trimAmount;
                nextClip.Duration -= trimAmount;
                nextClip.SourceStart += trimAmount;
            }

            clip.StartPosition = newStart;
        }

        /// <summary>
        /// Performs a ripple trim (trims and moves all subsequent clips).
        /// </summary>
        public void RippleTrim(TimelineTrack track, TimelineClip clip, TimeSpan trimAmount, bool trimIn)
        {
            if (trimIn)
            {
                TrimIn(clip, clip.StartTime + trimAmount, true);
                // Move all subsequent clips
                foreach (var c in track.Clips.Where(c => c.StartTime > clip.EndTime))
                {
                    c.StartPosition -= trimAmount;
                }
            }
            else
            {
                TrimOut(clip, clip.EndTime + trimAmount, true);
                // Move all subsequent clips
                foreach (var c in track.Clips.Where(c => c.StartTime >= clip.EndTime))
                {
                    c.StartPosition += trimAmount;
                }
            }
        }

        /// <summary>
        /// Performs a roll edit (adjusts boundary between two adjacent clips).
        /// </summary>
        public void RollEdit(TimelineClip leftClip, TimelineClip rightClip, TimeSpan rollAmount)
        {
            // Extend/trim left clip's out point
            leftClip.Duration += rollAmount;
            leftClip.SourceEnd += rollAmount;

            // Adjust right clip's in point
            rightClip.StartPosition += rollAmount;
            rightClip.Duration -= rollAmount;
            rightClip.SourceStart += rollAmount;
        }

        #endregion

        #region Ripple Operations

        /// <summary>
        /// Ripples (shifts) all clips after a position by a specified amount.
        /// </summary>
        public void Ripple(TimelineTrack track, TimeSpan afterPosition, TimeSpan amount)
        {
            var clipsToMove = track.Clips
                .Where(c => c.StartTime >= afterPosition)
                .OrderBy(c => c.StartTime)
                .ToList();

            foreach (var clip in clipsToMove)
            {
                clip.StartPosition += amount;
            }
        }

        /// <summary>
        /// Ripples after a delete operation to close gaps.
        /// </summary>
        public void RippleAfterDelete(TimelineTrack track, IEnumerable<TimelineClip> deletedClips)
        {
            var earliestDelete = deletedClips.Min(c => c.StartTime);
            var totalDuration = deletedClips.Sum(c => c.Duration.TotalSeconds);

            Ripple(track, earliestDelete, TimeSpan.FromSeconds(-totalDuration));
        }

        /// <summary>
        /// Ripples before an insert to make room.
        /// </summary>
        public void RippleForInsert(TimelineTrack track, TimeSpan insertPosition, TimeSpan insertDuration)
        {
            Ripple(track, insertPosition, insertDuration);
        }

        /// <summary>
        /// Removes gaps between clips on a track.
        /// </summary>
        public void RemoveGaps(TimelineTrack track)
        {
            var sortedClips = track.Clips.OrderBy(c => c.StartTime).ToList();
            var currentPosition = TimeSpan.Zero;

            foreach (var clip in sortedClips)
            {
                if (clip.StartTime > currentPosition)
                {
                    clip.StartPosition = currentPosition;
                }
                currentPosition = clip.EndTime;
            }
        }

        /// <summary>
        /// Ripple deletes a range from the timeline.
        /// </summary>
        public void RippleDelete(IEnumerable<TimelineTrack> tracks, TimeSpan start, TimeSpan end)
        {
            var duration = end - start;

            foreach (var track in tracks)
            {
                // Remove clips entirely within range
                var clipsToRemove = track.Clips
                    .Where(c => c.StartTime >= start && c.EndTime <= end)
                    .ToList();
                
                foreach (var clip in clipsToRemove)
                {
                    track.Clips.Remove(clip);
                }

                // Trim clips that span the boundaries
                foreach (var clip in track.Clips.ToList())
                {
                    if (clip.StartTime < start && clip.EndTime > end)
                    {
                        // Clip spans the entire range - split and remove middle
                        var rightPortion = CloneClip(clip);
                        rightPortion.Id = Guid.NewGuid();
                        rightPortion.StartPosition = start;
                        rightPortion.SourceStart += (end - clip.StartTime);
                        rightPortion.Duration = clip.EndTime - end;
                        
                        clip.Duration = start - clip.StartTime;
                        clip.SourceEnd = clip.SourceStart + clip.Duration;
                        
                        track.Clips.Add(rightPortion);
                    }
                    else if (clip.StartTime < start && clip.EndTime > start)
                    {
                        // Clip ends in the range
                        clip.Duration = start - clip.StartTime;
                    }
                    else if (clip.StartTime < end && clip.EndTime > end)
                    {
                        // Clip starts in the range
                        var trimAmount = end - clip.StartTime;
                        clip.StartPosition = start;
                        clip.SourceStart += trimAmount;
                        clip.Duration -= trimAmount;
                    }
                }

                // Ripple remaining clips
                Ripple(track, start, -duration);
            }
        }

        #endregion

        #region Snapping

        /// <summary>
        /// Finds the nearest snap point for a position.
        /// </summary>
        public TimeSpan? FindSnapPoint(
            IEnumerable<TimelineTrack> tracks,
            TimeSpan position,
            TimeSpan threshold,
            IEnumerable<TimeSpan>? additionalSnapPoints = null,
            TimelineClip? excludeClip = null)
        {
            var snapPoints = new List<TimeSpan> { TimeSpan.Zero }; // Always snap to zero

            // Add clip boundaries
            foreach (var track in tracks)
            {
                foreach (var clip in track.Clips)
                {
                    if (excludeClip != null && clip.Id == excludeClip.Id)
                        continue;

                    snapPoints.Add(clip.StartTime);
                    snapPoints.Add(clip.EndTime);
                }
            }

            // Add additional snap points (markers, playhead, etc.)
            if (additionalSnapPoints != null)
            {
                snapPoints.AddRange(additionalSnapPoints);
            }

            // Find nearest
            TimeSpan? nearest = null;
            var minDistance = threshold;

            foreach (var point in snapPoints)
            {
                var distance = TimeSpan.FromTicks(Math.Abs((position - point).Ticks));
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = point;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Snaps a clip to the nearest valid position.
        /// </summary>
        public TimeSpan SnapClipPosition(
            IEnumerable<TimelineTrack> tracks,
            TimelineClip clip,
            TimeSpan desiredStart,
            TimeSpan threshold,
            bool snapToBeats = false,
            IEnumerable<BeatMarker>? beats = null)
        {
            var snapPoints = new List<TimeSpan>();

            if (snapToBeats && beats != null)
            {
                snapPoints.AddRange(beats.Select(b => b.Time));
            }

            var snapped = FindSnapPoint(tracks, desiredStart, threshold, snapPoints, clip);
            return snapped ?? desiredStart;
        }

        #endregion

        #region Undo/Redo

        public void Undo(TimelineTrack track)
        {
            if (_undoStack.Count == 0) return;

            var edit = _undoStack.Pop();
            _redoStack.Push(edit);

            ApplyEdit(track, edit, reverse: true);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Redo(TimelineTrack track)
        {
            if (_redoStack.Count == 0) return;

            var edit = _redoStack.Pop();
            _undoStack.Push(edit);

            ApplyEdit(track, edit, reverse: false);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearHistory()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RecordEdit(TimelineEdit edit)
        {
            _undoStack.Push(edit);
            _redoStack.Clear();
        }

        private void ApplyEdit(TimelineTrack track, TimelineEdit edit, bool reverse)
        {
            switch (edit.Type)
            {
                case EditType.Delete:
                case EditType.Cut:
                    if (reverse)
                    {
                        // Undo delete - add clips back
                        foreach (var clip in edit.ClipsBefore)
                        {
                            track.Clips.Add(CloneClip(clip));
                        }
                    }
                    break;

                case EditType.Paste:
                    if (reverse)
                    {
                        // Undo paste - remove clips
                        foreach (var clip in edit.ClipsAfter)
                        {
                            var existing = track.Clips.FirstOrDefault(c => c.StringId == clip.StringId);
                            if (existing != null)
                            {
                                track.Clips.Remove(existing);
                            }
                        }
                    }
                    break;

                case EditType.Split:
                    // More complex - would need to merge clips back
                    break;
            }
        }

        #endregion

        #region Helpers

        private TimelineClip CloneClip(TimelineClip clip)
        {
            return new TimelineClip
            {
                Id = clip.Id,
                Name = clip.Name,
                SourcePath = clip.SourcePath,
                Type = clip.Type,
                StartPosition = clip.StartPosition,
                Duration = clip.Duration,
                SourceStart = clip.SourceStart,
                SourceEnd = clip.SourceEnd,
                SourceDuration = clip.SourceDuration,
                Speed = clip.Speed,
                Volume = clip.Volume,
                Opacity = clip.Opacity,
                IsMuted = clip.IsMuted,
                Filters = clip.Filters?.ToList() ?? new List<Filter>(),
                TransformKeyframes = clip.TransformKeyframes?.ToList() ?? new List<KeyframeTrack>()
            };
        }

        #endregion
    }

    /// <summary>
    /// Represents an edit operation for undo/redo.
    /// </summary>
    public class TimelineEdit
    {
        public EditType Type { get; set; }
        public string TrackId { get; set; } = string.Empty;
        public List<TimelineClip> ClipsBefore { get; set; } = new();
        public List<TimelineClip> ClipsAfter { get; set; } = new();
        public TimeSpan? Position { get; set; }
    }

    /// <summary>
    /// Types of timeline edits.
    /// </summary>
    public enum EditType
    {
        Insert,
        Delete,
        Cut,
        Copy,
        Paste,
        Split,
        Trim,
        Move,
        Ripple
    }
}
