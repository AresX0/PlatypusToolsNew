using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Local KQL (Kusto Query Language) engine that stores forensic data in SQLite
/// and translates KQL queries to SQL for execution.
/// </summary>
public class ForensicsKqlEngine : IDisposable
{
    private readonly string _databasePath;
    private SQLiteConnection? _connection;
    private bool _disposed;

    public ForensicsKqlEngine(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "forensics_data.db");
        
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task InitializeAsync()
    {
        if (_connection != null) return;

        var connectionString = $"Data Source={_databasePath};Version=3;";
        _connection = new SQLiteConnection(connectionString);
        await _connection.OpenAsync();

        await CreateTablesAsync();
    }

    private async Task CreateTablesAsync()
    {
        // Create forensic data tables matching common DFIR artifacts
        var createTablesSql = @"
            -- Process list from memory analysis
            CREATE TABLE IF NOT EXISTS Processes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                PID INTEGER,
                PPID INTEGER,
                ProcessName TEXT,
                CommandLine TEXT,
                Username TEXT,
                Computer TEXT,
                MemorySize INTEGER,
                Threads INTEGER,
                Handles INTEGER,
                StartTime DATETIME,
                SessionId INTEGER,
                IsHidden INTEGER DEFAULT 0,
                IsSuspicious INTEGER DEFAULT 0
            );

            -- Network connections from memory/live analysis
            CREATE TABLE IF NOT EXISTS NetworkConnections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                LocalAddress TEXT,
                LocalPort INTEGER,
                RemoteAddress TEXT,
                RemotePort INTEGER,
                Protocol TEXT,
                State TEXT,
                PID INTEGER,
                ProcessName TEXT,
                Computer TEXT,
                BytesSent INTEGER DEFAULT 0,
                BytesReceived INTEGER DEFAULT 0
            );

            -- Windows Event Logs
            CREATE TABLE IF NOT EXISTS SecurityEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME,
                Source TEXT,
                EventID INTEGER,
                Level TEXT,
                Channel TEXT,
                Computer TEXT,
                Account TEXT,
                TargetAccount TEXT,
                IpAddress TEXT,
                LogonType INTEGER,
                Status TEXT,
                Message TEXT,
                RawData TEXT
            );

            -- File system artifacts
            CREATE TABLE IF NOT EXISTS FileEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                ActionType TEXT,
                FilePath TEXT,
                FileName TEXT,
                Extension TEXT,
                FileSize INTEGER,
                SHA256 TEXT,
                MD5 TEXT,
                CreatedTime DATETIME,
                ModifiedTime DATETIME,
                AccessedTime DATETIME,
                IsDeleted INTEGER DEFAULT 0,
                Computer TEXT,
                AccountName TEXT
            );

            -- Registry events
            CREATE TABLE IF NOT EXISTS RegistryEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                ActionType TEXT,
                RegistryKey TEXT,
                RegistryValueName TEXT,
                RegistryValueData TEXT,
                RegistryValueType TEXT,
                Computer TEXT,
                AccountName TEXT,
                PreviousValue TEXT
            );

            -- Prefetch execution evidence
            CREATE TABLE IF NOT EXISTS PrefetchEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                ExecutableName TEXT,
                PrefetchHash TEXT,
                RunCount INTEGER,
                LastRunTime DATETIME,
                PreviousRunTimes TEXT,
                FilesReferenced TEXT,
                DirectoriesReferenced TEXT,
                Computer TEXT
            );

            -- Amcache program evidence
            CREATE TABLE IF NOT EXISTS AmcacheEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                FilePath TEXT,
                FileName TEXT,
                SHA1 TEXT,
                Publisher TEXT,
                Version TEXT,
                BinaryType TEXT,
                InstallDate DATETIME,
                Computer TEXT
            );

            -- SRUM (System Resource Usage Monitor)
            CREATE TABLE IF NOT EXISTS SrumEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                AppId TEXT,
                AppName TEXT,
                UserId TEXT,
                BytesSent INTEGER,
                BytesReceived INTEGER,
                ForegroundCycles INTEGER,
                BackgroundCycles INTEGER,
                Computer TEXT
            );

            -- Timeline events (Plaso/MFT)
            CREATE TABLE IF NOT EXISTS TimelineEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME,
                Source TEXT,
                SourceType TEXT,
                EventType TEXT,
                Description TEXT,
                FilePath TEXT,
                MacTime TEXT,
                Computer TEXT,
                Extra TEXT
            );

            -- Malware analysis results
            CREATE TABLE IF NOT EXISTS MalwareEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                FilePath TEXT,
                FileName TEXT,
                FileType TEXT,
                SHA256 TEXT,
                HasMacros INTEGER DEFAULT 0,
                HasAutoExec INTEGER DEFAULT 0,
                IsSuspicious INTEGER DEFAULT 0,
                SuspiciousIndicators TEXT,
                ExtractedStrings TEXT,
                AnalysisTool TEXT,
                Computer TEXT
            );

            -- Bulk extractor findings
            CREATE TABLE IF NOT EXISTS ExtractedFeatures (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                FeatureType TEXT,
                Value TEXT,
                Context TEXT,
                Offset INTEGER,
                SourceFile TEXT,
                Computer TEXT
            );

            -- Document metadata (OSINT)
            CREATE TABLE IF NOT EXISTS DocumentMetadata (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                FilePath TEXT,
                FileName TEXT,
                FileType TEXT,
                Author TEXT,
                Creator TEXT,
                Company TEXT,
                Title TEXT,
                Subject TEXT,
                Keywords TEXT,
                Software TEXT,
                CreatedDate DATETIME,
                ModifiedDate DATETIME,
                Computer TEXT
            );

            -- Generic forensic artifacts
            CREATE TABLE IF NOT EXISTS ForensicArtifacts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimeGenerated DATETIME DEFAULT CURRENT_TIMESTAMP,
                Source TEXT,
                ArtifactType TEXT,
                Name TEXT,
                Value TEXT,
                Description TEXT,
                Extra TEXT,
                Computer TEXT
            );

            -- Create indexes for common query patterns
            CREATE INDEX IF NOT EXISTS idx_processes_time ON Processes(TimeGenerated);
            CREATE INDEX IF NOT EXISTS idx_processes_name ON Processes(ProcessName);
            CREATE INDEX IF NOT EXISTS idx_network_remote ON NetworkConnections(RemoteAddress);
            CREATE INDEX IF NOT EXISTS idx_security_eventid ON SecurityEvents(EventID);
            CREATE INDEX IF NOT EXISTS idx_security_time ON SecurityEvents(TimeGenerated);
            CREATE INDEX IF NOT EXISTS idx_file_path ON FileEvents(FilePath);
            CREATE INDEX IF NOT EXISTS idx_registry_key ON RegistryEvents(RegistryKey);
            CREATE INDEX IF NOT EXISTS idx_timeline_time ON TimelineEvents(TimeGenerated);
        ";

        using var cmd = new SQLiteCommand(createTablesSql, _connection);
        await cmd.ExecuteNonQueryAsync();
    }

    #region KQL to SQL Translation

    /// <summary>
    /// Execute a KQL query and return results as a DataTable.
    /// </summary>
    public async Task<DataTable> ExecuteKqlAsync(string kqlQuery)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");

        var sql = TranslateKqlToSql(kqlQuery);
        
        using var cmd = new SQLiteCommand(sql, _connection);
        using var adapter = new SQLiteDataAdapter(cmd);
        var result = new DataTable();
        adapter.Fill(result);
        
        return result;
    }

    /// <summary>
    /// Translate KQL query to SQL.
    /// </summary>
    public string TranslateKqlToSql(string kqlQuery)
    {
        var lines = kqlQuery.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("//"))
            .ToList();

        if (lines.Count == 0)
            throw new ArgumentException("Empty KQL query");

        var tableName = "";
        var whereConditions = new List<string>();
        var projectColumns = new List<string>();
        var extendColumns = new List<string>();
        var summarizeAgg = "";
        var summarizeBy = "";
        var orderBy = "";
        var limit = "";
        var distinct = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart('|').Trim();

            // Table name (first line without pipe)
            if (!line.StartsWith("|") && string.IsNullOrEmpty(tableName))
            {
                tableName = ExtractTableName(trimmed);
                continue;
            }

            // where clause
            if (trimmed.StartsWith("where ", StringComparison.OrdinalIgnoreCase))
            {
                var condition = TranslateWhereClause(trimmed.Substring(6).Trim());
                whereConditions.Add(condition);
            }
            // project clause
            else if (trimmed.StartsWith("project ", StringComparison.OrdinalIgnoreCase))
            {
                projectColumns.AddRange(ParseProjectColumns(trimmed.Substring(8).Trim()));
            }
            // extend clause
            else if (trimmed.StartsWith("extend ", StringComparison.OrdinalIgnoreCase))
            {
                extendColumns.AddRange(ParseExtendColumns(trimmed.Substring(7).Trim()));
            }
            // summarize clause
            else if (trimmed.StartsWith("summarize ", StringComparison.OrdinalIgnoreCase))
            {
                (summarizeAgg, summarizeBy) = ParseSummarize(trimmed.Substring(10).Trim());
            }
            // sort/order by
            else if (trimmed.StartsWith("sort by ", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("order by ", StringComparison.OrdinalIgnoreCase))
            {
                var startIdx = trimmed.StartsWith("sort") ? 8 : 9;
                orderBy = TranslateOrderBy(trimmed.Substring(startIdx).Trim());
            }
            // top N
            else if (trimmed.StartsWith("top ", StringComparison.OrdinalIgnoreCase))
            {
                (limit, orderBy) = ParseTop(trimmed.Substring(4).Trim());
            }
            // take/limit
            else if (trimmed.StartsWith("take ", StringComparison.OrdinalIgnoreCase))
            {
                limit = trimmed.Substring(5).Trim();
            }
            else if (trimmed.StartsWith("limit ", StringComparison.OrdinalIgnoreCase))
            {
                limit = trimmed.Substring(6).Trim();
            }
            // distinct
            else if (trimmed.StartsWith("distinct", StringComparison.OrdinalIgnoreCase))
            {
                distinct = true;
                if (trimmed.Length > 8)
                {
                    projectColumns.AddRange(ParseProjectColumns(trimmed.Substring(8).Trim()));
                }
            }
            // count
            else if (trimmed.Equals("count", StringComparison.OrdinalIgnoreCase))
            {
                summarizeAgg = "COUNT(*) AS Count";
            }
        }

        // Build SQL query
        var sql = new StringBuilder();
        
        // SELECT clause
        sql.Append("SELECT ");
        if (distinct) sql.Append("DISTINCT ");
        
        if (!string.IsNullOrEmpty(summarizeAgg))
        {
            sql.Append(summarizeAgg);
            if (!string.IsNullOrEmpty(summarizeBy))
            {
                sql.Append(", ").Append(summarizeBy);
            }
        }
        else if (projectColumns.Count > 0)
        {
            sql.Append(string.Join(", ", projectColumns));
            if (extendColumns.Count > 0)
            {
                sql.Append(", ").Append(string.Join(", ", extendColumns));
            }
        }
        else
        {
            sql.Append("*");
            if (extendColumns.Count > 0)
            {
                sql.Append(", ").Append(string.Join(", ", extendColumns));
            }
        }

        // FROM clause
        sql.Append(" FROM ").Append(tableName);

        // WHERE clause
        if (whereConditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", whereConditions));
        }

        // GROUP BY clause
        if (!string.IsNullOrEmpty(summarizeBy))
        {
            sql.Append(" GROUP BY ").Append(summarizeBy);
        }

        // ORDER BY clause
        if (!string.IsNullOrEmpty(orderBy))
        {
            sql.Append(" ORDER BY ").Append(orderBy);
        }

        // LIMIT clause
        if (!string.IsNullOrEmpty(limit))
        {
            sql.Append(" LIMIT ").Append(limit);
        }

        return sql.ToString();
    }

    private string ExtractTableName(string input)
    {
        // Handle common table aliases
        var tableMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SecurityEvent", "SecurityEvents" },
            { "DeviceProcessEvents", "Processes" },
            { "DeviceNetworkEvents", "NetworkConnections" },
            { "DeviceFileEvents", "FileEvents" },
            { "DeviceRegistryEvents", "RegistryEvents" }
        };

        var tableName = input.Split(' ')[0].Trim();
        return tableMap.TryGetValue(tableName, out var mapped) ? mapped : tableName;
    }

    private string TranslateWhereClause(string kqlWhere)
    {
        var result = kqlWhere;

        // Handle ago() function - translate to datetime calculation
        result = Regex.Replace(result, @"ago\((\d+)([hdms])\)", match =>
        {
            var value = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;
            var modifier = unit switch
            {
                "h" => $"-{value} hours",
                "d" => $"-{value} days",
                "m" => $"-{value} minutes",
                "s" => $"-{value} seconds",
                _ => $"-{value} hours"
            };
            return $"datetime('now', '{modifier}')";
        }, RegexOptions.IgnoreCase);

        // Handle now()
        result = Regex.Replace(result, @"\bnow\(\)", "datetime('now')", RegexOptions.IgnoreCase);

        // Handle contains (case-insensitive)
        result = Regex.Replace(result, @"(\w+)\s+contains\s+""([^""]+)""", 
            "$1 LIKE '%$2%' COLLATE NOCASE", RegexOptions.IgnoreCase);

        // Handle !contains
        result = Regex.Replace(result, @"(\w+)\s+!contains\s+""([^""]+)""", 
            "$1 NOT LIKE '%$2%' COLLATE NOCASE", RegexOptions.IgnoreCase);

        // Handle startswith
        result = Regex.Replace(result, @"(\w+)\s+startswith\s+""([^""]+)""", 
            "$1 LIKE '$2%'", RegexOptions.IgnoreCase);

        // Handle endswith
        result = Regex.Replace(result, @"(\w+)\s+endswith\s+""([^""]+)""", 
            "$1 LIKE '%$2'", RegexOptions.IgnoreCase);

        // Handle =~ (case-insensitive equals)
        result = Regex.Replace(result, @"(\w+)\s+=~\s+""([^""]+)""", 
            "$1 = '$2' COLLATE NOCASE", RegexOptions.IgnoreCase);

        // Handle in operator
        result = Regex.Replace(result, @"(\w+)\s+in\s*\(([^)]+)\)", match =>
        {
            var column = match.Groups[1].Value;
            var values = match.Groups[2].Value;
            return $"{column} IN ({values})";
        }, RegexOptions.IgnoreCase);

        // Handle !in operator
        result = Regex.Replace(result, @"(\w+)\s+!in\s*\(([^)]+)\)", match =>
        {
            var column = match.Groups[1].Value;
            var values = match.Groups[2].Value;
            return $"{column} NOT IN ({values})";
        }, RegexOptions.IgnoreCase);

        // Handle has_any
        result = Regex.Replace(result, @"(\w+)\s+has_any\s*\(([^)]+)\)", match =>
        {
            var column = match.Groups[1].Value;
            var values = match.Groups[2].Value
                .Split(',')
                .Select(v => v.Trim().Trim('"', '\''))
                .Select(v => $"{column} LIKE '%{v}%'");
            return $"({string.Join(" OR ", values)})";
        }, RegexOptions.IgnoreCase);

        // Handle isnotnull
        result = Regex.Replace(result, @"isnotnull\((\w+)\)", "$1 IS NOT NULL", RegexOptions.IgnoreCase);

        // Handle isnull
        result = Regex.Replace(result, @"isnull\((\w+)\)", "$1 IS NULL", RegexOptions.IgnoreCase);

        // Handle isempty
        result = Regex.Replace(result, @"isempty\((\w+)\)", "($1 IS NULL OR $1 = '')", RegexOptions.IgnoreCase);

        // Handle between
        result = Regex.Replace(result, @"(\w+)\s+between\s*\(([^.]+)\s*\.\.\s*([^)]+)\)", 
            "$1 BETWEEN $2 AND $3", RegexOptions.IgnoreCase);

        // Handle 'and' / 'or'
        result = Regex.Replace(result, @"\band\b", "AND", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bor\b", "OR", RegexOptions.IgnoreCase);

        // Handle == to =
        result = result.Replace("==", "=");

        // Handle != 
        result = result.Replace("!=", "<>");

        return result;
    }

    private List<string> ParseProjectColumns(string projectClause)
    {
        var columns = new List<string>();
        var parts = SplitRespectingParens(projectClause);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Handle column rename: NewName = OldName
            var renameMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+)$");
            if (renameMatch.Success)
            {
                var alias = renameMatch.Groups[1].Value;
                var expr = TranslateExpression(renameMatch.Groups[2].Value.Trim());
                columns.Add($"{expr} AS {alias}");
            }
            else
            {
                columns.Add(trimmed);
            }
        }

        return columns;
    }

    private List<string> ParseExtendColumns(string extendClause)
    {
        var columns = new List<string>();
        var parts = SplitRespectingParens(extendClause);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // extend NewColumn = expression
            var match = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+)$");
            if (match.Success)
            {
                var alias = match.Groups[1].Value;
                var expr = TranslateExpression(match.Groups[2].Value.Trim());
                columns.Add($"{expr} AS {alias}");
            }
        }

        return columns;
    }

    private (string agg, string groupBy) ParseSummarize(string summarizeClause)
    {
        var byIndex = summarizeClause.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        string aggPart, byPart = "";

        if (byIndex >= 0)
        {
            aggPart = summarizeClause.Substring(0, byIndex).Trim();
            byPart = summarizeClause.Substring(byIndex + 4).Trim();
        }
        else
        {
            aggPart = summarizeClause;
        }

        // Translate aggregations
        var aggregations = new List<string>();
        foreach (var part in SplitRespectingParens(aggPart))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Handle alias = agg()
            var aliasMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+)$");
            if (aliasMatch.Success)
            {
                var alias = aliasMatch.Groups[1].Value;
                var agg = TranslateAggregation(aliasMatch.Groups[2].Value.Trim());
                aggregations.Add($"{agg} AS {alias}");
            }
            else
            {
                aggregations.Add(TranslateAggregation(trimmed));
            }
        }

        // Translate group by columns
        var groupByParts = new List<string>();
        if (!string.IsNullOrEmpty(byPart))
        {
            foreach (var part in SplitRespectingParens(byPart))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Handle bin() function
                var binMatch = Regex.Match(trimmed, @"bin\((\w+),\s*(\d+)([hdms])\)", RegexOptions.IgnoreCase);
                if (binMatch.Success)
                {
                    var column = binMatch.Groups[1].Value;
                    var value = binMatch.Groups[2].Value;
                    var unit = binMatch.Groups[3].Value;
                    // SQLite datetime truncation
                    var format = unit switch
                    {
                        "h" => "%Y-%m-%d %H:00:00",
                        "d" => "%Y-%m-%d",
                        "m" => "%Y-%m-%d %H:%M:00",
                        _ => "%Y-%m-%d %H:00:00"
                    };
                    groupByParts.Add($"strftime('{format}', {column})");
                }
                else
                {
                    groupByParts.Add(trimmed);
                }
            }
        }

        return (string.Join(", ", aggregations), string.Join(", ", groupByParts));
    }

    private string TranslateAggregation(string kqlAgg)
    {
        // count()
        if (Regex.IsMatch(kqlAgg, @"^count\(\)$", RegexOptions.IgnoreCase))
            return "COUNT(*)";

        // count(column)
        var countMatch = Regex.Match(kqlAgg, @"^count\((\w+)\)$", RegexOptions.IgnoreCase);
        if (countMatch.Success)
            return $"COUNT({countMatch.Groups[1].Value})";

        // countif(condition)
        var countifMatch = Regex.Match(kqlAgg, @"^countif\((.+)\)$", RegexOptions.IgnoreCase);
        if (countifMatch.Success)
        {
            var condition = TranslateWhereClause(countifMatch.Groups[1].Value);
            return $"SUM(CASE WHEN {condition} THEN 1 ELSE 0 END)";
        }

        // dcount(column) - distinct count
        var dcountMatch = Regex.Match(kqlAgg, @"^dcount\((\w+)\)$", RegexOptions.IgnoreCase);
        if (dcountMatch.Success)
            return $"COUNT(DISTINCT {dcountMatch.Groups[1].Value})";

        // sum(column)
        var sumMatch = Regex.Match(kqlAgg, @"^sum\((\w+)\)$", RegexOptions.IgnoreCase);
        if (sumMatch.Success)
            return $"SUM({sumMatch.Groups[1].Value})";

        // avg(column)
        var avgMatch = Regex.Match(kqlAgg, @"^avg\((\w+)\)$", RegexOptions.IgnoreCase);
        if (avgMatch.Success)
            return $"AVG({avgMatch.Groups[1].Value})";

        // min(column)
        var minMatch = Regex.Match(kqlAgg, @"^min\((\w+)\)$", RegexOptions.IgnoreCase);
        if (minMatch.Success)
            return $"MIN({minMatch.Groups[1].Value})";

        // max(column)
        var maxMatch = Regex.Match(kqlAgg, @"^max\((\w+)\)$", RegexOptions.IgnoreCase);
        if (maxMatch.Success)
            return $"MAX({maxMatch.Groups[1].Value})";

        // any(column) - SQLite: just select the first value
        var anyMatch = Regex.Match(kqlAgg, @"^any\((\w+)\)$", RegexOptions.IgnoreCase);
        if (anyMatch.Success)
            return $"MAX({anyMatch.Groups[1].Value})";

        // make_set(column) - group_concat distinct
        var makeSetMatch = Regex.Match(kqlAgg, @"^make_set\((\w+)\)$", RegexOptions.IgnoreCase);
        if (makeSetMatch.Success)
            return $"GROUP_CONCAT(DISTINCT {makeSetMatch.Groups[1].Value})";

        // make_list(column) - group_concat
        var makeListMatch = Regex.Match(kqlAgg, @"^make_list\((\w+)\)$", RegexOptions.IgnoreCase);
        if (makeListMatch.Success)
            return $"GROUP_CONCAT({makeListMatch.Groups[1].Value})";

        return kqlAgg;
    }

    private string TranslateExpression(string expr)
    {
        var result = expr;

        // strlen -> length
        result = Regex.Replace(result, @"strlen\((\w+)\)", "LENGTH($1)", RegexOptions.IgnoreCase);

        // tolower -> lower
        result = Regex.Replace(result, @"tolower\((\w+)\)", "LOWER($1)", RegexOptions.IgnoreCase);

        // toupper -> upper
        result = Regex.Replace(result, @"toupper\((\w+)\)", "UPPER($1)", RegexOptions.IgnoreCase);

        // now() -> datetime('now')
        result = Regex.Replace(result, @"\bnow\(\)", "datetime('now')", RegexOptions.IgnoreCase);

        // format_datetime
        result = Regex.Replace(result, @"format_datetime\((\w+),\s*""([^""]+)""\)", match =>
        {
            var column = match.Groups[1].Value;
            var format = match.Groups[2].Value
                .Replace("yyyy", "%Y")
                .Replace("MM", "%m")
                .Replace("dd", "%d")
                .Replace("HH", "%H")
                .Replace("mm", "%M")
                .Replace("ss", "%S");
            return $"strftime('{format}', {column})";
        }, RegexOptions.IgnoreCase);

        // case statement
        result = Regex.Replace(result, @"case\s*\(", "CASE WHEN ", RegexOptions.IgnoreCase);

        // strcat -> concatenation
        result = Regex.Replace(result, @"strcat\(([^)]+)\)", match =>
        {
            var parts = match.Groups[1].Value.Split(',').Select(p => p.Trim());
            return string.Join(" || ", parts);
        }, RegexOptions.IgnoreCase);

        return result;
    }

    private string TranslateOrderBy(string orderClause)
    {
        var result = orderClause;
        result = Regex.Replace(result, @"\basc\b", "ASC", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\bdesc\b", "DESC", RegexOptions.IgnoreCase);
        return result;
    }

    private (string limit, string orderBy) ParseTop(string topClause)
    {
        // top N by Column desc
        var match = Regex.Match(topClause, @"^(\d+)\s+by\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var limit = match.Groups[1].Value;
            var orderBy = TranslateOrderBy(match.Groups[2].Value.Trim());
            return (limit, orderBy);
        }

        // top N (just limit)
        var limitMatch = Regex.Match(topClause, @"^(\d+)");
        if (limitMatch.Success)
        {
            return (limitMatch.Groups[1].Value, "");
        }

        return ("", "");
    }

    private List<string> SplitRespectingParens(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var parenDepth = 0;

        foreach (var ch in input)
        {
            if (ch == '(') parenDepth++;
            else if (ch == ')') parenDepth--;

            if (ch == ',' && parenDepth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    #endregion

    #region Data Ingestion

    /// <summary>
    /// Insert process data from Volatility analysis.
    /// </summary>
    public async Task InsertProcessesAsync(IEnumerable<Dictionary<string, object?>> processes, string source, string computer)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var proc in processes)
            {
                var sql = @"INSERT INTO Processes 
                    (Source, Computer, PID, PPID, ProcessName, CommandLine, Username, MemorySize, Threads, Handles, StartTime, IsSuspicious)
                    VALUES (@Source, @Computer, @PID, @PPID, @ProcessName, @CommandLine, @Username, @MemorySize, @Threads, @Handles, @StartTime, @IsSuspicious)";

                using var cmd = new SQLiteCommand(sql, _connection, transaction);
                cmd.Parameters.AddWithValue("@Source", source);
                cmd.Parameters.AddWithValue("@Computer", computer);
                cmd.Parameters.AddWithValue("@PID", proc.GetValueOrDefault("PID") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PPID", proc.GetValueOrDefault("PPID") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProcessName", proc.GetValueOrDefault("ProcessName") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CommandLine", proc.GetValueOrDefault("CommandLine") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Username", proc.GetValueOrDefault("Username") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MemorySize", proc.GetValueOrDefault("MemorySize") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Threads", proc.GetValueOrDefault("Threads") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Handles", proc.GetValueOrDefault("Handles") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@StartTime", proc.GetValueOrDefault("StartTime") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IsSuspicious", proc.GetValueOrDefault("IsSuspicious") ?? 0);
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Insert network connections from analysis.
    /// </summary>
    public async Task InsertNetworkConnectionsAsync(IEnumerable<Dictionary<string, object?>> connections, string source, string computer)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var conn in connections)
            {
                var sql = @"INSERT INTO NetworkConnections 
                    (Source, Computer, LocalAddress, LocalPort, RemoteAddress, RemotePort, Protocol, State, PID, ProcessName)
                    VALUES (@Source, @Computer, @LocalAddress, @LocalPort, @RemoteAddress, @RemotePort, @Protocol, @State, @PID, @ProcessName)";

                using var cmd = new SQLiteCommand(sql, _connection, transaction);
                cmd.Parameters.AddWithValue("@Source", source);
                cmd.Parameters.AddWithValue("@Computer", computer);
                cmd.Parameters.AddWithValue("@LocalAddress", conn.GetValueOrDefault("LocalAddress") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LocalPort", conn.GetValueOrDefault("LocalPort") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RemoteAddress", conn.GetValueOrDefault("RemoteAddress") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RemotePort", conn.GetValueOrDefault("RemotePort") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Protocol", conn.GetValueOrDefault("Protocol") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@State", conn.GetValueOrDefault("State") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PID", conn.GetValueOrDefault("PID") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProcessName", conn.GetValueOrDefault("ProcessName") ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Insert security events from event logs.
    /// </summary>
    public async Task InsertSecurityEventsAsync(IEnumerable<Dictionary<string, object?>> events, string source)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var evt in events)
            {
                var sql = @"INSERT INTO SecurityEvents 
                    (TimeGenerated, Source, EventID, Level, Channel, Computer, Account, TargetAccount, IpAddress, LogonType, Status, Message, RawData)
                    VALUES (@TimeGenerated, @Source, @EventID, @Level, @Channel, @Computer, @Account, @TargetAccount, @IpAddress, @LogonType, @Status, @Message, @RawData)";

                using var cmd = new SQLiteCommand(sql, _connection, transaction);
                cmd.Parameters.AddWithValue("@TimeGenerated", evt.GetValueOrDefault("TimeGenerated") ?? DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@Source", source);
                cmd.Parameters.AddWithValue("@EventID", evt.GetValueOrDefault("EventID") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Level", evt.GetValueOrDefault("Level") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Channel", evt.GetValueOrDefault("Channel") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Computer", evt.GetValueOrDefault("Computer") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Account", evt.GetValueOrDefault("Account") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TargetAccount", evt.GetValueOrDefault("TargetAccount") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IpAddress", evt.GetValueOrDefault("IpAddress") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LogonType", evt.GetValueOrDefault("LogonType") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", evt.GetValueOrDefault("Status") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Message", evt.GetValueOrDefault("Message") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RawData", evt.GetValueOrDefault("RawData") ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Insert timeline events from Plaso/MFT.
    /// </summary>
    public async Task InsertTimelineEventsAsync(IEnumerable<Dictionary<string, object?>> events, string source)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var evt in events)
            {
                var sql = @"INSERT INTO TimelineEvents 
                    (TimeGenerated, Source, SourceType, EventType, Description, FilePath, MacTime, Computer, Extra)
                    VALUES (@TimeGenerated, @Source, @SourceType, @EventType, @Description, @FilePath, @MacTime, @Computer, @Extra)";

                using var cmd = new SQLiteCommand(sql, _connection, transaction);
                cmd.Parameters.AddWithValue("@TimeGenerated", evt.GetValueOrDefault("TimeGenerated") ?? DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@Source", source);
                cmd.Parameters.AddWithValue("@SourceType", evt.GetValueOrDefault("SourceType") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EventType", evt.GetValueOrDefault("EventType") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", evt.GetValueOrDefault("Description") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FilePath", evt.GetValueOrDefault("FilePath") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MacTime", evt.GetValueOrDefault("MacTime") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Computer", evt.GetValueOrDefault("Computer") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Extra", evt.GetValueOrDefault("Extra") ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Insert generic forensic artifacts.
    /// </summary>
    public async Task InsertArtifactsAsync(string artifactType, IEnumerable<Dictionary<string, object?>> artifacts, string source, string computer)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var transaction = _connection.BeginTransaction();
        try
        {
            foreach (var artifact in artifacts)
            {
                var sql = @"INSERT INTO ForensicArtifacts 
                    (Source, ArtifactType, Name, Value, Description, Extra, Computer)
                    VALUES (@Source, @ArtifactType, @Name, @Value, @Description, @Extra, @Computer)";

                using var cmd = new SQLiteCommand(sql, _connection, transaction);
                cmd.Parameters.AddWithValue("@Source", source);
                cmd.Parameters.AddWithValue("@ArtifactType", artifactType);
                cmd.Parameters.AddWithValue("@Name", artifact.GetValueOrDefault("Name") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Value", artifact.GetValueOrDefault("Value") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Description", artifact.GetValueOrDefault("Description") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Extra", artifact.GetValueOrDefault("Extra") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Computer", computer);
                await cmd.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Get list of available tables.
    /// </summary>
    public async Task<List<string>> GetTablesAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        var tables = new List<string>();
        using var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", _connection);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    /// <summary>
    /// Get columns for a table.
    /// </summary>
    public async Task<List<(string Name, string Type)>> GetTableColumnsAsync(string tableName)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        var columns = new List<(string, string)>();
        using var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", _connection);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(1), reader.GetString(2)));
        }
        return columns;
    }

    /// <summary>
    /// Get record count for a table.
    /// </summary>
    public async Task<long> GetTableRecordCountAsync(string tableName)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        using var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM {tableName}", _connection);
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    /// <summary>
    /// Clear all data from database.
    /// </summary>
    public async Task ClearAllDataAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        var tables = await GetTablesAsync();
        foreach (var table in tables.Where(t => !t.StartsWith("sqlite_")))
        {
            using var cmd = new SQLiteCommand($"DELETE FROM {table}", _connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // Vacuum to reclaim space
        using var vacuumCmd = new SQLiteCommand("VACUUM", _connection);
        await vacuumCmd.ExecuteNonQueryAsync();
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _connection?.Close();
        _connection?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// KQL query template for forensic investigations.
/// </summary>
public class KqlQueryTemplate
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Query { get; set; } = "";
    public string MitreAttack { get; set; } = "";
}

/// <summary>
/// Provides pre-built KQL query templates from the cheat sheet.
/// </summary>
public static class KqlCheatSheetTemplates
{
    public static List<KqlQueryTemplate> GetAllTemplates()
    {
        return new List<KqlQueryTemplate>
        {
            // Basic Syntax
            new KqlQueryTemplate
            {
                Name = "Basic Filter",
                Category = "Basic Syntax",
                Description = "Filter rows by a specific condition",
                Query = @"Processes
| where ProcessName contains ""powershell""
| project TimeGenerated, ProcessName, CommandLine, Username"
            },
            new KqlQueryTemplate
            {
                Name = "Time Range Filter",
                Category = "Basic Syntax",
                Description = "Filter by time using ago() function",
                Query = @"SecurityEvents
| where TimeGenerated > ago(24h)
| where EventID == 4625
| project TimeGenerated, Account, IpAddress, Computer"
            },
            new KqlQueryTemplate
            {
                Name = "Multiple Conditions",
                Category = "Basic Syntax",
                Description = "Combine multiple filter conditions",
                Query = @"Processes
| where TimeGenerated > ago(1h) and ProcessName contains ""cmd""
| where CommandLine contains ""/c"" or CommandLine contains ""/k""
| project TimeGenerated, ProcessName, CommandLine"
            },

            // Aggregations
            new KqlQueryTemplate
            {
                Name = "Count by Group",
                Category = "Aggregations",
                Description = "Count records grouped by a column",
                Query = @"SecurityEvents
| summarize Count = count() by EventID
| sort by Count desc
| take 10"
            },
            new KqlQueryTemplate
            {
                Name = "Multiple Aggregations",
                Category = "Aggregations",
                Description = "Multiple aggregation functions",
                Query = @"Processes
| summarize 
    TotalProcesses = count(),
    UniqueUsers = dcount(Username),
    AvgMemory = avg(MemorySize),
    MaxMemory = max(MemorySize)
| project TotalProcesses, UniqueUsers, AvgMemory, MaxMemory"
            },
            new KqlQueryTemplate
            {
                Name = "Time Bins",
                Category = "Aggregations",
                Description = "Group by time intervals",
                Query = @"SecurityEvents
| where TimeGenerated > ago(24h)
| summarize Count = count() by bin(TimeGenerated, 1h)
| sort by TimeGenerated asc"
            },
            new KqlQueryTemplate
            {
                Name = "Conditional Count",
                Category = "Aggregations",
                Description = "Count with conditions (countif)",
                Query = @"SecurityEvents
| summarize 
    SuccessfulLogons = countif(EventID == 4624),
    FailedLogons = countif(EventID == 4625)
| extend FailureRate = (FailedLogons * 100.0) / (SuccessfulLogons + FailedLogons)"
            },

            // Security - Authentication
            new KqlQueryTemplate
            {
                Name = "Failed Logon Attempts",
                Category = "Security - Authentication",
                Description = "Find accounts with multiple failed logon attempts (brute force detection)",
                MitreAttack = "T1110 - Brute Force",
                Query = @"SecurityEvents
| where TimeGenerated > ago(24h)
| where EventID == 4625
| summarize FailedAttempts = count() by Account, Computer, IpAddress
| where FailedAttempts > 5
| sort by FailedAttempts desc"
            },
            new KqlQueryTemplate
            {
                Name = "Logon Success After Failures",
                Category = "Security - Authentication",
                Description = "Find successful logons that followed failed attempts",
                MitreAttack = "T1110 - Brute Force",
                Query = @"SecurityEvents
| where TimeGenerated > ago(1h)
| where EventID in (4624, 4625)
| summarize 
    Failures = countif(EventID == 4625),
    Successes = countif(EventID == 4624)
    by Account, Computer
| where Failures > 3 and Successes > 0
| sort by Failures desc"
            },
            new KqlQueryTemplate
            {
                Name = "RDP Logons",
                Category = "Security - Authentication",
                Description = "Find Remote Desktop logons (LogonType 10)",
                MitreAttack = "T1021.001 - Remote Desktop Protocol",
                Query = @"SecurityEvents
| where TimeGenerated > ago(7d)
| where EventID == 4624 and LogonType == 10
| project TimeGenerated, Account, Computer, IpAddress
| sort by TimeGenerated desc"
            },

            // Security - Process
            new KqlQueryTemplate
            {
                Name = "Suspicious PowerShell",
                Category = "Security - Process",
                Description = "Find PowerShell with encoded commands or download cradles",
                MitreAttack = "T1059.001 - PowerShell",
                Query = @"Processes
| where ProcessName =~ ""powershell.exe"" or ProcessName =~ ""pwsh.exe""
| where CommandLine has_any (""-enc"", ""-encoded"", ""FromBase64String"", ""iex"", ""invoke-expression"", ""downloadstring"", ""webclient"")
| project TimeGenerated, ProcessName, CommandLine, Username, Computer
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "LOLBAS Detection",
                Category = "Security - Process",
                Description = "Living Off the Land Binaries detection",
                MitreAttack = "T1218 - Signed Binary Proxy Execution",
                Query = @"Processes
| where ProcessName in~ (""certutil.exe"", ""bitsadmin.exe"", ""wmic.exe"", ""rundll32.exe"", ""regsvr32.exe"", ""mshta.exe"", ""installutil.exe"")
| where CommandLine has_any (""http"", ""https"", ""ftp"", ""download"", ""urlcache"", ""verifyctl"", ""encode"", ""decode"")
| project TimeGenerated, ProcessName, CommandLine, Username
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Suspicious Parent-Child",
                Category = "Security - Process",
                Description = "Office apps spawning suspicious child processes",
                MitreAttack = "T1204.002 - Malicious File",
                Query = @"Processes
| where ProcessName in~ (""cmd.exe"", ""powershell.exe"", ""wscript.exe"", ""cscript.exe"", ""regsvr32.exe"")
| where PPID > 0
| project TimeGenerated, ProcessName, CommandLine, PID, PPID, Username
| sort by TimeGenerated desc"
            },

            // Security - Network
            new KqlQueryTemplate
            {
                Name = "External RDP/SSH Connections",
                Category = "Security - Network",
                Description = "Outbound connections to remote access ports",
                MitreAttack = "T1021 - Remote Services",
                Query = @"NetworkConnections
| where TimeGenerated > ago(1h)
| where RemotePort in (22, 3389, 5985, 5986)
| where RemoteAddress !startswith ""10."" and RemoteAddress !startswith ""192.168."" and RemoteAddress !startswith ""172.""
| summarize ConnectionCount = count() by ProcessName, RemoteAddress, RemotePort
| sort by ConnectionCount desc"
            },
            new KqlQueryTemplate
            {
                Name = "Beaconing Detection",
                Category = "Security - Network",
                Description = "Find regular interval connections (C2 beaconing)",
                MitreAttack = "T1071 - Application Layer Protocol",
                Query = @"NetworkConnections
| where TimeGenerated > ago(24h)
| summarize 
    ConnectionCount = count(),
    UniqueRemotePorts = dcount(RemotePort)
    by ProcessName, RemoteAddress
| where ConnectionCount > 10 and UniqueRemotePorts < 3
| sort by ConnectionCount desc"
            },
            new KqlQueryTemplate
            {
                Name = "Large Data Transfers",
                Category = "Security - Network",
                Description = "Potential data exfiltration via large outbound transfers",
                MitreAttack = "T1041 - Exfiltration Over C2 Channel",
                Query = @"NetworkConnections
| where TimeGenerated > ago(24h)
| where RemoteAddress !startswith ""10."" and RemoteAddress !startswith ""192.168.""
| summarize TotalBytes = sum(BytesSent) by ProcessName, RemoteAddress
| where TotalBytes > 100000000
| sort by TotalBytes desc"
            },

            // Security - Persistence
            new KqlQueryTemplate
            {
                Name = "Registry Run Keys",
                Category = "Security - Persistence",
                Description = "Modifications to Run/RunOnce registry keys",
                MitreAttack = "T1547.001 - Registry Run Keys",
                Query = @"RegistryEvents
| where TimeGenerated > ago(24h)
| where ActionType == ""RegistryValueSet""
| where RegistryKey has_any (""\\Run\\"", ""\\RunOnce\\"", ""\\RunServices\\"")
| project TimeGenerated, RegistryKey, RegistryValueName, RegistryValueData, AccountName
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Scheduled Task Creation",
                Category = "Security - Persistence",
                Description = "New scheduled tasks created",
                MitreAttack = "T1053.005 - Scheduled Task",
                Query = @"SecurityEvents
| where TimeGenerated > ago(7d)
| where EventID == 4698
| project TimeGenerated, Account, Computer, Message
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Service Installation",
                Category = "Security - Persistence",
                Description = "New services installed",
                MitreAttack = "T1543.003 - Windows Service",
                Query = @"SecurityEvents
| where TimeGenerated > ago(7d)
| where EventID == 7045
| project TimeGenerated, Computer, Message
| sort by TimeGenerated desc"
            },

            // Security - File
            new KqlQueryTemplate
            {
                Name = "Executable in Temp",
                Category = "Security - File",
                Description = "Executable files created in temp directories",
                MitreAttack = "T1204 - User Execution",
                Query = @"FileEvents
| where TimeGenerated > ago(24h)
| where ActionType == ""FileCreated""
| where Extension in ("".exe"", "".dll"", "".scr"", "".bat"", "".ps1"")
| where FilePath has_any (""temp"", ""tmp"", ""appdata\\local\\temp"")
| project TimeGenerated, FileName, FilePath, SHA256, AccountName
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Ransomware Indicators",
                Category = "Security - File",
                Description = "Rapid file encryption/rename activity",
                MitreAttack = "T1486 - Data Encrypted for Impact",
                Query = @"FileEvents
| where TimeGenerated > ago(1h)
| where ActionType in (""FileRenamed"", ""FileModified"")
| where FileName has_any ("".encrypted"", "".locked"", "".crypto"", "".crypt"")
| summarize FileCount = count() by AccountName, Computer
| where FileCount > 50
| sort by FileCount desc"
            },
            new KqlQueryTemplate
            {
                Name = "Sensitive File Access",
                Category = "Security - File",
                Description = "Access to files with sensitive keywords",
                Query = @"FileEvents
| where TimeGenerated > ago(24h)
| where FileName has_any (""password"", ""credential"", ""secret"", ""private"", ""key"", ""config"")
| project TimeGenerated, FileName, FilePath, ActionType, AccountName
| sort by TimeGenerated desc"
            },

            // Threat Hunting
            new KqlQueryTemplate
            {
                Name = "Lateral Movement",
                Category = "Threat Hunting",
                Description = "Accounts accessing multiple computers",
                MitreAttack = "T1021 - Remote Services",
                Query = @"SecurityEvents
| where TimeGenerated > ago(24h)
| where EventID == 4624 and LogonType in (3, 10)
| summarize 
    ComputersAccessed = dcount(Computer),
    Computers = make_set(Computer)
    by Account
| where ComputersAccessed >= 3
| sort by ComputersAccessed desc"
            },
            new KqlQueryTemplate
            {
                Name = "Credential Dumping",
                Category = "Threat Hunting",
                Description = "Potential LSASS access for credential theft",
                MitreAttack = "T1003.001 - LSASS Memory",
                Query = @"Processes
| where ProcessName =~ ""lsass.exe"" or CommandLine contains ""lsass""
| where CommandLine has_any (""procdump"", ""mimikatz"", ""sekurlsa"", ""wce"", ""comsvcs"")
| project TimeGenerated, ProcessName, CommandLine, Username
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "DCSync Detection",
                Category = "Threat Hunting",
                Description = "Domain Controller sync requests (potential credential theft)",
                MitreAttack = "T1003.006 - DCSync",
                Query = @"SecurityEvents
| where TimeGenerated > ago(7d)
| where EventID == 4662
| where Message contains ""Replicating Directory Changes""
| project TimeGenerated, Account, Computer, Message
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Golden Ticket",
                Category = "Threat Hunting",
                Description = "Potential Golden Ticket usage (TGT anomalies)",
                MitreAttack = "T1558.001 - Golden Ticket",
                Query = @"SecurityEvents
| where TimeGenerated > ago(7d)
| where EventID == 4769
| where Status != ""0x0""
| project TimeGenerated, Account, Computer, Status, Message
| sort by TimeGenerated desc"
            },

            // Memory Analysis
            new KqlQueryTemplate
            {
                Name = "Hidden Processes",
                Category = "Memory Analysis",
                Description = "Processes flagged as hidden during memory analysis",
                Query = @"Processes
| where IsHidden == 1 or IsSuspicious == 1
| project TimeGenerated, PID, ProcessName, CommandLine, Username, IsHidden, IsSuspicious
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Process Injection",
                Category = "Memory Analysis",
                Description = "Suspicious memory patterns (malfind results)",
                MitreAttack = "T1055 - Process Injection",
                Query = @"Processes
| where IsSuspicious == 1
| project TimeGenerated, PID, ProcessName, CommandLine
| sort by TimeGenerated desc"
            },

            // Timeline Analysis
            new KqlQueryTemplate
            {
                Name = "Activity Timeline",
                Category = "Timeline",
                Description = "All events in chronological order",
                Query = @"TimelineEvents
| where TimeGenerated > ago(24h)
| project TimeGenerated, SourceType, EventType, Description, FilePath
| sort by TimeGenerated desc
| take 100"
            },
            new KqlQueryTemplate
            {
                Name = "File Creation Timeline",
                Category = "Timeline",
                Description = "Files created in a time range",
                Query = @"TimelineEvents
| where TimeGenerated > ago(24h)
| where EventType contains ""Created""
| project TimeGenerated, FilePath, Description
| sort by TimeGenerated desc"
            },

            // Malware Analysis
            new KqlQueryTemplate
            {
                Name = "Macro Documents",
                Category = "Malware Analysis",
                Description = "Documents with VBA macros",
                Query = @"MalwareEvents
| where HasMacros == 1
| project TimeGenerated, FileName, FileType, HasAutoExec, IsSuspicious, SuspiciousIndicators
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "Suspicious Documents",
                Category = "Malware Analysis",
                Description = "Documents flagged as suspicious",
                Query = @"MalwareEvents
| where IsSuspicious == 1
| project TimeGenerated, FileName, FileType, SuspiciousIndicators, AnalysisTool
| sort by TimeGenerated desc"
            },

            // Extracted Features
            new KqlQueryTemplate
            {
                Name = "Email Addresses",
                Category = "Extracted Features",
                Description = "Email addresses extracted from data",
                Query = @"ExtractedFeatures
| where FeatureType == ""email""
| project TimeGenerated, Value, Context, SourceFile
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "URLs Extracted",
                Category = "Extracted Features",
                Description = "URLs found in data",
                Query = @"ExtractedFeatures
| where FeatureType == ""url""
| project TimeGenerated, Value, Context, SourceFile
| sort by TimeGenerated desc"
            },
            new KqlQueryTemplate
            {
                Name = "IP Addresses",
                Category = "Extracted Features",
                Description = "IP addresses extracted from data",
                Query = @"ExtractedFeatures
| where FeatureType == ""ip""
| project TimeGenerated, Value, Context, SourceFile
| sort by TimeGenerated desc"
            },

            // Statistics
            new KqlQueryTemplate
            {
                Name = "Database Summary",
                Category = "Statistics",
                Description = "Summary of all forensic data",
                Query = @"ForensicArtifacts
| summarize 
    TotalArtifacts = count(),
    UniqueTypes = dcount(ArtifactType),
    ArtifactTypes = make_set(ArtifactType)
| project TotalArtifacts, UniqueTypes, ArtifactTypes"
            },
            new KqlQueryTemplate
            {
                Name = "Events by Source",
                Category = "Statistics",
                Description = "Count of events grouped by data source",
                Query = @"SecurityEvents
| summarize EventCount = count() by Source
| sort by EventCount desc"
            }
        };
    }

    public static List<string> GetCategories()
    {
        return GetAllTemplates()
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    public static List<KqlQueryTemplate> GetTemplatesByCategory(string category)
    {
        return GetAllTemplates()
            .Where(t => t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
