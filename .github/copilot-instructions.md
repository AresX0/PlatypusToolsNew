# Copilot Instructions for PlatypusTools

## ⚠️ CRITICAL RULES - READ FIRST

### 🚫 NEVER REMOVE FUNCTIONALITY
**DO NOT remove any existing functionality, features, buttons, commands, or capabilities without explicit user approval.** This includes:
- Removing buttons or menu items
- Removing command bindings
- Removing ViewModel properties or methods
- Removing service methods
- Simplifying code by removing features

If you think something should be removed, ASK FIRST.

---

## 🔨 BUILD RULES

### THE ONE AND ONLY BUILD COMMAND

```powershell
cd C:\Projects\PlatypusToolsNew
.\Build-Release.ps1
```

**NEVER** run any other build commands for releases. No `dotnet build`, no `dotnet publish`, no manual MSI builds.

### Build-Release.ps1 Does Everything:
1. **Auto-increments version** (patch number)
2. **Updates version** in all files (csproj, Product.wxs)
3. **Cleans** all build directories
4. **Builds MSI** installer
5. **Copies MSI** to `C:\Projects\PlatypusToolsNew\releases\PlatypusToolsSetup-vX.Y.Z.msi`
6. **Builds portable EXE** to `publish\`

### Build Script Options:
```powershell
.\Build-Release.ps1                    # Auto-bump version and build
.\Build-Release.ps1 -NoVersionBump     # Rebuild without changing version
.\Build-Release.ps1 -Version "3.3.0.7" # Set specific version
.\Build-Release.ps1 -Archive           # Archive old builds before building
```

### Release Location
**ALL releases go to:** `C:\Projects\PlatypusToolsNew\releases\`

The MSI filename includes version: `PlatypusToolsSetup-vX.Y.Z.msi`

### GitHub Release Upload
After build completes, the script shows the exact command:
```powershell
gh release create vX.Y.Z "releases\PlatypusToolsSetup-vX.Y.Z.msi" --title "vX.Y.Z" --notes "Release notes"
```

---

## 🎨 UI DESIGN STANDARDS

### DataGrid Column Layout Pattern

**ALWAYS separate data columns from action columns.** Never put buttons in the same column as file paths or data.

#### ✅ CORRECT Pattern:
```xaml
<DataGrid.Columns>
    <DataGridTextColumn Header="Hash" Binding="{Binding Hash}" Width="250" />
    <DataGridTextColumn Header="# Files" Binding="{Binding Files.Count}" Width="60" />
    <DataGridTemplateColumn Header="Files" Width="*" MinWidth="200">
        <!-- Data content only - uses Width="*" to fill available space -->
        <DataGridTemplateColumn.CellTemplate>
            <DataTemplate>
                <StackPanel Orientation="Horizontal">
                    <CheckBox IsChecked="{Binding IsSelected}" />
                    <TextBlock Text="{Binding Path}" TextTrimming="CharacterEllipsis" ToolTip="{Binding Path}" />
                </StackPanel>
            </DataTemplate>
        </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>
    <DataGridTemplateColumn Header="Actions" Width="340" MinWidth="340">
        <!-- Buttons in SEPARATE column with fixed width -->
        <DataGridTemplateColumn.CellTemplate>
            <DataTemplate>
                <StackPanel Orientation="Horizontal">
                    <Button Content="Preview" Width="60" />
                    <Button Content="Open" Width="50" Margin="4,0,0,0" />
                    <Button Content="Folder" Width="55" Margin="4,0,0,0" />
                </StackPanel>
            </DataTemplate>
        </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>
</DataGrid.Columns>
```

#### ❌ WRONG Pattern (buttons in same column as data):
```xaml
<DataGridTemplateColumn Header="Files" Width="600">
    <DataTemplate>
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="{Binding Path}" Width="240" />  <!-- FIXED WIDTH = BAD -->
            <Button Content="Preview" />  <!-- BUTTONS IN SAME COLUMN = BAD -->
            <Button Content="Open" />
        </StackPanel>
    </DataTemplate>
</DataGridTemplateColumn>
```

### Standard Column Widths:
| Column Type | Width | Notes |
|-------------|-------|-------|
| Checkbox | 50-60 | Fixed |
| Hash/ID | 200-250 | Resizable |
| Count/Number | 60-80 | Fixed |
| File Path | `Width="*"` | Expands to fill, with `TextTrimming="CharacterEllipsis"` and `ToolTip` |
| Actions | 300-400 | Fixed, contains all buttons |
| Status | 80-100 | Fixed |

### Button Styling Standards:
```xaml
<!-- Primary Action (Green) -->
<Button Background="#FF4CAF50" Foreground="White" />

<!-- Secondary Action (Blue) -->
<Button Background="#FF2196F3" Foreground="White" />

<!-- Warning Action (Orange) -->
<Button Background="#FFFF9800" Foreground="White" />

<!-- Danger Action (Red) -->
<Button Background="#FFF44336" Foreground="White" />

<!-- Purple Action -->
<Button Background="#FF9C27B0" Foreground="White" />
```

### Standard View Layout:
```xaml
<Grid Margin="8">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />      <!-- Controls/Options -->
        <RowDefinition Height="Auto" />      <!-- Secondary controls -->
        <RowDefinition Height="5" />         <!-- GridSplitter -->
        <RowDefinition Height="*" />         <!-- Main DataGrid -->
        <RowDefinition Height="Auto" />      <!-- Status bar -->
    </Grid.RowDefinitions>
    
    <!-- GridSplitter between options and data -->
    <GridSplitter Grid.Row="2" Height="5" HorizontalAlignment="Stretch" 
                  Background="#CCCCCC" Cursor="SizeNS" ResizeDirection="Rows"/>
</Grid>
```

---

## 📁 Project Structure

| Path | Purpose |
|------|---------|
| `Build-Release.ps1` | **THE build script** - use this for all releases |
| `releases/` | **Output folder** for versioned MSI files |
| `publish/` | Self-contained EXE (portable version) |
| `PlatypusTools.UI/` | Main WPF application |
| `PlatypusTools.Core/` | Core services library |
| `PlatypusTools.Installer/` | WiX MSI installer project |

---

## ❌ DO NOT:
- Remove functionality without explicit approval
- Run `dotnet publish` manually for releases
- Run `dotnet build` on the installer project directly
- Copy MSI files to random locations
- Create new build scripts
- Manually edit version numbers
- Put buttons in the same DataGrid column as file paths
- Use fixed widths for file path columns (use `Width="*"`)

## ✅ DO:
- Always use `.\Build-Release.ps1`
- Check `releases\` folder for the output
- Use `gh release create` with the MSI from `releases\`
- Separate data columns from action columns in DataGrids
- Use `TextTrimming="CharacterEllipsis"` and `ToolTip` for paths
- Follow the button color conventions
- Ask before removing any existing functionality
