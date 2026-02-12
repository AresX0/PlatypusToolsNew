using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for MetadataTemplateService (TASK-296).
    /// Tests template CRUD, application, and persistence.
    /// </summary>
    [TestClass]
    public class MetadataTemplateServiceTests
    {
        private string _tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        #region Singleton Tests

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = MetadataTemplateService.Instance;
            var instance2 = MetadataTemplateService.Instance;
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region Initialize Tests

        [TestMethod]
        public async Task InitializeAsync_ValidDirectory_Succeeds()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);

            await service.InitializeAsync(templatesDir);

            Assert.IsNotNull(service.Templates);
        }

        #endregion

        #region Template CRUD Tests

        [TestMethod]
        public async Task CreateTemplateAsync_ValidName_CreatesTemplate()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            var template = await service.CreateTemplateAsync("Test Template", "Test Category");

            Assert.IsNotNull(template);
            Assert.AreEqual("Test Template", template.Name);
            Assert.AreEqual("Test Category", template.Category);
        }

        [TestMethod]
        public async Task GetTemplatesByCategory_ReturnsFiltered()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            await service.CreateTemplateAsync("Cat A Template", "CategoryA");
            await service.CreateTemplateAsync("Cat B Template", "CategoryB");

            var catA = service.GetTemplatesByCategory("CategoryA").ToList();
            Assert.IsTrue(catA.Count >= 1);
            Assert.IsTrue(catA.All(t => t.Category == "CategoryA"));
        }

        [TestMethod]
        public async Task GetCategories_ReturnsDistinctCategories()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            await service.CreateTemplateAsync("A", "Photos");
            await service.CreateTemplateAsync("B", "Music");

            var categories = service.GetCategories().ToList();
            Assert.IsTrue(categories.Count >= 2);
        }

        [TestMethod]
        public async Task DeleteTemplateAsync_RemovesTemplate()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            var template = await service.CreateTemplateAsync("To Delete", "Custom");
            var countBefore = service.Templates.Count;

            await service.DeleteTemplateAsync(template);

            Assert.AreEqual(countBefore - 1, service.Templates.Count);
        }

        [TestMethod]
        public async Task DuplicateTemplateAsync_CreatesCopy()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            var original = await service.CreateTemplateAsync("Original", "Custom");
            var duplicate = await service.DuplicateTemplateAsync(original);

            Assert.IsNotNull(duplicate);
            Assert.AreNotEqual(original.Name, duplicate.Name);
        }

        #endregion

        #region Export/Import Tests

        [TestMethod]
        public async Task ExportAndImport_RoundTrip()
        {
            var service = MetadataTemplateService.Instance;
            var templatesDir = Path.Combine(_tempDir, "templates");
            Directory.CreateDirectory(templatesDir);
            await service.InitializeAsync(templatesDir);

            var template = await service.CreateTemplateAsync("Export Test", "Custom");
            var exportPath = Path.Combine(_tempDir, "exported_template.json");

            await service.ExportTemplateAsync(template, exportPath);
            Assert.IsTrue(File.Exists(exportPath));

            var imported = await service.ImportTemplateAsync(exportPath);
            Assert.IsNotNull(imported);
        }

        #endregion
    }
}
