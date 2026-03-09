namespace PlatypusTools.Core.Models.Mail
{
    /// <summary>
    /// Represents a mail folder / mailbox (e.g., Inbox, Sent, custom folders).
    /// </summary>
    public class MailFolder
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
        public int TotalCount { get; set; }
        public List<MailFolder> SubFolders { get; set; } = new();

        /// <summary>
        /// Icon hint for display.
        /// </summary>
        public string Icon => Name.ToUpperInvariant() switch
        {
            "INBOX" => "📥",
            "SENT" or "SENT ITEMS" or "SENT MAIL" => "📤",
            "DRAFTS" => "📝",
            "TRASH" or "DELETED ITEMS" or "DELETED" => "🗑️",
            "JUNK" or "SPAM" or "JUNK E-MAIL" or "JUNK EMAIL" => "⚠️",
            "ARCHIVE" => "📦",
            _ => "📁"
        };

        public override string ToString() => $"{Icon} {Name} ({UnreadCount}/{TotalCount})";
    }
}
