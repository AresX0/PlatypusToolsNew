# PlatypusTools Architecture Document

**Version**: 3.2.5 (Video Editor Enhancement)  
**Last Updated**: January 18, 2026

---

## 1. Current Architecture Overview

### 1.1 Solution Structure

```
PlatypusToolsNew/
├── PlatypusTools.Core/           # Business logic, services, models
│   ├── Models/
│   │   ├── Audio/                # Audio player models (Track, Playlist, Lyrics)
│   │   ├── Video/                # Timeline, Clip, Transition models
│   │   └── ...                   # Other domain models
│   ├── Services/                 # Core services (FFmpeg, Media, etc.)
│   └── Config/                   # Configuration models
├── PlatypusTools.UI/             # WPF desktop application
│   ├── Views/                    # XAML views
│   ├── ViewModels/               # MVVM view models
│   ├── Services/                 # UI-specific services
│   ├── Controls/                 # Custom WPF controls
│   ├── Converters/               # Value converters
│   └── Themes/                   # Dark/Light themes
├── PlatypusTools.Core.Tests/     # Unit tests
├── PlatypusTools.UI.Tests/       # UI tests
└── PlatypusTools.Installer/      # WiX installer
```

### 1.2 Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Framework | .NET | 10.0 |
| UI | WPF | Built-in |
| Target OS | Windows | 10+ (19041+) |
| Media Processing | FFmpeg | External |
| Image Processing | SixLabors.ImageSharp | 3.1.6 |
| PDF | PdfSharpCore | 1.3.65 |
| Archives | SharpCompress | 0.38.0 |
| Metadata | TagLibSharp | 1.4.5 |
| Web Scraping | HtmlAgilityPack | 1.12.4 |
| Browser | WebView2 | 1.0.2792.45 |

### 1.3 Existing Video/Timeline Components

| Component | Location | Status |
|-----------|----------|--------|
| TimelineClip | Core/Models/Video/TimelineClip.cs | ✅ Complete |
| TimelineTrack | Core/Models/Video/TimelineTrack.cs | ✅ Complete |
| Transition | Core/Models/Video/Transition.cs | ✅ Complete |
| TimelineViewModel | UI/ViewModels/TimelineViewModel.cs | ✅ Basic |
| FFmpegService | Core/Services/FFmpegService.cs | ✅ Complete |
| VideoCombinerService | Core/Services/VideoCombinerService.cs | ✅ Complete |

---

## 2. Video Editor Enhancement (v3.2.5)

### 2.1 New Project Structure

We will add new namespaces within the existing projects to maintain compatibility:

```
PlatypusTools.Core/
├── Models/
│   └── Video/
│       ├── TimelineClip.cs       # Enhanced with keyframes, speed curves
│       ├── TimelineTrack.cs      # Enhanced with overlay, caption tracks
│       ├── Transition.cs         # Existing
│       ├── Keyframe.cs           # NEW: Bezier keyframe animation
│       ├── BeatMarker.cs         # NEW: Audio beat markers
│       ├── Caption.cs            # NEW: Auto-caption model
│       ├── OverlaySettings.cs    # NEW: Overlay transform/effects
│       └── ExportProfile.cs      # NEW: 4K60/HDR export presets
├── Services/
│   ├── Video/
│   │   ├── VideoEditorService.cs      # NEW: Main editor orchestration
│   │   ├── AudioImportService.cs      # NEW: Extract audio from media
│   │   ├── BeatDetectionService.cs    # NEW: NAudio FFT beat detection
│   │   ├── KeyframeInterpolator.cs    # NEW: Bezier curve evaluation
│   │   └── TimelineRenderer.cs        # NEW: Render timeline to output
│   └── AI/
│       ├── IAISpeechToText.cs         # NEW: Interface
│       ├── IAIBackgroundMatting.cs    # NEW: Interface
│       ├── IAIMotionTracker.cs        # NEW: Interface
│       ├── LocalWhisperProvider.cs    # NEW: Whisper ONNX
│       ├── LocalRvmMatting.cs         # NEW: RVM ONNX
│       └── LocalCsrtTracker.cs        # NEW: OpenCV CSRT

PlatypusTools.UI/
├── Views/
│   └── VideoEditorView.xaml           # NEW: Full video editor UI
├── ViewModels/
│   └── VideoEditorViewModel.cs        # NEW: Editor ViewModel
└── Controls/
    ├── TimelineControl.xaml           # NEW: Multi-track timeline
    ├── KeyframeCurveEditor.xaml       # NEW: Bezier curve editor
    ├── CaptionEditor.xaml             # NEW: Caption editing
    └── WaveformDisplay.xaml           # NEW: Audio waveform
```

### 2.2 New NuGet Packages Required

```xml
<!-- Audio Analysis -->
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="NAudio.WaveFormRenderer" Version="2.2.1" />

<!-- AI/ML -->
<PackageReference Include="Microsoft.ML.OnnxRuntime.DirectML" Version="1.20.0" />

<!-- Computer Vision (optional - behind feature flag) -->
<PackageReference Include="OpenCvSharp4" Version="4.10.0.20241108" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.10.0.20241108" />

<!-- Logging -->
<PackageReference Include="Serilog" Version="4.2.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
```

### 2.3 Configuration (appsettings.json)

```json
{
  "VideoEditor": {
    "Enabled": true,
    "PreviewFps": 24,
    "MaxPreviewResolution": "1080p"
  },
  "AI": {
    "Providers": {
      "SpeechToText": "LocalWhisper",
      "TextToSpeech": "LocalWindowsTTS",
      "BackgroundMatting": "LocalRVM",
      "MotionTracking": "LocalCSRT",
      "AutoReframe": "LocalOnnx",
      "VideoEnhance": "LocalRealESRGAN"
    },
    "UseCloud": false,
    "ModelsPath": ".\\models"
  },
  "ExportProfiles": {
    "HD_1080p30": { "Width": 1920, "Height": 1080, "Fps": 30, "Hdr": false, "Bitrate": "10M" },
    "UHD_4K60_HDR": { "Width": 3840, "Height": 2160, "Fps": 60, "Hdr": true, "Bitrate": "60M" }
  }
}
```

---

## 3. Feature Implementation Milestones

### M1: Timeline & Keyframes ✅ (This PR)
- [x] Enhanced TimelineClip with Bezier keyframes
- [x] Speed curves (0.1× - 100×)
- [x] Keyframe curve editor UI
- [x] Transform/opacity/effects keyframes

### M2: Beat Sync ✅ (This PR)
- [x] NAudio FFT spectral flux onset detection
- [x] Beat marker model and visualization
- [x] Snap-to-beat command
- [x] Waveform display

### M10: Audio Extract / Overlays ✅ (This PR)
- [x] AudioImportService with FFmpeg demux
- [x] Overlay tracks with transform controls
- [x] Duration handles for overlays
- [x] Background removal integration hooks

### M3-M9: Planned for Future PRs
- M3: Auto-Captions (Whisper ONNX)
- M4: Text-to-Speech
- M5: Background Removal (RVM ONNX)
- M6: Motion Tracking (OpenCV)
- M7: Auto Reframe
- M8: AI Enhance/Upscale
- M9: Templates & Effects

---

## 4. AI Service Contracts

### 4.1 Interface Definitions

```csharp
namespace PlatypusTools.Core.Services.AI
{
    public interface IAISpeechToText
    {
        Task<List<Caption>> TranscribeAsync(string audioPath, string language, 
            IProgress<double>? progress = null, CancellationToken ct = default);
    }

    public interface IAIBackgroundMatting
    {
        Task<byte[]> GenerateMaskAsync(byte[] frameData, int width, int height,
            CancellationToken ct = default);
    }

    public interface IAIMotionTracker
    {
        Task<TrackingResult> TrackObjectAsync(string videoPath, Rect initialBbox,
            IProgress<double>? progress = null, CancellationToken ct = default);
    }

    public interface IAIAutoReframe
    {
        Task<ReframeResult> ReframeAsync(string videoPath, AspectRatio targetRatio,
            IProgress<double>? progress = null, CancellationToken ct = default);
    }

    public interface IAIVideoEnhance
    {
        Task<string> EnhanceAsync(string videoPath, EnhanceOptions options,
            IProgress<double>? progress = null, CancellationToken ct = default);
    }
}
```

### 4.2 Local-First Philosophy

- All AI features work offline by default
- Models stored in `.\models` directory (not committed to repo)
- Post-clone PowerShell script downloads models with license prompts
- `UseCloud: false` by default; cloud adapters available via DI

---

## 5. Testing Strategy

### 5.1 Unit Tests
- Keyframe interpolation accuracy
- Beat detection with fixture audio
- Caption timecode merge/split
- Tracking drift on synthetic motion

### 5.2 Golden-Master Tests
- Render selected timestamps
- Hash frames for regression detection

### 5.3 Performance Benchmarks
- Preview FPS with 2× 1080p layers + LUT + captions
- CPU fallback envelope
- GPU utilization metrics

---

## 6. Privacy & Licensing

### 6.1 Privacy
- No network traffic unless `UseCloud: true`
- All AI inference runs locally
- User prompted before any cloud calls

### 6.2 Licensing
- NAudio: MIT
- OpenCvSharp4: Apache-2.0
- ONNX Runtime: MIT
- Serilog: Apache-2.0
- All permissive OSS only

---

*Document maintained by the PlatypusTools development team*
