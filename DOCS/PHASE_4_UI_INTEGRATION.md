# Phase 4: UI Integration Guide

## Overview

This guide walks through integrating the library indexing system into the WPF UI. Phase 4 connects ViewModel methods to XAML controls, enabling users to scan libraries, search, and organize their audio collection.

## Current State

✅ **ViewModel Ready**: All library methods implemented  
✅ **Service Layer**: LibraryIndexService initialized  
✅ **ObservableCollections**: Ready for UI binding  
✅ **Build Status**: Zero errors  

## Architecture Overview

```
┌─────────────────────────────────────────┐
│  UI Layer (MainWindow.xaml)             │
│  - Scan Button                          │
│  - Search TextBox                       │
│  - Organize Mode ComboBox               │
│  - Library Groups Display (TreeView)    │
│  - Tracks Display (DataGrid)            │
│  - Progress Display                     │
└──────────────────┬──────────────────────┘
                   │ Binding & Commands
┌──────────────────▼──────────────────────┐
│  AudioPlayerViewModel                   │
│  - ObservableCollections                │
│  - Commands (ScanLibrary, Search)       │
│  - Properties (IsScanning, ScanStatus)  │
└──────────────────┬──────────────────────┘
                   │ Method Calls
┌──────────────────▼──────────────────────┐
│  Services                               │
│  - LibraryIndexService                  │
│  - MetadataExtractorService             │
└─────────────────────────────────────────┘
```

## XAML Changes Required

### 1. Add Organize Mode Items

In `AudioPlayerViewModel.cs`, add organize mode definitions:

```csharp
public ObservableCollection<string> OrganizeModes { get; } = new()
{
    "All Tracks",
    "By Artist",
    "By Album",
    "By Genre",
    "By Folder"
};
```

### 2. Add Commands

Add RelayCommand properties to ViewModel:

```csharp
public RelayCommand<string> ScanLibraryCommand { get; }
public RelayCommand SearchCommand { get; }
public RelayCommand ClearSearchCommand { get; }

public AudioPlayerViewModel()
{
    // ... existing code ...
    
    ScanLibraryCommand = new RelayCommand<string>(async (directory) =>
    {
        if (!string.IsNullOrEmpty(directory))
            await ScanLibraryDirectoryAsync(directory);
    });
    
    SearchCommand = new RelayCommand(async () =>
    {
        SearchLibrary(SearchQuery);
    });
    
    ClearSearchCommand = new RelayCommand(() =>
    {
        SearchQuery = "";
        SearchLibrary("");
    });
}
```

## UI Implementation Steps

### Step 1: Add Scan Library Section

**Location**: MainWindow.xaml - Add to existing UI

```xml
<!-- Library Scanning Section -->
<Grid Background="#F5F5F5" Margin="0,0,0,10">
    <StackPanel Orientation="Vertical" Padding="10">
        <TextBlock Text="Library Management" 
                   FontSize="14" FontWeight="Bold" Margin="0,0,0,10"/>
        
        <!-- Scan Controls -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <Button Content="Scan Music Folder" 
                    Command="{Binding ScanLibraryCommand}"
                    CommandParameter="{Binding SelectedMusicFolder}"
                    Width="150" Padding="10,5"/>
            
            <TextBlock Text="{Binding ScanStatus}" 
                       Margin="10,0,0,0" VerticalAlignment="Center"
                       Foreground="#333333"/>
        </StackPanel>
        
        <!-- Scan Progress -->
        <ProgressBar Value="{Binding ScanProgress}" 
                     IsIndeterminate="{Binding IsScanning}"
                     Height="20" Margin="0,0,0,10"/>
        
        <!-- Statistics -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBlock Text="Tracks: " FontWeight="Bold"/>
            <TextBlock Text="{Binding LibraryTrackCount}"/>
            
            <TextBlock Text=" | Artists: " FontWeight="Bold" Margin="10,0,0,0"/>
            <TextBlock Text="{Binding LibraryArtistCount}"/>
            
            <TextBlock Text=" | Albums: " FontWeight="Bold" Margin="10,0,0,0"/>
            <TextBlock Text="{Binding LibraryAlbumCount}"/>
        </StackPanel>
    </StackPanel>
</Grid>

<!-- Add Statistics Properties to ViewModel -->
<!--
public int LibraryTrackCount => _allLibraryTracks.Count;
public int LibraryArtistCount => _libraryIndexService?.GetCurrentIndex()?.Statistics?.ArtistCount ?? 0;
public int LibraryAlbumCount => _libraryIndexService?.GetCurrentIndex()?.Statistics?.AlbumCount ?? 0;
-->
```

### Step 2: Add Organize Mode Selector

**Location**: MainWindow.xaml - Library Display Section

```xml
<!-- Organize Mode Selector -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,10">
    <TextBlock Text="View:" FontWeight="Bold" VerticalAlignment="Center"/>
    
    <ComboBox ItemsSource="{Binding OrganizeModes}"
              SelectedIndex="{Binding OrganizeModeIndex, UpdateSourceTrigger=PropertyChanged}"
              Width="150" Margin="10,0,0,0"
              SelectionChanged="OrganizeMode_SelectionChanged"/>
</ComboBox>
```

**Code-Behind Handler** (or convert to binding):

```csharp
private void OrganizeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    // ViewModel will automatically trigger RebuildLibraryGroups via property change
    viewModel.RebuildLibraryGroups();
}
```

### Step 3: Add Search Section

**Location**: MainWindow.xaml - Top of Library Section

```xml
<!-- Search Section -->
<Grid Background="#FFFFFF" Margin="0,0,0,10" Padding="10">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="Search:" FontWeight="Bold" VerticalAlignment="Center"/>
        
        <TextBox Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}"
                 Width="300" Height="30" Margin="10,0,0,0"
                 KeyDown="SearchBox_KeyDown"/>
        
        <Button Content="Clear" 
                Command="{Binding ClearSearchCommand}"
                Width="80" Margin="10,0,0,0" Padding="10,5"/>
    </StackPanel>
</Grid>

<!-- Add SearchBox_KeyDown Handler -->
<!--
private void SearchBox_KeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Return)
    {
        viewModel.SearchLibrary(viewModel.SearchQuery);
        e.Handled = true;
    }
}
-->
```

### Step 4: Update Library Groups Display

**Location**: MainWindow.xaml - Replace existing library display

```xml
<!-- Library Groups TreeView -->
<TreeView ItemsSource="{Binding LibraryGroups}" 
          Height="200" Margin="0,0,0,10"
          BorderBrush="#CCCCCC" BorderThickness="1">
    <TreeView.ItemTemplate>
        <HierarchicalDataTemplate ItemsSource="{Binding Value}">
            <!-- Group Header -->
            <TextBlock Text="{Binding Key}" FontWeight="Bold" Foreground="#0066CC"/>
            
            <!-- Track Items (Nested) -->
            <HierarchicalDataTemplate.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="{Binding DisplayTitle}" Width="200"/>
                        <TextBlock Text="{Binding DisplayArtist}" Width="150" Foreground="#666666"/>
                        <TextBlock Text="{Binding DisplayAlbum}" Width="150" Foreground="#666666"/>
                    </StackPanel>
                </DataTemplate>
            </HierarchicalDataTemplate.ItemTemplate>
        </HierarchicalDataTemplate>
    </TreeView.ItemTemplate>
</TreeView>
```

### Step 5: Update Tracks Display

**Location**: MainWindow.xaml - Replace existing track list

```xml
<!-- Library Tracks DataGrid -->
<DataGrid ItemsSource="{Binding LibraryTracks}"
          AutoGenerateColumns="False"
          Height="300"
          BorderBrush="#CCCCCC" BorderThickness="1">
    
    <DataGrid.Columns>
        <!-- Title Column -->
        <DataGridTextColumn Header="Title" Binding="{Binding DisplayTitle}" Width="200"/>
        
        <!-- Artist Column -->
        <DataGridTextColumn Header="Artist" Binding="{Binding DisplayArtist}" Width="150"/>
        
        <!-- Album Column -->
        <DataGridTextColumn Header="Album" Binding="{Binding DisplayAlbum}" Width="150"/>
        
        <!-- Genre Column -->
        <DataGridTextColumn Header="Genre" Binding="{Binding Genre}" Width="100"/>
        
        <!-- Duration Column -->
        <DataGridTextColumn Header="Duration" Binding="{Binding DurationFormatted}" Width="80"/>
        
        <!-- Action Column -->
        <DataGridTemplateColumn Header="Action" Width="80">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Button Content="Play" Padding="5,2"
                            Command="{Binding RelativeSource={RelativeSource AncestorType=Window}, Path=DataContext.PlayTrackCommand}"
                            CommandParameter="{Binding}"/>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
    </DataGrid.Columns>
</DataGrid>
```

### Step 6: Add Progress Display

**Location**: MainWindow.xaml - Add to status bar

```xml
<!-- Status Bar -->
<StatusBar Height="30" Background="#F0F0F0" Padding="5,0,5,0">
    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
        <!-- Scanning Indicator -->
        <TextBlock Text="Scanning..." 
                   Visibility="{Binding IsScanning, Converter={StaticResource BoolToVisibilityConverter}}"
                   Foreground="#FF6600" FontWeight="Bold"/>
        
        <!-- Status Message -->
        <TextBlock Text="{Binding ScanStatus}" 
                   Margin="20,0,0,0"
                   Foreground="#333333"/>
        
        <!-- Track Count -->
        <TextBlock Text="{Binding LibraryTrackCount, StringFormat='Tracks: {0}'}" 
                   Margin="auto,0,0,0"/>
    </StackPanel>
</StatusBar>
```

## ViewModel Properties to Add

Add these properties to support UI binding:

```csharp
// For UI Display
public int LibraryTrackCount 
    => _allLibraryTracks?.Count ?? 0;

public int LibraryArtistCount 
    => _libraryIndexService?.GetCurrentIndex()?.Statistics?.ArtistCount ?? 0;

public int LibraryAlbumCount 
    => _libraryIndexService?.GetCurrentIndex()?.Statistics?.AlbumCount ?? 0;

// Folder Selection
private string _selectedMusicFolder = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Music");
public string SelectedMusicFolder
{
    get => _selectedMusicFolder;
    set { if (_selectedMusicFolder != value) { _selectedMusicFolder = value; OnPropertyChanged(); } }
}

// Scan Progress
private int _scanProgress;
public int ScanProgress
{
    get => _scanProgress;
    set { if (_scanProgress != value) { _scanProgress = value; OnPropertyChanged(); } }
}

// Search Status
private string _searchQuery = "";
public string SearchQuery
{
    get => _searchQuery;
    set 
    { 
        if (_searchQuery != value) 
        { 
            _searchQuery = value; 
            OnPropertyChanged();
            // Auto-search as user types
            FilterLibraryTracks();
        } 
    }
}
```

## Implementation Checklist

### UI Controls
- [ ] Add "Scan Music Folder" button
- [ ] Add search textbox with clear button
- [ ] Add organize mode combobox
- [ ] Add library groups display (TreeView or ListBox)
- [ ] Add tracks display (DataGrid)
- [ ] Add progress bar for scanning
- [ ] Add status bar for messages
- [ ] Add statistics display (track/artist/album counts)

### ViewModel Integration
- [ ] Add ScanLibraryCommand
- [ ] Add SearchCommand
- [ ] Add ClearSearchCommand
- [ ] Add PlayTrackCommand (for double-click)
- [ ] Add OrganizeModes collection
- [ ] Add LibraryTrackCount property
- [ ] Add LibraryArtistCount property
- [ ] Add LibraryAlbumCount property
- [ ] Add ScanProgress property
- [ ] Add SelectedMusicFolder property

### Data Binding
- [ ] Bind organize mode to OrganizeModeIndex
- [ ] Bind search textbox to SearchQuery
- [ ] Bind library groups to LibraryGroups ObservableCollection
- [ ] Bind tracks to LibraryTracks ObservableCollection
- [ ] Bind progress bar to IsScanning and ScanProgress
- [ ] Bind status text to ScanStatus
- [ ] Bind button commands to ViewModel commands

### Testing
- [ ] Test scan button opens folder dialog
- [ ] Test scanning with progress display
- [ ] Test search updates track list in real-time
- [ ] Test organize mode changes view
- [ ] Test double-click plays track
- [ ] Test status displays correctly
- [ ] Test error messages display
- [ ] Test with real audio files

## Common Issues & Solutions

### Issue: Search Not Updating
**Solution**: Ensure `UpdateSourceTrigger=PropertyChanged` on TextBox

```xml
<TextBox Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}"/>
```

### Issue: Organize Mode Not Triggering Refresh
**Solution**: Call RebuildLibraryGroups in property setter

```csharp
private int _organizeModeIndex;
public int OrganizeModeIndex
{
    get => _organizeModeIndex;
    set
    {
        if (_organizeModeIndex != value)
        {
            _organizeModeIndex = value;
            OnPropertyChanged();
            RebuildLibraryGroups();  // Trigger update
        }
    }
}
```

### Issue: Scanning Blocks UI
**Solution**: Ensure ScanLibraryDirectoryAsync is called with await

```csharp
private async void ScanButton_Click(object sender, RoutedEventArgs e)
{
    await viewModel.ScanLibraryDirectoryAsync(selectedFolder);
}
```

### Issue: TreeView Not Displaying
**Solution**: Check LibraryGroups collection structure

```csharp
// Expected structure:
LibraryGroups = new ObservableCollection<KeyValuePair<string, List<AudioTrack>>>
{
    new KeyValuePair<string, List<AudioTrack>>("Artist 1", trackList1),
    new KeyValuePair<string, List<AudioTrack>>("Artist 2", trackList2),
}
```

## Next Steps After UI Integration

1. **Manual Testing** (Phase 5)
   - Test with real audio library
   - Verify persistence across restarts
   - Test all search/filter combinations
   - Test with large library (1000+ tracks)

2. **Performance Profiling** (Phase 6)
   - Measure scan speed
   - Optimize bottlenecks
   - Profile UI responsiveness

3. **Release Preparation** (Phase 7)
   - Create release build
   - Package installer
   - Create user documentation

## Resources

- MVVM Command Pattern: https://docs.microsoft.com/en-us/dotnet/desktop/wpf/advanced/commanding-overview
- WPF Data Binding: https://docs.microsoft.com/en-us/dotnet/desktop/wpf/data/data-binding-overview
- ObservableCollection: https://docs.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.observablecollection-1

## Summary

Phase 4 UI Integration will:
✅ Connect ViewModel methods to XAML controls  
✅ Enable library scanning from UI  
✅ Enable search and filtering  
✅ Display library with organization options  
✅ Show scan progress and status  
✅ Display library statistics  

**Expected Time**: 4-6 hours  
**Expected Complexity**: Medium  
**Testing Required**: Manual E2E testing  
**Next Phase**: Phase 5 (E2E Testing)
