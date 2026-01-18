# DataGrid Pattern Guide for PlatypusTools Views

This document describes the **required pattern** for all DataGrid implementations in PlatypusTools views to ensure consistent column resizing, scrolling, and visual appearance.

## TL;DR - Required Pattern

```xml
<ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto">
    <DataGrid ItemsSource="{Binding YourItems}"
              AutoGenerateColumns="False"
              CanUserAddRows="False"
              CanUserResizeColumns="True"
              CanUserSortColumns="True"
              ColumnHeaderHeight="32"
              HorizontalScrollBarVisibility="Disabled"
              VerticalScrollBarVisibility="Disabled">
        <DataGrid.ColumnHeaderStyle>
            <Style TargetType="DataGridColumnHeader">
                <Setter Property="HorizontalContentAlignment" Value="Left"/>
                <Setter Property="Padding" Value="8,4"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
            </Style>
        </DataGrid.ColumnHeaderStyle>
        <DataGrid.Columns>
            <DataGridTextColumn Header="Column Name" Binding="{Binding Property}" 
                                Width="200" MinWidth="100" CanUserResize="True"/>
            <!-- More columns... -->
        </DataGrid.Columns>
    </DataGrid>
</ScrollViewer>
```

## Why This Pattern Works

### The Scrolling Architecture

1. **MainWindow Layer**: Each view in MainWindow is already wrapped in a `ScrollViewer`:
   ```xml
   <TabItem Header="Your Tab">
       <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
           <views:YourView DataContext="{Binding YourViewModel}" />
       </ScrollViewer>
   </TabItem>
   ```

2. **View Layer**: Inside each view, DataGrids must be wrapped in their own `ScrollViewer` with the DataGrid's internal scrollbars **disabled**.

### Why Disable DataGrid Scrollbars?

When DataGrid has `HorizontalScrollBarVisibility="Auto"`:
- It creates internal scroll handling
- This **conflicts with column resize functionality**
- Column headers get "stuck" because the grid is trying to scroll horizontally
- Double-click auto-size works but drag-resize doesn't

When we disable the DataGrid scrollbars and use an external ScrollViewer:
- Column resize works correctly (both drag and double-click)
- Horizontal scrolling is handled by the parent ScrollViewer
- No conflicts between resize and scroll behaviors

## Complete Attribute Reference

### Required DataGrid Attributes

| Attribute | Value | Purpose |
|-----------|-------|---------|
| `AutoGenerateColumns` | `False` | Explicit column control |
| `CanUserResizeColumns` | `True` | Enable column resizing |
| `CanUserSortColumns` | `True` | Enable column sorting |
| `ColumnHeaderHeight` | `32` | Consistent header height |
| `HorizontalScrollBarVisibility` | `Disabled` | **CRITICAL**: Prevents resize conflicts |
| `VerticalScrollBarVisibility` | `Disabled` | **CRITICAL**: Prevents resize conflicts |

### Required Column Attributes

| Attribute | Purpose |
|-----------|---------|
| `Width` | Initial width (can be fixed value or `*` for fill) |
| `MinWidth` | Minimum resize width to prevent columns from disappearing |
| `CanUserResize` | `True` - Enable individual column resize |

### Required ColumnHeaderStyle

```xml
<DataGrid.ColumnHeaderStyle>
    <Style TargetType="DataGridColumnHeader">
        <Setter Property="HorizontalContentAlignment" Value="Left"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
</DataGrid.ColumnHeaderStyle>
```

This ensures:
- Consistent left-aligned headers
- Proper padding for readability
- Semi-bold font for visual distinction

## Common Optional Attributes

```xml
<DataGrid 
    ...
    SelectionMode="Single"              <!-- or "Extended" for multi-select -->
    IsReadOnly="True"                   <!-- Prevent inline editing -->
    CanUserDeleteRows="False"           <!-- Prevent row deletion -->
    GridLinesVisibility="None"          <!-- or "Horizontal", "Vertical", "All" -->
    AlternatingRowBackground="#f8f8f8"  <!-- Zebra striping -->
/>
```

## Column Width Strategies

### Fixed Width
```xml
<DataGridTextColumn Header="Status" Width="100" MinWidth="80"/>
```

### Star (Fill) Width
```xml
<DataGridTextColumn Header="Description" Width="*" MinWidth="150"/>
```
Use `*` for the column that should expand to fill available space.

### Proportional Width
```xml
<DataGridTextColumn Header="Name" Width="2*" MinWidth="200"/>
<DataGridTextColumn Header="Type" Width="*" MinWidth="100"/>
```
The "Name" column will be twice as wide as "Type".

## Checklist for New Views

When creating a new view with a DataGrid:

- [ ] DataGrid is wrapped in a `ScrollViewer` with `Auto` scrollbars
- [ ] DataGrid has `HorizontalScrollBarVisibility="Disabled"`
- [ ] DataGrid has `VerticalScrollBarVisibility="Disabled"`
- [ ] DataGrid has `ColumnHeaderHeight="32"`
- [ ] DataGrid has `CanUserResizeColumns="True"`
- [ ] DataGrid has `CanUserSortColumns="True"` (if sorting is needed)
- [ ] DataGrid has `<DataGrid.ColumnHeaderStyle>` block
- [ ] Each column has `CanUserResize="True"`
- [ ] Each column has a `MinWidth` set
- [ ] The view is registered in MainWindow with a `ScrollViewer` wrapper

## Troubleshooting

### Columns won't resize by dragging
- Check that DataGrid has `HorizontalScrollBarVisibility="Disabled"`
- Verify there's a parent `ScrollViewer` with `HorizontalScrollBarVisibility="Auto"`

### Double-click resize works but drag doesn't
- Same cause as above - internal scroll conflicts with resize

### DataGrid content doesn't scroll
- Ensure there's a parent `ScrollViewer`
- Ensure DataGrid's scrollbars are `Disabled`

### Headers look different across views
- Add the `<DataGrid.ColumnHeaderStyle>` block
- Add `ColumnHeaderHeight="32"`

## Views Updated to Follow This Pattern

The following views have been verified to follow this pattern:

### Working Reference Views
- FileCleanerView.xaml (Preview DataGrid)
- VideoConverterView.xaml
- WebsiteDownloaderView.xaml
- StartupManagerView.xaml
- ArchiveManagerView.xaml

### All Updated Views
- UpscalerView.xaml
- NetworkToolsView.xaml (both DataGrids)
- ImageResizerView.xaml
- IconConverterView.xaml
- HiderView.xaml
- FileRenamerView.xaml
- FileAnalyzerView.xaml (all 6 DataGrids)
- EmptyFolderScannerView.xaml
- DuplicatesView.xaml
- DiskCleanupView.xaml
- AudioPlayerView.xaml
- MetadataEditorView.xaml (both DataGrids)
- RecentCleanupView.xaml
- ProcessManagerView.xaml
- RegistryCleanerView.xaml
- ScheduledTasksView.xaml
- SystemRestoreView.xaml
- MediaLibraryView.xaml
- SystemAuditView.xaml

---
*Last updated: January 14, 2026*
