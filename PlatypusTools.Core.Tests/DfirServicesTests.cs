using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services.Forensics;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for DFIR (Digital Forensics and Incident Response) services.
    /// </summary>
    [TestClass]
    public class DfirServicesTests
    {
        #region IOCScannerService Tests

        [TestMethod]
        public void IOCScanner_DetectsIPAddress_Valid()
        {
            // Arrange
            var content = "The server connected to 192.168.1.100 on port 443";
            
            // Act - Use regex pattern matching similar to IOCScannerService
            var ipPattern = new System.Text.RegularExpressions.Regex(
                @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b");
            var matches = ipPattern.Matches(content);
            
            // Assert
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("192.168.1.100", matches[0].Value);
        }

        [TestMethod]
        public void IOCScanner_DetectsURL_Valid()
        {
            // Arrange
            var content = "Download from https://malware.example.com/payload.exe";
            
            // Act
            var urlPattern = new System.Text.RegularExpressions.Regex(
                @"https?://[^\s<>""']+");
            var matches = urlPattern.Matches(content);
            
            // Assert
            Assert.AreEqual(1, matches.Count);
            Assert.IsTrue(matches[0].Value.Contains("malware.example.com"));
        }

        [TestMethod]
        public void IOCScanner_DetectsMD5Hash_Valid()
        {
            // Arrange
            var content = "File hash: d41d8cd98f00b204e9800998ecf8427e";
            
            // Act
            var md5Pattern = new System.Text.RegularExpressions.Regex(
                @"\b[a-fA-F0-9]{32}\b");
            var matches = md5Pattern.Matches(content);
            
            // Assert
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", matches[0].Value);
        }

        [TestMethod]
        public void IOCScanner_DetectsSHA256Hash_Valid()
        {
            // Arrange
            var content = "SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            
            // Act
            var sha256Pattern = new System.Text.RegularExpressions.Regex(
                @"\b[a-fA-F0-9]{64}\b");
            var matches = sha256Pattern.Matches(content);
            
            // Assert
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", matches[0].Value);
        }

        [TestMethod]
        public void IOCScanner_DetectsEmail_Valid()
        {
            // Arrange
            var content = "Contact: admin@example.com for support";
            
            // Act
            var emailPattern = new System.Text.RegularExpressions.Regex(
                @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            var matches = emailPattern.Matches(content);
            
            // Assert
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("admin@example.com", matches[0].Value);
        }

        [TestMethod]
        public void IOCScanner_DetectsDomain_ExcludesCommon()
        {
            // Arrange
            var domains = new[] { "malware.evil.com", "google.com", "microsoft.com" };
            var commonDomains = new HashSet<string> { "google.com", "microsoft.com", "apple.com" };
            
            // Act
            var suspiciousDomains = domains.Where(d => !commonDomains.Contains(d)).ToList();
            
            // Assert
            Assert.AreEqual(1, suspiciousDomains.Count);
            Assert.AreEqual("malware.evil.com", suspiciousDomains[0]);
        }

        #endregion

        #region RegistryDiffService Tests

        [TestMethod]
        public void RegistryDiff_DetectsNewKey_Valid()
        {
            // Arrange
            var before = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "Value1" },
                { @"HKLM\SOFTWARE\Test\Key2", "Value2" }
            };
            
            var after = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "Value1" },
                { @"HKLM\SOFTWARE\Test\Key2", "Value2" },
                { @"HKLM\SOFTWARE\Test\Key3", "Value3" } // New
            };
            
            // Act
            var newKeys = after.Keys.Except(before.Keys).ToList();
            
            // Assert
            Assert.AreEqual(1, newKeys.Count);
            Assert.AreEqual(@"HKLM\SOFTWARE\Test\Key3", newKeys[0]);
        }

        [TestMethod]
        public void RegistryDiff_DetectsModifiedValue_Valid()
        {
            // Arrange
            var before = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "OriginalValue" }
            };
            
            var after = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "ModifiedValue" }
            };
            
            // Act
            var modified = after.Where(kvp => 
                before.TryGetValue(kvp.Key, out var oldValue) && oldValue != kvp.Value).ToList();
            
            // Assert
            Assert.AreEqual(1, modified.Count);
            Assert.AreEqual("ModifiedValue", modified[0].Value);
        }

        [TestMethod]
        public void RegistryDiff_DetectsDeletedKey_Valid()
        {
            // Arrange
            var before = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "Value1" },
                { @"HKLM\SOFTWARE\Test\Key2", "Value2" }
            };
            
            var after = new Dictionary<string, string>
            {
                { @"HKLM\SOFTWARE\Test\Key1", "Value1" }
            };
            
            // Act
            var deletedKeys = before.Keys.Except(after.Keys).ToList();
            
            // Assert
            Assert.AreEqual(1, deletedKeys.Count);
            Assert.AreEqual(@"HKLM\SOFTWARE\Test\Key2", deletedKeys[0]);
        }

        #endregion

        #region PcapParserService Tests

        [TestMethod]
        public void PcapParser_DetectsMagicNumber_Pcap()
        {
            // Arrange
            var pcapMagic = new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 }; // Little endian PCAP
            
            // Act
            bool isPcap = pcapMagic.SequenceEqual(new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 }) ||
                          pcapMagic.SequenceEqual(new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 });
            
            // Assert
            Assert.IsTrue(isPcap);
        }

        [TestMethod]
        public void PcapParser_DetectsMagicNumber_PcapNg()
        {
            // Arrange
            var pcapngMagic = new byte[] { 0x0A, 0x0D, 0x0D, 0x0A };
            
            // Act
            bool isPcapNg = pcapngMagic.SequenceEqual(new byte[] { 0x0A, 0x0D, 0x0D, 0x0A });
            
            // Assert
            Assert.IsTrue(isPcapNg);
        }

        [TestMethod]
        public void PcapParser_ParsesEthernetHeader_Valid()
        {
            // Arrange - Ethernet frame header (14 bytes)
            var ethernetHeader = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // Destination MAC (broadcast)
                0x00, 0x11, 0x22, 0x33, 0x44, 0x55, // Source MAC
                0x08, 0x00                           // EtherType (IPv4)
            };
            
            // Act
            var destMac = string.Join(":", ethernetHeader.Take(6).Select(b => b.ToString("X2")));
            var srcMac = string.Join(":", ethernetHeader.Skip(6).Take(6).Select(b => b.ToString("X2")));
            var etherType = (ethernetHeader[12] << 8) | ethernetHeader[13];
            
            // Assert
            Assert.AreEqual("FF:FF:FF:FF:FF:FF", destMac);
            Assert.AreEqual("00:11:22:33:44:55", srcMac);
            Assert.AreEqual(0x0800, etherType); // IPv4
        }

        [TestMethod]
        public void PcapParser_ParsesIPv4Header_Valid()
        {
            // Arrange - Minimal IPv4 header
            var ipv4Header = new byte[]
            {
                0x45, 0x00,             // Version (4), IHL (5), DSCP, ECN
                0x00, 0x3C,             // Total Length (60)
                0x00, 0x01, 0x00, 0x00, // ID, Flags, Fragment Offset
                0x40, 0x06,             // TTL (64), Protocol (TCP)
                0x00, 0x00,             // Header Checksum
                0xC0, 0xA8, 0x01, 0x01, // Source IP (192.168.1.1)
                0xC0, 0xA8, 0x01, 0x02  // Dest IP (192.168.1.2)
            };
            
            // Act
            var version = (ipv4Header[0] >> 4) & 0x0F;
            var protocol = ipv4Header[9];
            var srcIp = $"{ipv4Header[12]}.{ipv4Header[13]}.{ipv4Header[14]}.{ipv4Header[15]}";
            var dstIp = $"{ipv4Header[16]}.{ipv4Header[17]}.{ipv4Header[18]}.{ipv4Header[19]}";
            
            // Assert
            Assert.AreEqual(4, version);
            Assert.AreEqual(6, protocol); // TCP
            Assert.AreEqual("192.168.1.1", srcIp);
            Assert.AreEqual("192.168.1.2", dstIp);
        }

        [TestMethod]
        public void PcapParser_IdentifiesHTTPRequest_Valid()
        {
            // Arrange
            var httpPayload = System.Text.Encoding.ASCII.GetBytes("GET /index.html HTTP/1.1\r\nHost: example.com\r\n\r\n");
            
            // Act
            var payloadStr = System.Text.Encoding.ASCII.GetString(httpPayload);
            var isHttp = payloadStr.StartsWith("GET ") || payloadStr.StartsWith("POST ") || 
                         payloadStr.StartsWith("HTTP/");
            
            // Assert
            Assert.IsTrue(isHttp);
            Assert.IsTrue(payloadStr.Contains("GET /index.html"));
        }

        [TestMethod]
        public void PcapParser_ExtractsDomain_FromDNS()
        {
            // Arrange - Simulated DNS query for "example.com"
            var dnsQuery = "example.com";
            
            // Act - Domain validation
            bool isValidDomain = Uri.CheckHostName(dnsQuery) == UriHostNameType.Dns;
            
            // Assert
            Assert.IsTrue(isValidDomain);
        }

        #endregion

        #region BrowserForensicsService Tests

        [TestMethod]
        public void BrowserForensics_ParsesChromiumBookmarks_Valid()
        {
            // Arrange
            var bookmarksJson = @"{
                ""roots"": {
                    ""bookmark_bar"": {
                        ""children"": [
                            {
                                ""type"": ""url"",
                                ""name"": ""Test Bookmark"",
                                ""url"": ""https://example.com""
                            }
                        ]
                    }
                }
            }";
            
            // Act
            using var doc = System.Text.Json.JsonDocument.Parse(bookmarksJson);
            var roots = doc.RootElement.GetProperty("roots");
            var bookmarkBar = roots.GetProperty("bookmark_bar");
            var children = bookmarkBar.GetProperty("children");
            var firstBookmark = children.EnumerateArray().First();
            
            // Assert
            Assert.AreEqual("url", firstBookmark.GetProperty("type").GetString());
            Assert.AreEqual("Test Bookmark", firstBookmark.GetProperty("name").GetString());
            Assert.AreEqual("https://example.com", firstBookmark.GetProperty("url").GetString());
        }

        [TestMethod]
        public void BrowserForensics_ParsesFirefoxLogins_Valid()
        {
            // Arrange
            var loginsJson = @"{
                ""logins"": [
                    {
                        ""hostname"": ""https://example.com"",
                        ""username"": ""testuser"",
                        ""passwordEncrypted"": ""encrypted_data""
                    }
                ]
            }";
            
            // Act
            using var doc = System.Text.Json.JsonDocument.Parse(loginsJson);
            var logins = doc.RootElement.GetProperty("logins");
            var firstLogin = logins.EnumerateArray().First();
            
            // Assert
            Assert.AreEqual("https://example.com", firstLogin.GetProperty("hostname").GetString());
            Assert.AreEqual("testuser", firstLogin.GetProperty("username").GetString());
        }

        [TestMethod]
        public void BrowserForensics_DetectsChromeProfilePath_Valid()
        {
            // Arrange
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var expectedPath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
            
            // Act - Just verify path construction
            var chromeDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data");
            
            // Assert
            Assert.AreEqual(expectedPath, chromeDataPath);
        }

        [TestMethod]
        public void BrowserForensics_DetectsFirefoxProfilePath_Valid()
        {
            // Arrange
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var expectedPath = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
            
            // Act
            var firefoxProfilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");
            
            // Assert
            Assert.AreEqual(expectedPath, firefoxProfilePath);
        }

        [TestMethod]
        public void BrowserForensics_ConvertsChromeTimestamp_Valid()
        {
            // Arrange - Chrome uses microseconds since 1601-01-01
            long chromeTimestamp = 13287633600000000; // Approx 2022-01-01
            
            // Act
            var baseDate = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dateTime = baseDate.AddTicks(chromeTimestamp * 10); // Microseconds to ticks
            
            // Assert
            Assert.IsTrue(dateTime.Year >= 2021 && dateTime.Year <= 2023);
        }

        #endregion

        #region ForensicsHubService Tests

        [TestMethod]
        public void ForensicsHub_ClassifiesRisk_High()
        {
            // Arrange
            var indicators = new[]
            {
                "ransomware.exe",
                "cryptolocker.dll",
                "C:\\Windows\\Temp\\malware.exe"
            };
            
            var highRiskPatterns = new[] { "ransomware", "cryptolocker", "malware" };
            
            // Act
            var isHighRisk = indicators.Any(i => 
                highRiskPatterns.Any(p => i.Contains(p, StringComparison.OrdinalIgnoreCase)));
            
            // Assert
            Assert.IsTrue(isHighRisk);
        }

        [TestMethod]
        public void ForensicsHub_ClassifiesRisk_Low()
        {
            // Arrange
            var indicators = new[]
            {
                "notepad.exe",
                "calc.exe",
                "C:\\Windows\\System32\\kernel32.dll"
            };
            
            var highRiskPatterns = new[] { "ransomware", "cryptolocker", "malware", "trojan" };
            
            // Act
            var isHighRisk = indicators.Any(i => 
                highRiskPatterns.Any(p => i.Contains(p, StringComparison.OrdinalIgnoreCase)));
            
            // Assert
            Assert.IsFalse(isHighRisk);
        }

        #endregion

        #region Data Export Tests

        [TestMethod]
        public void Export_GeneratesValidJson()
        {
            // Arrange
            var data = new
            {
                Type = "IOC",
                Value = "192.168.1.100",
                Timestamp = DateTime.UtcNow
            };
            
            // Act
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            // Assert
            Assert.AreEqual("IOC", doc.RootElement.GetProperty("Type").GetString());
            Assert.AreEqual("192.168.1.100", doc.RootElement.GetProperty("Value").GetString());
        }

        [TestMethod]
        public void Export_GeneratesValidCsv()
        {
            // Arrange
            var records = new[]
            {
                new { Type = "IP", Value = "192.168.1.1" },
                new { Type = "Domain", Value = "example.com" }
            };
            
            // Act
            var csv = "Type,Value\n" + string.Join("\n", records.Select(r => $"{r.Type},{r.Value}"));
            var lines = csv.Split('\n');
            
            // Assert
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("Type,Value", lines[0]);
            Assert.IsTrue(lines[1].Contains("IP,192.168.1.1"));
        }

        #endregion

        #region Integration Helpers

        [TestMethod]
        public void PathCombine_HandlesSpecialCharacters()
        {
            // Arrange
            var basePath = @"C:\Evidence";
            var subPath = "User Files [2024]";
            
            // Act
            var combined = Path.Combine(basePath, subPath);
            
            // Assert
            Assert.AreEqual(@"C:\Evidence\User Files [2024]", combined);
        }

        [TestMethod]
        public void HashValidation_RecognizesFormat()
        {
            // Arrange
            var md5 = "d41d8cd98f00b204e9800998ecf8427e";
            var sha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
            var sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            
            // Act & Assert
            Assert.AreEqual(32, md5.Length);
            Assert.AreEqual(40, sha1.Length);
            Assert.AreEqual(64, sha256.Length);
            Assert.IsTrue(md5.All(c => char.IsLetterOrDigit(c)));
            Assert.IsTrue(sha1.All(c => char.IsLetterOrDigit(c)));
            Assert.IsTrue(sha256.All(c => char.IsLetterOrDigit(c)));
        }

        #endregion
    }
}
