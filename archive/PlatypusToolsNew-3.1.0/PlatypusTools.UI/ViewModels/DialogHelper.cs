using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Interop;

namespace PlatypusTools.UI.ViewModels
{
    internal static class DialogHelper
    {
        public static DialogResult ShowFolderDialog(FolderBrowserDialog dialog)
        {
            var owner = GetOwner();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        public static bool? ShowOpenFileDialog(Microsoft.Win32.OpenFileDialog dialog)
        {
            var owner = GetWpfOwner();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        private static System.Windows.Forms.IWin32Window? GetOwner()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var handle = new WindowInteropHelper(mainWindow).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        return new WindowWrapper(handle);
                    }
                }

                var processHandle = Process.GetCurrentProcess().MainWindowHandle;
                if (processHandle != IntPtr.Zero)
                {
                    return new WindowWrapper(processHandle);
                }
            }
            catch
            {
                // ignore and fall back to no owner
            }

            return null;
        }

        private static System.Windows.Window? GetWpfOwner()
        {
            try
            {
                return System.Windows.Application.Current?.MainWindow;
            }
            catch
            {
                return null;
            }
        }
    }
}
