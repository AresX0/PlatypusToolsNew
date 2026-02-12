using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services;
using System.Windows.Input;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="KeyboardShortcutService"/> - keyboard shortcut management.
    /// </summary>
    [TestClass]
    public class KeyboardShortcutServiceTests
    {
        #region Instance Tests

        [TestMethod]
        public void Instance_ReturnsNonNull()
        {
            Assert.IsNotNull(KeyboardShortcutService.Instance);
        }

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = KeyboardShortcutService.Instance;
            var instance2 = KeyboardShortcutService.Instance;
            
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region Shortcuts Collection Tests

        [TestMethod]
        public void Shortcuts_ContainsDefaultShortcuts()
        {
            var service = KeyboardShortcutService.Instance;
            
            // Verify some default shortcuts exist
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.Open"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.Save"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("Edit.Undo"));
        }

        [TestMethod]
        public void Shortcuts_FileOpen_IsCtrlO()
        {
            var service = KeyboardShortcutService.Instance;
            var gesture = service.GetShortcut("File.Open");
            
            Assert.IsNotNull(gesture);
            Assert.AreEqual(Key.O, gesture!.Key);
            Assert.AreEqual(ModifierKeys.Control, gesture.Modifiers);
        }

        [TestMethod]
        public void Shortcuts_FileSave_IsCtrlS()
        {
            var service = KeyboardShortcutService.Instance;
            var gesture = service.GetShortcut("File.Save");
            
            Assert.IsNotNull(gesture);
            Assert.AreEqual(Key.S, gesture!.Key);
            Assert.AreEqual(ModifierKeys.Control, gesture.Modifiers);
        }

        [TestMethod]
        public void Shortcuts_EditUndo_IsCtrlZ()
        {
            var service = KeyboardShortcutService.Instance;
            var gesture = service.GetShortcut("Edit.Undo");
            
            Assert.IsNotNull(gesture);
            Assert.AreEqual(Key.Z, gesture!.Key);
            Assert.AreEqual(ModifierKeys.Control, gesture.Modifiers);
        }

        #endregion

        #region GetShortcut Tests

        [TestMethod]
        public void GetShortcut_ReturnsNull_WhenCommandNotFound()
        {
            var service = KeyboardShortcutService.Instance;
            var gesture = service.GetShortcut("NonExistent.Command");
            
            Assert.IsNull(gesture);
        }

        [TestMethod]
        public void GetShortcut_ReturnsGesture_WhenCommandExists()
        {
            var service = KeyboardShortcutService.Instance;
            var gesture = service.GetShortcut("File.Exit");
            
            Assert.IsNotNull(gesture);
            Assert.AreEqual(Key.F4, gesture!.Key);
            Assert.AreEqual(ModifierKeys.Alt, gesture.Modifiers);
        }

        #endregion

        #region GetShortcutDisplayString Tests

        [TestMethod]
        public void GetShortcutDisplayString_ReturnsNull_WhenCommandNotFound()
        {
            var service = KeyboardShortcutService.Instance;
            var display = service.GetShortcutDisplayString("NonExistent.Command");
            
            Assert.IsNull(display);
        }

        [TestMethod]
        public void GetShortcutDisplayString_FormatsCtrlShortcut()
        {
            var service = KeyboardShortcutService.Instance;
            var display = service.GetShortcutDisplayString("File.Open");
            
            Assert.IsNotNull(display);
            Assert.AreEqual("Ctrl+O", display);
        }

        [TestMethod]
        public void GetShortcutDisplayString_FormatsCtrlShiftShortcut()
        {
            var service = KeyboardShortcutService.Instance;
            var display = service.GetShortcutDisplayString("File.SaveAs");
            
            Assert.IsNotNull(display);
            // Could be "Ctrl+Shift+S" - order depends on implementation
            Assert.IsTrue(display!.Contains("Ctrl"));
            Assert.IsTrue(display.Contains("Shift"));
            Assert.IsTrue(display.Contains("S"));
        }

        [TestMethod]
        public void GetShortcutDisplayString_FormatsAltShortcut()
        {
            var service = KeyboardShortcutService.Instance;
            var display = service.GetShortcutDisplayString("File.Exit");
            
            Assert.IsNotNull(display);
            Assert.AreEqual("Alt+F4", display);
        }

        [TestMethod]
        public void GetShortcutDisplayString_FormatsNoModifierShortcut()
        {
            var service = KeyboardShortcutService.Instance;
            var display = service.GetShortcutDisplayString("View.FullScreen");
            
            Assert.IsNotNull(display);
            Assert.AreEqual("F11", display);
        }

        #endregion

        #region Default Shortcuts Completeness

        [TestMethod]
        public void DefaultShortcuts_ContainsFileMenu()
        {
            var service = KeyboardShortcutService.Instance;
            
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.Open"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.Save"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.SaveAs"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("File.Exit"));
        }

        [TestMethod]
        public void DefaultShortcuts_ContainsEditMenu()
        {
            var service = KeyboardShortcutService.Instance;
            
            Assert.IsTrue(service.Shortcuts.ContainsKey("Edit.Undo"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("Edit.Redo"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("Edit.SelectAll"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("Edit.Find"));
        }

        [TestMethod]
        public void DefaultShortcuts_ContainsViewMenu()
        {
            var service = KeyboardShortcutService.Instance;
            
            Assert.IsTrue(service.Shortcuts.ContainsKey("View.ToggleTheme"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("View.FullScreen"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("View.ZoomIn"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("View.ZoomOut"));
        }

        [TestMethod]
        public void DefaultShortcuts_ContainsHelpMenu()
        {
            var service = KeyboardShortcutService.Instance;
            
            Assert.IsTrue(service.Shortcuts.ContainsKey("Help.Documentation"));
            Assert.IsTrue(service.Shortcuts.ContainsKey("Help.About"));
        }

        #endregion
    }
}
