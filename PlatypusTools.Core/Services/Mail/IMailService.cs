using PlatypusTools.Core.Models.Mail;

namespace PlatypusTools.Core.Services.Mail
{
    /// <summary>
    /// Interface for mail client operations (IMAP and Exchange).
    /// </summary>
    public interface IMailService : IDisposable
    {
        event EventHandler<string>? StatusChanged;
        event EventHandler<double>? ProgressChanged;

        bool IsConnected { get; }
        MailAccountType AccountType { get; }

        Task ConnectAsync(MailAccountConfig account, CancellationToken ct = default);
        Task DisconnectAsync();

        Task<List<MailFolder>> GetFoldersAsync(CancellationToken ct = default);
        Task<List<MailMessageItem>> GetMessagesAsync(string folderPath, int page = 0, int pageSize = 50, CancellationToken ct = default);
        Task<MailMessageItem> GetMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default);

        Task MoveMessageAsync(string fromFolder, string uniqueId, string toFolder, CancellationToken ct = default);
        Task DeleteMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default);
        Task SetReadAsync(string folderPath, string uniqueId, bool isRead, CancellationToken ct = default);
        Task SetFlaggedAsync(string folderPath, string uniqueId, bool isFlagged, CancellationToken ct = default);

        Task<byte[]?> DownloadAttachmentAsync(string folderPath, string messageId, string attachmentFileName, CancellationToken ct = default);

        /// <summary>
        /// Search messages in a folder.
        /// </summary>
        Task<List<MailMessageItem>> SearchAsync(string folderPath, string query, CancellationToken ct = default);
    }
}
