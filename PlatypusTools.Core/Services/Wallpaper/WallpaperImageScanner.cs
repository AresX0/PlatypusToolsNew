using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Recursively enumerates image files for the rotator and slideshow.
    /// </summary>
    public static class WallpaperImageScanner
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff", ".gif" };

        public static List<string> Scan(string directory, bool shuffle)
        {
            var found = new List<string>();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return found;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) >= 0)
                        found.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }

            if (shuffle)
            {
                var rng = new Random();
                found = found.OrderBy(_ => rng.Next()).ToList();
            }

            return found;
        }
    }
}
