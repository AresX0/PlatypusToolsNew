# Stargate + Time Vortex — 1080p Export with FFmpeg (UWP + Win2D)

This guide shows how to:

1) **Render 1080p PNG frames** off‑screen with Win2D (`CanvasRenderTarget`)
2) **Encode those frames into video** with **FFmpeg** (H.264 MP4 and optional VP9 WebM)

- **Why this works well:** Win2D can render at any pixel size off‑screen and save to PNG; you then turn the image sequence into a video with FFmpeg. citeturn15search37turn15search35turn15search40  
- **FFmpeg basics:** Use an image sequence input (`-framerate` with `%05d` pattern) and encode with `libx264` to MP4 (set `-pix_fmt yuv420p` for broad compatibility). citeturn15search55

---

## 0) Prereqs

- **FFmpeg** installed and on your PATH. (Commands below assume `ffmpeg` is available in your terminal.)  
- Your UWP app already renders the Stargate and the Time Vortex using **Win2D** (as in the earlier sample).  
- We’ll add **off‑screen exporters** that render frame sequences at **1920×1080**. Win2D supports this via `CanvasRenderTarget` and `CanvasImage.SaveAsync`. citeturn15search37turn15search40

---

## 1) Add a **1080p Frame Exporter** (UWP + Win2D)

> This renders frames to `ApplicationData.Current.LocalFolder\\VideoFrames\\<name>\\` so you don’t need extra file permissions. Then you’ll point FFmpeg at those PNGs.

### 1.1 Helper: Save a single frame

```csharp
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

public async Task SaveFrameAsync(
    string setName,          // e.g., "stargate" or "vortex"
    int frameIndex,          // 0..N-1
    int width = 1920, int height = 1080, float dpi = 96f)
{
    // Where to save (local app data)
    StorageFolder root = ApplicationData.Current.LocalFolder;
    StorageFolder framesDir = await root.CreateFolderAsync(
        Path.Combine("VideoFrames", setName), CreationCollisionOption.OpenIfExists);

    // Create 1080p off-screen target
    var device = CanvasDevice.GetSharedDevice();
    using var target = new CanvasRenderTarget(device, width, height, dpi);

    using (var ds = target.CreateDrawingSession())
    {
        ds.Clear(Colors.Black);

        // ---- DRAW YOUR SCENE HERE ----
        // Example: call the same methods you use in Stage_Draw.
        // Pass a known "center" based on width/height:
        var center = new Vector2(width / 2f, height / 2f);

        // Draw the ring/chevrons/wormhole *or* the time vortex
        // e.g., DrawRing(ds, center); DrawChevrons(ds, center, locked);
        // Set your shader properties: Time, Stability, Kawoosh, etc.
        // Then draw the wormhole with masking like before.
    }

    // Save as PNG (no special capabilities required in LocalFolder)
    string fileName = $"{setName}_{frameIndex:D5}.png";
    StorageFile file = await framesDir.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
    using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
    await CanvasImage.SaveAsync(target, new Windows.Foundation.Rect(0,0,width,height),
        dpi, device, stream, Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
}
```

- `CanvasRenderTarget` is the Win2D off‑screen render surface. citeturn15search37turn15search35  
- `CanvasImage.SaveAsync` can save any `ICanvasImage` (including effects graphs) to a stream—perfect for PNG frames. citeturn15search40

### 1.2 Export a sequence (e.g., **30 fps for 10 s** ⇒ 300 frames)

```csharp
public async Task ExportSequenceAsync(
    string setName,
    int width = 1920, int height = 1080,
    int fps = 30, int seconds = 10)
{
    int frames = fps * seconds;
    float dt = 1f / fps;

    // Reset your dialing state machine, time, etc.
    _time = 0f;
    _state = GateState.Dialing;
    _lockedChevrons = 0;
    _dialTimer = 0f;
    _ringAngle = 0f;
    _kawooshRadius = 0f;
    _horizonStability = 0f;

    for (int i = 0; i < frames; i++)
    {
        // Advance your logic by dt (the same logic you use in Stage_Update)
        AdvanceGateState(dt); // implement like Stage_Update

        // Save the frame
        await SaveFrameAsync(setName, i, width, height);

        _time += dt;
    }
}
```

> Run this **twice**: once with your **Stargate** scene active (dial → kawoosh → horizon), and once with your **Time Vortex** scene active (no gate), to produce two PNG sets:  
`VideoFrames\stargate\stargate_00000.png` … and `VideoFrames\vortex\vortex_00000.png`.

---

## 2) Encode to **1080p H.264 MP4** with FFmpeg

> From a terminal in the folder containing your sequential PNG frames:

### 2.1 Stargate (H.264, 30 fps, high quality MP4)

```bash
ffmpeg -y -framerate 30 -i "stargate_%05d.png" \
  -c:v libx264 -crf 18 -preset slow -pix_fmt yuv420p -movflags +faststart \
  stargate_1080p30.mp4
```
- `-framerate 30` sets the **input image sequence** playback rate.  
- `-c:v libx264 -crf 18` gives **visually lossless‑ish** quality for many scenes; raise to 20–23 if files are too large.  
- `-pix_fmt yuv420p` ensures compatibility with players/browsers.  
- `-movflags +faststart` for web streaming friendliness.  
These options are standard practice when turning image sequences into MP4 with FFmpeg. citeturn15search55turn15search53

### 2.2 Time Vortex (H.264, 30 fps)

```bash
ffmpeg -y -framerate 30 -i "vortex_%05d.png" \
  -c:v libx264 -crf 18 -preset slow -pix_fmt yuv420p -movflags +faststart \
  time_vortex_1080p30.mp4
```
Same reasoning as above. citeturn15search55

> **Note on frame patterns:** Windows builds can use `%05d` for sequences (`vortex_00001.png`, etc.). The wiki shows when to use `-framerate` (input) vs `-r` (output) and why `yuv420p` improves compatibility. citeturn15search55

---

## 3) (Optional) Encode **WebM VP9** for the web

If you want a royalty‑free WebM alongside MP4:

### 3.1 Single‑pass constant quality (quick, good)

```bash
ffmpeg -y -framerate 30 -i "stargate_%05d.png" \
  -c:v libvpx-vp9 -b:v 0 -crf 28 -pix_fmt yuv420p \
  stargate_1080p30.webm
```
- VP9 constant‑quality uses `-b:v 0 -crf N` (lower CRF = higher quality). 28–32 is a typical range; adjust to taste. citeturn15search47turn15search52[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m[0m

### 3.2 Two‑pass VP9 (slower, smaller)

```bash
ffmpeg -y -framerate 30 -i "stargate_%05d.png" \
  -c:v libvpx-vp9 -b:v 0 -crf 28 -pass 1 -an -f null NUL

ffmpeg -y -framerate 30 -i "stargate_%05d.png" \
  -c:v libvpx-vp9 -b:v 0 -crf 28 -pass 2 -pix_fmt yuv420p \
  stargate_1080p30.webm
```
Two‑pass VP9 is recommended by the FFmpeg VP9 guide for best compression efficiency. citeturn15search47

---

## 4) Tips for a clean 1080p export

- **Consistency:** Render exactly **1920×1080** frames. If you must mix sizes, add a `scale/pad` filter (example on FFmpeg wiki). citeturn15search55  
- **Frame rate:** Keep the same `-framerate` you used in your simulation step (`dt = 1/fps`). FFmpeg’s slideshow page explains input vs output frame rates. citeturn15search55  
- **Ordering:** `%05d` enforces numeric order. If your files are non‑sequential or mixed types, you can use **concat demuxer** with a file list. citeturn15search55turn15search59

---

## 5) Where the Win2D details come from

- **Off‑screen rendering** with `CanvasRenderTarget` and **saving** an `ICanvasImage` to a stream are supported in Win2D (see docs and examples). citeturn15search37turn15search35turn15search40

---

## 6) Putting it together (suggested workflow)

1. In your app, call:
   ```csharp
   await ExportSequenceAsync("stargate", 1920, 1080, fps: 30, seconds: 10);
   await ExportSequenceAsync("vortex",   1920, 1080, fps: 30, seconds: 10);
   ```
   (These produce `stargate_00000.png …` and `vortex_00000.png …`.)

2. In a terminal **inside** each frames folder, run:
   ```bash
   ffmpeg -y -framerate 30 -i "stargate_%05d.png" -c:v libx264 -crf 18 -preset slow -pix_fmt yuv420p -movflags +faststart stargate_1080p30.mp4
   ffmpeg -y -framerate 30 -i "vortex_%05d.png"   -c:v libx264 -crf 18 -preset slow -pix_fmt yuv420p -movflags +faststart time_vortex_1080p30.mp4
   ```
   (Optional) Also make **WebM VP9**:
   ```bash
   ffmpeg -y -framerate 30 -i "stargate_%05d.png" -c:v libvpx-vp9 -b:v 0 -crf 28 stargate_1080p30.webm
   ffmpeg -y -framerate 30 -i "vortex_%05d.png"   -c:v libvpx-vp9 -b:v 0 -crf 28 time_vortex_1080p30.webm
   ```
   citeturn15search55turn15search47

---

## 7) Quality knobs you can tweak

- **H.264 quality/size:** increase `CRF` from **18 → 20–23** to shrink files. (Lower CRF = higher quality.) citeturn15search55  
- **VP9 quality/size:** `-crf` **28–32** typical for 1080p sequences; use 2‑pass to squeeze files smaller. citeturn15search47  
- **Performance:** export fewer frames (shorter duration), or encode with a faster preset (e.g., `-preset medium` on x264). citeturn15search55
