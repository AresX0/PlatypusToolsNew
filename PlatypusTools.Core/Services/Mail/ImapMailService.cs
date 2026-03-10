using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using PlatypusTools.Core.Models.Mail;
using PlatypusMailFolder = PlatypusTools.Core.Models.Mail.MailFolder;

namespace PlatypusTools.Core.Services.Mail
{
    /// <summary>
    /// IMAP mail service implementation using MailKit.
    /// </summary>
    public class ImapMailService : IMailService, IDisposable
    {
        private ImapClient? _client;
        private bool _disposed;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<double>? ProgressChanged;

        public bool IsConnected => _client?.IsConnected == true && _client?.IsAuthenticated == true;
        public MailAccountType AccountType => MailAccountType.IMAP;

        private void RaiseStatus(string msg) => StatusChanged?.Invoke(this, msg);
        private void RaiseProgress(double pct) => ProgressChanged?.Invoke(this, pct);

        public async Task ConnectAsync(MailAccountConfig account, CancellationToken ct = default)
        {
            await DisconnectAsync();

            _client = new ImapClient();
            RaiseStatus($"Connecting to {account.ImapServer}:{account.ImapPort}...");

            var sslOptions = account.ImapUseSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;

            await _client.ConnectAsync(account.ImapServer, account.ImapPort, sslOptions, ct);
            RaiseStatus("Authenticating...");

            if (account.UseOAuth && !string.IsNullOrEmpty(account.OAuthAccessToken))
            {
                // OAuth2 XOAUTH2 SASL mechanism — uses access token instead of password
                var oauth2 = new SaslMechanismOAuth2(account.Username, account.OAuthAccessToken);
                await _client.AuthenticateAsync(oauth2, ct);
            }
            else
            {
                await _client.AuthenticateAsync(account.Username, account.Password, ct);
            }

            RaiseStatus($"Connected to {account.EmailAddress}");
        }

        public async Task DisconnectAsync()
        {
            if (_client != null)
            {
                if (_client.IsConnected)
                {
                    try { await _client.DisconnectAsync(true); } catch { }
                }
                _client.Dispose();
                _client = null;
            }
        }

        public async Task<List<PlatypusMailFolder>> GetFoldersAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            RaiseStatus("Loading folders...");

            var personal = _client!.GetFolder(_client.PersonalNamespaces[0]);
            var result = new List<PlatypusMailFolder>();
            await LoadFolderTreeAsync(personal, result, ct);

            // Also add INBOX if not already included
            try
            {
                var inbox = _client.Inbox;
                if (!result.Any(f => f.FullPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase)))
                {
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                    result.Insert(0, new PlatypusMailFolder
                    {
                        Name = "Inbox",
                        FullPath = inbox.FullName,
                        TotalCount = inbox.Count,
                        UnreadCount = inbox.Unread
                    });
                    await inbox.CloseAsync(false, ct);
                }
            }
            catch { }

            RaiseStatus($"Loaded {result.Count} folders");
            return result;
        }

        private async Task LoadFolderTreeAsync(IMailFolder parent, List<PlatypusMailFolder> result, CancellationToken ct)
        {
            var subfolders = await parent.GetSubfoldersAsync(false, ct);
            foreach (var sf in subfolders)
            {
                ct.ThrowIfCancellationRequested();
                var folder = new PlatypusMailFolder
                {
                    Name = sf.Name,
                    FullPath = sf.FullName
                };

                try
                {
                    if (sf.Exists)
                    {
                        await sf.OpenAsync(FolderAccess.ReadOnly, ct);
                        folder.TotalCount = sf.Count;
                        folder.UnreadCount = sf.Unread;
                        await sf.CloseAsync(false, ct);
                    }
                }
                catch { }

                // Recurse into subfolders
                await LoadFolderTreeAsync(sf, folder.SubFolders, ct);
                result.Add(folder);
            }
        }

        public async Task<List<MailMessageItem>> GetMessagesAsync(string folderPath, int page = 0, int pageSize = 50, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadOnly, ct);
            var result = new List<MailMessageItem>();

            if (folder.Count == 0)
            {
                await folder.CloseAsync(false, ct);
                return result;
            }

            // Get messages in reverse order (newest first)
            int start = Math.Max(0, folder.Count - ((page + 1) * pageSize));
            int end = Math.Max(0, folder.Count - (page * pageSize) - 1);

            if (start > end)
            {
                await folder.CloseAsync(false, ct);
                return result;
            }

            RaiseStatus($"Loading messages {start + 1}-{end + 1} of {folder.Count}...");

            var summaries = await folder.FetchAsync(start, end,
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Envelope |
                MessageSummaryItems.Flags |
                MessageSummaryItems.Size |
                MessageSummaryItems.BodyStructure |
                MessageSummaryItems.PreviewText, ct);

            int count = summaries.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                ct.ThrowIfCancellationRequested();
                var summary = summaries[i];
                var msg = MapSummaryToItem(summary, folderPath);
                result.Add(msg);
                RaiseProgress((double)(count - i) / count * 100);
            }

            await folder.CloseAsync(false, ct);
            RaiseStatus($"Loaded {result.Count} messages from {folderPath}");
            return result;
        }

        public async Task<MailMessageItem> GetMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadOnly, ct);

            if (!UniqueId.TryParse(uniqueId, out var uid))
                throw new ArgumentException($"Invalid message ID: {uniqueId}");

            var message = await folder.GetMessageAsync(uid, ct);
            var summaries = await folder.FetchAsync(new[] { uid },
                MessageSummaryItems.Flags | MessageSummaryItems.Size, ct);

            var item = new MailMessageItem
            {
                UniqueId = uniqueId,
                Subject = message.Subject ?? "(No Subject)",
                From = message.From?.ToString() ?? "",
                FromAddress = (message.From?.Mailboxes.FirstOrDefault())?.Address ?? "",
                To = message.To?.Mailboxes.Select(m => m.Address).ToList() ?? new(),
                Cc = message.Cc?.Mailboxes.Select(m => m.Address).ToList() ?? new(),
                DateUtc = message.Date.UtcDateTime,
                BodyHtml = message.HtmlBody ?? "",
                BodyText = message.TextBody ?? "",
                FolderPath = folderPath,
                HasAttachments = message.Attachments.Any()
            };

            if (summaries.Count > 0)
            {
                var s = summaries[0];
                item.IsRead = s.Flags?.HasFlag(MessageFlags.Seen) == true;
                item.IsFlagged = s.Flags?.HasFlag(MessageFlags.Flagged) == true;
                item.Size = s.Size ?? 0;
            }

            foreach (var att in message.Attachments)
            {
                if (att is MimePart part)
                {
                    item.Attachments.Add(new MailAttachment
                    {
                        FileName = part.FileName ?? "attachment",
                        ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                        Size = part.Content?.Stream?.Length ?? 0
                    });
                }
            }

            await folder.CloseAsync(false, ct);
            return item;
        }

        public async Task MoveMessageAsync(string fromFolder, string uniqueId, string toFolder, CancellationToken ct = default)
        {
            EnsureConnected();
            var source = await GetFolderAsync(fromFolder, FolderAccess.ReadWrite, ct);
            var dest = await GetFolderAsync(toFolder, FolderAccess.ReadOnly, ct);
            await dest.CloseAsync(false, ct);

            if (UniqueId.TryParse(uniqueId, out var uid))
            {
                var destFolder = await _client!.GetFolderAsync(toFolder, ct);
                await source.MoveToAsync(uid, destFolder, ct);
            }

            await source.CloseAsync(false, ct);
            RaiseStatus($"Moved message to {toFolder}");
        }

        public async Task DeleteMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadWrite, ct);

            if (UniqueId.TryParse(uniqueId, out var uid))
            {
                await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, ct);
                await folder.ExpungeAsync(ct);
            }

            await folder.CloseAsync(false, ct);
            RaiseStatus("Message deleted");
        }

        public async Task SetReadAsync(string folderPath, string uniqueId, bool isRead, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadWrite, ct);

            if (UniqueId.TryParse(uniqueId, out var uid))
            {
                if (isRead)
                    await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
                else
                    await folder.RemoveFlagsAsync(uid, MessageFlags.Seen, true, ct);
            }

            await folder.CloseAsync(false, ct);
        }

        public async Task SetFlaggedAsync(string folderPath, string uniqueId, bool isFlagged, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadWrite, ct);

            if (UniqueId.TryParse(uniqueId, out var uid))
            {
                if (isFlagged)
                    await folder.AddFlagsAsync(uid, MessageFlags.Flagged, true, ct);
                else
                    await folder.RemoveFlagsAsync(uid, MessageFlags.Flagged, true, ct);
            }

            await folder.CloseAsync(false, ct);
        }

        public async Task<byte[]?> DownloadAttachmentAsync(string folderPath, string messageId, string attachmentFileName, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadOnly, ct);

            if (!UniqueId.TryParse(messageId, out var uid))
                return null;

            var message = await folder.GetMessageAsync(uid, ct);

            foreach (var att in message.Attachments)
            {
                if (att is MimePart part && string.Equals(part.FileName, attachmentFileName, StringComparison.OrdinalIgnoreCase))
                {
                    using var ms = new MemoryStream();
                    if (part.Content != null)
                        await part.Content.DecodeToAsync(ms, ct);
                    await folder.CloseAsync(false, ct);
                    return ms.ToArray();
                }
            }

            await folder.CloseAsync(false, ct);
            return null;
        }

        public async Task<List<MailMessageItem>> SearchAsync(string folderPath, string query, CancellationToken ct = default)
        {
            EnsureConnected();
            var folder = await GetFolderAsync(folderPath, FolderAccess.ReadOnly, ct);
            var result = new List<MailMessageItem>();

            RaiseStatus($"Searching for \"{query}\"...");

            var searchQuery = SearchQuery.Or(
                SearchQuery.SubjectContains(query),
                SearchQuery.Or(
                    SearchQuery.FromContains(query),
                    SearchQuery.BodyContains(query)
                )
            );

            var uids = await folder.SearchAsync(searchQuery, ct);
            if (uids.Count > 0)
            {
                // Limit to 200 results
                var limitedUids = uids.Reverse().Take(200).ToList();
                var summaries = await folder.FetchAsync(limitedUids,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.Flags |
                    MessageSummaryItems.Size |
                    MessageSummaryItems.PreviewText, ct);

                foreach (var summary in summaries)
                {
                    result.Add(MapSummaryToItem(summary, folderPath));
                }
            }

            await folder.CloseAsync(false, ct);
            RaiseStatus($"Found {result.Count} matching messages");
            return result;
        }

        private MailMessageItem MapSummaryToItem(IMessageSummary summary, string folderPath)
        {
            return new MailMessageItem
            {
                UniqueId = summary.UniqueId.ToString(),
                Subject = summary.Envelope?.Subject ?? "(No Subject)",
                From = summary.Envelope?.From?.ToString() ?? "",
                FromAddress = summary.Envelope?.From?.Mailboxes.FirstOrDefault()?.Address ?? "",
                To = summary.Envelope?.To?.Mailboxes.Select(m => m.Address).ToList() ?? new(),
                Cc = summary.Envelope?.Cc?.Mailboxes.Select(m => m.Address).ToList() ?? new(),
                DateUtc = summary.Envelope?.Date?.UtcDateTime ?? DateTime.MinValue,
                IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
                HasAttachments = summary.Body is BodyPartMultipart multipart &&
                    multipart.BodyParts.OfType<BodyPartBasic>().Any(p => p.IsAttachment),
                PreviewText = summary.PreviewText ?? "",
                Size = summary.Size ?? 0,
                FolderPath = folderPath
            };
        }

        private async Task<IMailFolder> GetFolderAsync(string folderPath, FolderAccess access, CancellationToken ct)
        {
            var folder = folderPath.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                ? _client!.Inbox
                : await _client!.GetFolderAsync(folderPath, ct);

            await folder.OpenAsync(access, ct);
            return folder;
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to mail server. Please connect first.");
        }

        public async Task SendMessageAsync(MailAccountConfig account, string to, string cc, string subject, string body, List<string>? attachmentPaths = null, CancellationToken ct = default)
        {
            RaiseStatus("Composing message...");

            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(account.DisplayName, account.EmailAddress));

            foreach (var addr in to.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                message.To.Add(MailboxAddress.Parse(addr));

            if (!string.IsNullOrWhiteSpace(cc))
            {
                foreach (var addr in cc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    message.Cc.Add(MailboxAddress.Parse(addr));
            }

            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body };

            if (attachmentPaths != null)
            {
                foreach (var path in attachmentPaths)
                {
                    if (System.IO.File.Exists(path))
                        builder.Attachments.Add(path);
                }
            }

            message.Body = builder.ToMessageBody();

            RaiseStatus($"Connecting to SMTP {account.SmtpServer}:{account.SmtpPort}...");

            using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
            var sslOptions = account.SmtpUseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;

            await smtpClient.ConnectAsync(account.SmtpServer, account.SmtpPort, sslOptions, ct);

            if (account.UseOAuth && !string.IsNullOrEmpty(account.OAuthAccessToken))
            {
                var oauth2 = new SaslMechanismOAuth2(account.Username, account.OAuthAccessToken);
                await smtpClient.AuthenticateAsync(oauth2, ct);
            }
            else
            {
                await smtpClient.AuthenticateAsync(account.Username, account.Password, ct);
            }

            RaiseStatus("Sending...");
            await smtpClient.SendAsync(message, ct);
            await smtpClient.DisconnectAsync(true, ct);

            RaiseStatus("Message sent successfully");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try { _client?.Dispose(); } catch { }
            }
        }
    }
}
