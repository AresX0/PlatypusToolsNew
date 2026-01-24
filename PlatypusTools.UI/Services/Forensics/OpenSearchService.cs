using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for OpenSearch/Elasticsearch integration.
    /// Manages ingest pipelines, index templates, and data ingestion for DFIR analysis.
    /// </summary>
    public class OpenSearchService : ForensicOperationBase
    {
        private readonly HttpClient _httpClient;

        public override string OperationName => "OpenSearch Integration";

        #region Configuration

        /// <summary>
        /// Gets or sets the OpenSearch server URL.
        /// </summary>
        public string ServerUrl { get; set; } = "http://localhost:9200";

        /// <summary>
        /// Gets or sets the default index name prefix.
        /// </summary>
        public string IndexPrefix { get; set; } = "dfir";

        /// <summary>
        /// Gets or sets the default ingest pipeline name.
        /// </summary>
        public string PipelineName { get; set; } = "dfir-default";

        /// <summary>
        /// Gets or sets whether the connection is established.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Gets or sets authentication username (optional).
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets authentication password (optional).
        /// </summary>
        public string Password { get; set; } = string.Empty;

        #endregion

        public OpenSearchService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public OpenSearchService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        #region Connection

        /// <summary>
        /// Tests connection to OpenSearch server.
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                ReportProgress("Testing OpenSearch connection...");

                ConfigureAuthentication();
                var response = await _httpClient.GetAsync(ServerUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    IsConnected = true;
                    ReportProgress($"✓ OpenSearch connected: {ServerUrl}");
                    ReportProgress(content);
                    return true;
                }
                else
                {
                    IsConnected = false;
                    ReportProgress($"✗ OpenSearch connection failed: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ReportProgress($"✗ Connection failed: {ex.Message}");
                return false;
            }
        }

        private void ConfigureAuthentication()
        {
            if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
            {
                var byteArray = Encoding.ASCII.GetBytes($"{Username}:{Password}");
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            }
        }

        #endregion

        #region Ingest Pipelines

        /// <summary>
        /// Creates all DFIR ingest pipelines.
        /// </summary>
        public async Task<bool> CreatePipelinesAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress("Creating OpenSearch pipelines...");

            var pipelines = new[]
            {
                ("dfir-volatility", GetVolatilityPipeline()),
                ("dfir-evtx", GetEvtxPipeline()),
                ("dfir-amcache", GetAmcachePipeline()),
                ("dfir-mftecmd", GetMftPipeline()),
                ("osint-docmeta", GetDocMetaPipeline())
            };

            var success = true;

            foreach (var (name, body) in pipelines)
            {
                if (await CreatePipelineAsync(name, body, cancellationToken))
                {
                    ReportProgress($"✓ Created pipeline: {name}");
                }
                else
                {
                    ReportProgress($"✗ Failed to create: {name}");
                    success = false;
                }
            }

            return success;
        }

        private async Task<bool> CreatePipelineAsync(string name, string body, CancellationToken cancellationToken)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"{ServerUrl}/_ingest/pipeline/{name}", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                ReportProgress($"✗ Error creating {name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Index Templates

        /// <summary>
        /// Creates all DFIR index templates.
        /// </summary>
        public async Task<bool> CreateIndexTemplatesAsync(CancellationToken cancellationToken = default)
        {
            ReportProgress("Creating OpenSearch index templates...");

            var templates = new[]
            {
                ("dfir-volatility", GetVolatilityIndexTemplate()),
                ("dfir-evtx", GetEvtxIndexTemplate()),
                ("dfir-plaso", GetPlasoIndexTemplate())
            };

            var success = true;

            foreach (var (name, body) in templates)
            {
                if (await CreateIndexTemplateAsync(name, body, cancellationToken))
                {
                    ReportProgress($"✓ Created index template: {name}");
                }
                else
                {
                    ReportProgress($"✗ Failed to create template: {name}");
                    success = false;
                }
            }

            return success;
        }

        private async Task<bool> CreateIndexTemplateAsync(string name, string body, CancellationToken cancellationToken)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"{ServerUrl}/_index_template/{name}", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                ReportProgress($"✗ Error creating template {name}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Data Ingestion

        /// <summary>
        /// Ingests JSON artifacts to OpenSearch.
        /// </summary>
        public async Task<IngestResult> IngestArtifactsAsync(
            IEnumerable<string> jsonFilePaths,
            string indexName,
            CancellationToken cancellationToken = default)
        {
            var result = new IngestResult();
            var timestamp = DateTime.Now.ToString("yyyy.MM");
            var targetIndex = $"{indexName}-{timestamp}";

            ReportProgress($"Ingesting to {targetIndex}...");

            foreach (var filePath in jsonFilePaths.Where(f => f.EndsWith(".json") && File.Exists(f)))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var bulkRequest = BuildBulkRequest(json, targetIndex);

                    if (bulkRequest.Length > 0)
                    {
                        using var content = new StringContent(bulkRequest, Encoding.UTF8, "application/x-ndjson");
                        var response = await _httpClient.PostAsync(
                            $"{ServerUrl}/{targetIndex}/_bulk?pipeline={PipelineName}",
                            content,
                            cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            ReportProgress($"✓ Ingested: {Path.GetFileName(filePath)}");
                            result.SuccessCount++;
                        }
                        else
                        {
                            ReportProgress($"✗ Failed to ingest {Path.GetFileName(filePath)}: {response.StatusCode}");
                            result.FailedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress($"✗ Error ingesting {Path.GetFileName(filePath)}: {ex.Message}");
                    result.Errors.Add($"{filePath}: {ex.Message}");
                    result.FailedCount++;
                }
            }

            result.Success = result.SuccessCount > 0;
            result.Message = $"Ingested {result.SuccessCount} artifacts to OpenSearch";

            return result;
        }

        private string BuildBulkRequest(string json, string targetIndex)
        {
            var sb = new StringBuilder();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        sb.AppendLine($"{{\"index\":{{\"_index\":\"{targetIndex}\"}}}}");
                        sb.AppendLine(element.GetRawText());
                    }
                }
            }
            catch { }

            return sb.ToString();
        }

        #endregion

        #region Pipeline Definitions

        private string GetVolatilityPipeline() => @"{
  ""description"": ""Pipeline for Volatility 3 memory analysis output"",
  ""processors"": [
    {
      ""date"": {
        ""field"": ""timestamp"",
        ""formats"": [""ISO8601"", ""yyyy-MM-dd HH:mm:ss""],
        ""target_field"": ""@timestamp"",
        ""ignore_failure"": true
      }
    },
    {
      ""set"": {
        ""field"": ""event.category"",
        ""value"": ""process""
      }
    }
  ]
}";

        private string GetEvtxPipeline() => @"{
  ""description"": ""Pipeline for Windows Event Log (EVTX) data"",
  ""processors"": [
    {
      ""date"": {
        ""field"": ""TimeCreated"",
        ""formats"": [""ISO8601""],
        ""target_field"": ""@timestamp"",
        ""ignore_failure"": true
      }
    },
    {
      ""rename"": {
        ""field"": ""EventID"",
        ""target_field"": ""event.code"",
        ""ignore_failure"": true
      }
    }
  ]
}";

        private string GetAmcachePipeline() => @"{
  ""description"": ""Pipeline for Amcache artifact data"",
  ""processors"": [
    {
      ""date"": {
        ""field"": ""LastModified"",
        ""formats"": [""ISO8601""],
        ""target_field"": ""@timestamp"",
        ""ignore_failure"": true
      }
    }
  ]
}";

        private string GetMftPipeline() => @"{
  ""description"": ""Pipeline for MFT parser output"",
  ""processors"": [
    {
      ""date"": {
        ""field"": ""Created"",
        ""formats"": [""ISO8601""],
        ""target_field"": ""file.created"",
        ""ignore_failure"": true
      }
    }
  ]
}";

        private string GetDocMetaPipeline() => @"{
  ""description"": ""Pipeline for document metadata extraction"",
  ""processors"": [
    {
      ""date"": {
        ""field"": ""CreateDate"",
        ""formats"": [""yyyy:MM:dd HH:mm:ss"", ""ISO8601""],
        ""target_field"": ""@timestamp"",
        ""ignore_failure"": true
      }
    }
  ]
}";

        #endregion

        #region Index Template Definitions

        private string GetVolatilityIndexTemplate() => @"{
  ""index_patterns"": [""dfir-volatility-*""],
  ""template"": {
    ""settings"": {
      ""number_of_shards"": 1,
      ""number_of_replicas"": 0
    },
    ""mappings"": {
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""plugin"": { ""type"": ""keyword"" },
        ""PID"": { ""type"": ""integer"" },
        ""PPID"": { ""type"": ""integer"" },
        ""ImageFileName"": { ""type"": ""keyword"" },
        ""CommandLine"": { ""type"": ""text"" }
      }
    }
  }
}";

        private string GetEvtxIndexTemplate() => @"{
  ""index_patterns"": [""dfir-evtx-*""],
  ""template"": {
    ""settings"": {
      ""number_of_shards"": 1,
      ""number_of_replicas"": 0
    },
    ""mappings"": {
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""event.code"": { ""type"": ""keyword"" },
        ""event.provider"": { ""type"": ""keyword"" },
        ""message"": { ""type"": ""text"" }
      }
    }
  }
}";

        private string GetPlasoIndexTemplate() => @"{
  ""index_patterns"": [""dfir-plaso-*""],
  ""template"": {
    ""settings"": {
      ""number_of_shards"": 1,
      ""number_of_replicas"": 0
    },
    ""mappings"": {
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""datetime"": { ""type"": ""date"" },
        ""source_short"": { ""type"": ""keyword"" },
        ""message"": { ""type"": ""text"" }
      }
    }
  }
}";

        #endregion

        #region Cleanup

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        #endregion
    }

    #region Result Models

    public class IngestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; } = new();
    }

    #endregion
}
