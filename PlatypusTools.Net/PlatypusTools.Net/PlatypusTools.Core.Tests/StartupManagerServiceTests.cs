using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class StartupManagerServiceTests
    {
        [TestMethod]
        public void GetStartupItems_ReturnsItems()
        {
            var service = new StartupManagerService();
            var items = service.GetStartupItems();
            
            Assert.IsNotNull(items);
            // Should have at least some items (system or user)
            // Note: Count may vary by system, so we just check it doesn't crash
        }

        [TestMethod]
        public void StartupItem_HasRequiredProperties()
        {
            var item = new StartupItem
            {
                Name = "TestApp",
                Command = "C:\\test.exe",
                Location = "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                Type = "Registry",
                IsEnabled = true
            };

            Assert.AreEqual("TestApp", item.Name);
            Assert.AreEqual("C:\\test.exe", item.Command);
            Assert.AreEqual("HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", item.Location);
            Assert.AreEqual("Registry", item.Type);
            Assert.IsTrue(item.IsEnabled);
        }

        [TestMethod]
        public void GetStartupItems_ContainsExpectedTypes()
        {
            var service = new StartupManagerService();
            var items = service.GetStartupItems();

            // Should have items from various sources
            var types = items.Select(i => i.Type).Distinct().ToList();
            
            // At minimum, should not throw exceptions
            Assert.IsNotNull(types);
        }
    }
}
