using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Diagnostics
{
    /// <summary>
    /// Centralised crash dumper. Wires three handlers:
    ///   • AppDomain.UnhandledException
    ///   • Application.DispatcherUnhandledException (caller wires)
    ///   • TaskScheduler.UnobservedTaskException
    ///
    /// On each event, writes a stack-trace text file to
    /// %LOCALAPPDATA%\PlatypusTools\crashes\crash-yyyyMMdd-HHmmss.txt and
    /// (best-effort) a .dmp minidump alongside it via dbghelp's
    /// MiniDumpWriteDump. The .dmp is best-effort: failures are silently
    /// ignored so the trace still lands.
    ///
    /// SimpleLogger.Error is also called so the app's existing log file
    /// tracks the same event.
    /// </summary>
    public static class CrashHandler
    {
        private static readonly string CrashDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "crashes");

        private static int _installed;

        public static void Install()
        {
            if (System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;
            try { Directory.CreateDirectory(CrashDir); } catch { }

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Capture("AppDomain", ex);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Capture("UnobservedTask", e.Exception);
                e.SetObserved();
            };
        }

        public static void CaptureDispatcher(Exception ex) => Capture("Dispatcher", ex);

        public static string Capture(string source, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(CrashDir);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var basePath = Path.Combine(CrashDir, $"crash-{stamp}");
                var txtPath = basePath + ".txt";
                var sb = new StringBuilder();
                sb.AppendLine($"Source : {source}");
                sb.AppendLine($"UTC    : {DateTime.UtcNow:o}");
                sb.AppendLine($"App    : {Assembly.GetEntryAssembly()?.GetName().Version}");
                sb.AppendLine($"OS     : {RuntimeInformation.OSDescription}");
                sb.AppendLine($"Arch   : {RuntimeInformation.ProcessArchitecture}");
                sb.AppendLine();
                if (ex != null)
                {
                    sb.AppendLine(ex.ToString());
                }
                else
                {
                    sb.AppendLine("(no exception object)");
                }
                File.WriteAllText(txtPath, sb.ToString());

                // Best-effort minidump. Fails on non-Windows or if dbghelp is missing.
                try { WriteMiniDump(basePath + ".dmp"); } catch { }

                try { PlatypusTools.Core.Services.SimpleLogger.Error($"CrashHandler: {source} -> {txtPath}"); } catch { }
                try { ActivityLogService.Instance.Error("Crash", $"{source}: {ex?.Message ?? "(no message)"}"); } catch { }
                return txtPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        // -- minidump P/Invoke -----------------------------------------------

        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess, uint processId, IntPtr hFile, uint dumpType,
            IntPtr expParam, IntPtr userStreamParam, IntPtr callbackParam);

        private static void WriteMiniDump(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var p = Process.GetCurrentProcess();
            // 0x0002 = MiniDumpWithFullMemory is huge; 0x0000 = MiniDumpNormal — reasonable default.
            const uint MiniDumpNormal = 0x0000;
            MiniDumpWriteDump(p.Handle, (uint)p.Id, fs.SafeFileHandle.DangerousGetHandle(), MiniDumpNormal, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
