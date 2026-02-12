using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services;
using System;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RecentWorkspacesService"/> - recent workspaces management.
    /// </summary>
    [TestClass]
    public class RecentWorkspacesServiceTests
    {
        #region RecentWorkspace Model Tests

        [TestMethod]
        public void RecentWorkspace_DefaultValues_AreCorrect()
        {
            var workspace = new RecentWorkspace();
            
            Assert.AreEqual(string.Empty, workspace.Path);
            Assert.AreEqual(string.Empty, workspace.Name);
            Assert.IsNull(workspace.Module);
            Assert.AreEqual(0, workspace.AccessCount);
        }

        [TestMethod]
        public void RecentWorkspace_DisplayName_ReturnsName_WhenSet()
        {
            var workspace = new RecentWorkspace
            {
                Path = @"C:\Test\MyProject",
                Name = "My Custom Name"
            };
            
            Assert.AreEqual("My Custom Name", workspace.DisplayName);
        }

        [TestMethod]
        public void RecentWorkspace_DisplayName_ReturnsFolderName_WhenNameIsEmpty()
        {
            var workspace = new RecentWorkspace
            {
                Path = @"C:\Test\MyProject",
                Name = string.Empty
            };
            
            Assert.AreEqual("MyProject", workspace.DisplayName);
        }

        [TestMethod]
        public void RecentWorkspace_Exists_ReturnsTrue_WhenDirectoryExists()
        {
            var tempPath = Path.GetTempPath();
            var workspace = new RecentWorkspace { Path = tempPath };
            
            Assert.IsTrue(workspace.Exists);
        }

        [TestMethod]
        public void RecentWorkspace_Exists_ReturnsFalse_WhenDirectoryDoesNotExist()
        {
            var workspace = new RecentWorkspace { Path = @"C:\NonExistent\Path\12345" };
            
            Assert.IsFalse(workspace.Exists);
        }

        [TestMethod]
        public void RecentWorkspace_LastAccessedDisplay_FormatsCorrectly()
        {
            var workspace = new RecentWorkspace
            {
                LastAccessed = new DateTime(2025, 1, 15, 14, 30, 0)
            };
            
            // Format: "MMM d, yyyy h:mm tt"
            Assert.IsTrue(workspace.LastAccessedDisplay.Contains("Jan"));
            Assert.IsTrue(workspace.LastAccessedDisplay.Contains("15"));
            Assert.IsTrue(workspace.LastAccessedDisplay.Contains("2025"));
        }

        #endregion

        #region RecentFile Model Tests

        [TestMethod]
        public void RecentFile_DefaultValues_AreCorrect()
        {
            var file = new RecentFile();
            
            Assert.AreEqual(string.Empty, file.Path);
            Assert.AreEqual(string.Empty, file.Name);
            Assert.IsNull(file.Module);
            Assert.AreEqual(0, file.FileSize);
        }

        [TestMethod]
        public void RecentFile_FileSizeDisplay_ReturnsBytes_WhenSmall()
        {
            var file = new RecentFile { FileSize = 500 };
            Assert.AreEqual("500 B", file.FileSizeDisplay);
        }

        [TestMethod]
        public void RecentFile_FileSizeDisplay_ReturnsKB_WhenMedium()
        {
            var file = new RecentFile { FileSize = 2048 };
            Assert.AreEqual("2.0 KB", file.FileSizeDisplay);
        }

        [TestMethod]
        public void RecentFile_FileSizeDisplay_ReturnsMB_WhenLarge()
        {
            var file = new RecentFile { FileSize = 5 * 1024 * 1024 };
            Assert.AreEqual("5.0 MB", file.FileSizeDisplay);
        }

        [TestMethod]
        public void RecentFile_Exists_ReturnsFalse_WhenFileDoesNotExist()
        {
            var file = new RecentFile { Path = @"C:\NonExistent\File12345.txt" };
            Assert.IsFalse(file.Exists);
        }

        [TestMethod]
        public void RecentFile_Exists_ReturnsTrue_WhenFileExists()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var file = new RecentFile { Path = tempFile };
                Assert.IsTrue(file.Exists);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region RecentWorkspacesData Tests

        [TestMethod]
        public void RecentWorkspacesData_DefaultLists_AreEmpty()
        {
            var data = new RecentWorkspacesData();
            
            Assert.IsNotNull(data.Workspaces);
            Assert.IsNotNull(data.Files);
            Assert.IsNotNull(data.PinnedPaths);
            Assert.AreEqual(0, data.Workspaces.Count);
            Assert.AreEqual(0, data.Files.Count);
            Assert.AreEqual(0, data.PinnedPaths.Count);
        }

        [TestMethod]
        public void RecentWorkspacesData_CanAddWorkspaces()
        {
            var data = new RecentWorkspacesData();
            data.Workspaces.Add(new RecentWorkspace { Path = @"C:\Test1" });
            data.Workspaces.Add(new RecentWorkspace { Path = @"C:\Test2" });
            
            Assert.AreEqual(2, data.Workspaces.Count);
        }

        [TestMethod]
        public void RecentWorkspacesData_CanAddFiles()
        {
            var data = new RecentWorkspacesData();
            data.Files.Add(new RecentFile { Path = @"C:\Test1.txt" });
            data.Files.Add(new RecentFile { Path = @"C:\Test2.txt" });
            
            Assert.AreEqual(2, data.Files.Count);
        }

        [TestMethod]
        public void RecentWorkspacesData_CanAddPinnedPaths()
        {
            var data = new RecentWorkspacesData();
            data.PinnedPaths.Add(@"C:\Pinned1");
            data.PinnedPaths.Add(@"C:\Pinned2");
            
            Assert.AreEqual(2, data.PinnedPaths.Count);
        }

        #endregion

        #region Service Instance Tests

        [TestMethod]
        public void Instance_ReturnsNonNull()
        {
            Assert.IsNotNull(RecentWorkspacesService.Instance);
        }

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = RecentWorkspacesService.Instance;
            var instance2 = RecentWorkspacesService.Instance;
            
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void RecentWorkspaces_Collection_IsNotNull()
        {
            var service = RecentWorkspacesService.Instance;
            Assert.IsNotNull(service.RecentWorkspaces);
        }

        [TestMethod]
        public void RecentFiles_Collection_IsNotNull()
        {
            var service = RecentWorkspacesService.Instance;
            Assert.IsNotNull(service.RecentFiles);
        }

        [TestMethod]
        public void PinnedPaths_Collection_IsNotNull()
        {
            var service = RecentWorkspacesService.Instance;
            Assert.IsNotNull(service.PinnedPaths);
        }

        #endregion
    }
}
