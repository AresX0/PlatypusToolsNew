# Contributing to PlatypusTools.NET

Thank you for your interest in contributing to PlatypusTools.NET! This document provides guidelines for contributing to the project.

## Code of Conduct

- Be respectful and inclusive
- Welcome newcomers and help them get started
- Focus on constructive feedback
- Respect different viewpoints and experiences

## How to Contribute

### Reporting Bugs

If you find a bug:

1. **Check existing issues** to avoid duplicates
2. **Create a new issue** with:
   - Clear, descriptive title
   - Steps to reproduce
   - Expected vs actual behavior
   - Screenshots if applicable
   - System information (OS, .NET version)

### Suggesting Features

For feature requests:

1. **Search existing issues** first
2. **Create a new issue** describing:
   - The problem you're trying to solve
   - Your proposed solution
   - Alternative solutions considered
   - Why this would benefit other users

### Pull Requests

#### Before Starting

1. **Open an issue** to discuss major changes
2. **Fork the repository**
3. **Clone your fork**:
   ```powershell
   git clone https://github.com/yourusername/PlatypusTools.git
   ```

#### Development Workflow

1. **Create a feature branch**:
   ```powershell
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes**:
   - Follow existing code style
   - Add tests for new features
   - Update documentation as needed

3. **Test your changes**:
   ```powershell
   # Run all tests
   dotnet test
   
   # Build to ensure no errors
   dotnet build -c Release
   ```

4. **Commit your changes**:
   ```powershell
   git add .
   git commit -m "Add feature: description"
   ```
   
   Commit message format:
   - `Add feature: description` - New features
   - `Fix: description` - Bug fixes
   - `Update: description` - Updates to existing features
   - `Refactor: description` - Code refactoring
   - `Docs: description` - Documentation only

5. **Push to your fork**:
   ```powershell
   git push origin feature/your-feature-name
   ```

6. **Create Pull Request**:
   - Go to the original repository
   - Click "New Pull Request"
   - Select your fork and branch
   - Fill in the PR template

#### Pull Request Guidelines

- **One feature per PR** - Keep PRs focused
- **Add tests** - For new features or bug fixes
- **Update docs** - If changing functionality
- **Follow code style** - Match existing patterns
- **Write clear commits** - Descriptive commit messages
- **Link issues** - Reference related issues

### Code Style

#### C# Guidelines

- **Use C# 12 features** where appropriate
- **Follow Microsoft naming conventions**:
  - PascalCase for classes, methods, properties
  - camelCase for private fields (with `_` prefix)
  - UPPER_CASE for constants
- **Add XML documentation** for public APIs
- **Use async/await** for I/O operations
- **Implement proper disposal** (IDisposable)

Example:
```csharp
/// <summary>
/// Scans directory for duplicate files
/// </summary>
/// <param name="path">Directory path to scan</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>List of duplicate groups</returns>
public async Task<List<DuplicateGroup>> ScanAsync(
    string path, 
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

#### XAML Guidelines

- **Indent with 4 spaces**
- **Use data binding** over code-behind
- **Follow MVVM pattern**
- **Use meaningful x:Name** values

#### Testing Guidelines

- **Write unit tests** for services
- **Write integration tests** for complex workflows
- **Aim for 80%+ coverage**
- **Use descriptive test names**:
  ```csharp
  [Fact]
  public void ScanAsync_WithValidPath_ReturnsFiles()
  ```

### Project Structure

```
PlatypusTools.Net/
├── PlatypusTools.Core/          # Business logic
│   ├── Services/                # Feature implementations
│   ├── Models/                  # Data models
│   └── Utilities/               # Helper classes
├── PlatypusTools.UI/            # WPF frontend
│   ├── ViewModels/              # MVVM ViewModels
│   ├── Views/                   # XAML Views
│   └── Services/                # UI-specific services
├── PlatypusTools.Core.Tests/   # Unit tests
└── PlatypusTools.UI.Tests/     # UI tests
```

### Adding New Features

#### 1. Service Layer (Core)

Create service interface and implementation:

```csharp
// IMyService.cs
public interface IMyService
{
    Task<Result> DoSomethingAsync(string input);
}

// MyService.cs
public class MyService : IMyService
{
    public async Task<Result> DoSomethingAsync(string input)
    {
        // Implementation
    }
}
```

#### 2. ViewModel (UI)

Create ViewModel with commands:

```csharp
public class MyFeatureViewModel : BindableBase
{
    private readonly IMyService _service;
    
    public MyFeatureViewModel(IMyService service)
    {
        _service = service;
        DoSomethingCommand = new AsyncRelayCommand(DoSomethingAsync);
    }
    
    public ICommand DoSomethingCommand { get; }
    
    private async Task DoSomethingAsync()
    {
        // Call service and update UI
    }
}
```

#### 3. View (UI)

Create XAML view:

```xaml
<UserControl x:Class="PlatypusTools.UI.Views.MyFeatureView"
             d:DataContext="{d:DesignInstance Type=vm:MyFeatureViewModel}">
    <StackPanel>
        <!-- UI elements -->
    </StackPanel>
</UserControl>
```

#### 4. Tests

Add unit tests:

```csharp
public class MyServiceTests
{
    [Fact]
    public async Task DoSomethingAsync_WithValidInput_ReturnsSuccess()
    {
        // Arrange
        var service = new MyService();
        
        // Act
        var result = await service.DoSomethingAsync("test");
        
        // Assert
        Assert.NotNull(result);
    }
}
```

#### 5. Integration

- Add ViewModel to MainWindowViewModel
- Add View to MainWindow.xaml
- Update documentation

### Documentation

When adding features:

1. **Update README.md** - Add to features list
2. **Update TODO.md** - Mark as complete
3. **Add code comments** - For complex logic
4. **Update BUILD.md** - If adding dependencies

### Dependencies

#### Adding NuGet Packages

1. **Discuss in issue first** for major dependencies
2. **Use latest stable versions**
3. **Document in BUILD.md**
4. **Update LICENSE** if needed

```powershell
dotnet add package PackageName --version x.y.z
```

### Release Process

1. **Version bump** in project files
2. **Update CHANGELOG**
3. **Run all tests**
4. **Build installer**
5. **Create GitHub release**
6. **Update documentation**

## Getting Help

- **Issues**: For bugs and features
- **Discussions**: For questions and ideas
- **Email**: (add contact if available)

## Recognition

Contributors will be:
- Listed in CONTRIBUTORS.md
- Mentioned in release notes
- Credited in commit history

Thank you for contributing to PlatypusTools.NET! 🎉
