using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for comparing two files and producing a diff result.
    /// Supports text diff (line-by-line) and binary diff (byte comparison).
    /// </summary>
    public class FileDiffService
    {
        public enum DiffLineType { Unchanged, Added, Removed, Modified }

        public class DiffLine
        {
            public int? LeftLineNumber { get; set; }
            public int? RightLineNumber { get; set; }
            public string LeftText { get; set; } = "";
            public string RightText { get; set; } = "";
            public DiffLineType Type { get; set; }
        }

        public class DiffResult
        {
            public string LeftFile { get; set; } = "";
            public string RightFile { get; set; } = "";
            public long LeftSize { get; set; }
            public long RightSize { get; set; }
            public bool IsBinary { get; set; }
            public bool AreIdentical { get; set; }
            public List<DiffLine> Lines { get; set; } = new();
            public int AddedCount { get; set; }
            public int RemovedCount { get; set; }
            public int ModifiedCount { get; set; }
            public int UnchangedCount { get; set; }
            public TimeSpan Duration { get; set; }
        }

        /// <summary>
        /// Compare two files and return a diff result.
        /// </summary>
        public async Task<DiffResult> CompareFilesAsync(string leftPath, string rightPath)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new DiffResult
            {
                LeftFile = leftPath,
                RightFile = rightPath
            };

            if (!File.Exists(leftPath))
                throw new FileNotFoundException("Left file not found", leftPath);
            if (!File.Exists(rightPath))
                throw new FileNotFoundException("Right file not found", rightPath);

            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);
            result.LeftSize = leftInfo.Length;
            result.RightSize = rightInfo.Length;

            // Check if files are binary
            result.IsBinary = IsBinaryFile(leftPath) || IsBinaryFile(rightPath);

            if (result.IsBinary)
            {
                // Simple byte comparison for binary files
                var leftBytes = await File.ReadAllBytesAsync(leftPath);
                var rightBytes = await File.ReadAllBytesAsync(rightPath);
                result.AreIdentical = leftBytes.SequenceEqual(rightBytes);
                if (!result.AreIdentical)
                {
                    result.ModifiedCount = 1;
                    result.Lines.Add(new DiffLine
                    {
                        Type = DiffLineType.Modified,
                        LeftText = $"[Binary file: {result.LeftSize:N0} bytes]",
                        RightText = $"[Binary file: {result.RightSize:N0} bytes]"
                    });
                }
            }
            else
            {
                var leftLines = await File.ReadAllLinesAsync(leftPath);
                var rightLines = await File.ReadAllLinesAsync(rightPath);
                result.Lines = ComputeLCS(leftLines, rightLines);
                result.AreIdentical = result.Lines.All(l => l.Type == DiffLineType.Unchanged);
                result.AddedCount = result.Lines.Count(l => l.Type == DiffLineType.Added);
                result.RemovedCount = result.Lines.Count(l => l.Type == DiffLineType.Removed);
                result.UnchangedCount = result.Lines.Count(l => l.Type == DiffLineType.Unchanged);
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            return result;
        }

        /// <summary>
        /// LCS-based diff algorithm for text lines.
        /// </summary>
        private List<DiffLine> ComputeLCS(string[] left, string[] right)
        {
            int m = left.Length, n = right.Length;
            // Build LCS table
            var dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = left[i - 1] == right[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            // Backtrack to produce diff
            var result = new List<DiffLine>();
            int li = m, ri = n;
            var stack = new Stack<DiffLine>();

            while (li > 0 || ri > 0)
            {
                if (li > 0 && ri > 0 && left[li - 1] == right[ri - 1])
                {
                    stack.Push(new DiffLine { LeftLineNumber = li, RightLineNumber = ri, LeftText = left[li - 1], RightText = right[ri - 1], Type = DiffLineType.Unchanged });
                    li--; ri--;
                }
                else if (ri > 0 && (li == 0 || dp[li, ri - 1] >= dp[li - 1, ri]))
                {
                    stack.Push(new DiffLine { RightLineNumber = ri, RightText = right[ri - 1], Type = DiffLineType.Added });
                    ri--;
                }
                else
                {
                    stack.Push(new DiffLine { LeftLineNumber = li, LeftText = left[li - 1], Type = DiffLineType.Removed });
                    li--;
                }
            }

            while (stack.Count > 0)
                result.Add(stack.Pop());

            return result;
        }

        private static bool IsBinaryFile(string path)
        {
            try
            {
                var buffer = new byte[8192];
                using var fs = File.OpenRead(path);
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0) return true; // null byte = binary
                }
                return false;
            }
            catch { return false; }
        }
    }
}
