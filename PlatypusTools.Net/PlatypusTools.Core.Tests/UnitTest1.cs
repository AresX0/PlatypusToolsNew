using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestGetFilteredFiles_NoDir_ReturnsEmpty()
        {
            var items = PlatypusTools.Core.Services.FileCleaner.GetFilteredFiles("C:\\pathdoesnotexist", false);
            Assert.IsNotNull(items);
        }
    }
}