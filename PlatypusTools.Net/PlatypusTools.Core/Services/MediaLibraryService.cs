using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public class MediaLibraryService
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".m4a", ".wma", ".ogg", ".opus"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff"
        };

        public async Task<List<MediaItem>> ScanDirectoryAsync(string path, bool includeSubdirectories = true)
        {
            var items = new List<MediaItem>();

            await Task.Run(() =>
            {
                try
                {
                    var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var files = Directory.GetFiles(path, "*.*", searchOption);

                    foreach (var file in files)
                    {
                        try
                        {
                            var ext = Path.GetExtension(file);
                            var fileInfo = new FileInfo(file);

                            MediaType type = MediaType.Unknown;
                            if (VideoExtensions.Contains(ext)) type = MediaType.Video;
                            else if (AudioExtensions.Contains(ext)) type = MediaType.Audio;
                            else if (ImageExtensions.Contains(ext)) type = MediaType.Image;
                            else continue;

                            var item = new MediaItem
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                Type = type,
                                Size = fileInfo.Length,
                                DateAdded = fileInfo.CreationTime,
                                DateModified = fileInfo.LastWriteTime
                            };

                            items.Add(item);
                        }
                        catch { }
                    }
                }
                catch { }
            });

            return items;
        }

        public async Task<MediaMetadata?> GetMetadataAsync(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext))
            {
                return await GetFFprobeMetadataAsync(filePath);
            }
            else if (ImageExtensions.Contains(ext))
            {
                return GetImageMetadata(filePath);
            }

            return null;
        }

        private async Task<MediaMetadata?> GetFFprobeMetadataAsync(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var json = JsonDocument.Parse(output);
                var format = json.RootElement.GetProperty("format");

                var metadata = new MediaMetadata
                {
                    Duration = format.TryGetProperty("duration", out var dur) ? TimeSpan.FromSeconds(double.Parse(dur.GetString() ?? "0")) : TimeSpan.Zero,
                    Bitrate = format.TryGetProperty("bit_rate", out var br) ? long.Parse(br.GetString() ?? "0") : 0,
                    Format = format.TryGetProperty("format_name", out var fmt) ? fmt.GetString() : null
                };

                if (json.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var codecType))
                        {
                            var type = codecType.GetString();
                            if (type == "video")
                            {
                                metadata.Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                                metadata.Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                                metadata.VideoCodec = stream.TryGetProperty("codec_name", out var vc) ? vc.GetString() : null;
                                metadata.FrameRate = stream.TryGetProperty("r_frame_rate", out var fr) ? fr.GetString() : null;
                            }
                            else if (type == "audio")
                            {
                                metadata.AudioCodec = stream.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null;
                                metadata.SampleRate = stream.TryGetProperty("sample_rate", out var sr) ? int.Parse(sr.GetString() ?? "0") : 0;
                                metadata.Channels = stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
                            }
                        }
                    }
                }

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private MediaMetadata? GetImageMetadata(string filePath)
        {
            try
            {
                using var img = System.Drawing.Image.FromFile(filePath);
                return new MediaMetadata
                {
                    Width = img.Width,
                    Height = img.Height,
                    Format = img.RawFormat.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        public List<MediaItem> FilterByType(List<MediaItem> items, MediaType type)
        {
            return items.Where(i => i.Type == type).ToList();
        }

        public List<MediaItem> SortBySize(List<MediaItem> items, bool descending = true)
        {
            return descending ? items.OrderByDescending(i => i.Size).ToList() : items.OrderBy(i => i.Size).ToList();
        }

        public List<MediaItem> SortByDate(List<MediaItem> items, bool descending = true)
        {
            return descending ? items.OrderByDescending(i => i.DateModified).ToList() : items.OrderBy(i => i.DateModified).ToList();
        }
    }

    public class MediaItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateModified { get; set; }
        public MediaMetadata? Metadata { get; set; }
    }

    public class MediaMetadata
    {
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bitrate { get; set; }
        public string? Format { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public string? FrameRate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
    }

    public enum MediaType
    {
        Unknown,
        Video,
        Audio,
        Image
    }
}
