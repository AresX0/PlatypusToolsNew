using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Views;
using System.Threading;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class PromptPasswordWindowTests
    {
        [TestMethod]
        public void PromptPasswordWindow_ReturnsEnteredPassword()
        {
            var thread = new Thread(() =>
            {
                var dlg = new PromptPasswordWindow("Enter password for test") { Owner = null };
                // Schedule closing dialog with pre-filled password
                dlg.Loaded += (s, e) =>
                {
                    dlg.PwdBox.Password = "abc123";
                    dlg.DialogResult = true; // this will close the dialog immediately
                };
                var res = dlg.ShowDialog();
                Assert.IsTrue(res == true);
                Assert.AreEqual("abc123", dlg.EnteredPassword);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);
        }
    }
}