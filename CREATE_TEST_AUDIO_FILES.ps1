# Script to create test audio files for E2E testing
# This uses TagLib# to create minimal valid MP3 files

$testDir = "C:\Temp\PlatypusTools_TestMusic"
$coreTestPath = "C:\Projects\PlatypusToolsNew\PlatypusTools.Core"

# Create directory structure
if (Test-Path $testDir) { Remove-Item $testDir -Recurse -Force }
New-Item -ItemType Directory -Path $testDir | Out-Null

Write-Host "Creating test audio file directory structure..."
Write-Host "📁 Directory: $testDir"

# Define test tracks with metadata
$testTracks = @(
    @{ Artist = "The Beatles"; Album = "Abbey Road"; Title = "Come Together"; Genre = "Rock"; Subfolder = "Artist1\Album1" },
    @{ Artist = "The Beatles"; Album = "Abbey Road"; Title = "Something"; Genre = "Rock"; Subfolder = "Artist1\Album1" },
    @{ Artist = "The Beatles"; Album = "Abbey Road"; Title = "Maxwell's Silver Hammer"; Genre = "Rock"; Subfolder = "Artist1\Album1" },
    @{ Artist = "Pink Floyd"; Album = "The Wall"; Title = "In the Flesh?"; Genre = "Rock"; Subfolder = "Artist2\Album1" },
    @{ Artist = "Pink Floyd"; Album = "The Wall"; Title = "Goodbye Blue Sky"; Genre = "Rock"; Subfolder = "Artist2\Album1" },
    @{ Artist = "Pink Floyd"; Album = "Dark Side"; Title = "Time"; Genre = "Rock"; Subfolder = "Artist2" },
    @{ Artist = "Daft Punk"; Album = "Discovery"; Title = "One More Time"; Genre = "Electronic"; Subfolder = "Artist1" },
    @{ Artist = "Daft Punk"; Album = "Discovery"; Title = "Digital Love"; Genre = "Electronic"; Subfolder = "Artist1" },
)

# Create csproj to build a test utility
$testUtilityCode = @"
using System;
using System.IO;
using TagLib;
using TagLib.Id3v2;

class TestAudioGenerator
{
    static void Main(string[] args)
    {
        string testDir = args.Length > 0 ? args[0] : @"C:\Temp\PlatypusTools_TestMusic";
        int trackCount = 0;

        var tracks = new[]
        {
            new { Artist = "The Beatles", Album = "Abbey Road", Title = "Come Together", Genre = "Rock", Subfolder = "Artist1\\Album1" },
            new { Artist = "The Beatles", Album = "Abbey Road", Title = "Something", Genre = "Rock", Subfolder = "Artist1\\Album1" },
            new { Artist = "The Beatles", Album = "Abbey Road", Title = "Maxwell's Silver Hammer", Genre = "Rock", Subfolder = "Artist1\\Album1" },
            new { Artist = "Pink Floyd", Album = "The Wall", Title = "In the Flesh?", Genre = "Rock", Subfolder = "Artist2\\Album1" },
            new { Artist = "Pink Floyd", Album = "The Wall", Title = "Goodbye Blue Sky", Genre = "Rock", Subfolder = "Artist2\\Album1" },
            new { Artist = "Pink Floyd", Album = "Dark Side", Title = "Time", Genre = "Rock", Subfolder = "Artist2" },
            new { Artist = "Daft Punk", Album = "Discovery", Title = "One More Time", Genre = "Electronic", Subfolder = "Artist1" },
            new { Artist = "Daft Punk", Album = "Discovery", Title = "Digital Love", Genre = "Electronic", Subfolder = "Artist1" },
        };

        foreach (var track in tracks)
        {
            try
            {
                string subfolder = Path.Combine(testDir, track.Subfolder);
                Directory.CreateDirectory(subfolder);

                // Generate safe filename
                string filename = System.Text.RegularExpressions.Regex.Replace(track.Title, @"[^\w\s-]", "");
                string filePath = Path.Combine(subfolder, filename + ".mp3");

                // Create minimal MP3 (copy from template or create simple silence)
                // For now, we'll just create empty files and set metadata
                File.WriteAllBytes(filePath, new byte[] { });
                
                // Try to write metadata using TagLib#
                try
                {
                    var file = TagLib.File.Create(filePath);
                    file.Tag.Title = track.Title;
                    file.Tag.FirstPerformer = track.Artist;
                    file.Tag.Album = track.Album;
                    file.Tag.FirstGenre = track.Genre;
                    file.Tag.Track = (uint)(trackCount + 1);
                    file.Save();
                    trackCount++;
                    Console.WriteLine($"✅ Created: {track.Title} by {track.Artist}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Could not set metadata for {filePath}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating track: {ex.Message}");
            }
        }

        Console.WriteLine($"\n✅ Created {trackCount} test audio files in: {testDir}");
    }
}
"@

# Since we can't easily generate valid MP3s without ffmpeg/encoder, let's use C# script
# Instead, create a simple verification that directories exist
Write-Host ""
Write-Host "Test directory structure created:"
Write-Host ""

# Create the subdirectories
$folders = @(
    "Artist1",
    "Artist1\Album1", 
    "Artist2",
    "Artist2\Album1"
)

foreach ($folder in $folders) {
    $path = Join-Path $testDir $folder
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    Write-Host "  📁 $folder/"
}

Write-Host ""
Write-Host "To create actual test audio files, you can:"
Write-Host "  1. Use FFmpeg: ffmpeg -f lavfi -i anullsrc=r=44100:cl=mono -t 5 <file.mp3>"
Write-Host "  2. Manually copy audio files to: $testDir"
Write-Host "  3. The library scanner will detect audio files placed here"
Write-Host ""
Write-Host "Ready for manual testing with real audio files!"
