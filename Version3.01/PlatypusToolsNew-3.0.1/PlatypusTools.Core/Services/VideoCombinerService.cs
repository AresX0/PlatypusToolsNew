using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
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
    }
}