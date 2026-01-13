using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Native .NET metadata reader that doesn't require ExifTool.
    /// Supports images (JPEG, PNG, GIF, TIFF, RAW formats) and videos (MP4, MOV, AVI).
    /// </summary>
    public static class NativeMetadataReader
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".tiff", ".tif", ".bmp",
            ".heic", ".heif", ".webp", ".cr2", ".nef", ".arw", ".dng", ".orf", ".rw2"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm", ".m4v", ".3gp"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma", ".aiff"
        };

        /// <summary>
        /// Reads metadata from a file using native .NET libraries.
        /// </summary>
        public static Dictionary<string, string> ReadMetadata(string filePath)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(filePath))
                return metadata;

            var extension = Path.GetExtension(filePath);

            try
            {
                if (ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension))
                {
                    ReadWithMetadataExtractor(filePath, metadata);
                }

                if (AudioExtensions.Contains(extension) || VideoExtensions.Contains(extension))
                {
                    ReadWithTagLib(filePath, metadata);
                }

                // Add basic file info
                AddFileInfo(filePath, metadata);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeMetadataReader] Error reading {filePath}: {ex.Message}");
            }

            return metadata;
        }

        private static void ReadWithMetadataExtractor(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);

                foreach (var directory in directories)
                {
                    var groupName = directory.Name;

                    foreach (var tag in directory.Tags)
                    {
                        if (tag.Description == null) continue;

                        // Format key as [Group] TagName for consistency with ExifTool output
                        var key = $"[{groupName}] {tag.Name}";
                        
                        // Also add without group for simpler access
                        var simpleKey = tag.Name;

                        if (!metadata.ContainsKey(key))
                            metadata[key] = tag.Description;
                        
                        if (!metadata.ContainsKey(simpleKey))
                            metadata[simpleKey] = tag.Description;
                    }

                    // Special handling for common EXIF tags
                    if (directory is ExifSubIfdDirectory exifDir)
                    {
                        AddExifTag(metadata, exifDir, ExifDirectoryBase.TagDateTimeOriginal, "DateTimeOriginal");
                        AddExifTag(metadata, exifDir, ExifDirectoryBase.TagExposureTime, "ExposureTime");
                        AddExifTag(metadata, exifDir, ExifDirectoryBase.TagFNumber, "FNumber");
                        AddExifTag(metadata, exifDir, ExifDirectoryBase.TagIsoEquivalent, "ISO");
                        AddExifTag(metadata, exifDir, ExifDirectoryBase.TagFocalLength, "FocalLength");
                    }

                    if (directory is ExifIfd0Directory ifd0Dir)
                    {
                        AddExifTag(metadata, ifd0Dir, ExifDirectoryBase.TagMake, "Make");
                        AddExifTag(metadata, ifd0Dir, ExifDirectoryBase.TagModel, "Model");
                        AddExifTag(metadata, ifd0Dir, ExifDirectoryBase.TagSoftware, "Software");
                        AddExifTag(metadata, ifd0Dir, ExifDirectoryBase.TagArtist, "Artist");
                        AddExifTag(metadata, ifd0Dir, ExifDirectoryBase.TagCopyright, "Copyright");
                    }

                    // IPTC data
                    if (directory is IptcDirectory iptcDir)
                    {
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagCaption, "Caption");
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagByLine, "Author");
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagCopyrightNotice, "Copyright");
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagKeywords, "Keywords");
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagCity, "City");
                        AddIptcTag(metadata, iptcDir, IptcDirectory.TagCountryOrPrimaryLocationName, "Country");
                    }

                    // GPS data
                    if (directory is GpsDirectory gpsDir)
                    {
                        var location = gpsDir.GetGeoLocation();
                        if (location != null)
                        {
                            metadata["GPSLatitude"] = location.Latitude.ToString("F6");
                            metadata["GPSLongitude"] = location.Longitude.ToString("F6");
                            metadata["GPSPosition"] = $"{location.Latitude:F6}, {location.Longitude:F6}";
                        }
                    }

                    // XMP data
                    if (directory is XmpDirectory xmpDir)
                    {
                        var xmpMeta = xmpDir.GetXmpProperties();
                        if (xmpMeta != null)
                        {
                            foreach (var prop in xmpMeta)
                            {
                                var xmpKey = $"[XMP] {prop.Key}";
                                if (!metadata.ContainsKey(xmpKey))
                                    metadata[xmpKey] = prop.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeMetadataReader] MetadataExtractor error: {ex.Message}");
            }
        }

        private static void ReadWithTagLib(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);

                // Basic audio/video properties
                if (file.Properties != null)
                {
                    if (file.Properties.Duration.TotalSeconds > 0)
                        metadata["Duration"] = file.Properties.Duration.ToString(@"hh\:mm\:ss\.fff");

                    if (file.Properties.AudioBitrate > 0)
                        metadata["AudioBitrate"] = $"{file.Properties.AudioBitrate} kbps";

                    if (file.Properties.AudioSampleRate > 0)
                        metadata["AudioSampleRate"] = $"{file.Properties.AudioSampleRate} Hz";

                    if (file.Properties.AudioChannels > 0)
                        metadata["AudioChannels"] = file.Properties.AudioChannels.ToString();

                    if (file.Properties.VideoWidth > 0 && file.Properties.VideoHeight > 0)
                    {
                        metadata["ImageWidth"] = file.Properties.VideoWidth.ToString();
                        metadata["ImageHeight"] = file.Properties.VideoHeight.ToString();
                        metadata["ImageSize"] = $"{file.Properties.VideoWidth}x{file.Properties.VideoHeight}";
                    }

                    if (file.Properties.BitsPerSample > 0)
                        metadata["BitsPerSample"] = file.Properties.BitsPerSample.ToString();

                    // Codec info
                    var codecs = file.Properties.Codecs?.Where(c => c != null).ToList();
                    if (codecs != null && codecs.Count > 0)
                    {
                        var videoCodec = codecs.FirstOrDefault(c => c is TagLib.IVideoCodec);
                        var audioCodec = codecs.FirstOrDefault(c => c is TagLib.IAudioCodec);

                        if (videoCodec != null)
                            metadata["VideoCodec"] = videoCodec.Description ?? "Unknown";
                        if (audioCodec != null)
                            metadata["AudioCodec"] = audioCodec.Description ?? "Unknown";
                    }
                }

                // Tag information
                var tag = file.Tag;
                if (tag != null)
                {
                    if (!string.IsNullOrEmpty(tag.Title))
                        metadata["Title"] = tag.Title;

                    if (tag.Performers?.Length > 0)
                        metadata["Artist"] = string.Join("; ", tag.Performers);

                    if (tag.AlbumArtists?.Length > 0)
                        metadata["AlbumArtist"] = string.Join("; ", tag.AlbumArtists);

                    if (!string.IsNullOrEmpty(tag.Album))
                        metadata["Album"] = tag.Album;

                    if (tag.Year > 0)
                        metadata["Year"] = tag.Year.ToString();

                    if (tag.Track > 0)
                        metadata["Track"] = tag.Track.ToString();

                    if (tag.TrackCount > 0)
                        metadata["TrackCount"] = tag.TrackCount.ToString();

                    if (tag.Disc > 0)
                        metadata["Disc"] = tag.Disc.ToString();

                    if (tag.Genres?.Length > 0)
                        metadata["Genre"] = string.Join("; ", tag.Genres);

                    if (!string.IsNullOrEmpty(tag.Comment))
                        metadata["Comment"] = tag.Comment;

                    if (!string.IsNullOrEmpty(tag.Copyright))
                        metadata["Copyright"] = tag.Copyright;

                    if (tag.Composers?.Length > 0)
                        metadata["Composer"] = string.Join("; ", tag.Composers);

                    if (!string.IsNullOrEmpty(tag.Conductor))
                        metadata["Conductor"] = tag.Conductor;

                    if (!string.IsNullOrEmpty(tag.Description))
                        metadata["Description"] = tag.Description;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NativeMetadataReader] TagLib error: {ex.Message}");
            }
        }

        private static void AddFileInfo(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                
                if (!metadata.ContainsKey("FileName"))
                    metadata["FileName"] = fileInfo.Name;
                
                if (!metadata.ContainsKey("Directory"))
                    metadata["Directory"] = fileInfo.DirectoryName ?? "";
                
                if (!metadata.ContainsKey("FileSize"))
                    metadata["FileSize"] = FormatFileSize(fileInfo.Length);
                
                if (!metadata.ContainsKey("FileModifyDate"))
                    metadata["FileModifyDate"] = fileInfo.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss");
                
                if (!metadata.ContainsKey("FileCreateDate"))
                    metadata["FileCreateDate"] = fileInfo.CreationTime.ToString("yyyy:MM:dd HH:mm:ss");
                
                if (!metadata.ContainsKey("FileType"))
                    metadata["FileType"] = fileInfo.Extension.TrimStart('.').ToUpperInvariant();
            }
            catch { }
        }

        private static void AddExifTag<T>(Dictionary<string, string> metadata, T directory, int tagType, string keyName) 
            where T : MetadataExtractor.Directory
        {
            try
            {
                var value = directory.GetDescription(tagType);
                if (!string.IsNullOrEmpty(value) && !metadata.ContainsKey(keyName))
                    metadata[keyName] = value;
            }
            catch { }
        }

        private static void AddIptcTag(Dictionary<string, string> metadata, IptcDirectory directory, int tagType, string keyName)
        {
            try
            {
                var value = directory.GetDescription(tagType);
                if (!string.IsNullOrEmpty(value) && !metadata.ContainsKey(keyName))
                    metadata[keyName] = value;
            }
            catch { }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Checks if native reading is supported for the given file type.
        /// </summary>
        public static bool IsSupported(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return ImageExtensions.Contains(extension) || 
                   VideoExtensions.Contains(extension) || 
                   AudioExtensions.Contains(extension);
        }
    }
}
