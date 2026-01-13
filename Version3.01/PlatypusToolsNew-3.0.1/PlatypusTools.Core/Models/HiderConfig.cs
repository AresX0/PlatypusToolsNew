using System.Collections.Generic;

namespace PlatypusTools.Core.Models
{
    public class PasswordRecord
    {
        public string Salt { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public int Iterations { get; set; } = 100000;
    }

    public class HiderRecord
    {
        public string FolderPath { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore]
        public PasswordRecord? PasswordRecord { get; set; }
        // EncryptedPassword holds a DPAPI-protected blob containing a serialized PasswordRecord (legacy)
        public string? EncryptedPassword { get; set; }
        // EncryptedPasswordRef holds a Credential Manager key name which stores the protected blob
        public string? EncryptedPasswordRef { get; set; }
        public bool AclRestricted { get; set; }
        public bool EfsEnabled { get; set; }
    }

    public class HiderConfig
    {
        public List<HiderRecord> Folders { get; set; } = new List<HiderRecord>();
        public bool AutoHideEnabled { get; set; } = false;
        public int AutoHideMinutes { get; set; } = 5;
    }
}
