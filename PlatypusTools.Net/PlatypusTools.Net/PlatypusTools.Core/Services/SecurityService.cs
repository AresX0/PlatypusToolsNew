using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PlatypusTools.Core.Services
{
    public static class SecurityService
    {
        public static bool HideFolder(string folderPath, bool hide = true)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return false;
                var attrs = File.GetAttributes(folderPath);
                if (hide) attrs |= FileAttributes.Hidden | FileAttributes.System;
                else attrs &= ~FileAttributes.Hidden;
                File.SetAttributes(folderPath, attrs);
                return true;
            }
            catch { return false; }
        }

        public static bool RestrictFolderToAdministrators(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath)) return false;
                var di = new DirectoryInfo(folderPath);
                var ds = di.GetAccessControl();
                var sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var adminRule = new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow);
                ds.SetAccessRule(adminRule);
                di.SetAccessControl(ds);
                return true;
            }
            catch { return false; }
        }
    }
}