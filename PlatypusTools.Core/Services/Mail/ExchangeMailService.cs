using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using PlatypusTools.Core.Models.Mail;

namespace PlatypusTools.Core.Services.Mail
{
    /// <summary>
    /// Exchange mail service implementation using Microsoft Graph API.
    /// Supports Microsoft 365 / Exchange Online via OAuth.
    /// </summary>
    public class ExchangeMailService : IMailService, IDisposable
    {
        private GraphServiceClient? _graphClient;
        private string? _userId;
        private bool _disposed;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<double>? ProgressChanged;

        public bool IsConnected => _graphClient != null;
        public MailAccountType AccountType => MailAccountType.Exchange;

        private void RaiseStatus(string msg) => StatusChanged?.Invoke(this, msg);
        private void RaiseProgress(double pct) => ProgressChanged?.Invoke(this, pct);

        public async Task ConnectAsync(MailAccountConfig account, CancellationToken ct = default)
        {
            await DisconnectAsync();

            RaiseStatus("Authenticating with Microsoft... (browser window will open)");

            // Use interactive browser auth for user login
            // Default Client ID: Microsoft Office public client (supports Mail scopes)
            var clientId = string.IsNullOrEmpty(account.ClientId)
                ? "d3590ed6-52b3-4102-aeff-aad2292ab01c"
                : account.ClientId;

            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = string.IsNullOrEmpty(account.TenantId) ? "common" : account.TenantId,
                ClientId = clientId,
                RedirectUri = new Uri("http://localhost")
            });

            var scopes = new[] { "Mail.ReadWrite", "Mail.Send", "MailboxSettings.Read", "User.Read" };

            // Create GraphServiceClient (lightweight, doesn't auth yet)
            _graphClient = new GraphServiceClient(credential, scopes);

            // Auth happens on first API call - use a linked token with 2-min timeout
            // to prevent infinite hang if user never completes browser auth
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                // Get current user (triggers actual OAuth browser flow)
                var me = await _graphClient.Me.GetAsync(cancellationToken: linkedCts.Token).ConfigureAwait(false);
                _userId = me?.Id;
                RaiseStatus($"Connected as {me?.DisplayName ?? account.EmailAddress}");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _graphClient = null;
                throw new TimeoutException("Authentication timed out after 2 minutes. Please try again and complete the browser sign-in.");
            }
        }

        public Task DisconnectAsync()
        {
            _graphClient = null;
            _userId = null;
            RaiseStatus("Disconnected");
            return Task.CompletedTask;
        }

        public async Task<List<Models.Mail.MailFolder>> GetFoldersAsync(CancellationToken ct = default)
        {
            EnsureConnected();
            RaiseStatus("Loading folders...");

            var folders = await _graphClient!.Me.MailFolders.GetAsync(r =>
            {
                r.QueryParameters.Top = 100;
                r.QueryParameters.Select = new[] { "displayName", "totalItemCount", "unreadItemCount", "id" };
            }, ct);

            var result = new List<Models.Mail.MailFolder>();

            if (folders?.Value != null)
            {
                foreach (var f in folders.Value)
                {
                    var folder = new Models.Mail.MailFolder
                    {
                        Name = f.DisplayName ?? "Unknown",
                        FullPath = f.Id ?? "",
                        TotalCount = f.TotalItemCount ?? 0,
                        UnreadCount = f.UnreadItemCount ?? 0
                    };

                    // Load child folders
                    try
                    {
                        var children = await _graphClient.Me.MailFolders[f.Id].ChildFolders.GetAsync(r =>
                        {
                            r.QueryParameters.Top = 50;
                            r.QueryParameters.Select = new[] { "displayName", "totalItemCount", "unreadItemCount", "id" };
                        }, ct);

                        if (children?.Value != null)
                        {
                            foreach (var child in children.Value)
                            {
                                folder.SubFolders.Add(new Models.Mail.MailFolder
                                {
                                    Name = child.DisplayName ?? "Unknown",
                                    FullPath = child.Id ?? "",
                                    TotalCount = child.TotalItemCount ?? 0,
                                    UnreadCount = child.UnreadItemCount ?? 0
                                });
                            }
                        }
                    }
                    catch { }

                    result.Add(folder);
                }
            }

            RaiseStatus($"Loaded {result.Count} folders");
            return result;
        }

        public async Task<List<MailMessageItem>> GetMessagesAsync(string folderPath, int page = 0, int pageSize = 50, CancellationToken ct = default)
        {
            EnsureConnected();
            RaiseStatus($"Loading messages...");

            var messages = await _graphClient!.Me.MailFolders[folderPath].Messages.GetAsync(r =>
            {
                r.QueryParameters.Top = pageSize;
                r.QueryParameters.Skip = page * pageSize;
                r.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                r.QueryParameters.Select = new[]
                {
                    "id", "subject", "from", "toRecipients", "ccRecipients",
                    "receivedDateTime", "isRead", "flag", "hasAttachments",
                    "bodyPreview", "importance"
                };
            }, ct);

            var result = new List<MailMessageItem>();

            if (messages?.Value != null)
            {
                foreach (var m in messages.Value)
                {
                    result.Add(MapGraphMessage(m, folderPath));
                }
            }

            RaiseStatus($"Loaded {result.Count} messages");
            return result;
        }

        public async Task<MailMessageItem> GetMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default)
        {
            EnsureConnected();

            var message = await _graphClient!.Me.Messages[uniqueId].GetAsync(r =>
            {
                r.QueryParameters.Select = new[]
                {
                    "id", "subject", "from", "toRecipients", "ccRecipients",
                    "receivedDateTime", "isRead", "flag", "hasAttachments",
                    "body", "bodyPreview", "attachments", "importance"
                };
                r.QueryParameters.Expand = new[] { "attachments" };
            }, ct);

            if (message == null)
                throw new InvalidOperationException("Message not found");

            var item = MapGraphMessage(message, folderPath);
            item.BodyHtml = message.Body?.Content ?? "";
            item.BodyText = StripHtml(item.BodyHtml);

            if (message.Attachments != null)
            {
                foreach (var att in message.Attachments)
                {
                    item.Attachments.Add(new MailAttachment
                    {
                        FileName = att.Name ?? "attachment",
                        ContentType = att.ContentType ?? "application/octet-stream",
                        Size = att.Size ?? 0
                    });
                }
            }

            return item;
        }

        public async Task MoveMessageAsync(string fromFolder, string uniqueId, string toFolder, CancellationToken ct = default)
        {
            EnsureConnected();
            await _graphClient!.Me.Messages[uniqueId].Move.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = toFolder
                }, cancellationToken: ct);
            RaiseStatus("Message moved");
        }

        public async Task DeleteMessageAsync(string folderPath, string uniqueId, CancellationToken ct = default)
        {
            EnsureConnected();
            await _graphClient!.Me.Messages[uniqueId].DeleteAsync(cancellationToken: ct);
            RaiseStatus("Message deleted");
        }

        public async Task SetReadAsync(string folderPath, string uniqueId, bool isRead, CancellationToken ct = default)
        {
            EnsureConnected();
            await _graphClient!.Me.Messages[uniqueId].PatchAsync(new Message { IsRead = isRead }, cancellationToken: ct);
        }

        public async Task SetFlaggedAsync(string folderPath, string uniqueId, bool isFlagged, CancellationToken ct = default)
        {
            EnsureConnected();
            await _graphClient!.Me.Messages[uniqueId].PatchAsync(new Message
            {
                Flag = new FollowupFlag
                {
                    FlagStatus = isFlagged ? FollowupFlagStatus.Flagged : FollowupFlagStatus.NotFlagged
                }
            }, cancellationToken: ct);
        }

        public async Task<byte[]?> DownloadAttachmentAsync(string folderPath, string messageId, string attachmentFileName, CancellationToken ct = default)
        {
            EnsureConnected();

            var attachments = await _graphClient!.Me.Messages[messageId].Attachments.GetAsync(cancellationToken: ct);

            if (attachments?.Value != null)
            {
                var att = attachments.Value.FirstOrDefault(a =>
                    string.Equals(a.Name, attachmentFileName, StringComparison.OrdinalIgnoreCase));

                if (att is FileAttachment fileAtt)
                {
                    return fileAtt.ContentBytes;
                }
            }

            return null;
        }

        public async Task<List<MailMessageItem>> SearchAsync(string folderPath, string query, CancellationToken ct = default)
        {
            EnsureConnected();
            RaiseStatus($"Searching for \"{query}\"...");

            var messages = await _graphClient!.Me.MailFolders[folderPath].Messages.GetAsync(r =>
            {
                r.QueryParameters.Search = $"\"{query}\"";
                r.QueryParameters.Top = 200;
                r.QueryParameters.Select = new[]
                {
                    "id", "subject", "from", "toRecipients", "ccRecipients",
                    "receivedDateTime", "isRead", "flag", "hasAttachments",
                    "bodyPreview"
                };
            }, ct);

            var result = new List<MailMessageItem>();
            if (messages?.Value != null)
            {
                foreach (var m in messages.Value)
                {
                    result.Add(MapGraphMessage(m, folderPath));
                }
            }

            RaiseStatus($"Found {result.Count} matching messages");
            return result;
        }

        private MailMessageItem MapGraphMessage(Message m, string folderPath)
        {
            return new MailMessageItem
            {
                UniqueId = m.Id ?? "",
                Subject = m.Subject ?? "(No Subject)",
                From = m.From?.EmailAddress?.Name ?? m.From?.EmailAddress?.Address ?? "",
                FromAddress = m.From?.EmailAddress?.Address ?? "",
                To = m.ToRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? new(),
                Cc = m.CcRecipients?.Select(r => r.EmailAddress?.Address ?? "").ToList() ?? new(),
                DateUtc = m.ReceivedDateTime?.UtcDateTime ?? DateTime.MinValue,
                IsRead = m.IsRead ?? false,
                IsFlagged = m.Flag?.FlagStatus == FollowupFlagStatus.Flagged,
                HasAttachments = m.HasAttachments ?? false,
                PreviewText = m.BodyPreview ?? "",
                FolderPath = folderPath
            };
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            // Simple HTML stripping for preview
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to Exchange. Please connect first.");
        }

        public Task SendMessageAsync(MailAccountConfig account, string to, string cc, string subject, string body, List<string>? attachmentPaths = null, CancellationToken ct = default)
        {
            throw new NotSupportedException("Exchange Graph API send is not implemented. Use IMAP/SMTP account type instead.");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _graphClient = null;
            }
        }
    }
}
