using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class MetadataServiceTests
    {
        [TestMethod]
        public void GetMetadata_BasicFile_ReturnsInfo()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_meta_test.txt");
            File.WriteAllText(tmp, "hello");
            var meta = MetadataService.GetMetadata(tmp);
            Assert.IsTrue(meta.ContainsKey("Name"));
        }
    }
}