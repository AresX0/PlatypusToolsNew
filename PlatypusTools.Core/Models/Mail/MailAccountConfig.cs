using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Mail
{
    /// <summary>
    /// Configuration for a mail account (IMAP or Exchange).
    /// </summary>
    public class MailAccountConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public MailAccountType AccountType { get; set; } = MailAccountType.IMAP;

        // IMAP settings
        public string ImapServer { get; set; } = string.Empty;
        public int ImapPort { get; set; } = 993;
        public bool ImapUseSsl { get; set; } = true;
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;

        // Authentication
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Password is stored encrypted via DPAPI. Not serialized directly.
        /// </summary>
        [JsonIgnore]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// DPAPI-encrypted password (base64).
        /// </summary>
        public string EncryptedPassword { get; set; } = string.Empty;

        // OAuth settings
        /// <summary>Whether to use OAuth2 browser sign-in instead of password.</summary>
        public bool UseOAuth { get; set; }

        /// <summary>OAuth Client ID. Auto-filled for Microsoft; user-provided for Google/Yahoo.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>DPAPI-encrypted OAuth refresh token (base64).</summary>
        public string EncryptedRefreshToken { get; set; } = string.Empty;

        /// <summary>Runtime-only OAuth access token. Not persisted.</summary>
        [JsonIgnore]
        public string OAuthAccessToken { get; set; } = string.Empty;

        // Legacy Exchange fields (kept for backward compat)
        public string TenantId { get; set; } = string.Empty;

        // Display
        public bool IsDefault { get; set; }
        public DateTime LastSyncUtc { get; set; }
    }
}
