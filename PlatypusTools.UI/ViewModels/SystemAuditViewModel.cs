using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.UI.ViewModels
{
    public class SystemAuditViewModel : INotifyPropertyChanged
    {
        private readonly ISystemAuditService _service;

        public ObservableCollection<AuditItem> AuditItems { get; } = new();

        public bool IsElevated => ElevationHelper.IsElevated();
        public bool NeedsElevation => !IsElevated;

        private bool _isAuditing;
        public bool IsAuditing
        {
            get => _isAuditing;
            set { _isAuditing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRunAudit)); }
        }

        private string _statusMessage = "Ready to audit";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private int _totalIssues;
        public int TotalIssues
        {
            get => _totalIssues;
            set { _totalIssues = value; OnPropertyChanged(); }
        }

        private int _criticalIssues;
        public int CriticalIssues
        {
            get => _criticalIssues;
            set { _criticalIssues = value; OnPropertyChanged(); }
        }

        private int _warningIssues;
        public int WarningIssues
        {
            get => _warningIssues;
            set { _warningIssues = value; OnPropertyChanged(); }
        }

        private string _selectedCategory = "All";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(); FilterItems(); }
        }

        private AuditSeverity _selectedSeverity = AuditSeverity.Info;
        public AuditSeverity SelectedSeverity
        {
            get => _selectedSeverity;
            set { _selectedSeverity = value; OnPropertyChanged(); FilterItems(); }
        }

        public bool CanRunAudit => !IsAuditing;

        public ICommand RunFullAuditCommand { get; }
        public ICommand RunFirewallAuditCommand { get; }
        public ICommand RunUpdatesAuditCommand { get; }
        public ICommand RunStartupAuditCommand { get; }
        public ICommand ScanElevatedUsersCommand { get; }
        public ICommand ScanCriticalAclsCommand { get; }
        public ICommand ScanOutboundTrafficCommand { get; }
        public ICommand OpenUsersAndGroupsCommand { get; }
        public ICommand DisableUserCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand ResetPasswordCommand { get; }
        public ICommand FixIssueCommand { get; }
        public ICommand FixAllCommand { get; }
        public ICommand ExportReportCommand { get; }
        public ICommand ClearCommand { get; }

        private string _selectedUsername = string.Empty;
        public string SelectedUsername
        {
            get => _selectedUsername;
            set { _selectedUsername = value; OnPropertyChanged(); }
        }

        private string _newPassword = string.Empty;
        public string NewPassword
        {
            get => _newPassword;
            set { _newPassword = value; OnPropertyChanged(); }
        }

        private ObservableCollection<AuditItem> _allItems = new();

        public SystemAuditViewModel() : this(new SystemAuditService()) { }

        public SystemAuditViewModel(ISystemAuditService service)
        {
            _service = service;

            RunFullAuditCommand = new RelayCommand(_ => RunFullAudit(), _ => CanRunAudit);
            RunFirewallAuditCommand = new RelayCommand(_ => RunFirewallAudit(), _ => CanRunAudit);
            RunUpdatesAuditCommand = new RelayCommand(_ => RunUpdatesAudit(), _ => CanRunAudit);
            RunStartupAuditCommand = new RelayCommand(_ => RunStartupAudit(), _ => CanRunAudit);
            ScanElevatedUsersCommand = new RelayCommand(_ => ScanElevatedUsers(), _ => CanRunAudit);
            ScanCriticalAclsCommand = new RelayCommand(_ => ScanCriticalAcls(), _ => CanRunAudit);
            ScanOutboundTrafficCommand = new RelayCommand(_ => ScanOutboundTraffic(), _ => CanRunAudit);
            OpenUsersAndGroupsCommand = new RelayCommand(_ => OpenUsersAndGroups());
            DisableUserCommand = new RelayCommand(_ => DisableUser());
            DeleteUserCommand = new RelayCommand(_ => DeleteUser());
            ResetPasswordCommand = new RelayCommand(_ => ResetPassword());
            FixIssueCommand = new RelayCommand(obj => FixIssue(obj as AuditItem));
            FixAllCommand = new RelayCommand(_ => FixAll(), _ => AuditItems.Any(i => i.CanAutoFix));
            ExportReportCommand = new RelayCommand(_ => ExportReport());
            ClearCommand = new RelayCommand(_ => Clear());
        }

        private async void RunFullAudit()
        {
            IsAuditing = true;
            StatusMessage = "Running full system audit...";
            AuditItems.Clear();
            _allItems.Clear();

            try
            {
                var items = await _service.RunFullAudit();
                
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }

                UpdateStatistics();
                StatusMessage = $"Audit complete: {TotalIssues} issues found ({CriticalIssues} critical, {WarningIssues} warnings)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void RunFirewallAudit()
        {
            IsAuditing = true;
            StatusMessage = "Auditing firewall...";

            try
            {
                var items = await _service.AuditFirewall();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = "Firewall audit complete";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void RunUpdatesAudit()
        {
            IsAuditing = true;
            StatusMessage = "Auditing Windows updates...";

            try
            {
                var items = await _service.AuditWindowsUpdates();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = "Updates audit complete";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void RunStartupAudit()
        {
            IsAuditing = true;
            StatusMessage = "Auditing startup items...";

            try
            {
                var items = await _service.AuditStartupItems();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = "Startup audit complete";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void ScanElevatedUsers()
        {
            IsAuditing = true;
            StatusMessage = "Scanning for elevated users...";

            try
            {
                var items = await _service.ScanElevatedUsers();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = $"Found {items.Count} elevated users";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void ScanCriticalAcls()
        {
            IsAuditing = true;
            StatusMessage = "Scanning critical ACLs...";

            try
            {
                var items = await _service.ScanCriticalAcls();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = $"Found {items.Count} critical ACL issues";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private async void ScanOutboundTraffic()
        {
            IsAuditing = true;
            StatusMessage = "Scanning outbound traffic...";

            try
            {
                var items = await _service.ScanOutboundTraffic();
                foreach (var item in items)
                {
                    _allItems.Add(item);
                    AuditItems.Add(item);
                }
                
                UpdateStatistics();
                StatusMessage = $"Found {items.Count} outbound connections";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsAuditing = false;
            }
        }

        private void OpenUsersAndGroups()
        {
            try
            {
                _service.OpenUsersAndGroups();
                StatusMessage = "Opened Users and Groups management";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async void DisableUser()
        {
            if (string.IsNullOrWhiteSpace(SelectedUsername))
            {
                StatusMessage = "Please select a user from the audit results";
                return;
            }

            try
            {
                var success = await _service.DisableUser(SelectedUsername);
                StatusMessage = success 
                    ? $"Successfully disabled user: {SelectedUsername}" 
                    : $"Failed to disable user: {SelectedUsername}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async void DeleteUser()
        {
            if (string.IsNullOrWhiteSpace(SelectedUsername))
            {
                StatusMessage = "Please select a user from the audit results";
                return;
            }

            try
            {
                var success = await _service.DeleteUser(SelectedUsername);
                StatusMessage = success 
                    ? $"Successfully deleted user: {SelectedUsername}" 
                    : $"Failed to delete user: {SelectedUsername}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async void ResetPassword()
        {
            if (string.IsNullOrWhiteSpace(SelectedUsername))
            {
                StatusMessage = "Please select a user from the audit results";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                StatusMessage = "Please enter a new password";
                return;
            }

            try
            {
                var success = await _service.ResetUserPassword(SelectedUsername, NewPassword);
                StatusMessage = success 
                    ? $"Successfully reset password for: {SelectedUsername}" 
                    : $"Failed to reset password for: {SelectedUsername}";
                
                if (success)
                {
                    NewPassword = string.Empty;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async void FixIssue(AuditItem? item)
        {
            if (item == null || !item.CanAutoFix) return;

            StatusMessage = $"Fixing {item.Name}...";
            
            try
            {
                var success = await _service.FixIssue(item);
                if (success)
                {
                    item.Status = AuditStatus.Pass;
                    StatusMessage = $"Fixed: {item.Name}";
                    UpdateStatistics();
                }
                else
                {
                    StatusMessage = $"Failed to fix: {item.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async void FixAll()
        {
            var fixableItems = AuditItems.Where(i => i.CanAutoFix).ToList();
            StatusMessage = $"Fixing {fixableItems.Count} issues...";

            var fixedCount = 0;
            foreach (var item in fixableItems)
            {
                try
                {
                    if (await _service.FixIssue(item))
                    {
                        item.Status = AuditStatus.Pass;
                        fixedCount++;
                    }
                }
                catch { }
            }

            UpdateStatistics();
            StatusMessage = $"Fixed {fixedCount} of {fixableItems.Count} issues";
        }

        private void ExportReport()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                FileName = $"SystemAudit_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var isCsv = dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                    
                    if (isCsv)
                    {
                        // Export as CSV
                        var lines = new System.Collections.Generic.List<string>
                        {
                            "\"Category\",\"Name\",\"Description\",\"Severity\",\"Status\",\"Details\""
                        };

                        foreach (var item in AuditItems)
                        {
                            var csvLine = $"\"{EscapeCsv(item.Category)}\",\"{EscapeCsv(item.Name)}\",\"{EscapeCsv(item.Description)}\",\"{item.Severity}\",\"{item.Status}\",\"{EscapeCsv(item.Details)}\"";
                            lines.Add(csvLine);
                        }

                        System.IO.File.WriteAllLines(dialog.FileName, lines);
                    }
                    else
                    {
                        // Export as text
                        var lines = new System.Collections.Generic.List<string>
                        {
                            $"System Audit Report - {DateTime.Now}",
                            $"Total Issues: {TotalIssues}",
                            $"Critical: {CriticalIssues}, Warnings: {WarningIssues}",
                            "",
                            "Details:",
                            "".PadRight(80, '=')
                        };

                        foreach (var item in AuditItems)
                        {
                            lines.Add($"Category: {item.Category}");
                            lines.Add($"Name: {item.Name}");
                            lines.Add($"Description: {item.Description}");
                            lines.Add($"Severity: {item.Severity}");
                            lines.Add($"Status: {item.Status}");
                            if (!string.IsNullOrEmpty(item.Details))
                                lines.Add($"Details: {item.Details}");
                            lines.Add("".PadRight(80, '-'));
                        }

                        System.IO.File.WriteAllLines(dialog.FileName, lines);
                    }

                    StatusMessage = $"Report exported to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error exporting: {ex.Message}";
                }
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            
            // Escape quotes by doubling them
            return value.Replace("\"", "\"\"");
        }

        private void Clear()
        {
            AuditItems.Clear();
            _allItems.Clear();
            TotalIssues = 0;
            CriticalIssues = 0;
            WarningIssues = 0;
            StatusMessage = "Ready to audit";
        }

        private void FilterItems()
        {
            AuditItems.Clear();
            
            var filtered = _allItems.AsEnumerable();
            
            if (SelectedCategory != "All")
                filtered = filtered.Where(i => i.Category == SelectedCategory);

            foreach (var item in filtered)
                AuditItems.Add(item);
        }

        private void UpdateStatistics()
        {
            TotalIssues = AuditItems.Count(i => i.Status == AuditStatus.Fail);
            CriticalIssues = AuditItems.Count(i => i.Severity == AuditSeverity.Critical && i.Status == AuditStatus.Fail);
            WarningIssues = AuditItems.Count(i => i.Severity == AuditSeverity.Warning && i.Status == AuditStatus.Fail);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
