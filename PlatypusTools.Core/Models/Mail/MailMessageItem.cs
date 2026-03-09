namespace PlatypusTools.Core.Models.Mail
{
    /// <summary>
    /// Represents an email message for display.
    /// </summary>
    public class MailMessageItem
    {
        public string UniqueId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public DateTime DateUtc { get; set; }
        public bool IsRead { get; set; }
        public bool IsFlagged { get; set; }
        public bool HasAttachments { get; set; }
        public string PreviewText { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;
        public List<MailAttachment> Attachments { get; set; } = new();
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long Size { get; set; }

        public string DisplayDate => DateUtc.ToLocalTime().ToString("g");
        public string DisplaySize => Size switch
        {
            < 1024 => $"{Size} B",
            < 1048576 => $"{Size / 1024.0:F1} KB",
            _ => $"{Size / 1048576.0:F1} MB"
        };

        public string ReadIcon => IsRead ? "" : "●";
        public string FlagIcon => IsFlagged ? "🚩" : "";
        public string AttachmentIcon => HasAttachments ? "📎" : "";
    }

    /// <summary>
    /// Represents an email attachment.
    /// </summary>
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
        public byte[]? Data { get; set; }

        public string DisplaySize => Size switch
        {
            < 1024 => $"{Size} B",
            < 1048576 => $"{Size / 1024.0:F1} KB",
            _ => $"{Size / 1048576.0:F1} MB"
        };
    }
}
