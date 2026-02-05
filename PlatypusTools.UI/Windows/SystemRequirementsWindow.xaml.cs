using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Windows
{
    /// <summary>
    /// Pre-GUI system requirements check window.
    /// Shows before splash screen if requirements need to be verified.
    /// </summary>
    public partial class SystemRequirementsWindow : Window
    {
        private readonly SystemRequirementsService.SystemRequirementsResult _result;
        private readonly bool _hasFailedCritical;

        public bool UserWantsToContinue { get; private set; }
        public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

        public SystemRequirementsWindow(SystemRequirementsService.SystemRequirementsResult result)
        {
            InitializeComponent();
            _result = result;
            _hasFailedCritical = result.FailedChecks.Count > 0;

            PopulateSystemInfo();
            PopulateRequirementsList();
            UpdateSummary();
            ConfigureButtons();
        }

        private void PopulateSystemInfo()
        {
            var sys = _result.DetectedSystem;

            CpuInfo.Text = $"{sys.CpuName} ({sys.CpuCores}C/{sys.CpuThreads}T)";
            RamInfo.Text = $"{sys.RamGB:F1} GB";
            GpuInfo.Text = sys.GpuName;
            OsInfo.Text = $"Windows Build {sys.OsBuild} ({sys.Architecture})";
            ScreenInfo.Text = $"{sys.ScreenWidth}×{sys.ScreenHeight}";
            DiskInfo.Text = $"{sys.AvailableDiskSpaceMB / 1024.0:F1} GB free";
        }

        private void PopulateRequirementsList()
        {
            RequirementsList.Children.Clear();

            foreach (var check in _result.Checks)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 4, 0, 4)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Status icon
                var icon = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (check.IsMet)
                {
                    icon.Text = "\uE73E"; // Checkmark
                    icon.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                else if (check.IsRecommendation)
                {
                    icon.Text = "\uE7BA"; // Warning
                    icon.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                }
                else
                {
                    icon.Text = "\uE711"; // X
                    icon.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                }

                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);

                // Requirement details
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                var nameText = new TextBlock
                {
                    Text = check.Name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    FontSize = 13
                };
                textStack.Children.Add(nameText);

                var messageText = new TextBlock
                {
                    Text = check.Message,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                textStack.Children.Add(messageText);

                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                // Current vs Required
                var valueStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(10, 0, 0, 0)
                };

                var currentText = new TextBlock
                {
                    Text = check.CurrentValue,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                valueStack.Children.Add(currentText);

                if (!check.IsMet)
                {
                    var requiredText = new TextBlock
                    {
                        Text = $"Need: {check.RequiredValue}",
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    valueStack.Children.Add(requiredText);
                }

                Grid.SetColumn(valueStack, 2);
                grid.Children.Add(valueStack);

                border.Child = grid;
                RequirementsList.Children.Add(border);
            }
        }

        private void UpdateSummary()
        {
            if (_result.MeetsMinimumRequirements && _result.MeetsRecommendedRequirements)
            {
                HeaderStatus.Text = "Your system meets all requirements!";
                SummaryPanel.Background = new SolidColorBrush(Color.FromRgb(27, 94, 32)); // Dark green
                SummaryTitle.Text = "✓ All Requirements Met";
                SummaryMessage.Text = "Your system is fully compatible with PlatypusTools. Click Continue to launch the application.";
            }
            else if (_result.MeetsMinimumRequirements)
            {
                HeaderStatus.Text = "Your system meets minimum requirements with some recommendations.";
                SummaryPanel.Background = new SolidColorBrush(Color.FromRgb(130, 119, 23)); // Dark yellow
                SummaryTitle.Text = "⚠ Minimum Requirements Met";
                SummaryMessage.Text = $"Your system can run PlatypusTools, but {_result.Warnings.Count} recommendation(s) are not met. " +
                    "The application may have reduced performance in some areas.";
            }
            else
            {
                HeaderStatus.Text = "Your system does not meet the minimum requirements.";
                SummaryPanel.Background = new SolidColorBrush(Color.FromRgb(183, 28, 28)); // Dark red
                SummaryTitle.Text = "✗ Requirements Not Met";
                SummaryMessage.Text = $"{_result.FailedChecks.Count} critical requirement(s) not met. " +
                    "PlatypusTools may not function correctly on this system.";
            }
        }

        private void ConfigureButtons()
        {
            if (_hasFailedCritical)
            {
                // Show Continue Anyway + Exit buttons
                ContinueButton.Visibility = Visibility.Collapsed;
                ContinueAnywayButton.Visibility = Visibility.Visible;
                ExitButton.Visibility = Visibility.Visible;
            }
            else
            {
                // Just show Continue button
                ContinueButton.Visibility = Visibility.Visible;
                ContinueAnywayButton.Visibility = Visibility.Collapsed;
                ExitButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            UserWantsToContinue = true;
            Close();
        }

        private void ContinueAnywayButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Your system does not meet the minimum requirements. " +
                "PlatypusTools may crash or not function correctly.\n\n" +
                "Are you sure you want to continue?",
                "Continue Despite Warnings?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                UserWantsToContinue = true;
                Close();
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            UserWantsToContinue = false;
            Close();
        }

        private void CopyReportButton_Click(object sender, RoutedEventArgs e)
        {
            var report = GenerateReport();
            Clipboard.SetText(report);
            MessageBox.Show("System requirements report copied to clipboard!", "Report Copied", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== PlatypusTools System Requirements Report ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("--- System Information ---");
            var sys = _result.DetectedSystem;
            sb.AppendLine($"OS: Windows Build {sys.OsBuild} ({sys.Architecture})");
            sb.AppendLine($"CPU: {sys.CpuName}");
            sb.AppendLine($"CPU Cores: {sys.CpuCores} physical, {sys.CpuThreads} logical");
            sb.AppendLine($"RAM: {sys.RamGB:F1} GB");
            sb.AppendLine($"GPU: {sys.GpuName}");
            sb.AppendLine($"GPU Memory: {sys.GpuMemoryMB} MB");
            sb.AppendLine($"DirectX 11: {(sys.HasDirectX11 ? "Yes" : "No")}");
            sb.AppendLine($"Screen: {sys.ScreenWidth}×{sys.ScreenHeight}");
            sb.AppendLine($"Available Disk: {sys.AvailableDiskSpaceMB / 1024.0:F1} GB");
            sb.AppendLine();

            sb.AppendLine("--- Requirements Check ---");
            foreach (var check in _result.Checks)
            {
                var status = check.IsMet ? "PASS" : (check.IsRecommendation ? "WARN" : "FAIL");
                sb.AppendLine($"[{status}] {check.Name}");
                sb.AppendLine($"       Current: {check.CurrentValue}");
                if (!check.IsMet)
                    sb.AppendLine($"       Required: {check.RequiredValue}");
            }
            sb.AppendLine();

            sb.AppendLine("--- Summary ---");
            sb.AppendLine($"Meets Minimum: {(_result.MeetsMinimumRequirements ? "Yes" : "No")}");
            sb.AppendLine($"Meets Recommended: {(_result.MeetsRecommendedRequirements ? "Yes" : "No")}");
            sb.AppendLine($"Failed Checks: {_result.FailedChecks.Count}");
            sb.AppendLine($"Warnings: {_result.Warnings.Count}");

            return sb.ToString();
        }
    }
}
