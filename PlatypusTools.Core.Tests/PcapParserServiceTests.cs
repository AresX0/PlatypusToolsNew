using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services.Forensics;
using static PlatypusTools.Core.Services.Forensics.PcapParserService;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for PcapParserService (TASK-231).
    /// Tests PCAP file detection, parsing, and result structure.
    /// </summary>
    [TestClass]
    public class PcapParserServiceTests
    {
        #region Singleton Tests

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = PcapParserService.Instance;
            var instance2 = PcapParserService.Instance;

            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region DetectFileType Tests

        [TestMethod]
        public void DetectFileType_NonExistentFile_ReturnsUnknown()
        {
            var result = PcapParserService.Instance.DetectFileType(@"C:\nonexistent\file.pcap");
            Assert.AreEqual(PcapFileType.Unknown, result);
        }

        [TestMethod]
        public void DetectFileType_EmptyFile_ReturnsUnknown()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var result = PcapParserService.Instance.DetectFileType(tempFile);
                Assert.AreEqual(PcapFileType.Unknown, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void DetectFileType_PcapMagicBytes_DetectsPcap()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                // Write PCAP magic bytes (little-endian: 0xD4C3B2A1)
                var magic = new byte[] { 0xD4, 0xC3, 0xB2, 0xA1, 0x02, 0x00, 0x04, 0x00 };
                File.WriteAllBytes(tempFile, magic);

                var result = PcapParserService.Instance.DetectFileType(tempFile);
                Assert.AreEqual(PcapFileType.Pcap, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void DetectFileType_PcapNgMagicBytes_DetectsPcapNg()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                // Write PCAPNG magic bytes (Section Header Block: 0x0A0D0D0A)
                var magic = new byte[] { 0x0A, 0x0D, 0x0D, 0x0A, 0x00, 0x00, 0x00, 0x00 };
                File.WriteAllBytes(tempFile, magic);

                var result = PcapParserService.Instance.DetectFileType(tempFile);
                Assert.AreEqual(PcapFileType.PcapNg, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region ParseFile Tests

        [TestMethod]
        public async Task ParseFileAsync_NonExistentFile_ThrowsOrReturnsEmpty()
        {
            try
            {
                var result = await PcapParserService.Instance.ParseFileAsync(@"C:\nonexistent\file.pcap");
                // If it returns instead of throwing, verify structure
                Assert.IsNotNull(result);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is ArgumentException || ex is InvalidOperationException)
            {
                // Expected
            }
        }

        [TestMethod]
        public async Task ParseFileAsync_Cancellation_ThrowsOrReturns()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await PcapParserService.Instance.ParseFileAsync(@"C:\nonexistent\file.pcap", cts.Token);
            }
            catch (Exception)
            {
                // Expected: file not found or cancellation
            }
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void PcapFileType_HasExpectedValues()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(PcapFileType), PcapFileType.Unknown));
            Assert.IsTrue(Enum.IsDefined(typeof(PcapFileType), PcapFileType.Pcap));
            Assert.IsTrue(Enum.IsDefined(typeof(PcapFileType), PcapFileType.PcapNg));
        }

        [TestMethod]
        public void ProtocolType_HasCommonProtocols()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(ProtocolType), ProtocolType.TCP));
            Assert.IsTrue(Enum.IsDefined(typeof(ProtocolType), ProtocolType.UDP));
            Assert.IsTrue(Enum.IsDefined(typeof(ProtocolType), ProtocolType.HTTP));
            Assert.IsTrue(Enum.IsDefined(typeof(ProtocolType), ProtocolType.DNS));
        }

        [TestMethod]
        public void NetworkPacket_CanBeCreated()
        {
            var packet = new NetworkPacket
            {
                Timestamp = DateTime.UtcNow,
                SourceIP = System.Net.IPAddress.Parse("192.168.1.1"),
                DestinationIP = System.Net.IPAddress.Parse("10.0.0.1"),
                SourcePort = 80,
                DestinationPort = 443,
                Protocol = ProtocolType.TCP,
                CapturedLength = 1500
            };

            Assert.AreEqual(System.Net.IPAddress.Parse("192.168.1.1"), packet.SourceIP);
            Assert.AreEqual(ProtocolType.TCP, packet.Protocol);
            Assert.AreEqual(1500, packet.CapturedLength);
        }

        [TestMethod]
        public void PcapAnalysisResult_DefaultStructure()
        {
            var result = new PcapAnalysisResult();
            Assert.IsNotNull(result.Packets);
            Assert.IsNotNull(result.Connections);
            Assert.IsNotNull(result.Artifacts);
            Assert.IsNotNull(result.DnsQueries);
        }

        #endregion
    }
}
