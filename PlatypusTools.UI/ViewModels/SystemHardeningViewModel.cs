using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;

namespace PlatypusTools.UI.ViewModels
{
    public class HardeningCheckItem : BindableBase
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";

        private string _status = "⏳ Checking...";
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private string _details = "";
        public string Details { get => _details; set => SetProperty(ref _details, value); }

        private string _severity = "Info";
        public string Severity { get => _severity; set => SetProperty(ref _severity, value); }
    }

    public class SystemHardeningViewModel : BindableBase
    {
        public SystemHardeningViewModel()
        {
            RunChecksCommand = new RelayCommand(async _ => await RunAllChecksAsync(), _ => !IsBusy);
            ExportReportCommand = new RelayCommand(_ => ExportReport(), _ => Checks.Count > 0);
        }

        public ObservableCollection<HardeningCheckItem> Checks { get; } = new();

        private string _statusMessage = "Click 'Run Checks' to scan system security configuration.";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        private int _passCount;
        public int PassCount { get => _passCount; set => SetProperty(ref _passCount, value); }

        private int _warnCount;
        public int WarnCount { get => _warnCount; set => SetProperty(ref _warnCount, value); }

        private int _failCount;
        public int FailCount { get => _failCount; set => SetProperty(ref _failCount, value); }

        public ICommand RunChecksCommand { get; }
        public ICommand ExportReportCommand { get; }

        private async Task RunAllChecksAsync()
        {
            IsBusy = true;
            Checks.Clear();
            PassCount = WarnCount = FailCount = 0;
            StatusMessage = "Running security checks...";

            try
            {
                await Task.Run(() =>
                {
                    CheckWindowsFirewall();
                    CheckWindowsDefender();
                    CheckUac();
                    CheckBitLocker();
                    CheckRemoteDesktop();
                    CheckAutoLogin();
                    CheckGuestAccount();
                    CheckPasswordPolicy();
                    CheckWindowsUpdate();
                    CheckSmb1();
                    CheckOpenShares();
                    CheckSecureBoot();
                });

                PassCount = Checks.Count(c => c.Severity == "Pass");
                WarnCount = Checks.Count(c => c.Severity == "Warning");
                FailCount = Checks.Count(c => c.Severity == "Fail");
                StatusMessage = $"Complete: {PassCount} passed, {WarnCount} warnings, {FailCount} failures";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void AddCheck(string category, string name, string status, string details, string severity)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Checks.Add(new HardeningCheckItem
                {
                    Category = category,
                    Name = name,
                    Status = status,
                    Details = details,
                    Severity = severity
                });
            });
        }

        private void CheckWindowsFirewall()
        {
            try
            {
                var output = RunCommand("netsh", "advfirewall show allprofiles state");
                bool allOn = output.Split('\n').Count(l => l.Contains("ON", StringComparison.OrdinalIgnoreCase)) >= 3;
                if (allOn)
                    AddCheck("Network", "Windows Firewall", "✅ All profiles enabled", "Domain, Private, and Public profiles are ON", "Pass");
                else
                    AddCheck("Network", "Windows Firewall", "❌ Partially disabled", "One or more firewall profiles are OFF", "Fail");
            }
            catch (Exception ex)
            {
                AddCheck("Network", "Windows Firewall", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckWindowsDefender()
        {
            try
            {
                var svc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "WinDefend");
                if (svc != null && svc.Status == ServiceControllerStatus.Running)
                    AddCheck("Antivirus", "Windows Defender", "✅ Running", "WinDefend service is active", "Pass");
                else
                    AddCheck("Antivirus", "Windows Defender", "❌ Not running", "WinDefend service is stopped or not found", "Fail");
            }
            catch (Exception ex)
            {
                AddCheck("Antivirus", "Windows Defender", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckUac()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
                var lua = key?.GetValue("EnableLUA");
                if (lua is int val && val == 1)
                    AddCheck("Access Control", "UAC (User Account Control)", "✅ Enabled", "EnableLUA = 1", "Pass");
                else
                    AddCheck("Access Control", "UAC (User Account Control)", "❌ Disabled", "UAC is disabled — high security risk", "Fail");
            }
            catch (Exception ex)
            {
                AddCheck("Access Control", "UAC", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckBitLocker()
        {
            try
            {
                var output = RunCommand("manage-bde", "-status C:");
                if (output.Contains("Fully Encrypted", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("Protection On", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Encryption", "BitLocker (C:)", "✅ Encrypted", "System drive is BitLocker protected", "Pass");
                else if (output.Contains("Encryption in Progress", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Encryption", "BitLocker (C:)", "⏳ In progress", "Encryption is currently running", "Warning");
                else
                    AddCheck("Encryption", "BitLocker (C:)", "❌ Not encrypted", "System drive is not BitLocker protected", "Fail");
            }
            catch (Exception ex)
            {
                AddCheck("Encryption", "BitLocker", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckRemoteDesktop()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
                var deny = key?.GetValue("fDenyTSConnections");
                if (deny is int val && val == 1)
                    AddCheck("Remote Access", "Remote Desktop", "✅ Disabled", "RDP connections are denied", "Pass");
                else
                    AddCheck("Remote Access", "Remote Desktop", "⚠️ Enabled", "RDP is enabled — ensure NLA is required", "Warning");
            }
            catch (Exception ex)
            {
                AddCheck("Remote Access", "Remote Desktop", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckAutoLogin()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                var autoLogin = key?.GetValue("AutoAdminLogon")?.ToString();
                if (autoLogin == "1")
                    AddCheck("Authentication", "Auto-Login", "❌ Enabled", "Automatic login is configured — credentials may be stored in registry", "Fail");
                else
                    AddCheck("Authentication", "Auto-Login", "✅ Disabled", "No automatic login configured", "Pass");
            }
            catch (Exception ex)
            {
                AddCheck("Authentication", "Auto-Login", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckGuestAccount()
        {
            try
            {
                var output = RunCommand("net", "user Guest");
                if (output.Contains("Account active               No", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Authentication", "Guest Account", "✅ Disabled", "Guest account is inactive", "Pass");
                else
                    AddCheck("Authentication", "Guest Account", "❌ Active", "Guest account is enabled — disable it", "Fail");
            }
            catch (Exception ex)
            {
                AddCheck("Authentication", "Guest Account", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckPasswordPolicy()
        {
            try
            {
                var output = RunCommand("net", "accounts");
                var lines = output.Split('\n');
                var minLen = lines.FirstOrDefault(l => l.Contains("Minimum password length", StringComparison.OrdinalIgnoreCase));
                var complexity = "Check Group Policy for password complexity requirements";

                if (minLen != null && minLen.Contains("0"))
                    AddCheck("Password", "Password Policy", "⚠️ Weak", $"Minimum length is 0. {complexity}", "Warning");
                else
                    AddCheck("Password", "Password Policy", "✅ Configured", $"{minLen?.Trim()}. {complexity}", "Pass");
            }
            catch (Exception ex)
            {
                AddCheck("Password", "Password Policy", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckWindowsUpdate()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
                var lastStr = key?.GetValue("LastSuccessTime")?.ToString();
                if (DateTime.TryParse(lastStr, out var last))
                {
                    var days = (DateTime.Now - last).TotalDays;
                    if (days <= 30)
                        AddCheck("Updates", "Windows Update", "✅ Recent", $"Last install: {last:yyyy-MM-dd} ({days:F0} days ago)", "Pass");
                    else
                        AddCheck("Updates", "Windows Update", "⚠️ Overdue", $"Last install: {last:yyyy-MM-dd} ({days:F0} days ago)", "Warning");
                }
                else
                {
                    AddCheck("Updates", "Windows Update", "⚠️ Unknown", "Could not determine last update time", "Warning");
                }
            }
            catch (Exception ex)
            {
                AddCheck("Updates", "Windows Update", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckSmb1()
        {
            try
            {
                var output = RunCommand("powershell", "-NoProfile -Command \"(Get-SmbServerConfiguration).EnableSMB1Protocol\"");
                if (output.Trim().Equals("False", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Network", "SMBv1 Protocol", "✅ Disabled", "SMBv1 is disabled (prevents EternalBlue-type attacks)", "Pass");
                else if (output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Network", "SMBv1 Protocol", "❌ Enabled", "SMBv1 is enabled — vulnerable to EternalBlue. Disable it.", "Fail");
                else
                    AddCheck("Network", "SMBv1 Protocol", "⚠️ Unknown", $"Output: {output.Trim()}", "Warning");
            }
            catch (Exception ex)
            {
                AddCheck("Network", "SMBv1 Protocol", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckOpenShares()
        {
            try
            {
                var output = RunCommand("net", "share");
                var shares = output.Split('\n')
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("Share name") && !l.StartsWith("--") && !l.Contains("command completed"))
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.EndsWith("$"))
                    .ToList();

                if (shares.Count == 0)
                    AddCheck("Network", "Open Network Shares", "✅ None", "No non-default network shares found", "Pass");
                else
                    AddCheck("Network", "Open Network Shares", $"⚠️ {shares.Count} found", string.Join(", ", shares), "Warning");
            }
            catch (Exception ex)
            {
                AddCheck("Network", "Open Shares", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private void CheckSecureBoot()
        {
            try
            {
                var output = RunCommand("powershell", "-NoProfile -Command \"Confirm-SecureBootUEFI\"");
                if (output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase))
                    AddCheck("Boot", "Secure Boot", "✅ Enabled", "UEFI Secure Boot is active", "Pass");
                else
                    AddCheck("Boot", "Secure Boot", "⚠️ Disabled or N/A", "Secure Boot is not enabled or not supported", "Warning");
            }
            catch (Exception ex)
            {
                AddCheck("Boot", "Secure Boot", "⚠️ Check failed", ex.Message, "Warning");
            }
        }

        private static string RunCommand(string filename, string args)
        {
            var psi = new ProcessStartInfo(filename, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return "";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            return output;
        }

        private void ExportReport()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Hardening Report",
                Filter = "Text File (*.txt)|*.txt|CSV (*.csv)|*.csv",
                FileName = $"HardeningReport_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"System Hardening Report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Machine: {Environment.MachineName}");
                    sb.AppendLine($"User: {Environment.UserName}");
                    sb.AppendLine(new string('=', 80));
                    sb.AppendLine($"Results: {PassCount} Pass | {WarnCount} Warning | {FailCount} Fail");
                    sb.AppendLine(new string('-', 80));

                    foreach (var c in Checks)
                        sb.AppendLine($"[{c.Severity}] {c.Category} / {c.Name}: {c.Status}\n   {c.Details}");

                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                    StatusMessage = $"Report exported: {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }
    }
}
