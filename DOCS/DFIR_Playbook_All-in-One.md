
# DFIR Playbook (All‑in‑One)

**Date:** 2026-01-22  
**Author:** Joseph Say (prepared with assistance from M365 Copilot)

This Markdown consolidates the end‑to‑end workflow for **memory & Windows forensic artifact analysis** using open‑source tools, plus **Kusto** sample queries, **OpenSearch** ingest pipelines, and **C#/.NET 10** helpers for automation.

> **Stack overview**: Acquisition (WinPmem/DumpIt, KAPE), Parsing & Analysis (Volatility 3, EZ Tools, Plaso, Velociraptor), Storage/Query (OpenSearch + Dashboards DQL/KQL‑style **and/or** Microsoft **Kusto**), OSINT/metadata enrichment (FOCA‑type tools).

---

## 1) Requirements for Memory & Forensic Data Analysis

### A. Acquisition
- **Memory acquisition**: WinPmem (open‑source, RAW/crash dump) and DumpIt (free) for Windows. Preserve hashes and chain‑of‑custody.
- **Disk/triage acquisition**: KAPE Targets/Modules (with EZ Tools), Autopsy/SleuthKit. 

### B. Parsing & Analysis Frameworks
- **Volatility 3** for memory analysis; export **JSON/CSV** (e.g., `windows.pslist`, `windows.netscan`, `windows.malfind`, `dlllist`, `handles`).
- **EZ Tools** via **KAPE** for Windows artifacts (EvtxECmd, MFTECmd, AmcacheParser, JLECmd, PECmd, SrumECmd).
- **Plaso (log2timeline/psort)** to build super‑timelines; supports **OpenSearch** output and JSON/JSONL.
- **Velociraptor** (optional, fleet triage) exporting **JSON/JSONL**.

### C. Target Artifact Coverage (Windows)
- **Execution & presence**: Prefetch, **Amcache**, Shimcache, SRUM, Jump Lists, LNK. (Amcache evidences program presence; it does not guarantee execution timestamp.)
- **File system**: **$MFT**, **$UsnJrnl**, Recycle Bin, ShellBags.
- **Event logs**: Security, Sysmon, PowerShell/Operational, WMI, TaskScheduler.
- **Network**: Volatility `windows.netscan`, Windows logs.

### D. Data Lake & Querying
- **OpenSearch + Dashboards** (open‑source, DQL/KQL‑style search) or **Kusto** (ADX/Log Analytics) for query & visualization.
- Ingest via **Plaso → OpenSearch**, or bulk JSON/JSONL from **Volatility/EZ Tools/Velociraptor**.

---

## 2) FOCA‑type Tools (Metadata & Hidden Information)

**Purpose:** harvest document and image metadata (authors, usernames, printer paths, software versions), discover hidden objects in PDFs/Office, and enrich endpoint findings.

**Key tools**
- **FOCA** (Fingerprinting Organizations with Collected Archives) – enumerates public docs (PDF/DOCX/PPTX), extracts metadata; useful for OSINT and infrastructure discovery.
- **ExifTool** – universal metadata extractor (EXIF/XMP/IPTC/PDF/Office); easy to batch with JSON output.
- **bulk_extractor** – scans raw data for features (emails, URLs, CCNs, embedded data); great for large triage.
- **oletools** (e.g., `olevba`, `mraptor`) – VBA macro detection & suspicious keyword flags.
- **PDF analyzers** (e.g., `pdf-parser.py`, `peepdf`) – inspect embedded objects/JS/actions.

**Workflow**
1. Discover public documents (FOCA or search engine dorks) and collect samples in a controlled workspace.
2. Run **ExifTool** in batch to JSON; normalize fields (`doc.author`, `doc.company`, `app.name`, `app.version`, `os.usernames[]`).
3. Correlate discovered **usernames/paths** with endpoint artifacts (Amcache, Prefetch, EVTX, MFT) to validate presence or lateral movement.
4. Ingest the metadata JSON into OpenSearch (index pattern `osint-docmeta-*`) and pivot on **authors**, **internal hostnames**, **printer/server paths**.

---

## 3) Reference Architecture (OpenSearch + Dashboards)

Spin up a single‑node lab via Docker Compose:

```yaml
version: '3.8'
services:
  opensearch:
    image: opensearchproject/opensearch:2.13.0
    environment:
      - discovery.type=single-node
      - plugins.security.disabled=true
      - OPENSEARCH_JAVA_OPTS=-Xms2g -Xmx2g
    ulimits: { memlock: { soft: -1, hard: -1 }, nofile: { soft: 65536, hard: 65536 } }
    ports: ['9200:9200','9600:9600']
  dashboards:
    image: opensearchproject/opensearch-dashboards:2.13.0
    environment:
      - OPENSEARCH_HOSTS='["http://opensearch:9200"]'
      - DISABLE_SECURITY_DASHBOARDS_PLUGIN=true
    ports: ['5601:5601']
    depends_on: [opensearch]
```

---

## 4) Ingestion Paths

### Volatility 3 → JSON → OpenSearch
```bash
python3 vol.py -f mem.raw -r json windows.pslist > vol_pslist.json
python3 vol.py -f mem.raw -r json windows.netscan > vol_netscan.json
```
Use the `dfir-volatility` pipeline (below) and bulk‑load into indices like `dfir-vol-2026.01`.

### KAPE / EZ Tools CSV & JSON → OpenSearch
Parse EVTX/MFT/Amcache/Prefetch/SRUM and map to common fields: `@timestamp`, `host.name`, `user.name`, `event.provider`, `event.code`, `file.path`, `process.executable`, `process.hash.sha1`, `registry.path`, network fields.

### Plaso (log2timeline/psort) → OpenSearch
```bash
log2timeline.py --storage-file case.plaso /evidence
psort.py -o opensearch --server http://localhost:9200 --index_name dfir-plaso-2026.01 case.plaso
```

### Velociraptor → JSONL → OpenSearch (optional)
Collect artifacts (e.g., `Windows.KapeFiles.Targets`, `Windows.System.TaskScheduler`, `Windows.Memory.Acquisition`) and export **JSON/JSONL** for bulk ingest.

---

## 5) Kusto (ADX / Log Analytics) — Sample Queries

> Adjust table/column names to your schema. These examples target common table layouts.

### 5.1 Process Creation — Windows Security 4688 (SecurityEvent)
```kusto
// Suspicious process creation via Windows Security (Event ID 4688)
SecurityEvent
| where EventID == 4688
| extend Account = tostring(Account),
         CommandLine = tostring(CommandLine),
         NewProcessName = tostring(NewProcessName),
         ParentProcessName = tostring(ParentProcessName)
| where CommandLine has_any ("-enc", "-EncodedCommand", "rundll32", "regsvr32", "powershell", "cmd /c")
| project TimeGenerated, Computer, Account, NewProcessName, CommandLine, ParentProcessName
| order by TimeGenerated desc
```

### 5.2 Sysmon Process Creation — Event ID 1 (Event table)
```kusto
// Sysmon EID 1
Event
| where EventID == 1 and Source == "Microsoft-Windows-Sysmon"
| extend Image = tostring(EventData.Image),
         CommandLine = tostring(EventData.CommandLine),
         ParentImage = tostring(EventData.ParentImage),
         User = tostring(UserName)
| where CommandLine has_any ("-enc", "-EncodedCommand", "rundll32", "regsvr32", "powershell")
| project TimeGenerated, Computer, User, Image, CommandLine, ParentImage
| order by TimeGenerated desc
```

### 5.3 PowerShell Operational (Event table)
```kusto
Event
| where EventLog == "Microsoft-Windows-PowerShell/Operational"
| extend PsMessage = tostring(RenderedDescription)
| where PsMessage has_any ("DownloadString", "FromBase64String", "IEX", "New-Object Net.WebClient")
| project TimeGenerated, Computer, EventID, PsMessage
| order by TimeGenerated desc
```

### 5.4 Correlate Amcache presence with Process Creation (custom tables)
Assume `Amcache_CL(TimeGenerated, FilePath_s, Sha1_s, Host_s)` and `ProcCreate_CL(TimeGenerated, Host_s, Image_s, CommandLine_s, Sha1_s)`:
```kusto
let tStart = ago(30d);
Amcache_CL
| where TimeGenerated >= tStart
| project Host_s, FilePath_s, Sha1_s
| join kind=innerunique (
    ProcCreate_CL
    | where TimeGenerated >= tStart
    | project TimeGenerated, Host_s, Image_s, CommandLine_s, Sha1_s
) on Sha1_s
| order by TimeGenerated desc
```

### 5.5 Volatility netscan to Process Creation (custom tables)
Assume `VolNetscan_CL(TimeGenerated, Host_s, LocalIP_s, LocalPort_d, RemoteIP_s, RemotePort_d, Pid_d)` and `ProcCreate_CL` above:
```kusto
VolNetscan_CL
| where TimeGenerated >= ago(7d)
| join kind=leftouter (
    ProcCreate_CL
    | summarize arg_max(TimeGenerated, *) by Host_s, Pid_d
) on Host_s, $left.Pid_d == $right.Pid_d
| project TimeGenerated, Host_s, LocalIP_s, LocalPort_d, RemoteIP_s, RemotePort_d, Pid_d, Image_s, CommandLine_s
| order by TimeGenerated desc
```

---

## 6) OpenSearch Ingest Pipelines & Index Templates

> Create via **Dev Tools** or REST. Use with Bulk API: `POST /<index>/_bulk?pipeline=<name>`.

### 6.1 Volatility pipeline + index template
```json
POST _ingest/pipeline/dfir-volatility
{
  "description": "Normalize Volatility outputs",
  "processors": [
    { "set": { "field": "artifact_category", "value": "memory" } },
    { "rename": { "field": "PID", "target_field": "process.pid", "ignore_failure": true } },
    { "rename": { "field": "PPID", "target_field": "process.parent.pid", "ignore_failure": true } },
    { "rename": { "field": "LocalIP", "target_field": "net.local.ip", "ignore_failure": true } },
    { "rename": { "field": "LocalPort", "target_field": "net.local.port", "ignore_failure": true } },
    { "rename": { "field": "RemoteIP", "target_field": "net.remote.ip", "ignore_failure": true } },
    { "rename": { "field": "RemotePort", "target_field": "net.remote.port", "ignore_failure": true } },
    { "date": { "field": "Timestamp", "target_field": "@timestamp", "formats": ["ISO8601","yyyy-MM-dd HH:mm:ss","epoch_millis"], "ignore_failure": true } }
  ]
}

PUT _index_template/dfir-volatility
{
  "index_patterns": ["dfir-vol-*"]
  ,"template": {
    "settings": { "number_of_shards": 1 },
    "mappings": {
      "dynamic": true,
      "properties": {
        "@timestamp": { "type": "date" },
        "artifact_category": { "type": "keyword" },
        "artifact_type": { "type": "keyword" },
        "host.name": { "type": "keyword" },
        "process.pid": { "type": "long" },
        "process.parent.pid": { "type": "long" },
        "net.local.ip": { "type": "ip" },
        "net.remote.ip": { "type": "ip" }
      }
    }
  }
}
```

### 6.2 EVTX (EvtxECmd JSON) pipeline
```json
POST _ingest/pipeline/dfir-evtx
{
  "processors": [
    { "set": { "field": "artifact_category", "value": "windows_eventlog" } },
    { "rename": { "field": "Event.System.EventID", "target_field": "event.code", "ignore_failure": true } },
    { "rename": { "field": "Event.System.Channel", "target_field": "event.provider", "ignore_failure": true } },
    { "rename": { "field": "Event.System.TimeCreated.SystemTime", "target_field": "@timestamp", "ignore_failure": true } },
    { "rename": { "field": "Event.EventData.Image", "target_field": "process.executable", "ignore_failure": true } },
    { "rename": { "field": "Event.EventData.CommandLine", "target_field": "process.command_line", "ignore_failure": true } }
  ]
}
```

### 6.3 Amcache pipeline
```json
POST _ingest/pipeline/dfir-amcache
{
  "processors": [
    { "set": { "field": "artifact_category", "value": "windows_artifact" } },
    { "rename": { "field": "path", "target_field": "file.path", "ignore_failure": true } },
    { "rename": { "field": "sha1", "target_field": "file.hash.sha1", "ignore_failure": true } },
    { "date": { "field": "first_seen", "target_field": "@timestamp", "formats": ["ISO8601","yyyy-MM-dd HH:mm:ss"], "ignore_failure": true } }
  ]
}
```

### 6.4 MFT (MFTECmd) pipeline
```json
POST _ingest/pipeline/dfir-mftecmd
{
  "processors": [
    { "set": { "field": "artifact_category", "value": "filesystem" } },
    { "rename": { "field": "Path", "target_field": "file.path", "ignore_failure": true } },
    { "rename": { "field": "Created0x10", "target_field": "file.created", "ignore_failure": true } },
    { "rename": { "field": "Modified0x10", "target_field": "file.modified", "ignore_failure": true } },
    { "rename": { "field": "Accessed0x10", "target_field": "file.accessed", "ignore_failure": true } },
    { "date": { "field": "Created0x10", "target_field": "@timestamp", "formats": ["ISO8601","yyyy-MM-dd HH:mm:ss"], "ignore_failure": true } }
  ]
}
```

### 6.5 OSINT Doc Metadata (FOCA/ExifTool) pipeline
```json
POST _ingest/pipeline/osint-docmeta
{
  "processors": [
    { "set": { "field": "artifact_category", "value": "doc_metadata" } },
    { "rename": { "field": "Author", "target_field": "doc.author", "ignore_failure": true } },
    { "rename": { "field": "Creator", "target_field": "app.name", "ignore_failure": true } },
    { "rename": { "field": "Producer", "target_field": "app.version", "ignore_failure": true } },
    { "rename": { "field": "Company", "target_field": "doc.company", "ignore_failure": true } },
    { "set": { "field": "@timestamp", "value": "{{_ingest.timestamp}}" } }
  ]
}
```

---

## 7) C#/.NET 10 Helpers (OpenSearch)

Minimal examples using `HttpClient` to create pipelines and bulk‑ingest **JSONL** with a pipeline. Target Framework: **net10.0**.

### 7.1 Create a pipeline programmatically
```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class Pipelines
{
    static async Task Main()
    {
        var http = new HttpClient { BaseAddress = new Uri("http://localhost:9200/") };
        var body = @"{\n  \"processors\": [ { \"set\": { \"field\": \"artifact_category\", \"value\": \"windows_eventlog\" } } ]\n}";
        var req = new HttpRequestMessage(HttpMethod.Put, "_ingest/pipeline/dfir-evtx")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        Console.WriteLine(await res.Content.ReadAsStringAsync());
    }
}
```

### 7.2 Bulk ingest JSONL with a pipeline
```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class BulkIngest
{
    static async Task Main(string[] args)
    {
        var index = args.Length > 0 ? args[0] : "dfir-evtx-2026.01";
        var pipeline = args.Length > 1 ? args[1] : "dfir-evtx";
        var jsonlPath = args.Length > 2 ? args[2] : "evtx.jsonl";

        var http = new HttpClient { BaseAddress = new Uri("http://localhost:9200/") };
        using var fs = File.OpenText(jsonlPath);
        var sb = new StringBuilder();
        string? line;
        while ((line = fs.ReadLine()) != null)
        {
            sb.AppendLine($"{{ \"index\": {{ \"_index\": \"{index}\" }} }}");
            sb.AppendLine(line);
        }
        var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
        var res = await http.PostAsync($"{index}/_bulk?pipeline={pipeline}", content);
        var resp = await res.Content.ReadAsStringAsync();
        Console.WriteLine(resp);
        res.EnsureSuccessStatusCode();
    }
}
```

### 7.3 Minimal `.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

---

## 8) Next Steps
1. Confirm your ADX/Log Analytics table schemas; update Kusto column names accordingly.
2. Create the pipelines and bulk‑load a small dataset (Volatility JSON, EvtxECmd JSON, MFTECmd CSV→JSON).
3. Add OpenSearch index patterns and build Dashboards with DQL/KQL‑style filters.
4. Integrate the C# utilities into your **.NET 10** solution for automated ingest.

---

## 9) References
- **OpenSearch Dashboards Query Language (DQL/KQL‑style)**: https://docs.opensearch.org/latest/dashboards/dql/
- **Volatility 3 documentation & renderers**: https://volatility3.readthedocs.io/
- **Plaso output modules (incl. `opensearch`)**: https://github.com/log2timeline/plaso/blob/main/docs/sources/user/Output-and-formatting.md
- **KAPE Targets/Modules (EZ Tools)**: https://github.com/EricZimmerman/KapeFiles
- **Velociraptor artifacts CLI/collections**: https://docs.velociraptor.app/docs/cli/artifacts/
- **WinPmem (Windows memory imager)**: https://github.com/Velocidex/WinPmem
- **DumpIt (Windows memory dump)**: https://www.magnetforensics.com/resources/magnet-dumpit-for-windows/
- **Amcache overview & caveats**: https://www.thedfirspot.com/post/evidence-of-program-existence-amcache/
- **Shimcache vs Amcache**: https://www.magnetforensics.com/blog/shimcache-vs-amcache-key-windows-forensic-artifacts/
- **mesquidar/ForensicsTools curated list**: https://github.com/mesquidar/ForensicsTools

