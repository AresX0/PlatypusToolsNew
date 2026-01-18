using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    public class VideoCombinerService
    {
        public VideoCombinerService()
        {
        }

        public virtual async Task<FFmpegResult> CombineAsync(IEnumerable<string> inputFiles, string outputFile, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            // Create concat file
            var sb = new StringBuilder();
            foreach (var f in inputFiles)
            {
                sb.AppendLine($"file '{f.Replace("'", "'\\''")}'");
            }

            var tempList = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempList, sb.ToString(), cancellationToken);

            // Ask ffmpeg to emit progress information to stdout using the progress pipe
            var args = $"-f concat -safe 0 -i \"{tempList}\" -c copy -progress pipe:1 -nostats \"{outputFile}\"";
            try
            {
                var result = await FFmpegService.RunAsync(args, null, progress, cancellationToken);
                return result;
            }
            finally
            {
                try { File.Delete(tempList); } catch { }
            }
        }

        /// <summary>
        /// Combines videos with transitions between clips. Requires re-encoding.
        /// </summary>
        public virtual async Task<FFmpegResult> CombineWithTransitionsAsync(
            IEnumerable<string> inputFiles, 
            string outputFile, 
            string transitionType,
            double transitionDurationSeconds,
            IProgress<string>? progress = null, 
            CancellationToken cancellationToken = default)
        {
            var files = inputFiles.ToList();
            if (files.Count == 0)
                return new FFmpegResult { ExitCode = -1, StdErr = "No input files" };

            if (files.Count == 1)
            {
                // Single file, just copy
                return await CombineAsync(files, outputFile, progress, cancellationToken);
            }

            // Get durations for each input
            var durations = new List<double>();
            foreach (var f in files)
            {
                var dur = await FFprobeService.GetDurationSecondsAsync(f);
                durations.Add(dur > 0 ? dur : 10); // Default 10s if unknown
            }

            // Build complex filter for transitions
            var filter = BuildTransitionFilter(files.Count, durations, transitionType, transitionDurationSeconds);
            
            // Build FFmpeg command with inputs and complex filter
            var inputArgs = new StringBuilder();
            foreach (var f in files)
            {
                inputArgs.Append($"-i \"{f}\" ");
            }

            var args = $"{inputArgs}-filter_complex \"{filter}\" -map \"[outv]\" -map \"[outa]\" -c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -progress pipe:1 -nostats \"{outputFile}\"";
            
            return await FFmpegService.RunAsync(args, null, progress, cancellationToken);
        }

        private string BuildTransitionFilter(int fileCount, List<double> durations, string transitionType, double transitionDuration)
        {
            var sb = new StringBuilder();
            var transFilter = GetFFmpegTransitionFilter(transitionType);
            
            // For xfade transitions, we need to chain them progressively
            // Each video needs to have matching format first
            for (int i = 0; i < fileCount; i++)
            {
                sb.Append($"[{i}:v]scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30[v{i}];");
                sb.Append($"[{i}:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[a{i}];");
            }

            // Chain video transitions
            if (fileCount == 2)
            {
                // Simple case: two videos
                var offset = Math.Max(0, durations[0] - transitionDuration);
                sb.Append($"[v0][v1]xfade=transition={transFilter}:duration={transitionDuration}:offset={offset:F3}[outv];");
                sb.Append($"[a0][a1]acrossfade=d={transitionDuration}[outa]");
            }
            else
            {
                // Multiple videos: chain transitions
                double currentOffset = 0;
                string lastVideo = "v0";
                string lastAudio = "a0";

                for (int i = 1; i < fileCount; i++)
                {
                    currentOffset += durations[i - 1] - transitionDuration;
                    if (currentOffset < 0) currentOffset = 0;

                    var outLabel = i == fileCount - 1 ? "outv" : $"vt{i}";
                    var outAudioLabel = i == fileCount - 1 ? "outa" : $"at{i}";

                    sb.Append($"[{lastVideo}][v{i}]xfade=transition={transFilter}:duration={transitionDuration}:offset={currentOffset:F3}[{outLabel}];");
                    sb.Append($"[{lastAudio}][a{i}]acrossfade=d={transitionDuration}[{outAudioLabel}]");
                    
                    if (i < fileCount - 1)
                    {
                        sb.Append(";");
                        lastVideo = $"vt{i}";
                        lastAudio = $"at{i}";
                        currentOffset = 0; // Reset for chained calculation
                    }
                }
            }

            return sb.ToString();
        }

        private string GetFFmpegTransitionFilter(string transitionType)
        {
            return transitionType switch
            {
                "Cross Dissolve" => "fade",
                "Fade to Black" => "fadeblack",
                "Fade to White" => "fadewhite",
                "Wipe Left" => "wipeleft",
                "Wipe Right" => "wiperight",
                "Wipe Up" => "wipeup",
                "Wipe Down" => "wipedown",
                "Slide Left" => "slideleft",
                "Slide Right" => "slideright",
                "Slide Up" => "slideup",
                "Slide Down" => "slidedown",
                "Circle Open" => "circleopen",
                "Circle Close" => "circleclose",
                "Dissolve" => "dissolve",
                "Pixelize" => "pixelize",
                "Radial" => "radial",
                _ => "fade"
            };
        }
    }
}