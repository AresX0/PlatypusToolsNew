using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-004: One-click export of forensics/audit/scan results to HTML or CSV report.
    /// Provides reusable report generation for any DataGrid or result set.
    /// </summary>
    public static class ReportExportService
    {
        /// <summary>
        /// Exports a collection of data to HTML report with styling.
        /// </summary>
        public static async Task<string?> ExportToHtmlAsync(
            string title,
            string description,
            IEnumerable<string> columnHeaders,
            IEnumerable<IEnumerable<string>> rows,
            string? suggestedFileName = null)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "HTML Report (*.html)|*.html|CSV (*.csv)|*.csv|Text (*.txt)|*.txt",
                FileName = suggestedFileName ?? $"{title.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}",
                Title = "Export Report"
            };

            if (sfd.ShowDialog() != true) return null;

            var ext = Path.GetExtension(sfd.FileName).ToLower();
            string content;

            if (ext == ".csv")
                content = GenerateCsv(columnHeaders, rows);
            else if (ext == ".txt")
                content = GeneratePlainText(title, description, columnHeaders, rows);
            else
                content = GenerateHtml(title, description, columnHeaders, rows);

            await File.WriteAllTextAsync(sfd.FileName, content, Encoding.UTF8);

            ToastNotificationService.Instance.ShowSuccess(
                $"Report saved to {Path.GetFileName(sfd.FileName)}", "Export Complete");

            return sfd.FileName;
        }

        /// <summary>
        /// Exports DataGrid contents directly.
        /// </summary>
        public static async Task<string?> ExportDataGridAsync(
            System.Windows.Controls.DataGrid dataGrid,
            string title,
            string? description = null)
        {
            var headers = new List<string>();
            foreach (var col in dataGrid.Columns)
            {
                headers.Add(col.Header?.ToString() ?? $"Column{col.DisplayIndex}");
            }

            var rows = new List<List<string>>();
            foreach (var item in dataGrid.Items)
            {
                if (item == null) continue;
                var row = new List<string>();
                foreach (var col in dataGrid.Columns)
                {
                    var cellContent = GetCellValue(col, item);
                    row.Add(cellContent);
                }
                rows.Add(row);
            }

            return await ExportToHtmlAsync(title, description ?? $"Exported {rows.Count} records", headers, rows);
        }

        private static string GetCellValue(System.Windows.Controls.DataGridColumn col, object item)
        {
            try
            {
                if (col is System.Windows.Controls.DataGridTextColumn tc &&
                    tc.Binding is System.Windows.Data.Binding b &&
                    !string.IsNullOrEmpty(b.Path?.Path))
                {
                    var prop = item.GetType().GetProperty(b.Path.Path);
                    return prop?.GetValue(item)?.ToString() ?? "";
                }
                return item.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string GenerateHtml(
            string title,
            string description,
            IEnumerable<string> headers,
            IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\">");
            sb.AppendLine($"<title>{Escape(title)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: #1e1e2e; color: #cdd6f4; }
h1 { color: #89b4fa; border-bottom: 2px solid #313244; padding-bottom: 10px; }
p.desc { color: #a6adc8; margin-bottom: 20px; }
p.meta { color: #6c7086; font-size: 12px; }
table { border-collapse: collapse; width: 100%; margin: 20px 0; }
th { background: #313244; color: #cdd6f4; padding: 10px 12px; text-align: left; font-weight: 600; border: 1px solid #45475a; }
td { padding: 8px 12px; border: 1px solid #45475a; }
tr:nth-child(even) { background: #181825; }
tr:hover { background: #313244; }
.footer { margin-top: 30px; color: #6c7086; font-size: 11px; text-align: center; }
");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>🦆 {Escape(title)}</h1>");
            sb.AppendLine($"<p class=\"desc\">{Escape(description)}</p>");
            sb.AppendLine($"<p class=\"meta\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | PlatypusTools</p>");

            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr>");
            foreach (var h in headers)
                sb.AppendLine($"<th>{Escape(h)}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            int rowCount = 0;
            foreach (var row in rows)
            {
                sb.AppendLine("<tr>");
                foreach (var cell in row)
                    sb.AppendLine($"<td>{Escape(cell)}</td>");
                sb.AppendLine("</tr>");
                rowCount++;
            }
            sb.AppendLine("</tbody></table>");

            sb.AppendLine($"<p class=\"meta\">Total records: {rowCount}</p>");
            sb.AppendLine("<div class=\"footer\">Generated by PlatypusTools — https://github.com/AresX0/PlatypusToolsNew</div>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string GenerateCsv(
            IEnumerable<string> headers,
            IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(CsvEscape)));
            return sb.ToString();
        }

        private static string GeneratePlainText(
            string title,
            string description,
            IEnumerable<string> headers,
            IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine(description);
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine(string.Join("\t", headers));
            sb.AppendLine(new string('-', 80));
            foreach (var row in rows)
                sb.AppendLine(string.Join("\t", row));
            return sb.ToString();
        }

        private static string Escape(string s) =>
            System.Net.WebUtility.HtmlEncode(s ?? "");

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}
