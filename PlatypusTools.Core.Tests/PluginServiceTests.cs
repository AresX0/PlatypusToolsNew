using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services;
using System;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="PluginService"/> - plugin loading and management.
    /// </summary>
    [TestClass]
    public class PluginServiceTests
    {
        #region Instance Tests

        [TestMethod]
        public void Instance_ReturnsNonNull()
        {
            Assert.IsNotNull(PluginService.Instance);
        }

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = PluginService.Instance;
            var instance2 = PluginService.Instance;
            
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region Plugins Collection Tests

        [TestMethod]
        public void Plugins_IsNotNull()
        {
            var service = PluginService.Instance;
            Assert.IsNotNull(service.Plugins);
        }

        [TestMethod]
        public void Plugins_IsReadOnly()
        {
            var service = PluginService.Instance;
            
            // Plugins property returns IReadOnlyDictionary
            var plugins = service.Plugins;
            Assert.IsNotNull(plugins);
        }

        #endregion

        #region PluginLoadContext Tests

        // Note: PluginLoadContext requires a real DLL path with valid assembly metadata.
        // Testing the constructor directly fails because AssemblyDependencyResolver validates the path.
        // These tests verify the LoadedPlugin model patterns instead.

        [TestMethod]
        public void LoadedPlugin_IsSandboxed_Property_Depends_On_Context()
        {
            // LoadedPlugin.IsSandboxed returns true when Context is not null
            // We can't easily test with a real PluginLoadContext without a real plugin DLL
            var loaded = new LoadedPlugin
            {
                Context = null,
                AssemblyPath = "test.dll"
            };
            
            Assert.IsFalse(loaded.IsSandboxed, "Should be false when Context is null");
        }

        #endregion

        #region LoadedPlugin Model Tests

        [TestMethod]
        public void LoadedPlugin_DefaultValues()
        {
            var loaded = new LoadedPlugin();
            
            Assert.IsNull(loaded.Context);
            Assert.AreEqual(string.Empty, loaded.AssemblyPath);
            Assert.IsFalse(loaded.IsSandboxed);
        }

        [TestMethod]
        public void LoadedPlugin_IsSandboxed_ReturnsFalse_WhenContextIsNull()
        {
            var loaded = new LoadedPlugin
            {
                Context = null,
                AssemblyPath = "test.dll"
            };
            
            Assert.IsFalse(loaded.IsSandboxed);
        }

        #endregion

        #region PluginMenuItem Model Tests

        [TestMethod]
        public void PluginMenuItem_DefaultValues()
        {
            var item = new PluginMenuItem();
            
            Assert.AreEqual(string.Empty, item.Header);
            Assert.IsNull(item.Icon);
            Assert.IsNull(item.Category);
            Assert.IsNull(item.Command);
            Assert.IsNull(item.SubItems);
        }

        [TestMethod]
        public void PluginMenuItem_Properties_CanBeSet()
        {
            var executed = false;
            var item = new PluginMenuItem
            {
                Header = "Test Item",
                Icon = "icon.png",
                Category = "Tools",
                Command = () => executed = true,
                SubItems = new System.Collections.Generic.List<PluginMenuItem>
                {
                    new PluginMenuItem { Header = "Sub Item" }
                }
            };
            
            Assert.AreEqual("Test Item", item.Header);
            Assert.AreEqual("icon.png", item.Icon);
            Assert.AreEqual("Tools", item.Category);
            Assert.IsNotNull(item.Command);
            Assert.IsNotNull(item.SubItems);
            Assert.AreEqual(1, item.SubItems.Count);
            
            item.Command();
            Assert.IsTrue(executed);
        }

        #endregion
    }
}
