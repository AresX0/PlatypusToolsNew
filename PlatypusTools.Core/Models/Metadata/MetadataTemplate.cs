using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.Core.Models.Metadata
{
    /// <summary>
    /// Type of metadata field.
    /// </summary>
    public enum MetadataFieldType
    {
        Text,
        MultilineText,
        Number,
        Date,
        DateTime,
        Keywords,      // Comma-separated list
        Rating,        // 1-5 stars
        Boolean,
        Selection,     // From predefined list
        Coordinates    // GPS coordinates
    }
    
    /// <summary>
    /// Represents a single metadata field definition.
    /// </summary>
    public class MetadataField : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _displayName = string.Empty;
        private string _value = string.Empty;
        private MetadataFieldType _type = MetadataFieldType.Text;
        private bool _isEnabled = true;
        private string? _exifTag;
        private string? _iptcTag;
        private string? _xmpTag;
        private List<string>? _options;
        
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }
        
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }
        
        public MetadataFieldType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// EXIF tag name for this field.
        /// </summary>
        public string? ExifTag
        {
            get => _exifTag;
            set { _exifTag = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// IPTC tag name for this field.
        /// </summary>
        public string? IptcTag
        {
            get => _iptcTag;
            set { _iptcTag = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// XMP tag name for this field.
        /// </summary>
        public string? XmpTag
        {
            get => _xmpTag;
            set { _xmpTag = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Options for Selection type fields.
        /// </summary>
        public List<string>? Options
        {
            get => _options;
            set { _options = value; OnPropertyChanged(); }
        }
        
        public MetadataField Clone()
        {
            return new MetadataField
            {
                Name = Name,
                DisplayName = DisplayName,
                Value = Value,
                Type = Type,
                IsEnabled = IsEnabled,
                ExifTag = ExifTag,
                IptcTag = IptcTag,
                XmpTag = XmpTag,
                Options = Options != null ? new List<string>(Options) : null
            };
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Represents a metadata template preset.
    /// </summary>
    public class MetadataTemplate : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _category = "Custom";
        private DateTime _createdAt = DateTime.Now;
        private DateTime _modifiedAt = DateTime.Now;
        private bool _isBuiltIn;
        private bool _isFavorite;
        
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }
        
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }
        
        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(); }
        }
        
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }
        
        public DateTime ModifiedAt
        {
            get => _modifiedAt;
            set { _modifiedAt = value; OnPropertyChanged(); }
        }
        
        public bool IsBuiltIn
        {
            get => _isBuiltIn;
            set { _isBuiltIn = value; OnPropertyChanged(); }
        }
        
        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Fields in this template.
        /// </summary>
        public List<MetadataField> Fields { get; set; } = new();
        
        /// <summary>
        /// Creates a deep copy of the template.
        /// </summary>
        public MetadataTemplate Clone()
        {
            return new MetadataTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name + " (Copy)",
                Description = Description,
                Category = Category,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                IsBuiltIn = false,
                IsFavorite = false,
                Fields = Fields.Select(f => f.Clone()).ToList()
            };
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Result of applying a metadata template.
    /// </summary>
    public class MetadataApplyResult
    {
        public string FilePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int FieldsApplied { get; set; }
        public int FieldsSkipped { get; set; }
        public int FieldsFailed { get; set; }
        public List<string> AppliedFields { get; set; } = new();
        public List<string> SkippedFields { get; set; } = new();
        public Dictionary<string, string> FailedFields { get; set; } = new();
    }
    
    /// <summary>
    /// Options for applying metadata templates.
    /// </summary>
    public class MetadataApplyOptions
    {
        /// <summary>
        /// Only apply fields that are enabled in the template.
        /// </summary>
        public bool OnlyEnabledFields { get; set; } = true;
        
        /// <summary>
        /// Overwrite existing values.
        /// </summary>
        public bool OverwriteExisting { get; set; } = true;
        
        /// <summary>
        /// Skip fields that already have values.
        /// </summary>
        public bool SkipExisting { get; set; } = false;
        
        /// <summary>
        /// Append to existing keywords instead of replacing.
        /// </summary>
        public bool AppendKeywords { get; set; } = true;
        
        /// <summary>
        /// Create backup before modifying.
        /// </summary>
        public bool CreateBackup { get; set; } = true;
        
        /// <summary>
        /// Process subdirectories.
        /// </summary>
        public bool IncludeSubdirectories { get; set; } = false;
        
        /// <summary>
        /// File types to process.
        /// </summary>
        public List<string> FileTypes { get; set; } = new() { ".jpg", ".jpeg", ".png", ".tiff", ".raw", ".cr2", ".nef" };
    }
}
