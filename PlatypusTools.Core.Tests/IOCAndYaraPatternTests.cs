using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for IOC scanning patterns and YARA models (TASK-229, TASK-230).
    /// Tests the IOC detection regex patterns used by the scanner services.
    /// </summary>
    [TestClass]
    public class IOCAndYaraPatternTests
    {
        #region IOC Detection Patterns

        private static readonly Regex IPv4Pattern = new(
            @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
            RegexOptions.Compiled);

        private static readonly Regex MD5Pattern = new(@"\b[a-fA-F0-9]{32}\b", RegexOptions.Compiled);
        private static readonly Regex SHA1Pattern = new(@"\b[a-fA-F0-9]{40}\b", RegexOptions.Compiled);
        private static readonly Regex SHA256Pattern = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
        private static readonly Regex URLPattern = new(@"https?://[^\s<>""']+", RegexOptions.Compiled);
        private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex DomainPattern = new(@"\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}\b", RegexOptions.Compiled);
        private static readonly Regex CVEPattern = new(@"CVE-\d{4}-\d{4,}", RegexOptions.Compiled);

        #endregion

        #region IPv4 Tests

        [TestMethod]
        [DataRow("192.168.1.1", true)]
        [DataRow("10.0.0.1", true)]
        [DataRow("255.255.255.255", true)]
        [DataRow("0.0.0.0", true)]
        [DataRow("172.16.0.1", true)]
        [DataRow("256.1.1.1", false)]
        [DataRow("1.1.1.999", false)]
        [DataRow("not an ip", false)]
        public void IPv4_DetectsValidAddresses(string input, bool shouldMatch)
        {
            var result = IPv4Pattern.IsMatch(input);
            Assert.AreEqual(shouldMatch, result, $"IPv4 pattern should {(shouldMatch ? "" : "not ")}match: {input}");
        }

        [TestMethod]
        public void IPv4_FindsMultipleInText()
        {
            var text = "Server 192.168.1.1 connected to 10.0.0.5 via gateway 172.16.0.1";
            var matches = IPv4Pattern.Matches(text);
            Assert.AreEqual(3, matches.Count);
        }

        [TestMethod]
        public void IPv4_DetectsInternalRanges()
        {
            var internalIPs = new[] { "10.0.0.1", "172.16.0.1", "192.168.0.1" };
            foreach (var ip in internalIPs)
            {
                Assert.IsTrue(IPv4Pattern.IsMatch(ip), $"Should match internal IP: {ip}");
            }
        }

        #endregion

        #region Hash Detection Tests

        [TestMethod]
        public void MD5_DetectsValidHash()
        {
            var hash = "d41d8cd98f00b204e9800998ecf8427e";
            Assert.IsTrue(MD5Pattern.IsMatch(hash));
        }

        [TestMethod]
        public void MD5_RejectsInvalidLength()
        {
            var shortHash = "d41d8cd98f00b204e9800998ecf842"; // 31 chars
            Assert.IsFalse(MD5Pattern.IsMatch(shortHash));
        }

        [TestMethod]
        public void SHA1_DetectsValidHash()
        {
            var hash = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
            Assert.IsTrue(SHA1Pattern.IsMatch(hash));
        }

        [TestMethod]
        public void SHA256_DetectsValidHash()
        {
            var hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            Assert.IsTrue(SHA256Pattern.IsMatch(hash));
        }

        [TestMethod]
        public void Hash_ExtractsFromContext()
        {
            var text = "Malware hash: d41d8cd98f00b204e9800998ecf8427e found in system32";
            var matches = MD5Pattern.Matches(text);
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", matches[0].Value);
        }

        #endregion

        #region URL Detection Tests

        [TestMethod]
        [DataRow("https://example.com", true)]
        [DataRow("http://malware.test/payload.exe", true)]
        [DataRow("https://sub.domain.com/path?q=1", true)]
        [DataRow("ftp://server.com", false)]
        [DataRow("not a url", false)]
        public void URL_DetectsValidURLs(string input, bool shouldMatch)
        {
            Assert.AreEqual(shouldMatch, URLPattern.IsMatch(input));
        }

        [TestMethod]
        public void URL_ExtractsMultipleFromText()
        {
            var text = "Downloaded from https://evil.com/malware and https://c2.server/cmd";
            var matches = URLPattern.Matches(text);
            Assert.AreEqual(2, matches.Count);
        }

        #endregion

        #region Email Detection Tests

        [TestMethod]
        [DataRow("user@example.com", true)]
        [DataRow("admin@corp.co.uk", true)]
        [DataRow("test+tag@gmail.com", true)]
        [DataRow("notanemail", false)]
        [DataRow("@domain.com", false)]
        public void Email_DetectsValidAddresses(string input, bool shouldMatch)
        {
            Assert.AreEqual(shouldMatch, EmailPattern.IsMatch(input));
        }

        #endregion

        #region Domain Detection Tests

        [TestMethod]
        [DataRow("example.com", true)]
        [DataRow("sub.domain.co.uk", true)]
        [DataRow("malware-c2.evil.org", true)]
        public void Domain_DetectsValidDomains(string input, bool shouldMatch)
        {
            Assert.AreEqual(shouldMatch, DomainPattern.IsMatch(input));
        }

        #endregion

        #region CVE Detection Tests

        [TestMethod]
        [DataRow("CVE-2024-1234", true)]
        [DataRow("CVE-2023-12345", true)]
        [DataRow("CVE-2021-44228", true)]
        [DataRow("CVE-2024-123", false)] // Too short
        [DataRow("cve-2024-1234", false)] // Lowercase
        public void CVE_DetectsValidIdentifiers(string input, bool shouldMatch)
        {
            Assert.AreEqual(shouldMatch, CVEPattern.IsMatch(input));
        }

        [TestMethod]
        public void CVE_ExtractsLog4Shell()
        {
            var text = "Vulnerability CVE-2021-44228 (Log4Shell) detected in application log";
            var matches = CVEPattern.Matches(text);
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("CVE-2021-44228", matches[0].Value);
        }

        #endregion

        #region Combined IOC Scanning Tests

        [TestMethod]
        public void CombinedScan_FindsAllIOCTypes()
        {
            var text = @"
                Suspicious activity detected:
                - C2 server: 192.168.1.100
                - Malware URL: https://evil.com/payload.exe
                - File hash: d41d8cd98f00b204e9800998ecf8427e
                - Phishing email: admin@evil-corp.com
                - CVE exploited: CVE-2024-5678
            ";

            Assert.IsTrue(IPv4Pattern.IsMatch(text), "Should find IP");
            Assert.IsTrue(URLPattern.IsMatch(text), "Should find URL");
            Assert.IsTrue(MD5Pattern.IsMatch(text), "Should find MD5");
            Assert.IsTrue(EmailPattern.IsMatch(text), "Should find email");
            Assert.IsTrue(CVEPattern.IsMatch(text), "Should find CVE");
        }

        [TestMethod]
        public void IOCCount_MultipleOfSameType()
        {
            var text = "IPs: 10.0.0.1, 10.0.0.2, 10.0.0.3, 10.0.0.4, 10.0.0.5";
            var matches = IPv4Pattern.Matches(text);
            Assert.AreEqual(5, matches.Count);
        }

        #endregion
    }
}
