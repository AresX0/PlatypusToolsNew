using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for PcapParserService.
    /// </summary>
    [TestClass]
    public class PcapParserServiceTests
    {
        private PcapParserService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new PcapParserService();
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("PCAP Parser", _service.OperationName);
        }

        [TestMethod]
        public async Task AnalyzeAsync_WithNonExistentFile_ShouldReturnError()
        {
            var result = await _service.AnalyzeAsync(@"C:\nonexistent.pcap");

            Assert.IsTrue(result.Errors.Count > 0);
            Assert.IsTrue(result.Errors[0].Contains("not found"));
        }

        [TestMethod]
        public void PcapPacket_ShouldStoreAllFields()
        {
            var packet = new PcapPacket
            {
                Index = 42,
                Timestamp = DateTime.Now,
                CapturedLength = 100,
                OriginalLength = 100,
                SourceIP = IPAddress.Parse("192.168.1.1"),
                DestIP = IPAddress.Parse("10.0.0.1"),
                SourcePort = 443,
                DestPort = 54321,
                Protocol = 6 // TCP
            };

            Assert.AreEqual(42, packet.Index);
            Assert.AreEqual("192.168.1.1", packet.SourceIP.ToString());
            Assert.AreEqual("10.0.0.1", packet.DestIP.ToString());
            Assert.AreEqual(443, packet.SourcePort);
        }

        [TestMethod]
        public void NetworkArtifact_ShouldTrackCounts()
        {
            var artifact = new NetworkArtifact
            {
                Type = "IP",
                Value = "8.8.8.8",
                Count = 1
            };

            artifact.Count++;
            artifact.PacketIndices.Add(1);
            artifact.PacketIndices.Add(5);

            Assert.AreEqual(2, artifact.Count);
            Assert.AreEqual(2, artifact.PacketIndices.Count);
        }

        [TestMethod]
        public void DnsRecord_ShouldStoreQueryInfo()
        {
            var dns = new DnsRecord
            {
                QueryName = "malware.com",
                RecordType = "A",
                Answer = "1.2.3.4",
                Timestamp = DateTime.Now
            };

            Assert.AreEqual("malware.com", dns.QueryName);
            Assert.AreEqual("A", dns.RecordType);
        }

        [TestMethod]
        public void HttpRequest_ShouldStoreRequestDetails()
        {
            var request = new HttpRequest
            {
                Method = "GET",
                Host = "evil.com",
                Path = "/malware.exe",
                UserAgent = "Mozilla/5.0"
            };

            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual("evil.com", request.Host);
            Assert.AreEqual("/malware.exe", request.Path);
        }

        [TestMethod]
        public void NetworkConnection_ShouldCalculateDuration()
        {
            var conn = new NetworkConnection
            {
                SourceIP = IPAddress.Parse("192.168.1.100"),
                DestIP = IPAddress.Parse("10.0.0.50"),
                Protocol = "TCP",
                FirstSeen = DateTime.Now.AddMinutes(-5),
                LastSeen = DateTime.Now
            };

            var duration = conn.LastSeen - conn.FirstSeen;
            Assert.IsTrue(duration.TotalMinutes >= 5);
        }

        [TestMethod]
        public void ExportIOCs_ShouldGenerateTextOutput()
        {
            var result = new PcapAnalysisResult();
            result.UniqueIPs.Add("192.168.1.1");
            result.UniqueIPs.Add("10.0.0.1");
            result.UniqueDomains.Add("test.com");
            result.UniqueURLs.Add("http://test.com/page");

            var output = _service.ExportIOCs(result);

            Assert.IsTrue(output.Contains("# IP Addresses"));
            Assert.IsTrue(output.Contains("192.168.1.1"));
            Assert.IsTrue(output.Contains("# Domains"));
            Assert.IsTrue(output.Contains("test.com"));
            Assert.IsTrue(output.Contains("# URLs"));
        }

        [TestMethod]
        public void ExportConnectionsCsv_ShouldGenerateValidCsv()
        {
            var result = new PcapAnalysisResult();
            result.Connections.Add(new NetworkConnection
            {
                SourceIP = IPAddress.Parse("192.168.1.1"),
                SourcePort = 54321,
                DestIP = IPAddress.Parse("10.0.0.1"),
                DestPort = 443,
                Protocol = "TCP",
                PacketCount = 100
            });

            var csv = _service.ExportConnectionsCsv(result);

            Assert.IsTrue(csv.Contains("SourceIP,SourcePort"));
            Assert.IsTrue(csv.Contains("192.168.1.1"));
            Assert.IsTrue(csv.Contains("443"));
        }

        [TestMethod]
        public void AnalysisResult_ShouldCalculateDuration()
        {
            var result = new PcapAnalysisResult
            {
                StartTime = DateTime.Now.AddHours(-1),
                EndTime = DateTime.Now
            };

            Assert.IsTrue(result.Duration.TotalHours >= 1);
        }

        [TestMethod]
        public void ServiceOptions_ShouldHaveDefaultValues()
        {
            Assert.IsTrue(_service.ExtractDns);
            Assert.IsTrue(_service.ExtractHttp);
            Assert.IsTrue(_service.ExtractTls);
            Assert.IsFalse(_service.ExtractPayloads);
            Assert.AreEqual(1024, _service.MaxPayloadBytes);
        }
    }
}
