namespace PlatypusTools.Core.Models
{
    public class RenameOperation : BindableModel
    {

        public string OriginalPath { get; set; } = string.Empty;
        public string ProposedPath { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ProposedFileName { get; set; } = string.Empty;
        public RenameStatus Status { get; set; } = RenameStatus.Pending;
        
        private bool _isSelected = true;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set 
            { 
                if (_isSelected != value) 
                { 
                    _isSelected = value; 
                    OnPropertyChanged(); 
                } 
            } 
        }
        
        public string? ErrorMessage { get; set; }
        public bool RequiresCaseOnlyRename { get; set; }
        public string? MetadataSummary { get; set; }
        
        /// <summary>
        /// Stores the core filename (without prefix/episode) for alphabetical sorting.
        /// Preserved across all renaming operations to maintain original sort order.
        /// </summary>
        public string OriginalFileNameForSorting { get; set; } = string.Empty;
    }

    public enum RenameStatus
    {
        Pending,
        Success,
        Failed,
        Skipped,
        NoChange,
        DryRun
    }

    public enum FileTypeFilter
    {
        All,
        Video,
        Picture,
        Document,
        Audio,
        Archive
    }
}
