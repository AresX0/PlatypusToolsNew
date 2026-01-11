namespace PlatypusTools.Core.Models
{
    public class RenameOperation
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string ProposedPath { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ProposedFileName { get; set; } = string.Empty;
        public RenameStatus Status { get; set; } = RenameStatus.Pending;
        public bool IsSelected { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public bool RequiresCaseOnlyRename { get; set; }
        public string? MetadataSummary { get; set; }
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
