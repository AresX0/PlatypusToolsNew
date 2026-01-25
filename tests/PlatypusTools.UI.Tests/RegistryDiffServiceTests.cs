using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.Linq;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for RegistryDiffService.
    /// </summary>
    [TestClass]
    public class RegistryDiffServiceTests
    {
        private RegistryDiffService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new RegistryDiffService();
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("Registry Diff Tool", _service.OperationName);
        }

        [TestMethod]
        public void Compare_WithEmptySnapshots_ShouldReturnNoChanges()
        {
            var baseline = new RegistrySnapshot { Name = "Baseline" };
            var current = new RegistrySnapshot { Name = "Current" };

            var result = _service.Compare(baseline, current);

            Assert.AreEqual(0, result.TotalChanges);
        }

        [TestMethod]
        public void Compare_WithAddedKey_ShouldDetectAddition()
        {
            var baseline = new RegistrySnapshot { Name = "Baseline" };
            var current = new RegistrySnapshot
            {
                Name = "Current",
                Keys = { new RegistryKeySnapshot { Path = @"HKLM\SOFTWARE\NewKey" } }
            };

            var result = _service.Compare(baseline, current);

            Assert.AreEqual(1, result.KeysAdded);
            Assert.AreEqual(RegistryChangeType.KeyAdded, result.Changes[0].ChangeType);
        }

        [TestMethod]
        public void Compare_WithRemovedKey_ShouldDetectRemoval()
        {
            var baseline = new RegistrySnapshot
            {
                Name = "Baseline",
                Keys = { new RegistryKeySnapshot { Path = @"HKLM\SOFTWARE\OldKey" } }
            };
            var current = new RegistrySnapshot { Name = "Current" };

            var result = _service.Compare(baseline, current);

            Assert.AreEqual(1, result.KeysRemoved);
            Assert.AreEqual(RegistryChangeType.KeyRemoved, result.Changes[0].ChangeType);
        }

        [TestMethod]
        public void Compare_WithAddedValue_ShouldDetectValueAddition()
        {
            var baseline = new RegistrySnapshot
            {
                Name = "Baseline",
                Keys = { new RegistryKeySnapshot { Path = @"HKLM\SOFTWARE\Test" } }
            };
            var current = new RegistrySnapshot
            {
                Name = "Current",
                Keys = { new RegistryKeySnapshot 
                { 
                    Path = @"HKLM\SOFTWARE\Test",
                    Values = { new RegistryValueSnapshot { Name = "NewValue", Value = "Data" } }
                }}
            };

            var result = _service.Compare(baseline, current);

            Assert.AreEqual(1, result.ValuesAdded);
        }

        [TestMethod]
        public void Compare_WithModifiedValue_ShouldDetectModification()
        {
            var baseline = new RegistrySnapshot
            {
                Name = "Baseline",
                Keys = { new RegistryKeySnapshot 
                { 
                    Path = @"HKLM\SOFTWARE\Test",
                    Values = { new RegistryValueSnapshot { Name = "MyValue", Value = "OldData" } }
                }}
            };
            var current = new RegistrySnapshot
            {
                Name = "Current",
                Keys = { new RegistryKeySnapshot 
                { 
                    Path = @"HKLM\SOFTWARE\Test",
                    Values = { new RegistryValueSnapshot { Name = "MyValue", Value = "NewData" } }
                }}
            };

            var result = _service.Compare(baseline, current);

            Assert.AreEqual(1, result.ValuesModified);
            Assert.AreEqual("OldData", result.Changes[0].OldValue);
            Assert.AreEqual("NewData", result.Changes[0].NewValue);
        }

        [TestMethod]
        public void ExportToHtml_ShouldGenerateValidHtml()
        {
            var result = new RegistryDiffResult();
            result.Changes.Add(new RegistryChange
            {
                ChangeType = RegistryChangeType.ValueAdded,
                KeyPath = @"HKLM\SOFTWARE\Test",
                ValueName = "Test",
                NewValue = "Value",
                Category = "Other",
                Severity = "info"
            });

            var html = _service.ExportToHtml(result);

            Assert.IsTrue(html.Contains("<!DOCTYPE html>"));
            Assert.IsTrue(html.Contains("Registry Diff Report"));
            Assert.IsTrue(html.Contains("ValueAdded"));
        }

        [TestMethod]
        public void ExportToCsv_ShouldGenerateValidCsv()
        {
            var result = new RegistryDiffResult();
            result.Changes.Add(new RegistryChange
            {
                ChangeType = RegistryChangeType.KeyAdded,
                KeyPath = @"HKLM\SOFTWARE\Malware",
                Category = "Persistence",
                Severity = "critical"
            });

            var csv = _service.ExportToCsv(result);

            Assert.IsTrue(csv.Contains("ChangeType,KeyPath"));
            Assert.IsTrue(csv.Contains("KeyAdded"));
            Assert.IsTrue(csv.Contains("Persistence"));
        }

        [TestMethod]
        public void RegistryChangeType_ShouldHaveAllExpectedTypes()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(RegistryChangeType), RegistryChangeType.KeyAdded));
            Assert.IsTrue(Enum.IsDefined(typeof(RegistryChangeType), RegistryChangeType.KeyRemoved));
            Assert.IsTrue(Enum.IsDefined(typeof(RegistryChangeType), RegistryChangeType.ValueAdded));
            Assert.IsTrue(Enum.IsDefined(typeof(RegistryChangeType), RegistryChangeType.ValueRemoved));
            Assert.IsTrue(Enum.IsDefined(typeof(RegistryChangeType), RegistryChangeType.ValueModified));
        }

        [TestMethod]
        public void DiffResult_Counters_ShouldCalculateCorrectly()
        {
            var result = new RegistryDiffResult();
            result.Changes.Add(new RegistryChange { ChangeType = RegistryChangeType.KeyAdded });
            result.Changes.Add(new RegistryChange { ChangeType = RegistryChangeType.KeyAdded });
            result.Changes.Add(new RegistryChange { ChangeType = RegistryChangeType.ValueModified });

            Assert.AreEqual(2, result.KeysAdded);
            Assert.AreEqual(0, result.KeysRemoved);
            Assert.AreEqual(1, result.ValuesModified);
            Assert.AreEqual(3, result.TotalChanges);
        }
    }
}
