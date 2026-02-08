# PlatypusTools v3.4.0 — Audio Visualizer System

**Status**: ✅ **Production — 22 GPU-Rendered Modes**  
**Date**: February 8, 2026  
**Version**: 3.4.0  

---

## Overview

The audio visualizer has been completely rewritten to deliver 22 hardware-accelerated visualizer
modes rendered through SkiaSharp's `SKElement.OnSkiaPaintSurface`. All modes support:

- Embedded playback (inside audio player view)
- Fullscreen mode with OSD overlay and arrow-key mode switching
- Windows screensaver mode with continuous idle animation

---

## Visualizer Modes (0–21)

| # | Mode | Description |
|---|------|-------------|
| 0 | **Bars** | Classic FFT spectrum bars with gradient fill |
| 1 | **Mirror** | Symmetric spectrum reflected across center |
| 2 | **Waveform** | Time-domain waveform line |
| 3 | **Circular** | Radial bar ring around center |
| 4 | **Radial** | Outward-expanding radial lines |
| 5 | **Particles** | Particle-based FFT visualization with HSL color |
| 6 | **Aurora** | Multi-layer aurora borealis waves |
| 7 | **Wave Grid** | 3D perspective wave grid |
| 8 | **Starfield** | Forward-flying star field |
| 9 | **Toasters** | Flying toasters animation |
| 10 | **Matrix** | Matrix rain with glow (fullscreen-aware) |
| 11 | **Star Wars Crawl** | Perspective text crawl |
| 12 | **Stargate** | Wormhole/vortex tunnel |
| 13 | **Klingon** | Klingon-themed spectrum with logo overlay |
| 14 | **Federation** | Federation particle nebula with logo |
| 15 | **Jedi** | Lightsaber array driven by FFT bands |
| 16 | **TimeLord** | TARDIS vortex with feedback buffer |
| 17 | **VU Meter** | Analog VU meter pair |
| 18 | **Oscilloscope** | Traditional oscilloscope trace |
| 19 | **Milkdrop** | Milkdrop-style feedback buffer with warping |
| 20 | **3D Bars** | Perspective 3D bar graph |
| 21 | **Waterfall** | Scrolling spectrogram heatmap |

---

## Architecture

### Rendering Pipeline

```
NAudio WaveOut → SampleProvider → FFT (1024-point)
    ↓
EnhancedAudioPlayerViewModel.SpectrumData (float[])
    ↓
EnhancedAudioPlayerView.OnSpectrumData()
    ↓  Interlocked frame-skip guard
    ↓  DispatcherPriority.Input
AudioVisualizerView.UpdateSpectrumData(float[], int modeIndex)
    ↓  CleanupModeResources(old, new) on mode change
    ↓  InvalidateVisual() forced on mode switch
SKElement.OnSkiaPaintSurface(SKPaintSurfaceEventArgs)
    ↓  switch (modeIndex) → per-mode renderer
GPU surface composited by WPF
```

### Key Components

| File | Role |
|------|------|
| `AudioVisualizerView.xaml.cs` | All 22 renderers (~10 000 lines), cleanup, GPU resource management |
| `EnhancedAudioPlayerView.xaml.cs` | Fullscreen window lifecycle, spectrum dispatch, OSD overlay |
| `EnhancedAudioPlayerViewModel.cs` | Playback state, spectrum data property |
| `ScreensaverWindow.xaml.cs` | Screensaver wrapper with 45 ms animation timer |
| `ScreensaverInstallerService.cs` | Copies app to `%ProgramData%\PlatypusTools\Screensaver\` |

### Fullscreen Mode

- **Entry**: Double-click visualizer or press **F11**
- **Mode switching**: **←/→** or **↑/↓** arrow keys cycle modes
- **OSD overlay**: Mode name, ◀/▶ buttons, track info — auto-hides after inactivity
- **Exit**: **Escape** or double-click

Fullscreen uses an externally driven `AudioVisualizerView` instance inside a borderless
`Window`. Spectrum data is dispatched from the player view at `DispatcherPriority.Input`.
Smoothing multipliers (1.5× rise / 1.3× fall) compensate for dispatch latency.

---

## Performance & Stability Fixes (v3.4.0)

### Memory Leak Fixes

| Leak | Impact | Fix |
|------|--------|-----|
| `SKMaskFilter.CreateBlur()` × 20 sites | ~1 400 native objects/sec (Klingon) | `using var` on all 20 call sites |
| `SKTypeface.FromFamilyName()` per frame | 16 GDI handles/frame | 8 cached static typefaces |
| 8 `SKBitmap` fields on unload | GPU memory retained indefinitely | `DisposeGpuResources()` method |

### Fullscreen Freeze Fixes

| Issue | Root Cause | Fix |
|-------|-----------|-----|
| Keyboard input ignored | `DispatcherPriority.Send` blocked input queue | Changed to `.Input` |
| Dispatch pile-up → freeze | `bool` guard had race condition | `Interlocked.CompareExchange` + `finally` reset |
| Milkdrop exit crash | Feedback buffer disposed mid-render | Local `currentBuffer` null-guard |
| TimeLord exit crash | Vortex buffer disposed mid-render | Local `vortexBuf` null-guard |
| Mode switch stuck | Exception in `CleanupModeResources` blocked mode update | try/catch; mode always updated |
| Subsequent mode dim/dead | `_animationPhase = 0` reset on TimeLord exit | Removed (shared global field) |
| Render exception freezes SKElement | WPF stops calling `OnPaintSurface` after throw | try/catch with red error indicator |
| Spurious `OnUnloaded` kills fullscreen | WPF fires Unloaded during reparenting | Skip cleanup if `_isExternallyDriven && IsLoaded` |

### Other Fixes

- Matrix columns never cleared on mode switch → `CleanupModeResources()` centralized cleanup
- Matrix column list unbounded growth → trim loop in software path
- Redundant `SetColorScheme()` call per frame removed
- Particle color HSL hue range corrected (0–1, not 0–360)
- Matrix fullscreen glow uses lighter blur radius

---

## Screensaver System

The screensaver installer (`ScreensaverInstallerService.cs`) copies the full application
directory to `%ProgramData%\PlatypusTools\Screensaver\` and registers the EXE in the Windows
screensaver registry key.

`ScreensaverWindow.xaml.cs` creates an `AudioVisualizerView` with a 45 ms `DispatcherTimer`
that pumps evolving idle spectrum data. All 22 modes animate without audio input. The
`ScreensaverConfigWindow.xaml` lets users pick their preferred mode.

---

## Build & Release

- **Build**: `.\Build-Release.ps1`
- **Platform**: Windows x64, .NET 10, self-contained single-file
- **Dependencies**: SkiaSharp 3.119.0, NAudio 2.2.1
- **Output**: MSI in `releases/`, portable EXE in `publish/`
