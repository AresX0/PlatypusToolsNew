using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Singleton daemon that cycles through images in a folder and applies them as the desktop wallpaper
    /// (and optionally the lock-screen image). Burns NASA / custom overlays via <see cref="WallpaperOverlayRenderer"/>.
    /// </summary>
    public class WallpaperRotatorService
    {
        private static readonly Lazy<WallpaperRotatorService> _instance = new(() => new WallpaperRotatorService());
        public static WallpaperRotatorService Instance => _instance.Value;

        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private List<string> _images = new();
        private int _index = 0;
        private int _overlayPage = 0;
        private NasaInfoService.NasaSnapshot? _nasaSnapshot;

        public event EventHandler<RotatorStateChangedEventArgs>? StateChanged;

        public bool IsRunning { get; private set; }
        public string? CurrentImagePath { get; private set; }
        public DateTimeOffset? NextChangeAt { get; private set; }
        public string LastStatus { get; private set; } = "";

        private readonly NasaInfoService _nasa = new();

        public void Start(WallpaperRotatorConfig config)
        {
            lock (_lock)
            {
                if (IsRunning) Stop();

                _images = WallpaperImageScanner.Scan(config.ImagesDirectory, config.Shuffle);
                if (_images.Count == 0)
                {
                    LastStatus = "No images found in selected folder.";
                    Raise();
                    return;
                }

                _index = 0;
                _overlayPage = 0;
                _cts = new CancellationTokenSource();
                IsRunning = true;
                LastStatus = $"Started — {_images.Count} image(s).";
                Raise();

                var token = _cts.Token;
                _loopTask = Task.Run(() => RunLoopAsync(config, token), token);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = null;
                IsRunning = false;
                NextChangeAt = null;
                LastStatus = "Stopped.";
                Raise();
            }
        }

        /// <summary>Applies the very next image immediately and resets the timer.</summary>
        public async Task ApplyNextNowAsync(WallpaperRotatorConfig config)
        {
            if (_images.Count == 0)
                _images = WallpaperImageScanner.Scan(config.ImagesDirectory, config.Shuffle);
            if (_images.Count == 0)
            {
                LastStatus = "No images available.";
                Raise();
                return;
            }
            await ApplyOneAsync(config, CancellationToken.None).ConfigureAwait(false);
        }

        private async Task RunLoopAsync(WallpaperRotatorConfig config, CancellationToken ct)
        {
            try
            {
                await ApplyOneAsync(config, ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    var interval = TimeSpan.FromSeconds(Math.Max(10, config.WallpaperIntervalSeconds));
                    NextChangeAt = DateTimeOffset.Now + interval;
                    Raise();

                    try { await Task.Delay(interval, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }

                    await ApplyOneAsync(config, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* clean shutdown */ }
            catch (Exception ex)
            {
                LastStatus = $"Daemon error: {ex.Message}";
                Raise();
            }
        }

        private async Task ApplyOneAsync(WallpaperRotatorConfig config, CancellationToken ct)
        {
            if (_images.Count == 0) return;
            var src = _images[_index % _images.Count];
            _index = (_index + 1) % _images.Count;

            // Refresh NASA snapshot if needed (cheap; cached)
            IReadOnlyList<string>? overlay = null;
            if (config.BurnOverlayOnWallpaper)
                overlay = await BuildOverlayAsync(config, ct).ConfigureAwait(false);

            var (sw, sh) = WallpaperSetter.GetPrimaryScreenSize();
            var staged = WallpaperSetter.GetStagedWallpaperPath();

            bool ok = WallpaperOverlayRenderer.Render(src, staged, sw, sh, config.FitMode, overlay, config.OverlayOpacity);
            if (!ok)
            {
                LastStatus = $"Failed to render: {Path.GetFileName(src)}";
                Raise();
                return;
            }

            if (!WallpaperSetter.SetDesktopWallpaper(staged))
            {
                LastStatus = $"Failed to set wallpaper: {Path.GetFileName(src)}";
                Raise();
                return;
            }

            CurrentImagePath = src;

            if (config.ApplyToLockScreen)
            {
                if (!WallpaperSetter.SetLockScreenImage(staged, out var msg))
                    LastStatus = $"Wallpaper OK; lock screen failed: {msg}";
                else
                    LastStatus = $"Applied: {Path.GetFileName(src)} (incl. lock screen)";
            }
            else
            {
                LastStatus = $"Applied: {Path.GetFileName(src)}";
            }

            _overlayPage++;
            Raise();
        }

        private async Task<IReadOnlyList<string>?> BuildOverlayAsync(WallpaperRotatorConfig config, CancellationToken ct)
        {
            switch ((config.OverlaySource ?? "nasa").ToLowerInvariant())
            {
                case "none":
                    return null;
                case "custom":
                {
                    if (string.IsNullOrWhiteSpace(config.CustomOverlayText)) return null;
                    var lines = new List<string>();
                    foreach (var raw in config.CustomOverlayText.Replace("\r", "").Split('\n'))
                        lines.Add(raw);
                    return lines;
                }
                default:
                {
                    _nasaSnapshot ??= await _nasa.GetAsync(forceRefresh: false, ct).ConfigureAwait(false);
                    return NasaInfoService.BuildOverlayLines(_nasaSnapshot, maxLines: 18);
                }
            }
        }

        private void Raise()
        {
            StateChanged?.Invoke(this, new RotatorStateChangedEventArgs
            {
                IsRunning = IsRunning,
                CurrentImagePath = CurrentImagePath,
                NextChangeAt = NextChangeAt,
                Status = LastStatus,
            });
        }
    }

    public class RotatorStateChangedEventArgs : EventArgs
    {
        public bool IsRunning { get; init; }
        public string? CurrentImagePath { get; init; }
        public DateTimeOffset? NextChangeAt { get; init; }
        public string Status { get; init; } = "";
    }
}
