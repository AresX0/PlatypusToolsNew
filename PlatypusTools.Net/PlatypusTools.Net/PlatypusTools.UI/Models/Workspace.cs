namespace PlatypusTools.UI.Models
{
    public class Workspace
    {
        public string SelectedFolder { get; set; } = string.Empty;
        public string FileCleanerTarget { get; set; } = string.Empty;
        public string FileCleanerFilter { get; set; } = string.Empty;
        public string RecentTargets { get; set; } = string.Empty;
        public string DuplicatesFolder { get; set; } = string.Empty;
    }
}