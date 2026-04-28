using System.Collections.Generic;

namespace PlatypusTools.UI.Services.Scheduling
{
    /// <summary>
    /// Phase 2.4 — built-in starter templates for Windows Task Scheduler.
    /// Each template ships as a Task Scheduler XML string the user can register
    /// with `schtasks /create /xml` (or import via Task Scheduler MMC).
    /// </summary>
    public sealed record ScheduledJobTemplate(
        string DisplayName,
        string Description,
        string SuggestedTaskName,
        string SuggestedFileName,
        string TaskXml);

    public static class ScheduledJobTemplates
    {
        public static readonly IReadOnlyList<ScheduledJobTemplate> All = new[]
        {
            new ScheduledJobTemplate(
                "Backup Home Folder (robocopy)",
                "Mirrors %USERPROFILE% to D:\\Backup nightly at 2 AM.",
                "PlatypusTools - Backup Home",
                "platypus-backup-home.xml",
                BuildXml(
                    description: "Mirror user profile to backup volume",
                    triggerCron: "<CalendarTrigger><StartBoundary>2025-01-01T02:00:00</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>",
                    command: "%SystemRoot%\\System32\\robocopy.exe",
                    arguments: "\"%USERPROFILE%\" \"D:\\Backup\\%USERNAME%\" /MIR /R:2 /W:5 /XJ /NP /LOG:\"D:\\Backup\\backup.log\"")),

            new ScheduledJobTemplate(
                "Cleanup Temp Folders",
                "Deletes files older than 7 days from %TEMP% weekly on Sunday.",
                "PlatypusTools - Cleanup Temp",
                "platypus-cleanup-temp.xml",
                BuildXml(
                    description: "Weekly temp cleanup",
                    triggerCron: "<CalendarTrigger><StartBoundary>2025-01-05T03:00:00</StartBoundary><ScheduleByWeek><DaysOfWeek><Sunday/></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek></CalendarTrigger>",
                    command: "%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    arguments: "-NoProfile -Command \"Get-ChildItem $env:TEMP -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue\"")),

            new ScheduledJobTemplate(
                "Convert Inbox Videos to MP4",
                "Runs ffmpeg over D:\\Inbox\\*.* -> H.264 MP4. Daily 1 AM.",
                "PlatypusTools - Media Convert MP4",
                "platypus-media-convert-mp4.xml",
                BuildXml(
                    description: "Batch ffmpeg convert daily",
                    triggerCron: "<CalendarTrigger><StartBoundary>2025-01-01T01:00:00</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay></CalendarTrigger>",
                    command: "%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    arguments: "-NoProfile -Command \"Get-ChildItem D:\\Inbox -File | ForEach-Object { ffmpeg -y -i $_.FullName -c:v libx264 -crf 22 -c:a aac (Join-Path D:\\Outbox ($_.BaseName + '.mp4')) }\"")),

            new ScheduledJobTemplate(
                "Forensics Quick Collection",
                "Runs a one-shot triage script on Monday 8 AM (autoruns, netstat, processes).",
                "PlatypusTools - Forensics Collect",
                "platypus-forensics-collect.xml",
                BuildXml(
                    description: "Weekly forensic snapshot",
                    triggerCron: "<CalendarTrigger><StartBoundary>2025-01-06T08:00:00</StartBoundary><ScheduleByWeek><DaysOfWeek><Monday/></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek></CalendarTrigger>",
                    command: "%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    arguments: "-NoProfile -Command \"$out = \\\"$env:USERPROFILE\\Documents\\Triage_$(Get-Date -Format yyyyMMdd_HHmm)\\\"; New-Item -ItemType Directory -Path $out -Force | Out-Null; Get-Process | Export-Csv \\\"$out\\processes.csv\\\" -NoTypeInformation; Get-NetTCPConnection | Export-Csv \\\"$out\\netstat.csv\\\" -NoTypeInformation; Get-Service | Export-Csv \\\"$out\\services.csv\\\" -NoTypeInformation\""))
        };

        private static string BuildXml(string description, string triggerCron, string command, string arguments)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>{System.Security.SecurityElement.Escape(description)}</Description>
    <Author>PlatypusTools</Author>
  </RegistrationInfo>
  <Triggers>
    {triggerCron}
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{System.Security.SecurityElement.Escape(command)}</Command>
      <Arguments>{System.Security.SecurityElement.Escape(arguments)}</Arguments>
    </Exec>
  </Actions>
</Task>";
        }
    }
}
