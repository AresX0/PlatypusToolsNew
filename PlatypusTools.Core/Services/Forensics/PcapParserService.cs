using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Forensics
{
    /// <summary>
    /// Service for parsing PCAP and PCAPNG network capture files.
    /// Extracts network artifacts, IOCs, and connection metadata.
    /// </summary>
    public class PcapParserService
    {
        private static readonly Lazy<PcapParserService> _instance = new(() => new PcapParserService());
        public static PcapParserService Instance => _instance.Value;

        // Magic numbers for file type detection
        private static readonly byte[] PCAP_MAGIC_LE = { 0xD4, 0xC3, 0xB2, 0xA1 };
        private static readonly byte[] PCAP_MAGIC_BE = { 0xA1, 0xB2, 0xC3, 0xD4 };
        private static readonly byte[] PCAPNG_MAGIC = { 0x0A, 0x0D, 0x0D, 0x0A };

        public event EventHandler<PcapParseProgress>? ProgressChanged;
        public event EventHandler<NetworkArtifact>? ArtifactFound;

        #region Models

        public enum PcapFileType
        {
            Unknown,
            Pcap,
            PcapNg
        }

        public enum ProtocolType
        {
            Unknown,
            Ethernet,
            IPv4,
            IPv6,
            TCP,
            UDP,
            ICMP,
            ARP,
            DNS,
            HTTP,
            HTTPS,
            FTP,
            SMTP,
            SSH,
            Telnet,
            RDP,
            SMB,
            DHCP,
            NTP
        }

        public class PcapParseProgress
        {
            public long BytesProcessed { get; set; }
            public long TotalBytes { get; set; }
            public int PacketsProcessed { get; set; }
            public int ArtifactsFound { get; set; }
            public double PercentComplete => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
        }

        public class PcapFileInfo
        {
            public string FilePath { get; set; } = string.Empty;
            public PcapFileType FileType { get; set; }
            public bool IsBigEndian { get; set; }
            public int MajorVersion { get; set; }
            public int MinorVersion { get; set; }
            public int SnapLength { get; set; }
            public int LinkType { get; set; }
            public DateTime? CaptureStartTime { get; set; }
            public DateTime? CaptureEndTime { get; set; }
            public long FileSize { get; set; }
            public int TotalPackets { get; set; }
        }

        public class NetworkPacket
        {
            public int PacketNumber { get; set; }
            public DateTime Timestamp { get; set; }
            public int CapturedLength { get; set; }
            public int OriginalLength { get; set; }
            public byte[] Data { get; set; } = Array.Empty<byte>();

            // Parsed fields
            public string? SourceMAC { get; set; }
            public string? DestinationMAC { get; set; }
            public IPAddress? SourceIP { get; set; }
            public IPAddress? DestinationIP { get; set; }
            public int SourcePort { get; set; }
            public int DestinationPort { get; set; }
            public ProtocolType Protocol { get; set; }
            public string? PayloadPreview { get; set; }
        }

        public class NetworkConnection
        {
            public string ConnectionId { get; set; } = string.Empty;
            public IPAddress? SourceIP { get; set; }
            public IPAddress? DestinationIP { get; set; }
            public int SourcePort { get; set; }
            public int DestinationPort { get; set; }
            public ProtocolType Protocol { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public long BytesSent { get; set; }
            public long BytesReceived { get; set; }
            public int PacketCount { get; set; }
            public List<string> Flags { get; set; } = new();
        }

        public class NetworkArtifact
        {
            public string Type { get; set; } = string.Empty; // URL, Email, Domain, IP, File, Credential, etc.
            public string Value { get; set; } = string.Empty;
            public string Context { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public int PacketNumber { get; set; }
            public ProtocolType Protocol { get; set; }
            public string? SourceIP { get; set; }
            public string? DestinationIP { get; set; }
            public double Confidence { get; set; } = 1.0;
            public Dictionary<string, string> Metadata { get; set; } = new();
        }

        public class DnsQuery
        {
            public DateTime Timestamp { get; set; }
            public string QueryName { get; set; } = string.Empty;
            public string QueryType { get; set; } = string.Empty;
            public string? Response { get; set; }
            public IPAddress? ClientIP { get; set; }
            public IPAddress? ServerIP { get; set; }
            public int TransactionId { get; set; }
        }

        public class HttpRequest
        {
            public DateTime Timestamp { get; set; }
            public string Method { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public string UserAgent { get; set; } = string.Empty;
            public string ContentType { get; set; } = string.Empty;
            public int ContentLength { get; set; }
            public Dictionary<string, string> Headers { get; set; } = new();
            public IPAddress? ClientIP { get; set; }
            public IPAddress? ServerIP { get; set; }
            public int ClientPort { get; set; }
            public int ServerPort { get; set; }
        }

        public class PcapAnalysisResult
        {
            public PcapFileInfo FileInfo { get; set; } = new();
            public List<NetworkPacket> Packets { get; set; } = new();
            public List<NetworkConnection> Connections { get; set; } = new();
            public List<NetworkArtifact> Artifacts { get; set; } = new();
            public List<DnsQuery> DnsQueries { get; set; } = new();
            public List<HttpRequest> HttpRequests { get; set; } = new();
            public Dictionary<string, int> ProtocolDistribution { get; set; } = new();
            public Dictionary<string, int> TopTalkers { get; set; } = new();
            public List<string> Errors { get; set; } = new();
            public TimeSpan ParseDuration { get; set; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Detects the file type of a capture file.
        /// </summary>
        public PcapFileType DetectFileType(string filePath)
        {
            if (!File.Exists(filePath))
                return PcapFileType.Unknown;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var magic = new byte[4];
                if (fs.Read(magic, 0, 4) < 4)
                    return PcapFileType.Unknown;

                if (magic.SequenceEqual(PCAP_MAGIC_LE) || magic.SequenceEqual(PCAP_MAGIC_BE))
                    return PcapFileType.Pcap;

                if (magic.SequenceEqual(PCAPNG_MAGIC))
                    return PcapFileType.PcapNg;

                return PcapFileType.Unknown;
            }
            catch
            {
                return PcapFileType.Unknown;
            }
        }

        /// <summary>
        /// Parses a PCAP or PCAPNG file and extracts network artifacts.
        /// </summary>
        public async Task<PcapAnalysisResult> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var result = new PcapAnalysisResult();

            try
            {
                var fileType = DetectFileType(filePath);
                if (fileType == PcapFileType.Unknown)
                {
                    result.Errors.Add($"Unknown or unsupported file format: {filePath}");
                    return result;
                }

                result.FileInfo = await ReadFileHeaderAsync(filePath, fileType);

                // Parse packets
                await ParsePacketsAsync(filePath, fileType, result, cancellationToken);

                // Analyze connections
                AnalyzeConnections(result);

                // Extract artifacts
                await ExtractArtifactsAsync(result, cancellationToken);

                // Calculate statistics
                CalculateStatistics(result);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Parse error: {ex.Message}");
            }

            result.ParseDuration = DateTime.UtcNow - startTime;
            return result;
        }

        /// <summary>
        /// Quick scan for IOCs without full packet parsing.
        /// </summary>
        public async Task<List<NetworkArtifact>> QuickScanForIOCsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var artifacts = new List<NetworkArtifact>();

            try
            {
                var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
                var text = Encoding.ASCII.GetString(content.Where(b => b >= 0x20 && b < 0x7F).ToArray());

                // Extract URLs
                var urlRegex = new Regex(@"https?://[^\s<>\""']+", RegexOptions.IgnoreCase);
                foreach (Match match in urlRegex.Matches(text))
                {
                    artifacts.Add(new NetworkArtifact
                    {
                        Type = "URL",
                        Value = match.Value,
                        Context = "Extracted from packet payload",
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Extract email addresses
                var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                foreach (Match match in emailRegex.Matches(text))
                {
                    artifacts.Add(new NetworkArtifact
                    {
                        Type = "Email",
                        Value = match.Value,
                        Context = "Extracted from packet payload",
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Extract domains
                var domainRegex = new Regex(@"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}\b", RegexOptions.IgnoreCase);
                foreach (Match match in domainRegex.Matches(text))
                {
                    if (!match.Value.Contains("@") && match.Value.Length > 4)
                    {
                        artifacts.Add(new NetworkArtifact
                        {
                            Type = "Domain",
                            Value = match.Value,
                            Context = "Extracted from packet payload",
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }

                // Extract potential file paths
                var pathRegex = new Regex(@"[A-Za-z]:\\[^\s<>\""'|?*]+|/[a-z][^\s<>\""']+", RegexOptions.IgnoreCase);
                foreach (Match match in pathRegex.Matches(text))
                {
                    artifacts.Add(new NetworkArtifact
                    {
                        Type = "FilePath",
                        Value = match.Value,
                        Context = "Extracted from packet payload",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                artifacts.Add(new NetworkArtifact
                {
                    Type = "Error",
                    Value = ex.Message,
                    Context = "Quick scan failed"
                });
            }

            return artifacts.DistinctBy(a => a.Value).ToList();
        }

        /// <summary>
        /// Exports analysis results to various formats.
        /// </summary>
        public async Task ExportResultsAsync(PcapAnalysisResult result, string outputPath, string format, CancellationToken cancellationToken = default)
        {
            switch (format.ToLower())
            {
                case "json":
                    await ExportToJsonAsync(result, outputPath, cancellationToken);
                    break;
                case "csv":
                    await ExportToCsvAsync(result, outputPath, cancellationToken);
                    break;
                case "html":
                    await ExportToHtmlAsync(result, outputPath, cancellationToken);
                    break;
                default:
                    throw new ArgumentException($"Unsupported export format: {format}");
            }
        }

        #endregion

        #region Private Methods

        private async Task<PcapFileInfo> ReadFileHeaderAsync(string filePath, PcapFileType fileType)
        {
            var info = new PcapFileInfo
            {
                FilePath = filePath,
                FileType = fileType,
                FileSize = new FileInfo(filePath).Length
            };

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            if (fileType == PcapFileType.Pcap)
            {
                var magic = br.ReadUInt32();
                info.IsBigEndian = magic == 0xA1B2C3D4;

                info.MajorVersion = ReadUInt16(br, info.IsBigEndian);
                info.MinorVersion = ReadUInt16(br, info.IsBigEndian);

                br.ReadInt32(); // thiszone
                br.ReadUInt32(); // sigfigs
                info.SnapLength = (int)ReadUInt32(br, info.IsBigEndian);
                info.LinkType = (int)ReadUInt32(br, info.IsBigEndian);
            }
            else if (fileType == PcapFileType.PcapNg)
            {
                // PCAPNG Section Header Block
                br.ReadUInt32(); // Block type
                var blockLength = br.ReadUInt32();
                var byteOrderMagic = br.ReadUInt32();
                info.IsBigEndian = byteOrderMagic == 0x1A2B3C4D;
                info.MajorVersion = ReadUInt16(br, info.IsBigEndian);
                info.MinorVersion = ReadUInt16(br, info.IsBigEndian);
            }

            await Task.CompletedTask;
            return info;
        }

        private async Task ParsePacketsAsync(string filePath, PcapFileType fileType, PcapAnalysisResult result, CancellationToken cancellationToken)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            var progress = new PcapParseProgress { TotalBytes = fs.Length };
            var isBigEndian = result.FileInfo.IsBigEndian;

            // Skip header
            if (fileType == PcapFileType.Pcap)
                fs.Seek(24, SeekOrigin.Begin);
            else
                SkipPcapNgHeaders(br);

            int packetNumber = 0;
            while (fs.Position < fs.Length && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    NetworkPacket? packet = null;

                    if (fileType == PcapFileType.Pcap)
                        packet = ReadPcapPacket(br, isBigEndian, ++packetNumber);
                    else
                        packet = ReadPcapNgPacket(br, isBigEndian, ++packetNumber);

                    if (packet != null)
                    {
                        ParsePacketLayers(packet);
                        result.Packets.Add(packet);

                        progress.PacketsProcessed++;
                        progress.BytesProcessed = fs.Position;

                        if (packetNumber % 1000 == 0)
                            ProgressChanged?.Invoke(this, progress);
                    }
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Packet {packetNumber}: {ex.Message}");
                }
            }

            result.FileInfo.TotalPackets = result.Packets.Count;
            if (result.Packets.Any())
            {
                result.FileInfo.CaptureStartTime = result.Packets.First().Timestamp;
                result.FileInfo.CaptureEndTime = result.Packets.Last().Timestamp;
            }

            await Task.CompletedTask;
        }

        private NetworkPacket? ReadPcapPacket(BinaryReader br, bool isBigEndian, int packetNumber)
        {
            var tsSec = ReadUInt32(br, isBigEndian);
            var tsUsec = ReadUInt32(br, isBigEndian);
            var capturedLen = (int)ReadUInt32(br, isBigEndian);
            var originalLen = (int)ReadUInt32(br, isBigEndian);

            if (capturedLen > 65535 || capturedLen <= 0)
                return null;

            var data = br.ReadBytes(capturedLen);

            return new NetworkPacket
            {
                PacketNumber = packetNumber,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(tsSec).DateTime.AddMicroseconds(tsUsec),
                CapturedLength = capturedLen,
                OriginalLength = originalLen,
                Data = data
            };
        }

        private NetworkPacket? ReadPcapNgPacket(BinaryReader br, bool isBigEndian, int packetNumber)
        {
            var blockType = ReadUInt32(br, isBigEndian);
            var blockTotalLength = (int)ReadUInt32(br, isBigEndian);

            if (blockType != 6) // Enhanced Packet Block
            {
                br.BaseStream.Seek(blockTotalLength - 8, SeekOrigin.Current);
                return null;
            }

            var interfaceId = ReadUInt32(br, isBigEndian);
            var timestampHigh = ReadUInt32(br, isBigEndian);
            var timestampLow = ReadUInt32(br, isBigEndian);
            var capturedLen = (int)ReadUInt32(br, isBigEndian);
            var originalLen = (int)ReadUInt32(br, isBigEndian);

            if (capturedLen > 65535 || capturedLen <= 0)
            {
                br.BaseStream.Seek(blockTotalLength - 28, SeekOrigin.Current);
                return null;
            }

            var data = br.ReadBytes(capturedLen);

            // Skip padding and trailing block length
            var padding = (4 - (capturedLen % 4)) % 4;
            br.BaseStream.Seek(padding + 4, SeekOrigin.Current);

            var timestamp = ((long)timestampHigh << 32) | timestampLow;

            return new NetworkPacket
            {
                PacketNumber = packetNumber,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000).DateTime,
                CapturedLength = capturedLen,
                OriginalLength = originalLen,
                Data = data
            };
        }

        private void SkipPcapNgHeaders(BinaryReader br)
        {
            // Skip Section Header Block
            br.BaseStream.Seek(4, SeekOrigin.Begin); // Skip magic
            var blockLength = br.ReadUInt32();
            br.BaseStream.Seek(blockLength - 8, SeekOrigin.Current);

            // Skip Interface Description Block if present
            var nextBlockType = br.ReadUInt32();
            if (nextBlockType == 1) // IDB
            {
                var idbLength = br.ReadUInt32();
                br.BaseStream.Seek(idbLength - 8, SeekOrigin.Current);
            }
            else
            {
                br.BaseStream.Seek(-4, SeekOrigin.Current);
            }
        }

        private void ParsePacketLayers(NetworkPacket packet)
        {
            if (packet.Data.Length < 14)
                return;

            // Ethernet header
            packet.DestinationMAC = FormatMAC(packet.Data, 0);
            packet.SourceMAC = FormatMAC(packet.Data, 6);
            var etherType = (packet.Data[12] << 8) | packet.Data[13];

            int ipOffset = 14;

            // Handle VLAN tags
            if (etherType == 0x8100)
            {
                ipOffset += 4;
                etherType = (packet.Data[16] << 8) | packet.Data[17];
            }

            if (etherType == 0x0800) // IPv4
            {
                ParseIPv4(packet, ipOffset);
            }
            else if (etherType == 0x86DD) // IPv6
            {
                ParseIPv6(packet, ipOffset);
            }
            else if (etherType == 0x0806) // ARP
            {
                packet.Protocol = ProtocolType.ARP;
            }
        }

        private void ParseIPv4(NetworkPacket packet, int offset)
        {
            if (packet.Data.Length < offset + 20)
                return;

            packet.Protocol = ProtocolType.IPv4;

            var headerLen = (packet.Data[offset] & 0x0F) * 4;
            var protocol = packet.Data[offset + 9];

            packet.SourceIP = new IPAddress(new[] { packet.Data[offset + 12], packet.Data[offset + 13], packet.Data[offset + 14], packet.Data[offset + 15] });
            packet.DestinationIP = new IPAddress(new[] { packet.Data[offset + 16], packet.Data[offset + 17], packet.Data[offset + 18], packet.Data[offset + 19] });

            int transportOffset = offset + headerLen;

            switch (protocol)
            {
                case 6: // TCP
                    ParseTCP(packet, transportOffset);
                    break;
                case 17: // UDP
                    ParseUDP(packet, transportOffset);
                    break;
                case 1: // ICMP
                    packet.Protocol = ProtocolType.ICMP;
                    break;
            }
        }

        private void ParseIPv6(NetworkPacket packet, int offset)
        {
            if (packet.Data.Length < offset + 40)
                return;

            packet.Protocol = ProtocolType.IPv6;

            var nextHeader = packet.Data[offset + 6];
            packet.SourceIP = new IPAddress(packet.Data.Skip(offset + 8).Take(16).ToArray());
            packet.DestinationIP = new IPAddress(packet.Data.Skip(offset + 24).Take(16).ToArray());

            int transportOffset = offset + 40;

            switch (nextHeader)
            {
                case 6: // TCP
                    ParseTCP(packet, transportOffset);
                    break;
                case 17: // UDP
                    ParseUDP(packet, transportOffset);
                    break;
            }
        }

        private void ParseTCP(NetworkPacket packet, int offset)
        {
            if (packet.Data.Length < offset + 20)
                return;

            packet.Protocol = ProtocolType.TCP;
            packet.SourcePort = (packet.Data[offset] << 8) | packet.Data[offset + 1];
            packet.DestinationPort = (packet.Data[offset + 2] << 8) | packet.Data[offset + 3];

            // Determine application protocol
            var port = Math.Min(packet.SourcePort, packet.DestinationPort);
            packet.Protocol = port switch
            {
                80 => ProtocolType.HTTP,
                443 => ProtocolType.HTTPS,
                21 => ProtocolType.FTP,
                22 => ProtocolType.SSH,
                23 => ProtocolType.Telnet,
                25 or 587 => ProtocolType.SMTP,
                3389 => ProtocolType.RDP,
                445 or 139 => ProtocolType.SMB,
                _ => ProtocolType.TCP
            };

            // Extract payload preview
            var dataOffset = ((packet.Data[offset + 12] >> 4) & 0x0F) * 4;
            var payloadOffset = offset + dataOffset;
            if (payloadOffset < packet.Data.Length)
            {
                var payloadLen = Math.Min(100, packet.Data.Length - payloadOffset);
                var payload = packet.Data.Skip(payloadOffset).Take(payloadLen).ToArray();
                packet.PayloadPreview = Encoding.ASCII.GetString(payload.Where(b => b >= 0x20 && b < 0x7F).ToArray());
            }
        }

        private void ParseUDP(NetworkPacket packet, int offset)
        {
            if (packet.Data.Length < offset + 8)
                return;

            packet.Protocol = ProtocolType.UDP;
            packet.SourcePort = (packet.Data[offset] << 8) | packet.Data[offset + 1];
            packet.DestinationPort = (packet.Data[offset + 2] << 8) | packet.Data[offset + 3];

            var port = Math.Min(packet.SourcePort, packet.DestinationPort);
            packet.Protocol = port switch
            {
                53 => ProtocolType.DNS,
                67 or 68 => ProtocolType.DHCP,
                123 => ProtocolType.NTP,
                _ => ProtocolType.UDP
            };
        }

        private void AnalyzeConnections(PcapAnalysisResult result)
        {
            var connections = new Dictionary<string, NetworkConnection>();

            foreach (var packet in result.Packets.Where(p => p.SourceIP != null && p.DestinationIP != null))
            {
                var key = GetConnectionKey(packet);

                if (!connections.TryGetValue(key, out var conn))
                {
                    conn = new NetworkConnection
                    {
                        ConnectionId = key,
                        SourceIP = packet.SourceIP,
                        DestinationIP = packet.DestinationIP,
                        SourcePort = packet.SourcePort,
                        DestinationPort = packet.DestinationPort,
                        Protocol = packet.Protocol,
                        FirstSeen = packet.Timestamp
                    };
                    connections[key] = conn;
                }

                conn.LastSeen = packet.Timestamp;
                conn.PacketCount++;
                conn.BytesSent += packet.CapturedLength;
            }

            result.Connections = connections.Values.ToList();
        }

        private async Task ExtractArtifactsAsync(PcapAnalysisResult result, CancellationToken cancellationToken)
        {
            foreach (var packet in result.Packets)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Extract DNS queries
                if (packet.Protocol == ProtocolType.DNS && packet.PayloadPreview != null)
                {
                    ExtractDnsQuery(packet, result);
                }

                // Extract HTTP requests
                if (packet.Protocol == ProtocolType.HTTP && packet.PayloadPreview != null)
                {
                    ExtractHttpRequest(packet, result);
                }

                // Extract IOCs from payload
                if (!string.IsNullOrEmpty(packet.PayloadPreview))
                {
                    ExtractPayloadArtifacts(packet, result);
                }
            }

            // Deduplicate artifacts
            result.Artifacts = result.Artifacts
                .GroupBy(a => new { a.Type, a.Value })
                .Select(g => g.First())
                .ToList();

            await Task.CompletedTask;
        }

        private void ExtractDnsQuery(NetworkPacket packet, PcapAnalysisResult result)
        {
            // Simplified DNS extraction from payload preview
            var query = new DnsQuery
            {
                Timestamp = packet.Timestamp,
                ClientIP = packet.SourcePort > 1024 ? packet.SourceIP : packet.DestinationIP,
                ServerIP = packet.SourcePort > 1024 ? packet.DestinationIP : packet.SourceIP
            };

            // Extract domain from payload
            var domainMatch = Regex.Match(packet.PayloadPreview ?? "", @"\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}\b", RegexOptions.IgnoreCase);
            if (domainMatch.Success)
            {
                query.QueryName = domainMatch.Value;
                result.DnsQueries.Add(query);

                result.Artifacts.Add(new NetworkArtifact
                {
                    Type = "DNS",
                    Value = query.QueryName,
                    Context = $"DNS query from {query.ClientIP}",
                    Timestamp = packet.Timestamp,
                    PacketNumber = packet.PacketNumber,
                    Protocol = ProtocolType.DNS,
                    SourceIP = query.ClientIP?.ToString(),
                    DestinationIP = query.ServerIP?.ToString()
                });

                ArtifactFound?.Invoke(this, result.Artifacts.Last());
            }
        }

        private void ExtractHttpRequest(NetworkPacket packet, PcapAnalysisResult result)
        {
            var payload = packet.PayloadPreview ?? "";

            // Check for HTTP methods
            var methodMatch = Regex.Match(payload, @"^(GET|POST|PUT|DELETE|HEAD|OPTIONS|PATCH)\s+(\S+)\s+HTTP", RegexOptions.IgnoreCase);
            if (methodMatch.Success)
            {
                var request = new HttpRequest
                {
                    Timestamp = packet.Timestamp,
                    Method = methodMatch.Groups[1].Value,
                    Url = methodMatch.Groups[2].Value,
                    ClientIP = packet.SourceIP,
                    ServerIP = packet.DestinationIP,
                    ClientPort = packet.SourcePort,
                    ServerPort = packet.DestinationPort
                };

                // Extract Host header
                var hostMatch = Regex.Match(payload, @"Host:\s*(\S+)", RegexOptions.IgnoreCase);
                if (hostMatch.Success)
                    request.Host = hostMatch.Groups[1].Value;

                // Extract User-Agent
                var uaMatch = Regex.Match(payload, @"User-Agent:\s*(.+?)(?:\r|\n)", RegexOptions.IgnoreCase);
                if (uaMatch.Success)
                    request.UserAgent = uaMatch.Groups[1].Value.Trim();

                result.HttpRequests.Add(request);

                // Add as artifact
                var fullUrl = request.Host.Length > 0 ? $"http://{request.Host}{request.Url}" : request.Url;
                result.Artifacts.Add(new NetworkArtifact
                {
                    Type = "HTTP",
                    Value = fullUrl,
                    Context = $"{request.Method} request from {request.ClientIP}",
                    Timestamp = packet.Timestamp,
                    PacketNumber = packet.PacketNumber,
                    Protocol = ProtocolType.HTTP,
                    SourceIP = packet.SourceIP?.ToString(),
                    DestinationIP = packet.DestinationIP?.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["Method"] = request.Method,
                        ["UserAgent"] = request.UserAgent
                    }
                });

                ArtifactFound?.Invoke(this, result.Artifacts.Last());
            }
        }

        private void ExtractPayloadArtifacts(NetworkPacket packet, PcapAnalysisResult result)
        {
            var payload = packet.PayloadPreview ?? "";

            // URLs
            var urlMatches = Regex.Matches(payload, @"https?://[^\s<>\""']+", RegexOptions.IgnoreCase);
            foreach (Match match in urlMatches)
            {
                var artifact = new NetworkArtifact
                {
                    Type = "URL",
                    Value = match.Value,
                    Context = $"Found in {packet.Protocol} packet",
                    Timestamp = packet.Timestamp,
                    PacketNumber = packet.PacketNumber,
                    Protocol = packet.Protocol,
                    SourceIP = packet.SourceIP?.ToString(),
                    DestinationIP = packet.DestinationIP?.ToString()
                };
                result.Artifacts.Add(artifact);
                ArtifactFound?.Invoke(this, artifact);
            }

            // Email addresses
            var emailMatches = Regex.Matches(payload, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            foreach (Match match in emailMatches)
            {
                var artifact = new NetworkArtifact
                {
                    Type = "Email",
                    Value = match.Value,
                    Context = $"Found in {packet.Protocol} packet",
                    Timestamp = packet.Timestamp,
                    PacketNumber = packet.PacketNumber,
                    Protocol = packet.Protocol
                };
                result.Artifacts.Add(artifact);
                ArtifactFound?.Invoke(this, artifact);
            }

            // Potential credentials (username/password patterns)
            var credMatches = Regex.Matches(payload, @"(user(name)?|login|email|pass(word)?|pwd|auth)[=:]\s*[^\s&]+", RegexOptions.IgnoreCase);
            foreach (Match match in credMatches)
            {
                var artifact = new NetworkArtifact
                {
                    Type = "PotentialCredential",
                    Value = match.Value,
                    Context = "SENSITIVE - Potential credential found",
                    Timestamp = packet.Timestamp,
                    PacketNumber = packet.PacketNumber,
                    Protocol = packet.Protocol,
                    Confidence = 0.7
                };
                result.Artifacts.Add(artifact);
                ArtifactFound?.Invoke(this, artifact);
            }
        }

        private void CalculateStatistics(PcapAnalysisResult result)
        {
            // Protocol distribution
            result.ProtocolDistribution = result.Packets
                .GroupBy(p => p.Protocol.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Top talkers (by packet count)
            var ipCounts = new Dictionary<string, int>();
            foreach (var packet in result.Packets)
            {
                if (packet.SourceIP != null)
                {
                    var ip = packet.SourceIP.ToString();
                    ipCounts[ip] = ipCounts.GetValueOrDefault(ip) + 1;
                }
            }

            result.TopTalkers = ipCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private string GetConnectionKey(NetworkPacket packet)
        {
            var ips = new[] { packet.SourceIP?.ToString() ?? "", packet.DestinationIP?.ToString() ?? "" }.OrderBy(x => x).ToArray();
            var ports = new[] { packet.SourcePort, packet.DestinationPort }.OrderBy(x => x).ToArray();
            return $"{ips[0]}:{ports[0]}-{ips[1]}:{ports[1]}-{packet.Protocol}";
        }

        private string FormatMAC(byte[] data, int offset)
        {
            return string.Join(":", data.Skip(offset).Take(6).Select(b => b.ToString("X2")));
        }

        private ushort ReadUInt16(BinaryReader br, bool bigEndian)
        {
            var value = br.ReadUInt16();
            return bigEndian ? (ushort)((value >> 8) | (value << 8)) : value;
        }

        private uint ReadUInt32(BinaryReader br, bool bigEndian)
        {
            var value = br.ReadUInt32();
            if (!bigEndian) return value;
            return ((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value & 0xFF0000) >> 8) | ((value & 0xFF000000) >> 24);
        }

        #endregion

        #region Export Methods

        private async Task ExportToJsonAsync(PcapAnalysisResult result, string outputPath, CancellationToken cancellationToken)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        }

        private async Task ExportToCsvAsync(PcapAnalysisResult result, string outputPath, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();

            // Artifacts CSV
            sb.AppendLine("Type,Value,Context,Timestamp,PacketNumber,Protocol,SourceIP,DestinationIP");
            foreach (var artifact in result.Artifacts)
            {
                sb.AppendLine($"\"{artifact.Type}\",\"{artifact.Value}\",\"{artifact.Context}\",\"{artifact.Timestamp:O}\",{artifact.PacketNumber},\"{artifact.Protocol}\",\"{artifact.SourceIP}\",\"{artifact.DestinationIP}\"");
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString(), cancellationToken);
        }

        private async Task ExportToHtmlAsync(PcapAnalysisResult result, string outputPath, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><title>PCAP Analysis Report</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;margin:20px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:8px;text-align:left}th{background:#4CAF50;color:white}.section{margin:20px 0}</style></head><body>");
            sb.AppendLine($"<h1>PCAP Analysis Report</h1>");
            sb.AppendLine($"<p>File: {result.FileInfo.FilePath}</p>");
            sb.AppendLine($"<p>Packets: {result.FileInfo.TotalPackets} | Connections: {result.Connections.Count} | Artifacts: {result.Artifacts.Count}</p>");

            sb.AppendLine("<div class='section'><h2>Artifacts</h2><table><tr><th>Type</th><th>Value</th><th>Context</th><th>Timestamp</th></tr>");
            foreach (var artifact in result.Artifacts.Take(100))
            {
                sb.AppendLine($"<tr><td>{artifact.Type}</td><td>{System.Web.HttpUtility.HtmlEncode(artifact.Value)}</td><td>{artifact.Context}</td><td>{artifact.Timestamp:O}</td></tr>");
            }
            sb.AppendLine("</table></div>");

            sb.AppendLine("</body></html>");
            await File.WriteAllTextAsync(outputPath, sb.ToString(), cancellationToken);
        }

        #endregion
    }
}
