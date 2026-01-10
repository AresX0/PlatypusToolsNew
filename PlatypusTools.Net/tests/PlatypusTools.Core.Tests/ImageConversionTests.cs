using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class ImageConversionTests
    {
        [TestMethod]
        public void ConvertImage_ResizesAndCreatesFile()
        {
            var tmp = Path.GetTempFileName();
            var dst = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg");
            try
            {
                // create a small bitmap
                using (var bmp = new System.Drawing.Bitmap(200, 200))
                {
                    bmp.Save(tmp);
                }

                var ok = ImageConversionService.ConvertImage(tmp, dst, 100, 100, 80);
                Assert.IsTrue(ok);
                Assert.IsTrue(File.Exists(dst));
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
                try { File.Delete(dst); } catch { }
            }
        }
    }
}