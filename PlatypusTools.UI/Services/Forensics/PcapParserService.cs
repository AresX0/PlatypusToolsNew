using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    #region Models

    /// <summary>
    /// PCAP file header (global header).
    /// </summary>
    public class PcapHeader
    {
        public uint MagicNumber { get; set; }
        public ushort VersionMajor { get; set; }
        public ushort VersionMinor { get; set; }
        public int ThisZone { get; set; }
        public uint SigFigs { get; set; }
        public uint SnapLen { get; set; }
        public uint Network { get; set; } // Link-layer type
        public bool IsLittleEndian { get; set; }
        public bool IsNanoseconds { get; set; }
    }

    /// <summary>
    /// Individual packet from PCAP.
    /// </summary>
    public class PcapPacket
    {
        public int Index { get; set; }
        public DateTime Timestamp { get; set; }
        public uint CapturedLength { get; set; }
        public uint OriginalLength { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        
        // Parsed fields
        public string? SourceMac { get; set; }
        public string? DestMac { get; set; }
        public ushort EtherType { get; set; }
        public IPAddress? SourceIP { get; set; }
        public IPAddress? DestIP { get; set; }
        public byte Protocol { get; set; }
        public ushort SourcePort { get; set; }
        public ushort DestPort { get; set; }
        public string? Payload { get; set; }
        public List<string> Flags { get; set; } = new();
    }

    /// <summary>
    /// Extracted network artifact from PCAP.
    /// </summary>
    public class NetworkArtifact
    {
        public string Type { get; set; } = string.Empty; // DNS, HTTP, TLS, IP, Domain, etc.
        public string Value { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int Count { get; set; } = 1;
        public List<int> PacketIndices { get; } = new();
    }

    /// <summary>
    /// DNS query/response extracted from PCAP.
    /// </summary>
    public class DnsRecord
    {
        public string QueryName { get; set; } = string.Empty;
        public string RecordType { get; set; } = string.Empty;
        public string? Answer { get; set; }
        public DateTime Timestamp { get; set; }
        public IPAddress? SourceIP { get; set; }
        public IPAddress? DestIP { get; set; }
    }

    /// <summary>
    /// HTTP request/response extracted from PCAP.
    /// </summary>
    public class HttpRequest
    {
        public string Method { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
        public DateTime Timestamp { get; set; }
        public IPAddress? SourceIP { get; set; }
        public IPAddress? DestIP { get; set; }
        public int PacketIndex { get; set; }
    }

    /// <summary>
    /// Network connection summary.
    /// </summary>
    public class NetworkConnection
    {
        public IPAddress SourceIP { get; set; } = IPAddress.None;
        public ushort SourcePort { get; set; }
        public IPAddress DestIP { get; set; } = IPAddress.None;
        public ushort DestPort { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public int PacketCount { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public List<string> Flags { get; } = new();
    }

    /// <summary>
    /// Complete PCAP analysis result.
    /// </summary>
    public class PcapAnalysisResult
    {
        public string FilePath { get; set; } = string.Empty;
        public PcapHeader Header { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public int TotalPackets { get; set; }
        public long TotalBytes { get; set; }

        public List<NetworkArtifact> Artifacts { get; } = new();
        public List<DnsRecord> DnsRecords { get; } = new();
        public List<HttpRequest> HttpRequests { get; } = new();
        public List<NetworkConnection> Connections { get; } = new();
        public Dictionary<string, int> ProtocolStats { get; } = new();
        public Dictionary<string, int> PortStats { get; } = new();
        public List<string> Errors { get; } = new();
        
        // IOC extractions
        public HashSet<string> UniqueIPs { get; } = new();
        public HashSet<string> UniqueDomains { get; } = new();
        public HashSet<string> UniqueURLs { get; } = new();
    }

    #endregion

    /// <summary>
    /// PCAP Parser Service for network forensics.
    /// Parses pcap/pcapng files and extracts network artifacts, IOCs, and connections.
    /// </summary>
    public class PcapParserService : ForensicOperationBase
    {
        public override string OperationName => "PCAP Parser";

        // Protocol numbers
        private const byte PROTO_ICMP = 1;
        private const byte PROTO_TCP = 6;
        private const byte PROTO_UDP = 17;

        // EtherTypes
        private const ushort ETHERTYPE_IPV4 = 0x0800;
        private const ushort ETHERTYPE_ARP = 0x0806;
        private const ushort ETHERTYPE_IPV6 = 0x86DD;

        // Options
        public bool ExtractDns { get; set; } = true;
        public bool ExtractHttp { get; set; } = true;
        public bool ExtractTls { get; set; } = true;
        public bool ExtractPayloads { get; set; } = false;
        public int MaxPayloadBytes { get; set; } = 1024;
        public int MaxPackets { get; set; } = 1000000;

        #region Parsing

        /// <summary>
        /// Analyzes a PCAP or PCAPNG file.
        /// </summary>
        public async Task<PcapAnalysisResult> AnalyzeAsync(string filePath, CancellationToken token = default)
        {
            var result = new PcapAnalysisResult { FilePath = filePath };

            LogHeader($"Analyzing PCAP: {Path.GetFileName(filePath)}");

            if (!File.Exists(filePath))
            {
                result.Errors.Add($"File not found: {filePath}");
                return result;
            }

            await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                using var reader = new BinaryReader(stream);

                // Read and validate header
                var magic = reader.ReadUInt32();
                if (!ValidateMagic(magic, result))
                {
                    result.Errors.Add("Invalid PCAP file format");
                    return;
                }

                result.Header = ReadPcapHeader(reader, magic);
                Log($"PCAP v{result.Header.VersionMajor}.{result.Header.VersionMinor}, SnapLen: {result.Header.SnapLen}");

                var connectionMap = new Dictionary<string, NetworkConnection>();

                // Read packets
                var packetIndex = 0;
                while (stream.Position < stream.Length && packetIndex < MaxPackets)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var packet = ReadPacket(reader, result.Header, packetIndex);
                        if (packet == null) break;

                        packetIndex++;
                        result.TotalPackets++;
                        result.TotalBytes += packet.OriginalLength;

                        // Parse Ethernet frame
                        ParseEthernet(packet);

                        // Track timestamps
                        if (result.TotalPackets == 1 || packet.Timestamp < result.StartTime)
                            result.StartTime = packet.Timestamp;
                        if (packet.Timestamp > result.EndTime)
                            result.EndTime = packet.Timestamp;

                        // Extract artifacts
                        if (packet.SourceIP != null)
                        {
                            result.UniqueIPs.Add(packet.SourceIP.ToString());
                            AddOrUpdateArtifact(result.Artifacts, "IP", packet.SourceIP.ToString(), $"Source IP", packet);
                        }
                        if (packet.DestIP != null)
                        {
                            result.UniqueIPs.Add(packet.DestIP.ToString());
                            AddOrUpdateArtifact(result.Artifacts, "IP", packet.DestIP.ToString(), $"Dest IP", packet);
                        }

                        // Update connection tracking
                        if (packet.SourceIP != null && packet.DestIP != null)
                        {
                            UpdateConnection(connectionMap, packet);
                        }

                        // Protocol stats
                        var protoName = GetProtocolName(packet.Protocol);
                        result.ProtocolStats.TryGetValue(protoName, out var protoCount);
                        result.ProtocolStats[protoName] = protoCount + 1;

                        // Port stats for TCP/UDP
                        if (packet.Protocol == PROTO_TCP || packet.Protocol == PROTO_UDP)
                        {
                            var portKey = $"{packet.DestPort}/{protoName}";
                            result.PortStats.TryGetValue(portKey, out var portCount);
                            result.PortStats[portKey] = portCount + 1;
                        }

                        // Extract DNS
                        if (ExtractDns && (packet.SourcePort == 53 || packet.DestPort == 53))
                        {
                            ExtractDnsRecords(packet, result);
                        }

                        // Extract HTTP
                        if (ExtractHttp && (packet.DestPort == 80 || packet.SourcePort == 80))
                        {
                            ExtractHttpRequest(packet, result);
                        }

                        // Extract TLS/HTTPS indicators
                        if (ExtractTls && (packet.DestPort == 443 || packet.SourcePort == 443))
                        {
                            ExtractTlsInfo(packet, result);
                        }

                        if (packetIndex % 10000 == 0)
                        {
                            ReportProgress($"{packetIndex} packets");
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Packet {packetIndex}: {ex.Message}");
                    }
                }

                result.Connections.AddRange(connectionMap.Values);
            }, token);

            LogSuccess($"Analysis complete: {result.TotalPackets} packets, {result.UniqueIPs.Count} unique IPs, {result.DnsRecords.Count} DNS records");
            return result;
        }

        private bool ValidateMagic(uint magic, PcapAnalysisResult result)
        {
            // Standard PCAP
            if (magic == 0xa1b2c3d4 || magic == 0xd4c3b2a1)
                return true;
            // Nanosecond PCAP
            if (magic == 0xa1b23c4d || magic == 0x4d3cb2a1)
                return true;
            // PCAPNG section header
            if (magic == 0x0a0d0d0a)
            {
                result.Errors.Add("PCAPNG format detected - basic parsing only");
                return true;
            }
            return false;
        }

        private PcapHeader ReadPcapHeader(BinaryReader reader, uint magic)
        {
            var header = new PcapHeader
            {
                MagicNumber = magic,
                IsLittleEndian = magic == 0xa1b2c3d4 || magic == 0xa1b23c4d,
                IsNanoseconds = magic == 0xa1b23c4d || magic == 0x4d3cb2a1
            };

            header.VersionMajor = reader.ReadUInt16();
            header.VersionMinor = reader.ReadUInt16();
            header.ThisZone = reader.ReadInt32();
            header.SigFigs = reader.ReadUInt32();
            header.SnapLen = reader.ReadUInt32();
            header.Network = reader.ReadUInt32();

            return header;
        }

        private PcapPacket? ReadPacket(BinaryReader reader, PcapHeader header, int index)
        {
            if (reader.BaseStream.Position + 16 > reader.BaseStream.Length)
                return null;

            var tsSec = reader.ReadUInt32();
            var tsUsec = reader.ReadUInt32();
            var capLen = reader.ReadUInt32();
            var origLen = reader.ReadUInt32();

            if (capLen > header.SnapLen || capLen > 65535)
                return null;

            var timestamp = DateTimeOffset.FromUnixTimeSeconds(tsSec).DateTime;
            if (header.IsNanoseconds)
                timestamp = timestamp.AddTicks(tsUsec / 100);
            else
                timestamp = timestamp.AddTicks(tsUsec * 10);

            var data = reader.ReadBytes((int)capLen);

            return new PcapPacket
            {
                Index = index,
                Timestamp = timestamp,
                CapturedLength = capLen,
                OriginalLength = origLen,
                Data = data
            };
        }

        private void ParseEthernet(PcapPacket packet)
        {
            if (packet.Data.Length < 14) return;

            // Ethernet header: Dest MAC (6) + Source MAC (6) + EtherType (2)
            packet.DestMac = FormatMac(packet.Data, 0);
            packet.SourceMac = FormatMac(packet.Data, 6);
            packet.EtherType = BinaryPrimitives.ReadUInt16BigEndian(packet.Data.AsSpan(12, 2));

            if (packet.EtherType == ETHERTYPE_IPV4 && packet.Data.Length >= 34)
            {
                ParseIPv4(packet, 14);
            }
            else if (packet.EtherType == ETHERTYPE_IPV6 && packet.Data.Length >= 54)
            {
                ParseIPv6(packet, 14);
            }
        }

        private void ParseIPv4(PcapPacket packet, int offset)
        {
            var ihl = (packet.Data[offset] & 0x0F) * 4;
            if (packet.Data.Length < offset + ihl) return;

            packet.Protocol = packet.Data[offset + 9];
            packet.SourceIP = new IPAddress(packet.Data.AsSpan(offset + 12, 4));
            packet.DestIP = new IPAddress(packet.Data.AsSpan(offset + 16, 4));

            var transportOffset = offset + ihl;

            if (packet.Protocol == PROTO_TCP && packet.Data.Length >= transportOffset + 20)
            {
                ParseTcp(packet, transportOffset);
            }
            else if (packet.Protocol == PROTO_UDP && packet.Data.Length >= transportOffset + 8)
            {
                ParseUdp(packet, transportOffset);
            }
        }

        private void ParseIPv6(PcapPacket packet, int offset)
        {
            if (packet.Data.Length < offset + 40) return;

            packet.Protocol = packet.Data[offset + 6]; // Next header
            packet.SourceIP = new IPAddress(packet.Data.AsSpan(offset + 8, 16));
            packet.DestIP = new IPAddress(packet.Data.AsSpan(offset + 24, 16));

            var transportOffset = offset + 40;

            if (packet.Protocol == PROTO_TCP && packet.Data.Length >= transportOffset + 20)
            {
                ParseTcp(packet, transportOffset);
            }
            else if (packet.Protocol == PROTO_UDP && packet.Data.Length >= transportOffset + 8)
            {
                ParseUdp(packet, transportOffset);
            }
        }

        private void ParseTcp(PcapPacket packet, int offset)
        {
            packet.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet.Data.AsSpan(offset, 2));
            packet.DestPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Data.AsSpan(offset + 2, 2));

            var flags = packet.Data[offset + 13];
            if ((flags & 0x02) != 0) packet.Flags.Add("SYN");
            if ((flags & 0x10) != 0) packet.Flags.Add("ACK");
            if ((flags & 0x01) != 0) packet.Flags.Add("FIN");
            if ((flags & 0x04) != 0) packet.Flags.Add("RST");
            if ((flags & 0x08) != 0) packet.Flags.Add("PSH");

            var dataOffset = (packet.Data[offset + 12] >> 4) * 4;
            if (ExtractPayloads && packet.Data.Length > offset + dataOffset)
            {
                var payloadLen = Math.Min(packet.Data.Length - offset - dataOffset, MaxPayloadBytes);
                if (payloadLen > 0)
                {
                    packet.Payload = Encoding.ASCII.GetString(packet.Data, offset + dataOffset, payloadLen);
                }
            }
        }

        private void ParseUdp(PcapPacket packet, int offset)
        {
            packet.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(packet.Data.AsSpan(offset, 2));
            packet.DestPort = BinaryPrimitives.ReadUInt16BigEndian(packet.Data.AsSpan(offset + 2, 2));

            if (ExtractPayloads && packet.Data.Length > offset + 8)
            {
                var payloadLen = Math.Min(packet.Data.Length - offset - 8, MaxPayloadBytes);
                if (payloadLen > 0)
                {
                    packet.Payload = Encoding.ASCII.GetString(packet.Data, offset + 8, payloadLen);
                }
            }
        }

        #endregion

        #region Extraction

        private void ExtractDnsRecords(PcapPacket packet, PcapAnalysisResult result)
        {
            // Basic DNS parsing - payload starts after UDP header
            if (string.IsNullOrEmpty(packet.Payload) || packet.Payload.Length < 12) return;

            try
            {
                // This is a simplified DNS parser - full implementation would parse binary DNS
                // Look for domain patterns in payload
                var payload = packet.Payload;
                var domains = ExtractDomainsFromPayload(payload);

                foreach (var domain in domains)
                {
                    result.UniqueDomains.Add(domain);
                    result.DnsRecords.Add(new DnsRecord
                    {
                        QueryName = domain,
                        RecordType = packet.SourcePort == 53 ? "Response" : "Query",
                        Timestamp = packet.Timestamp,
                        SourceIP = packet.SourceIP,
                        DestIP = packet.DestIP
                    });
                    AddOrUpdateArtifact(result.Artifacts, "Domain", domain, "DNS", packet);
                }
            }
            catch { /* Skip malformed DNS */ }
        }

        private void ExtractHttpRequest(PcapPacket packet, PcapAnalysisResult result)
        {
            if (string.IsNullOrEmpty(packet.Payload)) return;

            var payload = packet.Payload;
            if (!payload.StartsWith("GET ") && !payload.StartsWith("POST ") && 
                !payload.StartsWith("PUT ") && !payload.StartsWith("HEAD ") &&
                !payload.StartsWith("DELETE ")) return;

            try
            {
                var lines = payload.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var firstLine = lines[0].Split(' ');
                if (firstLine.Length < 2) return;

                var request = new HttpRequest
                {
                    Method = firstLine[0],
                    Path = firstLine[1],
                    Timestamp = packet.Timestamp,
                    SourceIP = packet.SourceIP,
                    DestIP = packet.DestIP,
                    PacketIndex = packet.Index
                };

                foreach (var line in lines.Skip(1))
                {
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        var key = line.Substring(0, colonIdx).Trim();
                        var value = line.Substring(colonIdx + 1).Trim();
                        request.Headers[key] = value;

                        if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Host = value;
                            result.UniqueDomains.Add(value);
                        }
                        else if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                        {
                            request.UserAgent = value;
                        }
                    }
                }

                result.HttpRequests.Add(request);

                var url = $"http://{request.Host}{request.Path}";
                result.UniqueURLs.Add(url);
                AddOrUpdateArtifact(result.Artifacts, "URL", url, $"{request.Method} request", packet);
            }
            catch { /* Skip malformed HTTP */ }
        }

        private void ExtractTlsInfo(PcapPacket packet, PcapAnalysisResult result)
        {
            // Look for TLS Client Hello with SNI extension
            // This is a simplified extraction
            if (packet.Data.Length < 50) return;

            // TLS handshake starts with 0x16 (handshake) 0x03 0x01-0x03 (version)
            var payloadStart = FindTlsStart(packet.Data);
            if (payloadStart < 0) return;

            try
            {
                var sni = ExtractSNI(packet.Data, payloadStart);
                if (!string.IsNullOrEmpty(sni))
                {
                    result.UniqueDomains.Add(sni);
                    AddOrUpdateArtifact(result.Artifacts, "TLS-SNI", sni, "TLS Server Name Indication", packet);
                }
            }
            catch { /* Skip if parsing fails */ }
        }

        private int FindTlsStart(byte[] data)
        {
            for (int i = 0; i < data.Length - 5; i++)
            {
                if (data[i] == 0x16 && data[i + 1] == 0x03 && data[i + 2] >= 0x01 && data[i + 2] <= 0x03)
                {
                    return i;
                }
            }
            return -1;
        }

        private string? ExtractSNI(byte[] data, int offset)
        {
            // Very simplified SNI extraction - in reality this is more complex
            // Look for the SNI extension (type 0x0000) in TLS handshake
            for (int i = offset; i < data.Length - 10; i++)
            {
                // Look for extension type 0x0000 (SNI)
                if (data[i] == 0x00 && data[i + 1] == 0x00)
                {
                    // Skip to hostname
                    var hostnameOffset = i + 9;
                    if (hostnameOffset + 2 < data.Length)
                    {
                        var hostnameLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(hostnameOffset, 2));
                        if (hostnameLen > 0 && hostnameLen < 256 && hostnameOffset + 2 + hostnameLen <= data.Length)
                        {
                            var hostname = Encoding.ASCII.GetString(data, hostnameOffset + 2, hostnameLen);
                            if (IsValidDomain(hostname))
                            {
                                return hostname;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private IEnumerable<string> ExtractDomainsFromPayload(string payload)
        {
            // Simple domain extraction from DNS payload
            var domains = new List<string>();
            var sb = new StringBuilder();
            
            foreach (var c in payload)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-')
                {
                    sb.Append(c);
                }
                else if (sb.Length > 4)
                {
                    var candidate = sb.ToString().Trim('.');
                    if (IsValidDomain(candidate))
                    {
                        domains.Add(candidate);
                    }
                    sb.Clear();
                }
                else
                {
                    sb.Clear();
                }
            }

            if (sb.Length > 4)
            {
                var candidate = sb.ToString().Trim('.');
                if (IsValidDomain(candidate))
                {
                    domains.Add(candidate);
                }
            }

            return domains.Distinct();
        }

        private bool IsValidDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain) || domain.Length < 4) return false;
            if (!domain.Contains('.')) return false;
            if (domain.StartsWith(".") || domain.EndsWith(".")) return false;
            
            var parts = domain.Split('.');
            if (parts.Length < 2) return false;
            if (parts.Last().Length < 2 || parts.Last().Length > 10) return false;
            
            return parts.All(p => p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c == '-'));
        }

        #endregion

        #region Helpers

        private static string FormatMac(byte[] data, int offset)
        {
            return string.Join(":", data.Skip(offset).Take(6).Select(b => b.ToString("X2")));
        }

        private static string GetProtocolName(byte protocol)
        {
            return protocol switch
            {
                PROTO_ICMP => "ICMP",
                PROTO_TCP => "TCP",
                PROTO_UDP => "UDP",
                _ => $"Proto-{protocol}"
            };
        }

        private void AddOrUpdateArtifact(List<NetworkArtifact> artifacts, string type, string value, string context, PcapPacket packet)
        {
            var existing = artifacts.FirstOrDefault(a => a.Type == type && a.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Count++;
                existing.LastSeen = packet.Timestamp;
                existing.PacketIndices.Add(packet.Index);
            }
            else
            {
                artifacts.Add(new NetworkArtifact
                {
                    Type = type,
                    Value = value,
                    Context = context,
                    FirstSeen = packet.Timestamp,
                    LastSeen = packet.Timestamp,
                    PacketIndices = { packet.Index }
                });
            }
        }

        private void UpdateConnection(Dictionary<string, NetworkConnection> connections, PcapPacket packet)
        {
            if (packet.SourceIP == null || packet.DestIP == null) return;

            var key = $"{packet.SourceIP}:{packet.SourcePort}-{packet.DestIP}:{packet.DestPort}";
            var reverseKey = $"{packet.DestIP}:{packet.DestPort}-{packet.SourceIP}:{packet.SourcePort}";

            if (connections.TryGetValue(key, out var conn))
            {
                conn.PacketCount++;
                conn.BytesSent += packet.OriginalLength;
                conn.LastSeen = packet.Timestamp;
                foreach (var flag in packet.Flags)
                {
                    if (!conn.Flags.Contains(flag)) conn.Flags.Add(flag);
                }
            }
            else if (connections.TryGetValue(reverseKey, out conn))
            {
                conn.PacketCount++;
                conn.BytesReceived += packet.OriginalLength;
                conn.LastSeen = packet.Timestamp;
            }
            else
            {
                connections[key] = new NetworkConnection
                {
                    SourceIP = packet.SourceIP,
                    SourcePort = packet.SourcePort,
                    DestIP = packet.DestIP,
                    DestPort = packet.DestPort,
                    Protocol = GetProtocolName(packet.Protocol),
                    PacketCount = 1,
                    BytesSent = packet.OriginalLength,
                    FirstSeen = packet.Timestamp,
                    LastSeen = packet.Timestamp,
                    Flags = { string.Join(",", packet.Flags) }
                };
            }
        }

        #endregion

        #region Export

        /// <summary>
        /// Exports analysis results to JSON.
        /// </summary>
        public string ExportToJson(PcapAnalysisResult result)
        {
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Exports IOCs to a simple text format for use with other tools.
        /// </summary>
        public string ExportIOCs(PcapAnalysisResult result)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("# IP Addresses");
            foreach (var ip in result.UniqueIPs.OrderBy(i => i))
            {
                sb.AppendLine(ip);
            }

            sb.AppendLine();
            sb.AppendLine("# Domains");
            foreach (var domain in result.UniqueDomains.OrderBy(d => d))
            {
                sb.AppendLine(domain);
            }

            sb.AppendLine();
            sb.AppendLine("# URLs");
            foreach (var url in result.UniqueURLs.OrderBy(u => u))
            {
                sb.AppendLine(url);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Exports connections to CSV.
        /// </summary>
        public string ExportConnectionsCsv(PcapAnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SourceIP,SourcePort,DestIP,DestPort,Protocol,Packets,BytesSent,BytesReceived,FirstSeen,LastSeen,Flags");

            foreach (var conn in result.Connections.OrderByDescending(c => c.PacketCount))
            {
                sb.AppendLine($"{conn.SourceIP},{conn.SourcePort},{conn.DestIP},{conn.DestPort},{conn.Protocol},{conn.PacketCount},{conn.BytesSent},{conn.BytesReceived},{conn.FirstSeen:yyyy-MM-dd HH:mm:ss},{conn.LastSeen:yyyy-MM-dd HH:mm:ss},\"{string.Join(";", conn.Flags)}\"");
            }

            return sb.ToString();
        }

        #endregion
    }
}
