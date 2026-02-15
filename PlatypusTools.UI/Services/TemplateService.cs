using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing file naming templates with variables and formatting.
    /// </summary>
    public class TemplateService
    {
        private static readonly Lazy<TemplateService> _instance = new(() => new TemplateService());
        public static TemplateService Instance => _instance.Value;
        
        private readonly string _templatesFolder;
        private readonly Dictionary<string, NamingTemplate> _templates = new();
        
        public ObservableCollection<NamingTemplate> AllTemplates { get; } = new();
        public event EventHandler? TemplatesChanged;
        
        // Built-in variables
        public static readonly Dictionary<string, string> BuiltInVariables = new()
        {
            { "{filename}", "Original file name without extension" },
            { "{ext}", "File extension with dot" },
            { "{extension}", "File extension without dot" },
            { "{counter}", "Sequential counter (001, 002, ...)" },
            { "{counter:n}", "Counter with n digits" },
            { "{date}", "Current date (yyyy-MM-dd)" },
            { "{date:format}", "Current date with custom format" },
            { "{time}", "Current time (HH-mm-ss)" },
            { "{datetime}", "Current date and time" },
            { "{year}", "Current year" },
            { "{month}", "Current month" },
            { "{day}", "Current day" },
            { "{folder}", "Parent folder name" },
            { "{size}", "File size in bytes" },
            { "{size:kb}", "File size in KB" },
            { "{size:mb}", "File size in MB" },
            { "{width}", "Image/video width" },
            { "{height}", "Image/video height" },
            { "{duration}", "Video/audio duration" },
            { "{random}", "Random 8-character string" },
            { "{random:n}", "Random n-character string" },
            { "{upper:text}", "Text in uppercase" },
            { "{lower:text}", "Text in lowercase" },
            { "{title:text}", "Text in title case" },
            { "{trim:text}", "Text with trimmed spaces" },
            { "{replace:old:new}", "Replace text in filename" }
        };
        
        private TemplateService()
        {
            _templatesFolder = Path.Combine(
                SettingsManager.DataDirectory, "Templates");
            Directory.CreateDirectory(_templatesFolder);
            LoadTemplates();
        }
        
        /// <summary>
        /// Gets all templates for a specific category.
        /// </summary>
        public IEnumerable<NamingTemplate> GetByCategory(string category)
        {
            return AllTemplates.Where(t => 
                string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Saves a template.
        /// </summary>
        public void SaveTemplate(NamingTemplate template)
        {
            template.Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
            template.ModifiedDate = DateTime.Now;
            
            var existing = _templates.Values.FirstOrDefault(t => t.Id == template.Id);
            if (existing != null)
            {
                AllTemplates.Remove(existing);
            }
            
            _templates[template.Id.ToString()] = template;
            AllTemplates.Add(template);
            SaveAllTemplates();
            TemplatesChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Deletes a template.
        /// </summary>
        public void DeleteTemplate(Guid templateId)
        {
            var key = templateId.ToString();
            if (_templates.TryGetValue(key, out var template))
            {
                _templates.Remove(key);
                AllTemplates.Remove(template);
                SaveAllTemplates();
                TemplatesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        /// <summary>
        /// Applies a template to a file.
        /// </summary>
        public string ApplyTemplate(NamingTemplate template, string filePath, int index = 0, 
            Dictionary<string, string>? customVariables = null)
        {
            return ApplyPattern(template.Pattern, filePath, index, customVariables);
        }
        
        /// <summary>
        /// Applies a pattern string to generate a new filename.
        /// </summary>
        public string ApplyPattern(string pattern, string filePath, int index = 0,
            Dictionary<string, string>? customVariables = null)
        {
            var result = pattern;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var directory = Path.GetDirectoryName(filePath) ?? "";
            var parentFolder = Path.GetFileName(directory);
            var fileInfo = File.Exists(filePath) ? new FileInfo(filePath) : null;
            
            // Basic variables
            result = result.Replace("{filename}", fileName);
            result = result.Replace("{ext}", extension);
            result = result.Replace("{extension}", extension.TrimStart('.'));
            result = result.Replace("{folder}", parentFolder);
            
            // Counter with format
            result = Regex.Replace(result, @"\{counter(?::(\d+))?\}", match =>
            {
                var digits = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 3;
                return (index + 1).ToString($"D{digits}");
            });
            
            // Date/time variables
            var now = DateTime.Now;
            result = result.Replace("{year}", now.Year.ToString());
            result = result.Replace("{month}", now.Month.ToString("D2"));
            result = result.Replace("{day}", now.Day.ToString("D2"));
            result = result.Replace("{time}", now.ToString("HH-mm-ss"));
            result = result.Replace("{datetime}", now.ToString("yyyy-MM-dd_HH-mm-ss"));
            
            result = Regex.Replace(result, @"\{date(?::([^}]+))?\}", match =>
            {
                var format = match.Groups[1].Success ? match.Groups[1].Value : "yyyy-MM-dd";
                return now.ToString(format);
            });
            
            // File size
            if (fileInfo != null)
            {
                result = result.Replace("{size}", fileInfo.Length.ToString());
                result = result.Replace("{size:kb}", $"{fileInfo.Length / 1024.0:F0}");
                result = result.Replace("{size:mb}", $"{fileInfo.Length / (1024.0 * 1024.0):F1}");
            }
            
            // Random
            result = Regex.Replace(result, @"\{random(?::(\d+))?\}", match =>
            {
                var length = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 8;
                return GenerateRandomString(length);
            });
            
            // Text transformations
            result = Regex.Replace(result, @"\{upper:([^}]+)\}", m => m.Groups[1].Value.ToUpperInvariant());
            result = Regex.Replace(result, @"\{lower:([^}]+)\}", m => m.Groups[1].Value.ToLowerInvariant());
            result = Regex.Replace(result, @"\{title:([^}]+)\}", m => ToTitleCase(m.Groups[1].Value));
            result = Regex.Replace(result, @"\{trim:([^}]+)\}", m => m.Groups[1].Value.Trim());
            
            // Replace
            result = Regex.Replace(result, @"\{replace:([^:}]+):([^}]*)\}", m =>
            {
                var oldValue = m.Groups[1].Value;
                var newValue = m.Groups[2].Value;
                return result.Replace(oldValue, newValue);
            });
            
            // Custom variables
            if (customVariables != null)
            {
                foreach (var kv in customVariables)
                {
                    result = result.Replace($"{{{kv.Key}}}", kv.Value);
                }
            }
            
            // Clean up invalid filename characters
            result = CleanFileName(result);
            
            return result;
        }
        
        /// <summary>
        /// Previews a pattern without actually renaming.
        /// </summary>
        public List<string> PreviewPattern(string pattern, IEnumerable<string> files, 
            Dictionary<string, string>? customVariables = null)
        {
            var results = new List<string>();
            var fileList = files.ToList();
            
            for (int i = 0; i < fileList.Count; i++)
            {
                results.Add(ApplyPattern(pattern, fileList[i], i, customVariables));
            }
            
            return results;
        }
        
        /// <summary>
        /// Validates a pattern string.
        /// </summary>
        public PatternValidationResult ValidatePattern(string pattern)
        {
            var result = new PatternValidationResult { IsValid = true };
            
            if (string.IsNullOrWhiteSpace(pattern))
            {
                result.IsValid = false;
                result.Errors.Add("Pattern cannot be empty");
                return result;
            }
            
            // Check for unbalanced braces
            var openBraces = pattern.Count(c => c == '{');
            var closeBraces = pattern.Count(c => c == '}');
            if (openBraces != closeBraces)
            {
                result.IsValid = false;
                result.Errors.Add("Unbalanced braces in pattern");
            }
            
            // Check for invalid variable names
            var matches = Regex.Matches(pattern, @"\{([^}]+)\}");
            foreach (Match match in matches)
            {
                var variable = match.Groups[1].Value.Split(':')[0];
                if (!IsValidVariable(variable))
                {
                    result.Warnings.Add($"Unknown variable: {{{variable}}}");
                }
            }
            
            return result;
        }
        
        private bool IsValidVariable(string variable)
        {
            var validVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "filename", "ext", "extension", "counter", "date", "time", "datetime",
                "year", "month", "day", "folder", "size", "width", "height", "duration",
                "random", "upper", "lower", "title", "trim", "replace"
            };
            return validVariables.Contains(variable);
        }
        
        private void LoadTemplates()
        {
            _templates.Clear();
            AllTemplates.Clear();
            
            // Add built-in templates
            AddBuiltInTemplates();
            
            // Load custom templates
            var templatesFile = Path.Combine(_templatesFolder, "templates.json");
            if (File.Exists(templatesFile))
            {
                try
                {
                    var json = File.ReadAllText(templatesFile);
                    var templates = JsonSerializer.Deserialize<List<NamingTemplate>>(json);
                    if (templates != null)
                    {
                        foreach (var template in templates)
                        {
                            template.IsBuiltIn = false;
                            _templates[template.Id.ToString()] = template;
                            AllTemplates.Add(template);
                        }
                    }
                }
                catch { }
            }
        }
        
        private void AddBuiltInTemplates()
        {
            var builtInTemplates = new[]
            {
                new NamingTemplate
                {
                    Id = Guid.Parse("00000001-0000-0000-0000-000000000001"),
                    Name = "Date Prefix",
                    Pattern = "{date}_{filename}{ext}",
                    Description = "Adds today's date as a prefix",
                    Category = "General",
                    IsBuiltIn = true
                },
                new NamingTemplate
                {
                    Id = Guid.Parse("00000001-0000-0000-0000-000000000002"),
                    Name = "Sequential",
                    Pattern = "{filename}_{counter}{ext}",
                    Description = "Adds a sequential counter",
                    Category = "General",
                    IsBuiltIn = true
                },
                new NamingTemplate
                {
                    Id = Guid.Parse("00000001-0000-0000-0000-000000000003"),
                    Name = "Photo Organization",
                    Pattern = "IMG_{date:yyyyMMdd}_{counter:4}{ext}",
                    Description = "Standard photo naming convention",
                    Category = "Media",
                    IsBuiltIn = true
                },
                new NamingTemplate
                {
                    Id = Guid.Parse("00000001-0000-0000-0000-000000000004"),
                    Name = "Video Export",
                    Pattern = "VID_{date:yyyyMMdd}_{time}{ext}",
                    Description = "Standard video naming with timestamp",
                    Category = "Media",
                    IsBuiltIn = true
                },
                new NamingTemplate
                {
                    Id = Guid.Parse("00000001-0000-0000-0000-000000000005"),
                    Name = "Clean Filename",
                    Pattern = "{lower:{trim:{filename}}}{ext}",
                    Description = "Lowercase and trimmed filename",
                    Category = "General",
                    IsBuiltIn = true
                }
            };
            
            foreach (var template in builtInTemplates)
            {
                _templates[template.Id.ToString()] = template;
                AllTemplates.Add(template);
            }
        }
        
        private void SaveAllTemplates()
        {
            try
            {
                var customTemplates = _templates.Values.Where(t => !t.IsBuiltIn).ToList();
                var templatesFile = Path.Combine(_templatesFolder, "templates.json");
                var json = JsonSerializer.Serialize(customTemplates, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(templatesFile, json);
            }
            catch { }
        }
        
        private static string GenerateRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
        }
        
        private static string ToTitleCase(string text)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
        }
        
        private static string CleanFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("", fileName.Where(c => !invalid.Contains(c)));
        }
    }
    
    public class NamingTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Template";
        public string Pattern { get; set; } = "{filename}{ext}";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "General";
        public bool IsBuiltIn { get; set; }
        public bool IsFavorite { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
    }
    
    public class PatternValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
