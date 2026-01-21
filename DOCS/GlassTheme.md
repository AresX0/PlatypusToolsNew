# Aero Glass Theme

PlatypusTools supports a modern "Glass" theme that provides translucent, blur-behind visual effects reminiscent of Windows Vista/7 Aero and updated for Windows 10/11.

## Features

- **Translucent window effects** using DWM (Desktop Window Manager) APIs
- **Windows 11 Mica/Acrylic** backdrops for modern appearance
- **Windows 10 Acrylic** blur effects
- **Graceful degradation** on unsupported systems
- **Light and Dark mode** support with appropriate tints
- **Accessibility compliant** with proper contrast ratios

## System Requirements

| Windows Version | Effect Type | Support Level |
|-----------------|-------------|---------------|
| Windows 11 (22000+) | Mica, Acrylic | Full |
| Windows 10 1809+ (17763+) | Acrylic, Blur | Full |
| Windows 10 earlier | Basic Blur | Partial |
| Windows 7/8 | Basic Blur | Limited |

## Enabling Glass Effects

### Via Settings

1. Open **Settings** (⚙ button or `Ctrl+,`)
2. Navigate to **Appearance**
3. Check **Enable Glass Effects**
4. Choose an effect level:
   - **Auto** - System chooses best option (recommended)
   - **High** - Maximum transparency (Mica on Windows 11)
   - **Medium** - Balanced effect (Acrylic)
   - **Low** - Subtle blur

### Via Code

```csharp
using PlatypusTools.UI.Services;
using PlatypusTools.UI.Interop;

// Enable glass on the main window
ThemeManager.Instance.SetMainWindow(mainWindow);
ThemeManager.Instance.GlassLevel = GlassLevel.Auto;
ThemeManager.Instance.IsGlassEnabled = true;
```

## Effect Levels

### Auto (Recommended)
Automatically selects the best effect based on:
- Windows version
- Hardware capabilities
- System transparency settings
- Battery saver mode

### High
- Windows 11: Mica backdrop
- Windows 10: Full acrylic with high blur

### Medium
- Windows 11: Acrylic backdrop
- Windows 10: Acrylic with moderate blur

### Low
- All versions: Basic blur with minimal transparency

### Off
- Disables all glass effects
- Uses solid theme colors

## Automatic Fallbacks

Glass effects are automatically disabled when:

- **System transparency is disabled** in Windows Settings
- **Battery saver mode** is active
- **Remote Desktop** session is detected
- **High Contrast mode** is enabled
- **Hardware acceleration** is unavailable

## Glass Theme Resources

The Glass theme provides additional XAML resources for styling:

### Brushes
- `GlassTintBrush` - Main glass tint
- `GlassAccentBrush` - Accent color for highlights
- `GlassBorderBrush` - Semi-transparent borders
- `GlassHighlightBrush` - Top highlight gradient
- `GlassSurfaceBrush` - Depth effect gradient

### Styles
- `GlassPanelStyle` - For glass panel borders
- `GlassCardStyle` - For content cards
- `GlassButtonStyle` - Glass-styled buttons
- `GlassTextBoxStyle` - Glass-styled inputs
- `GlassHeaderStyle` - Header areas
- `GlassSidebarStyle` - Navigation sidebars
- `GlassTabControlStyle` / `GlassTabItemStyle` - Tab controls
- `GlassListViewStyle` / `GlassListViewItemStyle` - List views

### Effects
- `GlassTextGlow` - White text glow for dark backgrounds
- `GlassTextGlowDark` - Black text glow for light backgrounds

## Using Glass Styles in Views

```xml
<Border Style="{StaticResource GlassCardStyle}">
    <StackPanel>
        <TextBlock Text="Glass Card Content"/>
        <Button Style="{StaticResource GlassButtonStyle}" 
                Content="Glass Button"/>
    </StackPanel>
</Border>
```

## Accessibility Considerations

The Glass theme maintains WCAG AA compliance:

- **Contrast ratios** of 4.5:1 or higher for text
- **Text glow effects** automatically applied when needed
- **High Contrast mode** detection with solid color fallback
- **Keyboard navigation** and focus visuals preserved

## Performance Notes

- Glass effects use GPU-accelerated composition
- Limited to top-level surfaces (window, header, sidebar)
- Nested glass surfaces are avoided to prevent overdraw
- Battery saver mode automatically disables effects

## Troubleshooting

### Glass effects don't appear

1. Check Windows Settings > Personalization > Colors > Transparency effects
2. Verify you're running Windows 10 1809 or later
3. Check if battery saver is active
4. Try restarting the application

### Visual artifacts or glitches

1. Update graphics drivers
2. Try a lower Glass Level (Medium or Low)
3. Disable and re-enable glass effects

### High CPU/GPU usage

1. Lower the Glass Level to "Low"
2. Disable animations in Settings
3. Ensure graphics drivers are up to date

## API Reference

### GlassLevel Enum
```csharp
public enum GlassLevel
{
    Off = 0,      // No glass effect
    Low = 1,      // Subtle blur
    Medium = 2,   // Balanced effect
    High = 3,     // Maximum transparency
    Auto = 4      // System-determined
}
```

### ThemeManager
```csharp
// Properties
bool IsGlassSupported { get; }
bool IsGlassEnabled { get; set; }
GlassLevel GlassLevel { get; set; }
bool IsDarkTheme { get; set; }

// Methods
void SetMainWindow(Window window);
string GetGlassSupportInfo();

// Events
event EventHandler ThemeChanged;
event EventHandler GlassSettingsChanged;
```

### DwmGlassHelper
```csharp
// Static Methods
bool EnableGlass(Window window, GlassLevel level, Color? tintColor = null);
void DisableGlass(Window window);
void SetDarkMode(Window window, bool isDark);

// Static Properties
bool IsGlassSupported { get; }
bool IsTransparencyDisabled { get; }
bool IsWindows11 { get; }
bool IsWindows10WithAcrylic { get; }
int WindowsBuildNumber { get; }
```

## Version History

- **v3.2.6.1** - Initial Glass theme implementation
  - DWM interop for Windows 10/11
  - Mica, Acrylic, and blur support
  - Settings integration
  - Resource dictionaries
  - Documentation
