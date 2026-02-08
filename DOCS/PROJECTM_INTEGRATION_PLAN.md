# ProjectM / Butterchurn Milkdrop Visualizer Integration Plan

## Overview

Integrate Milkdrop-compatible visualizations into PlatypusTools audio player.

## Option A: Butterchurn (WebView2) - RECOMMENDED

**Effort: 3 days**

### Description
Butterchurn is a JavaScript/WebGL2 implementation of Milkdrop. Runs in a WebView2 control embedded in WPF.

### Advantages
- No native DLLs to distribute
- Same visual quality as projectM (both implement Milkdrop)
- NPM packages for easy updates
- Works cross-platform in any browser-based environment

### Implementation Steps

1. **Add WebView2 NuGet Package**
```xml
<PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2535.41" />
```

2. **Create Visualizer HTML Page**
```html
<!DOCTYPE html>
<html>
<head>
    <script src="butterchurn.min.js"></script>
    <script src="butterchurnPresets.min.js"></script>
</head>
<body>
    <canvas id="canvas"></canvas>
    <script>
        const canvas = document.getElementById('canvas');
        const audioContext = new AudioContext();
        const visualizer = butterchurn.createVisualizer(audioContext, canvas, {
            width: canvas.clientWidth,
            height: canvas.clientHeight
        });

        // Load preset
        visualizer.loadPreset(butterchurnPresets.getPresetByName('Rovastar - Sunflower Passion'), 0.0);

        function render() {
            visualizer.render();
            requestAnimationFrame(render);
        }
        render();

        // C# will call this with audio data
        window.receiveAudioData = function(floatArray) {
            // Feed to visualizer
        };
    </script>
</body>
</html>
```

3. **Add WebView2 Control to XAML**
```xml
<WebView2 x:Name="VisualizerWebView" 
          Source="Assets/visualizer.html"
          Visibility="{Binding IsButterchurnMode}"/>
```

4. **Pipe Audio Data via JavaScript Bridge**
```csharp
// From NAudio sample provider
public void SendAudioData(float[] samples)
{
    var json = JsonSerializer.Serialize(samples);
    VisualizerWebView.CoreWebView2.ExecuteScriptAsync($"window.receiveAudioData({json})");
}
```

### Files Needed
- `Assets/butterchurn.min.js` (~500KB)
- `Assets/butterchurnPresets.min.js` (~2MB for 100+ presets)
- `Assets/visualizer.html`

---

## Option B: ProjectM Native (P/Invoke + OpenGL)

**Effort: 7-10 days**

### Description
Native C++ library with P/Invoke bindings, rendered via OpenTK.GLWpfControl.

### Advantages
- Best performance (native OpenGL)
- Full projectM API access
- No JavaScript bridge overhead

### Disadvantages
- Must build projectM from source (vcpkg)
- Must distribute native DLLs
- More complex OpenGL context management

### Required Packages
```xml
<PackageReference Include="OpenTK.GLWpfControl" Version="5.0.0-pre.1" />
```

### P/Invoke Bindings
```csharp
public static class ProjectMNative
{
    private const string DllName = "projectM-4.dll";

    public enum ProjectMChannels { Mono = 1, Stereo = 2 }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr projectm_create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void projectm_destroy(IntPtr instance);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void projectm_set_window_size(IntPtr instance, 
        UIntPtr width, UIntPtr height);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void projectm_render_frame(IntPtr instance);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void projectm_pcm_add_float(IntPtr instance, 
        float[] samples, uint count, ProjectMChannels channels);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void projectm_load_preset_file(IntPtr instance, 
        string filename, [MarshalAs(UnmanagedType.I1)] bool smoothTransition);
}
```

### Build with vcpkg
```powershell
git clone https://github.com/microsoft/vcpkg
.\vcpkg\bootstrap-vcpkg.bat
vcpkg install projectm:x64-windows
# Output: vcpkg\installed\x64-windows\bin\projectM-4.dll
```

### DLLs to Distribute
- `projectM-4.dll`
- `projectm-eval.dll`
- `glew32.dll` (may be required)

---

## Preset Resources

| Pack | Size | Count | Link |
|------|------|-------|------|
| Cream of the Crop | ~100MB | ~10,000 | https://github.com/projectM-visualizer/presets-cream-of-the-crop |
| Milkdrop Mega Pack | 4.08GB | 130,000+ | Google Drive link in projectM repo |
| Classic projectM | ~50MB | ~4,000 | https://github.com/projectM-visualizer/presets-projectm-classic |

---

## Timeline Comparison

| Task | Butterchurn | ProjectM Native |
|------|-------------|-----------------|
| Package setup | 0.5 days | 1 day (vcpkg build) |
| Visualizer control | 1 day | 2 days (OpenGL hosting) |
| Audio bridge | 1 day | 1 day |
| Preset loading | 0.5 days | 1.5 days |
| Testing | Included | 2 days |
| **Total** | **3 days** | **7-10 days** |

---

## Recommendation

Start with **Butterchurn (Option A)** for faster iteration. Can migrate to native projectM later if performance is insufficient.

## References

- projectM GitHub: https://github.com/projectM-visualizer/projectm
- Butterchurn: https://github.com/jberg/butterchurn
- OpenTK.GLWpfControl: https://www.nuget.org/packages/OpenTK.GLWpfControl
- WebView2: https://www.nuget.org/packages/Microsoft.Web.WebView2
