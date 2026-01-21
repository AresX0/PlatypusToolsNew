# LCARS Theme Implementation

## Overview

The LCARS (Library Computer Access/Retrieval System) theme brings the iconic Star Trek: The Next Generation interface design to PlatypusTools. This futuristic theme features distinctive rounded pill-shaped buttons, angular header bars, and the characteristic color palette from the 24th century Starfleet computer interfaces.

## Color Palette

### Primary Colors (Gold/Tan Family)
| Color | Hex | Usage |
|-------|-----|-------|
| LCARS Gold | `#D39D4E` | Primary buttons, headers, active elements |
| LCARS Gold Light | `#ECB84E` | Hover states |
| LCARS Tan | `#C4A661` | Pressed states, borders |
| LCARS Tan Light | `#DBC597` | Subtle accents |
| LCARS Orange | `#CC6633` | Warnings, alerts |

### Secondary Colors (Peach/Coral Family)
| Color | Hex | Usage |
|-------|-----|-------|
| LCARS Peach | `#FF9966` | Sidebar buttons, secondary actions |
| LCARS Coral | `#FF7744` | Pressed states |
| LCARS Salmon | `#FF9F80` | Hover states |

### Tertiary Colors (Lavender/Purple Family)
| Color | Hex | Usage |
|-------|-----|-------|
| LCARS Lavender | `#CC99CC` | Accent elements, highlights |
| LCARS Purple | `#9977AA` | Data displays |
| LCARS Violet | `#9999CC` | Information panels |

### Status Colors
| Color | Hex | Usage |
|-------|-----|-------|
| LCARS Blue | `#99CCFF` | Information, status |
| LCARS Cyan | `#99FFFF` | Active connections |
| LCARS Green | `#66CC66` | Success, active |
| LCARS Red | `#CC3333` | Errors, warnings |
| LCARS Red Alert | `#FF3333` | Critical alerts |

### Neutral Colors
| Color | Hex | Usage |
|-------|-----|-------|
| LCARS Black | `#000000` | Window background |
| LCARS Black Light | `#1A1A1A` | Panel backgrounds |
| LCARS Gray | `#333333` | Secondary backgrounds |
| LCARS Gray Light | `#666666` | Disabled states |
| LCARS White | `#FFFFFF` | Text on dark backgrounds |

## Typography

LCARS uses condensed fonts for its distinctive look. The theme uses a fallback chain:
- **Primary**: Arial Narrow
- **Fallback**: Trebuchet MS
- **System Fallback**: Arial, Segoe UI

For an authentic LCARS experience, consider installing:
- Swiss 911 Ultra Compressed
- LCARS fonts from fan communities

## Special Styles

### Pill Buttons
The classic LCARS rounded-end buttons are available in several variants:

```xml
<!-- Full pill button (rounded both ends) -->
<Button Style="{StaticResource LcarsPillButtonStyle}" Content="ACCESS"/>

<!-- Left-rounded pill (for sidebar stacks) -->
<Button Style="{StaticResource LcarsPillLeftButtonStyle}" Content="SYSTEMS"/>

<!-- Right-rounded pill -->
<Button Style="{StaticResource LcarsPillRightButtonStyle}" Content="STATUS"/>

<!-- Color variants -->
<Button Style="{StaticResource LcarsPillPeachButtonStyle}" Content="SECONDARY"/>
<Button Style="{StaticResource LcarsPillLavenderButtonStyle}" Content="TERTIARY"/>
<Button Style="{StaticResource LcarsPillBlueButtonStyle}" Content="INFO"/>
<Button Style="{StaticResource LcarsPillRedButtonStyle}" Content="ALERT"/>
```

### Header Bar
LCARS header bars with the characteristic curved corner:

```xml
<Border Style="{StaticResource LcarsHeaderBarStyle}">
    <TextBlock Text="MAIN ENGINEERING" Style="{StaticResource LcarsHeaderTextStyle}"/>
</Border>
```

### Sidebar Buttons
Stacked sidebar buttons with rounded left ends:

```xml
<StackPanel Width="120">
    <Button Style="{StaticResource LcarsSidebarButtonStyle}" Content="SCAN"/>
    <Button Style="{StaticResource LcarsSidebarButtonStyle}" Content="ANALYZE"/>
    <Button Style="{StaticResource LcarsSidebarButtonStyle}" Content="EXPORT"/>
</StackPanel>
```

### Elbow Corners
The iconic LCARS elbow corner pieces:

```xml
<Border Style="{StaticResource LcarsElbowStyle}"/>
```

### Dividers
LCARS-style divider lines:

```xml
<Rectangle Style="{StaticResource LcarsDividerStyle}"/>
```

## Usage

### Enabling LCARS Theme

1. Open Settings (Ctrl+,)
2. Navigate to Appearance
3. Select "LCARS (Star Trek)" from the Theme options

### Programmatic Theme Selection

```csharp
using PlatypusTools.UI.Services;

// Apply LCARS theme
ThemeManager.ApplyTheme(ThemeManager.LCARS);

// Check if LCARS is active
bool isLcars = ThemeManager.Instance.IsLcarsTheme;
```

## Design Guidelines

### Do's
- Use pill buttons for primary actions
- Keep text uppercase for headers
- Use the gold color palette for interactive elements
- Maintain high contrast (light text on dark backgrounds)
- Group related controls with colored bars

### Don'ts
- Don't use glass effects with LCARS (they're incompatible)
- Don't overcrowd interfaces (LCARS is about clean panels)
- Don't use lowercase for labels (LCARS convention is uppercase)

## File Structure

```
PlatypusTools.UI/
├── Themes/
│   ├── LCARS.xaml      # Main LCARS theme resource dictionary
│   ├── Light.xaml      # Light theme
│   ├── Dark.xaml       # Dark theme
│   └── Glass.xaml      # Glass overlay
├── Services/
│   └── ThemeManager.cs # Theme switching logic
```

## Compatibility

- **Glass Effects**: Not compatible with LCARS theme (auto-disabled)
- **Windows Versions**: Works on all supported Windows versions
- **Accessibility**: WCAG AA compliant color contrast ratios

## Credits

LCARS is a trademark of CBS Studios Inc. This theme is a fan-made tribute and is not affiliated with or endorsed by CBS Studios or Paramount Global.

The LCARS interface design was created by Michael Okuda for Star Trek: The Next Generation.
