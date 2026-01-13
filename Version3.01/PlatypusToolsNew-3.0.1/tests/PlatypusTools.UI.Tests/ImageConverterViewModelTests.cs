using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using System.Threading;
using System;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class ImageConverterViewModelTests
    {
        [TestMethod]
        public async Task ConvertAsync_ReportsProgressAndLog()
        {
            Task<bool> FakeConverter(string src, string dest, int? mw, int? mh, long q)
            {
                return Task.FromResult(true);
            }

            var vm = new ImageConverterViewModel(FakeConverter);
            vm.Files.Add("a.jpg");
            vm.OutputFolder = System.IO.Path.GetTempPath();

            var task = vm.ConvertAsync();
            await task;
            Assert.IsFalse(vm.IsRunning);
            Assert.AreEqual(100.0, vm.Progress);
            StringAssert.Contains(vm.Log, "Converted");
        }
    }
}