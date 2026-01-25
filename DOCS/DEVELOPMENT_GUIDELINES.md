# PlatypusTools Development Guidelines

**Created**: January 20, 2026  
**Purpose**: Ensure new features and tabs follow established patterns for consistency, performance, and maintainability.

---

## 📋 Quick Checklist for New Features

Before submitting code for a new tab or feature:

- [ ] ViewModel inherits from `BindableBase` or `DisposableBindableBase`
- [ ] Uses `RelayCommand` / `AsyncRelayCommand` for all commands
- [ ] Uses `FileDialogService` for file/folder dialogs
- [ ] Uses `ServiceLocator` for shared services
- [ ] Uses `ImageHelper` for all image loading
- [ ] DataGrids have virtualization enabled
- [ ] No outer ScrollViewer wrapping DataGrids
- [ ] CancellationTokenSource uses `DisposableHelper` patterns
- [ ] Tab registered in `TabVisibilityService` if user-configurable
- [ ] Follows existing XAML styling conventions

---

## 🏗️ Architecture Patterns

### ViewModel Base Classes

**Location**: `PlatypusTools.UI/ViewModels/`

| Class | Use When |
|-------|----------|
| `BindableBase` | Standard ViewModel with property change notification |
| `DisposableBindableBase` | ViewModel with CancellationTokenSource or other disposable resources |
| `AsyncBindableBase` | ViewModel requiring async initialization (heavy I/O on load) |

```csharp
// Standard ViewModel
public class MyViewModel : BindableBase
{
    private string _myProperty = string.Empty;
    public string MyProperty
    {
        get => _myProperty;
        set { _myProperty = value; RaisePropertyChanged(); }
    }
    
    // Or use SetProperty helper:
    public string MyProperty2
    {
        get => _myProperty;
        set => SetProperty(ref _myProperty, value);
    }
}

// ViewModel with cancellable operations
public class MyLongRunningViewModel : DisposableBindableBase
{
    public async Task DoWorkAsync()
    {
        var cts = GetOrCreateOperationCts(); // Auto-cancels previous operation
        try
        {
            await SomeService.ProcessAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
    }
    
    public void Cancel() => CancelCurrentOperation();
    
    protected override void DisposeManagedResources()
    {
        // Clean up additional resources here
    }
}

// ViewModel with deferred initialization
public class MyHeavyViewModel : AsyncBindableBase
{
    protected override async Task OnInitializeAsync(CancellationToken ct)
    {
        // This runs once when tab is first shown
        await LoadDataFromDiskAsync(ct);
    }
}
```

---

## 🎮 Command Patterns

**Location**: `PlatypusTools.UI/ViewModels/RelayCommand.cs`, `AsyncRelayCommand.cs`

### Synchronous Commands

```csharp
public ICommand MyCommand { get; }

public MyViewModel()
{
    MyCommand = new RelayCommand(_ => DoSomething(), _ => CanDoSomething());
}
```

### Async Commands

```csharp
public ICommand ProcessCommand { get; }

public MyViewModel()
{
    // Parameterless async
    ProcessCommand = new AsyncRelayCommand(ProcessAsync, () => !IsProcessing);
}

private async Task ProcessAsync()
{
    IsProcessing = true;
    try
    {
        await _service.DoWorkAsync();
    }
    finally
    {
        IsProcessing = false;
    }
}
```

### Typed Async Commands

```csharp
public ICommand DeleteItemCommand { get; }

public MyViewModel()
{
    DeleteItemCommand = new AsyncRelayCommand<MyItem>(DeleteItemAsync);
}

private async Task DeleteItemAsync(MyItem item)
{
    await _service.DeleteAsync(item);
}
```

---

## 📁 File Dialogs

**Location**: `PlatypusTools.UI/Services/FileDialogService.cs`

**ALWAYS use FileDialogService instead of creating dialogs manually.**

```csharp
// Folder selection
var folder = FileDialogService.BrowseForFolder("Select output folder");
var sourceFolder = FileDialogService.BrowseForSourceFolder();
var outputFolder = FileDialogService.BrowseForOutputFolder();

// Single file
var file = FileDialogService.OpenFile("Select a file", FileDialogService.AllFilter);
var video = FileDialogService.OpenFile("Select video", FileDialogService.VideoFilter);

// Multiple files
var files = FileDialogService.OpenFiles("Select files", FileDialogService.ImageFilter);

// Specialized dialogs (use these when applicable)
var videos = FileDialogService.OpenVideoFiles();
var audios = FileDialogService.OpenAudioFiles();
var images = FileDialogService.OpenImageFiles();
var media = FileDialogService.OpenMediaFiles();
var pdfs = FileDialogService.OpenPdfFiles();

// Save dialog
var savePath = FileDialogService.SaveFile("Save As", FileDialogService.PdfFilter, "output.pdf");
```

### Available Filters

```csharp
FileDialogService.VideoFilter  // "Video Files|*.mp4;*.avi;*.mkv;..."
FileDialogService.AudioFilter  // "Audio Files|*.mp3;*.wav;*.flac;..."
FileDialogService.ImageFilter  // "Image Files|*.jpg;*.png;*.bmp;..."
FileDialogService.MediaFilter  // "Media Files|*.mp4;*.mp3;*.jpg;..."
FileDialogService.PdfFilter    // "PDF Files|*.pdf"
FileDialogService.AllFilter    // "All Files|*.*"
```

---

## 🔧 Service Access

**Location**: `PlatypusTools.UI/Services/ServiceLocator.cs`

**Use ServiceLocator for shared/singleton services instead of creating new instances.**

```csharp
// ❌ DON'T create new instances
private readonly FFmpegService _ffmpeg = new();

// ✅ DO use ServiceLocator
private readonly FFmpegService _ffmpeg = ServiceLocator.FFmpeg;
private readonly FFprobeService _ffprobe = ServiceLocator.FFprobe;
private readonly VideoConverterService _converter = ServiceLocator.VideoConverter;
```

### Available Services

```csharp
// Media services
ServiceLocator.FFmpeg
ServiceLocator.FFprobe
ServiceLocator.BeatDetection
ServiceLocator.VideoConverter
ServiceLocator.VideoCombiner
ServiceLocator.Upscaler

// System services
ServiceLocator.ProcessManager
ServiceLocator.ScheduledTasks
ServiceLocator.StartupManager
ServiceLocator.SystemRestore
ServiceLocator.FileRenamer
ServiceLocator.DiskSpaceAnalyzer
ServiceLocator.PdfTools

// Video editor services
ServiceLocator.TimelineOperations
ServiceLocator.KeyframeInterpolator
```

---

## 🖼️ Image Loading

**Location**: `PlatypusTools.UI/Utilities/ImageHelper.cs`

**ALWAYS use ImageHelper for loading images to prevent memory leaks.**

```csharp
// ❌ DON'T do this (memory leak, file lock)
var bmp = new BitmapImage(new Uri(path));

// ✅ DO use ImageHelper
var image = ImageHelper.LoadFromFile(path);
var thumbnail = ImageHelper.LoadThumbnail(path, 200);  // 200px width
var fromBitmap = ImageHelper.FromDrawingBitmap(bitmap);
var fromIcon = ImageHelper.FromIcon(icon);
```

### Why ImageHelper?

- Uses `CacheOption.OnLoad` to release file handles immediately
- Calls `Freeze()` for thread-safety and reduced memory
- Handles errors gracefully (returns null on failure)
- Consistent loading pattern across the app

---

## 🗑️ Disposal Patterns

**Location**: `PlatypusTools.UI/Utilities/DisposableHelper.cs`

### CancellationTokenSource Management

```csharp
private CancellationTokenSource? _cts;

// Starting a new operation (cancels previous)
_cts = DisposableHelper.ReplaceCts(ref _cts);
await DoWorkAsync(_cts.Token);

// Cancelling
DisposableHelper.SafeCancel(_cts);

// Cleanup
DisposableHelper.SafeDisposeCts(ref _cts);
```

### Generic Disposal

```csharp
private SomeDisposable? _resource;

// Safe dispose with null check
DisposableHelper.SafeDispose(ref _resource);
```

---

## 📊 DataGrid Best Practices

### ✅ Correct Pattern

```xml
<DataGrid ItemsSource="{Binding Items}"
          AutoGenerateColumns="False"
          CanUserAddRows="False"
          ScrollViewer.HorizontalScrollBarVisibility="Auto"
          ScrollViewer.VerticalScrollBarVisibility="Auto"
          EnableRowVirtualization="True"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling">
    <DataGrid.Columns>
        <!-- columns here -->
    </DataGrid.Columns>
</DataGrid>
```

### ❌ Incorrect Pattern (causes scrolling issues)

```xml
<!-- DON'T wrap DataGrid in ScrollViewer -->
<ScrollViewer>
    <DataGrid HorizontalScrollBarVisibility="Disabled" 
              VerticalScrollBarVisibility="Disabled">
    </DataGrid>
</ScrollViewer>
```

### Why?

- Outer ScrollViewer breaks DataGrid's built-in virtualization
- Headers scroll away instead of staying frozen
- Mouse wheel scrolling doesn't work properly

---

## 📋 ListBox/ListView Virtualization

```xml
<ListBox ItemsSource="{Binding LargeCollection}"
         VirtualizingStackPanel.IsVirtualizing="True"
         VirtualizingStackPanel.VirtualizationMode="Recycling"
         ScrollViewer.CanContentScroll="True">
</ListBox>
```

---

## 🎨 XAML Styling Conventions

### Theme Resources

**Location**: `PlatypusTools.UI/Themes/Light.xaml`, `Dark.xaml`

Use theme resources for colors:

```xml
<!-- Text colors -->
<TextBlock Foreground="{DynamicResource TextPrimary}" />
<TextBlock Foreground="{DynamicResource TextSecondary}" />

<!-- Backgrounds -->
<Border Background="{DynamicResource BackgroundPrimary}" />
<Border Background="{DynamicResource BackgroundSecondary}" />

<!-- Accents -->
<Button Background="{DynamicResource AccentBrush}" />
```

### Standard Margins & Padding

```xml
<!-- Page/Tab content -->
<Grid Margin="16">

<!-- GroupBox padding -->
<GroupBox Header="Section" Padding="8">

<!-- Button padding -->
<Button Padding="16,8" />        <!-- Primary actions -->
<Button Padding="12,6" />        <!-- Secondary actions -->
<Button Padding="8,4" />         <!-- Compact/toolbar -->

<!-- Spacing between elements -->
<StackPanel Spacing="8">         <!-- Standard -->
<StackPanel Spacing="4">         <!-- Compact -->
```

### Common Button Styles

```xml
<!-- Primary action (blue) -->
<Button Content="Process" Background="#1E88E5" Foreground="White" Padding="24,8" />

<!-- Danger action (red) -->
<Button Content="Delete" Background="#F44336" Foreground="White" Padding="16,8" />

<!-- Secondary action (default) -->
<Button Content="Cancel" Padding="16,8" />
```

### GroupBox Layout

```xml
<GroupBox Header="Settings" Padding="8" Margin="0,0,0,8">
    <StackPanel>
        <!-- content -->
    </StackPanel>
</GroupBox>
```

---

## 🔌 Tab Registration

### Adding a New Tab

1. **Create View** in `Views/MyNewView.xaml`
2. **Create ViewModel** in `ViewModels/MyNewViewModel.cs`
3. **Add Lazy property** in `MainWindowViewModel.cs`:

```csharp
private readonly Lazy<MyNewViewModel> _myNewVM = new(() => new MyNewViewModel());
public MyNewViewModel MyNewVM => _myNewVM.Value;
```

4. **Add TabItem** in `MainWindow.xaml`:

```xml
<TabItem Header="My New Tab" 
         Visibility="{Binding IsMyNewTabVisible, Source={x:Static services:TabVisibilityService.Instance}, Converter={StaticResource BoolToVisibility}}">
    <controls:LazyTabContent>
        <views:MyNewView DataContext="{Binding MyNewVM}" />
    </controls:LazyTabContent>
</TabItem>
```

5. **Register in TabVisibilityService** (if user-configurable):

```csharp
// In TabVisibilityService.cs
private bool _isMyNewTabVisible = true;
public bool IsMyNewTabVisible
{
    get => _isMyNewTabVisible;
    set { _isMyNewTabVisible = value; RaisePropertyChanged(); }
}
```

6. **Add to Settings UI** in `SettingsWindow.xaml`:

```xml
<CheckBox Content="My New Tab" IsChecked="{Binding IsMyNewTabVisible, Source={x:Static services:TabVisibilityService.Instance}}" />
```

---

## ⚡ Performance Guidelines

### Startup

- Use `Lazy<T>` for ViewModels in MainWindowViewModel
- Use `LazyTabContent` control for tab content
- Use `AsyncBindableBase` for heavy initialization
- Don't block the UI thread in constructors

### Long-Running Operations

- Always use `async/await` for I/O operations
- Support cancellation with `CancellationToken`
- Show progress with `StatusBarViewModel.Instance.StartOperation()`
- Update UI via non-blocking `Dispatcher.InvokeAsync()` (avoid blocking `Invoke()`)

```csharp
public async Task ProcessAsync()
{
    var cts = GetOrCreateOperationCts();
    StatusBarViewModel.Instance.StartOperation("Processing...", isCancellable: true);
    
    try
    {
        await Task.Run(async () =>
        {
            for (int i = 0; i < items.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                await ProcessItem(items[i]);
                
                // Use InvokeAsync to avoid blocking background thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Progress = (i + 1) * 100 / items.Count;
                    StatusMessage = $"Processing {i + 1} of {items.Count}...";
                });
            }
        }, cts.Token);
    }
    finally
    {
        StatusBarViewModel.Instance.EndOperation();
    }
}

// For event handlers (non-async), use fire-and-forget pattern:
private void OnDataReceived(object? sender, string data)
{
    _ = Application.Current.Dispatcher.InvokeAsync(() => OutputText += data);
}
```

### Memory

- Use `ImageHelper` for all image loading
- Dispose `CancellationTokenSource` when done
- Use virtualization for large lists
- Call `Freeze()` on frozen objects

---

## 📁 File Organization

```
PlatypusTools.UI/
├── Controls/           # Reusable custom controls
├── Converters/         # IValueConverter implementations
├── Models/             # Data models (non-ViewModel)
├── Services/           # Business logic services
│   ├── FileDialogService.cs
│   ├── ServiceLocator.cs
│   └── TabVisibilityService.cs
├── Themes/             # Resource dictionaries
│   ├── Light.xaml
│   └── Dark.xaml
├── Utilities/          # Helper classes
│   ├── DisposableHelper.cs
│   ├── ImageHelper.cs
│   └── StartupProfiler.cs
├── ViewModels/         # MVVM ViewModels
│   ├── BindableBase.cs
│   ├── DisposableBindableBase.cs
│   ├── AsyncBindableBase.cs
│   ├── RelayCommand.cs
│   └── AsyncRelayCommand.cs
└── Views/              # XAML views
```

---

## 🧪 Testing Checklist

Before merging new code:

1. **Build succeeds** with no errors
2. **App launches** without exceptions
3. **New tab loads** without blocking UI
4. **Scrolling works** in DataGrids (headers stay frozen)
5. **Cancel buttons** properly cancel operations
6. **Memory usage** doesn't grow unbounded during operations
7. **Tab visibility** setting works if applicable

---

## 📚 Related Documentation

- [TODO_PRIORITY.md](TODO_PRIORITY.md) - Feature roadmap and implementation status
- [TODO.md](TODO.md) - Full project TODO list
- [IMPLEMENTATION_MANIFEST.md](IMPLEMENTATION_MANIFEST.md) - Detailed feature specs
- [PROJECT_DOCUMENTATION.md](PROJECT_DOCUMENTATION.md) - File structure reference
- [BUILD.md](../BUILD.md) - Build instructions

---

*Last updated: January 20, 2026*
