# Error-Resilient Tab Loading Pattern

## Overview

PlatypusTools uses an error-resilient tab loading pattern to ensure that if one view/tab has an issue (XAML error, missing resource, etc.), it doesn't crash the entire application. Other tabs remain functional.

## Components

### 1. LazyTabContent Control

Located in: `PlatypusTools.UI/Controls/LazyTabContent.cs`

A ContentControl that:
- **Lazy loads** views only when the tab becomes visible (faster startup)
- **Catches exceptions** during view instantiation
- **Displays error UI** inline instead of crashing
- **Provides retry** functionality

#### Usage:

```xaml
<TabItem Header="My Feature">
    <controls:LazyTabContent 
        ViewType="{x:Type views:MyFeatureView}" 
        ViewDataContext="{Binding MyFeatureViewModel}" />
</TabItem>
```

### 2. SafeContentLoader Control

Located in: `PlatypusTools.UI/Controls/SafeContentLoader.cs`

A ContentControl that wraps any content and catches instantiation errors.

#### Usage:

```xaml
<controls:SafeContentLoader ViewType="{x:Type views:MyView}" 
                            ViewDataContext="{Binding MyViewModel}" />
```

## Benefits

1. **Application Stability**: One broken view doesn't crash the entire app
2. **Faster Startup**: Views are loaded on-demand, not all at once
3. **Better UX**: Users see helpful error messages instead of crash dialogs
4. **Debugging**: Error details are shown inline and logged to Debug output
5. **Recovery**: Retry buttons allow users to attempt reloading

## Implementation Guidelines

### For New Views

1. Create your View (UserControl) and ViewModel as normal
2. Register the ViewModel property in `MainWindowViewModel.cs`
3. Add the tab using `LazyTabContent`:

```xaml
<TabItem Header="New Feature">
    <controls:LazyTabContent 
        ViewType="{x:Type views:NewFeatureView}" 
        ViewDataContext="{Binding NewFeature}" />
</TabItem>
```

### For Existing Views

Replace direct view references with `LazyTabContent`:

**Before:**
```xaml
<TabItem Header="My Tab">
    <ScrollViewer>
        <views:MyView DataContext="{Binding MyViewModel}" />
    </ScrollViewer>
</TabItem>
```

**After:**
```xaml
<TabItem Header="My Tab">
    <controls:LazyTabContent 
        ViewType="{x:Type views:MyView}" 
        ViewDataContext="{Binding MyViewModel}" />
</TabItem>
```

### Required XAML Namespace

Add to your XAML file:
```xaml
xmlns:controls="clr-namespace:PlatypusTools.UI.Controls"
```

## Error Handling Flow

```
Tab Selected
    │
    ▼
LazyTabContent.OnVisibilityChanged()
    │
    ▼
LoadViewAsync()
    │
    ├─► Success: Display view with DataContext
    │
    └─► Exception: Display error UI with:
            - Error message
            - View name that failed
            - Exception details (expandable)
            - Retry button
```

## Debugging Tips

1. Check Debug Output window for `[LazyTabContent]` log entries
2. Expand "Technical Details" in error display for full stack trace
3. Common issues:
   - Missing StaticResource → Add resource to App.xaml
   - Missing converter → Register in App.xaml Resources
   - Null DataContext → Check ViewModel initialization in MainWindowViewModel

## Converter Best Practices

To avoid StaticResource errors:

1. **Register all converters in App.xaml**:
```xaml
<Application.Resources>
    <converters:MyConverter x:Key="MyConverter" />
</Application.Resources>
```

2. **Use consistent naming**: Key should match class name
3. **Prefer DynamicResource** for theme-related resources
4. **Test each view individually** before integrating

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                      MainWindow                          │
│  ┌─────────────────────────────────────────────────────┐│
│  │                    TabControl                        ││
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐   ││
│  │  │   TabItem   │ │   TabItem   │ │   TabItem   │   ││
│  │  │ ┌─────────┐ │ │ ┌─────────┐ │ │ ┌─────────┐ │   ││
│  │  │ │  Lazy   │ │ │ │  Lazy   │ │ │ │  Lazy   │ │   ││
│  │  │ │ TabCont │ │ │ │ TabCont │ │ │ │ TabCont │ │   ││
│  │  │ │ (View1) │ │ │ │ (View2) │ │ │ │ (ERROR) │ │   ││
│  │  │ └─────────┘ │ │ └─────────┘ │ │ └─────────┘ │   ││
│  │  └─────────────┘ └─────────────┘ └─────────────┘   ││
│  └─────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
                           │
        View3 has error ───┘ but View1 & View2 work fine!
```
