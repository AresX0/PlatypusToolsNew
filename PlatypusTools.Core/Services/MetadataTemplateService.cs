using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Metadata;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing and applying metadata templates.
    /// </summary>
    public class MetadataTemplateService
    {
        private static MetadataTemplateService? _instance;
        public static MetadataTemplateService Instance => _instance ??= new MetadataTemplateService();
        
        private readonly List<MetadataTemplate> _templates = new();
        private string _templatesDirectory = string.Empty;
        private string? _exiftoolPath;
        
        public event EventHandler<MetadataTemplate>? TemplateAdded;
        public event EventHandler<MetadataTemplate>? TemplateUpdated;
        public event EventHandler<MetadataTemplate>? TemplateDeleted;
        public event EventHandler<MetadataApplyResult>? FileProcessed;
        public event EventHandler<double>? ProgressChanged;
        
        public IReadOnlyList<MetadataTemplate> Templates => _templates.AsReadOnly();
        
        /// <summary>
        /// Initializes the service with the templates directory.
        /// </summary>
        public async Task InitializeAsync(string templatesDirectory, string? exiftoolPath = null)
        {
            _templatesDirectory = templatesDirectory;
            _exiftoolPath = exiftoolPath;
            
            if (!Directory.Exists(_templatesDirectory))
            {
                Directory.CreateDirectory(_templatesDirectory);
            }
            
            await LoadTemplatesAsync();
            EnsureBuiltInTemplates();
        }
        
        /// <summary>
        /// Loads templates from disk.
        /// </summary>
        private async Task LoadTemplatesAsync()
        {
            _templates.Clear();
            
            var files = Directory.GetFiles(_templatesDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var template = JsonSerializer.Deserialize<MetadataTemplate>(json);
                    if (template != null)
                    {
                        _templates.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load template {file}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Ensures built-in templates exist.
        /// </summary>
        private void EnsureBuiltInTemplates()
        {
            var builtIns = CreateBuiltInTemplates();
            foreach (var template in builtIns)
            {
                if (!_templates.Any(t => t.Name == template.Name && t.IsBuiltIn))
                {
                    _templates.Add(template);
                    _ = SaveTemplateAsync(template);
                }
            }
        }
        
        /// <summary>
        /// Creates built-in template presets.
        /// </summary>
        private List<MetadataTemplate> CreateBuiltInTemplates()
        {
            return new List<MetadataTemplate>
            {
                // Copyright template
                new MetadataTemplate
                {
                    Name = "Copyright Info",
                    Description = "Basic copyright and creator information",
                    Category = "Copyright",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "Creator", DisplayName = "Creator/Artist", Type = MetadataFieldType.Text, ExifTag = "Artist", IptcTag = "By-line", XmpTag = "dc:creator" },
                        new() { Name = "Copyright", DisplayName = "Copyright Notice", Type = MetadataFieldType.Text, ExifTag = "Copyright", IptcTag = "CopyrightNotice", XmpTag = "dc:rights" },
                        new() { Name = "CopyrightStatus", DisplayName = "Copyright Status", Type = MetadataFieldType.Selection, Options = new() { "Copyrighted", "Public Domain", "Unknown" }, XmpTag = "xmpRights:Marked" },
                        new() { Name = "UsageTerms", DisplayName = "Usage Terms", Type = MetadataFieldType.MultilineText, XmpTag = "xmpRights:UsageTerms" },
                        new() { Name = "WebStatement", DisplayName = "Copyright URL", Type = MetadataFieldType.Text, XmpTag = "xmpRights:WebStatement" }
                    }
                },
                
                // Location template
                new MetadataTemplate
                {
                    Name = "Location Info",
                    Description = "Geographic location and place information",
                    Category = "Location",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "Country", DisplayName = "Country", Type = MetadataFieldType.Text, IptcTag = "Country-PrimaryLocationName", XmpTag = "photoshop:Country" },
                        new() { Name = "State", DisplayName = "State/Province", Type = MetadataFieldType.Text, IptcTag = "Province-State", XmpTag = "photoshop:State" },
                        new() { Name = "City", DisplayName = "City", Type = MetadataFieldType.Text, IptcTag = "City", XmpTag = "photoshop:City" },
                        new() { Name = "Location", DisplayName = "Sublocation", Type = MetadataFieldType.Text, IptcTag = "Sub-location", XmpTag = "Iptc4xmpCore:Location" },
                        new() { Name = "GPS", DisplayName = "GPS Coordinates", Type = MetadataFieldType.Coordinates, ExifTag = "GPSPosition" }
                    }
                },
                
                // Description template
                new MetadataTemplate
                {
                    Name = "Description & Keywords",
                    Description = "Title, description, and keyword tags",
                    Category = "Description",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "Title", DisplayName = "Title", Type = MetadataFieldType.Text, IptcTag = "ObjectName", XmpTag = "dc:title" },
                        new() { Name = "Headline", DisplayName = "Headline", Type = MetadataFieldType.Text, IptcTag = "Headline", XmpTag = "photoshop:Headline" },
                        new() { Name = "Description", DisplayName = "Description/Caption", Type = MetadataFieldType.MultilineText, IptcTag = "Caption-Abstract", XmpTag = "dc:description" },
                        new() { Name = "Keywords", DisplayName = "Keywords", Type = MetadataFieldType.Keywords, IptcTag = "Keywords", XmpTag = "dc:subject" },
                        new() { Name = "Rating", DisplayName = "Rating", Type = MetadataFieldType.Rating, XmpTag = "xmp:Rating" }
                    }
                },
                
                // Social Media template
                new MetadataTemplate
                {
                    Name = "Social Media Ready",
                    Description = "Optimized for social media sharing",
                    Category = "Social",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "Title", DisplayName = "Title", Type = MetadataFieldType.Text, XmpTag = "dc:title" },
                        new() { Name = "Description", DisplayName = "Description", Type = MetadataFieldType.MultilineText, XmpTag = "dc:description" },
                        new() { Name = "Creator", DisplayName = "Creator", Type = MetadataFieldType.Text, XmpTag = "dc:creator" },
                        new() { Name = "Keywords", DisplayName = "Hashtags", Type = MetadataFieldType.Keywords, XmpTag = "dc:subject" },
                        new() { Name = "Copyright", DisplayName = "Copyright", Type = MetadataFieldType.Text, XmpTag = "dc:rights" }
                    }
                },
                
                // Stock Photo template
                new MetadataTemplate
                {
                    Name = "Stock Photo Submission",
                    Description = "Complete metadata for stock photo submissions",
                    Category = "Stock",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "Title", DisplayName = "Title", Type = MetadataFieldType.Text, IptcTag = "ObjectName", XmpTag = "dc:title" },
                        new() { Name = "Description", DisplayName = "Description", Type = MetadataFieldType.MultilineText, IptcTag = "Caption-Abstract", XmpTag = "dc:description" },
                        new() { Name = "Keywords", DisplayName = "Keywords (50+)", Type = MetadataFieldType.Keywords, IptcTag = "Keywords", XmpTag = "dc:subject" },
                        new() { Name = "Creator", DisplayName = "Photographer", Type = MetadataFieldType.Text, IptcTag = "By-line", XmpTag = "dc:creator" },
                        new() { Name = "Copyright", DisplayName = "Copyright", Type = MetadataFieldType.Text, IptcTag = "CopyrightNotice", XmpTag = "dc:rights" },
                        new() { Name = "Country", DisplayName = "Country", Type = MetadataFieldType.Text, IptcTag = "Country-PrimaryLocationName", XmpTag = "photoshop:Country" },
                        new() { Name = "ModelRelease", DisplayName = "Model Release", Type = MetadataFieldType.Selection, Options = new() { "None", "Not Applicable", "Limited or Incomplete Releases", "Model Released" }, XmpTag = "plus:ModelReleaseStatus" },
                        new() { Name = "PropertyRelease", DisplayName = "Property Release", Type = MetadataFieldType.Selection, Options = new() { "None", "Not Applicable", "Limited or Incomplete Releases", "Property Released" }, XmpTag = "plus:PropertyReleaseStatus" }
                    }
                },
                
                // Privacy/Cleanup template
                new MetadataTemplate
                {
                    Name = "Privacy Cleanup",
                    Description = "Remove sensitive metadata while preserving basic info",
                    Category = "Privacy",
                    IsBuiltIn = true,
                    Fields = new List<MetadataField>
                    {
                        new() { Name = "GPS", DisplayName = "Remove GPS", Type = MetadataFieldType.Text, Value = "", ExifTag = "GPSPosition", IsEnabled = true },
                        new() { Name = "Software", DisplayName = "Remove Software", Type = MetadataFieldType.Text, Value = "", ExifTag = "Software", IsEnabled = true },
                        new() { Name = "Serial", DisplayName = "Remove Camera Serial", Type = MetadataFieldType.Text, Value = "", ExifTag = "SerialNumber", IsEnabled = true },
                        new() { Name = "LensSerial", DisplayName = "Remove Lens Serial", Type = MetadataFieldType.Text, Value = "", ExifTag = "LensSerialNumber", IsEnabled = true }
                    }
                }
            };
        }
        
        /// <summary>
        /// Gets templates by category.
        /// </summary>
        public IEnumerable<MetadataTemplate> GetTemplatesByCategory(string category)
        {
            return _templates.Where(t => t.Category == category);
        }
        
        /// <summary>
        /// Gets favorite templates.
        /// </summary>
        public IEnumerable<MetadataTemplate> GetFavorites()
        {
            return _templates.Where(t => t.IsFavorite);
        }
        
        /// <summary>
        /// Gets all available categories.
        /// </summary>
        public IEnumerable<string> GetCategories()
        {
            return _templates.Select(t => t.Category).Distinct().OrderBy(c => c);
        }
        
        /// <summary>
        /// Creates a new template.
        /// </summary>
        public async Task<MetadataTemplate> CreateTemplateAsync(string name, string category = "Custom")
        {
            var template = new MetadataTemplate
            {
                Name = name,
                Category = category,
                IsBuiltIn = false
            };
            
            _templates.Add(template);
            await SaveTemplateAsync(template);
            TemplateAdded?.Invoke(this, template);
            
            return template;
        }
        
        /// <summary>
        /// Saves a template to disk.
        /// </summary>
        public async Task SaveTemplateAsync(MetadataTemplate template)
        {
            template.ModifiedAt = DateTime.Now;
            
            var fileName = SanitizeFileName(template.Name) + "_" + template.Id + ".json";
            var filePath = Path.Combine(_templatesDirectory, fileName);
            
            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
            
            TemplateUpdated?.Invoke(this, template);
        }
        
        /// <summary>
        /// Deletes a template.
        /// </summary>
        public async Task DeleteTemplateAsync(MetadataTemplate template)
        {
            if (template.IsBuiltIn)
            {
                throw new InvalidOperationException("Cannot delete built-in templates");
            }
            
            _templates.Remove(template);
            
            // Find and delete the file
            var files = Directory.GetFiles(_templatesDirectory, $"*{template.Id}.json");
            foreach (var file in files)
            {
                File.Delete(file);
            }
            
            TemplateDeleted?.Invoke(this, template);
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Duplicates a template.
        /// </summary>
        public async Task<MetadataTemplate> DuplicateTemplateAsync(MetadataTemplate source)
        {
            var clone = source.Clone();
            _templates.Add(clone);
            await SaveTemplateAsync(clone);
            TemplateAdded?.Invoke(this, clone);
            return clone;
        }
        
        /// <summary>
        /// Applies a template to files.
        /// </summary>
        public async Task<List<MetadataApplyResult>> ApplyTemplateAsync(
            MetadataTemplate template,
            IEnumerable<string> filePaths,
            MetadataApplyOptions options,
            CancellationToken ct = default,
            IProgress<double>? progress = null)
        {
            var results = new List<MetadataApplyResult>();
            var files = filePaths.ToList();
            var processed = 0;
            
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;
                
                var result = await ApplyTemplateToFileAsync(template, file, options, ct);
                results.Add(result);
                FileProcessed?.Invoke(this, result);
                
                processed++;
                var progressValue = (double)processed / files.Count * 100;
                progress?.Report(progressValue);
                ProgressChanged?.Invoke(this, progressValue);
            }
            
            return results;
        }
        
        /// <summary>
        /// Applies a template to a single file.
        /// </summary>
        private async Task<MetadataApplyResult> ApplyTemplateToFileAsync(
            MetadataTemplate template,
            string filePath,
            MetadataApplyOptions options,
            CancellationToken ct)
        {
            var result = new MetadataApplyResult { FilePath = filePath };
            
            try
            {
                // Build exiftool arguments
                var args = new List<string>();
                
                if (options.CreateBackup)
                {
                    // ExifTool creates backup by default, -overwrite_original to disable
                }
                else
                {
                    args.Add("-overwrite_original");
                }
                
                var fields = options.OnlyEnabledFields 
                    ? template.Fields.Where(f => f.IsEnabled) 
                    : template.Fields;
                
                foreach (var field in fields)
                {
                    if (string.IsNullOrEmpty(field.Value) && field.Type != MetadataFieldType.Text)
                    {
                        result.SkippedFields.Add(field.Name);
                        result.FieldsSkipped++;
                        continue;
                    }
                    
                    // Determine which tag to use (prefer XMP, then IPTC, then EXIF)
                    var tag = field.XmpTag ?? field.IptcTag ?? field.ExifTag;
                    if (string.IsNullOrEmpty(tag))
                    {
                        result.SkippedFields.Add(field.Name);
                        result.FieldsSkipped++;
                        continue;
                    }
                    
                    // Handle keywords specially (append vs replace)
                    if (field.Type == MetadataFieldType.Keywords && options.AppendKeywords)
                    {
                        var keywords = field.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var keyword in keywords)
                        {
                            args.Add($"-{tag}+={keyword.Trim()}");
                        }
                    }
                    else
                    {
                        args.Add($"-{tag}={field.Value}");
                    }
                    
                    result.AppliedFields.Add(field.Name);
                    result.FieldsApplied++;
                }
                
                // Add file path
                args.Add($"\"{filePath}\"");
                
                // Run exiftool
                var exiftool = _exiftoolPath ?? "exiftool";
                var psi = new ProcessStartInfo
                {
                    FileName = exiftool,
                    Arguments = string.Join(" ", args),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(ct);
                    
                    if (process.ExitCode == 0)
                    {
                        result.Success = true;
                    }
                    else
                    {
                        var error = await process.StandardError.ReadToEndAsync(ct);
                        result.Success = false;
                        result.ErrorMessage = error;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            
            return result;
        }
        
        /// <summary>
        /// Copies metadata from one file to others.
        /// </summary>
        public async Task<List<MetadataApplyResult>> CopyMetadataAsync(
            string sourceFile,
            IEnumerable<string> targetFiles,
            IEnumerable<string>? tagsToInclude = null,
            IEnumerable<string>? tagsToExclude = null,
            CancellationToken ct = default,
            IProgress<double>? progress = null)
        {
            var results = new List<MetadataApplyResult>();
            var files = targetFiles.ToList();
            var processed = 0;
            
            // Build exiftool arguments for copying
            var baseArgs = new List<string> { "-tagsfromfile", $"\"{sourceFile}\"" };
            
            if (tagsToInclude?.Any() == true)
            {
                foreach (var tag in tagsToInclude)
                {
                    baseArgs.Add($"-{tag}");
                }
            }
            else
            {
                baseArgs.Add("-all"); // Copy all tags
            }
            
            if (tagsToExclude?.Any() == true)
            {
                foreach (var tag in tagsToExclude)
                {
                    baseArgs.Add($"--{tag}");
                }
            }
            
            foreach (var targetFile in files)
            {
                if (ct.IsCancellationRequested) break;
                
                var result = new MetadataApplyResult { FilePath = targetFile };
                
                try
                {
                    var args = new List<string>(baseArgs) { $"\"{targetFile}\"" };
                    
                    var exiftool = _exiftoolPath ?? "exiftool";
                    var psi = new ProcessStartInfo
                    {
                        FileName = exiftool,
                        Arguments = string.Join(" ", args),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        await process.WaitForExitAsync(ct);
                        result.Success = process.ExitCode == 0;
                        
                        if (!result.Success)
                        {
                            result.ErrorMessage = await process.StandardError.ReadToEndAsync(ct);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
                
                results.Add(result);
                FileProcessed?.Invoke(this, result);
                
                processed++;
                var progressValue = (double)processed / files.Count * 100;
                progress?.Report(progressValue);
                ProgressChanged?.Invoke(this, progressValue);
            }
            
            return results;
        }
        
        /// <summary>
        /// Imports a template from a JSON file.
        /// </summary>
        public async Task<MetadataTemplate?> ImportTemplateAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var template = JsonSerializer.Deserialize<MetadataTemplate>(json);
                
                if (template != null)
                {
                    template.Id = Guid.NewGuid().ToString();
                    template.IsBuiltIn = false;
                    template.CreatedAt = DateTime.Now;
                    template.ModifiedAt = DateTime.Now;
                    
                    _templates.Add(template);
                    await SaveTemplateAsync(template);
                    TemplateAdded?.Invoke(this, template);
                    
                    return template;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to import template: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Exports a template to a JSON file.
        /// </summary>
        public async Task ExportTemplateAsync(MetadataTemplate template, string filePath)
        {
            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }
        
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalid.Contains(c)).ToArray());
        }
    }
}
