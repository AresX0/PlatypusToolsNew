using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Local user identity (per device). The IdentityKeyPublic is what other
    /// devices use as the recipient's permanent identity. The matching private
    /// key never leaves the device and is stored encrypted at rest (DPAPI on
    /// Windows, libsecret/Keychain elsewhere — fall back to AES with the user
    /// password-derived key).
    /// </summary>
    public sealed class PlatytalkIdentity
    {
        public required string UserId { get; init; }
        public required string DeviceId { get; init; }
        public required string DisplayName { get; set; }
        public required byte[] IdentityKeyPublic { get; init; }      // X25519 32 bytes
        public required byte[] IdentityKeyPrivate { get; init; }     // X25519 32 bytes
        public required byte[] SigningKeyPublic { get; init; }       // Ed25519 32 bytes
        public required byte[] SigningKeyPrivate { get; init; }      // Ed25519 32/64 bytes
        public string? PhoneE164 { get; set; }
        public string? Email { get; set; }
        public bool IsRegistered { get; set; }
        public bool MfaEnabled { get; set; }
        public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    }

    public enum VerificationChannel { Sms, Email }

    public sealed class RegistrationChallenge
    {
        public required string Handle { get; init; }                 // phone or email
        public required VerificationChannel Channel { get; init; }
        public required string ChallengeId { get; init; }
        public DateTime ExpiresUtc { get; init; }
    }

    public sealed class PlatytalkContact
    {
        public required string ContactId { get; init; }              // server-issued
        public required string DisplayName { get; set; }
        public required byte[] IdentityKeyPublic { get; init; }
        public string? PhoneE164 { get; set; }
        public string? Email { get; set; }
        public string SafetyNumber { get; set; } = string.Empty;     // human-verifiable fingerprint
        public bool IsVerified { get; set; }
        public DateTime AddedUtc { get; init; } = DateTime.UtcNow;
    }

    public enum ConversationKind { Direct, Group }

    public sealed class PlatytalkConversation
    {
        public required string ConversationId { get; init; }
        public required ConversationKind Kind { get; init; }
        public string Title { get; set; } = string.Empty;
        public List<string> ParticipantIds { get; } = new();
        public TimeSpan? DisappearingAfter { get; set; }             // null = off
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
        public string? LastMessagePreview { get; set; }
        public int UnreadCount { get; set; }
    }

    public enum MessageDirection { Outgoing, Incoming }
    public enum MessageStatus { Pending, Sent, Delivered, Read, Failed, RemotelyDeleted }

    public sealed class PlatytalkMessage
    {
        public required string MessageId { get; init; }
        public required string ConversationId { get; init; }
        public required string SenderId { get; init; }
        public required MessageDirection Direction { get; init; }
        public string Body { get; set; } = string.Empty;
        public byte[]? CipherBlob { get; set; }
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
        public DateTime? ExpiresUtc { get; set; }
        public MessageStatus Status { get; set; } = MessageStatus.Pending;
        public bool IsTombstoned { get; set; }                       // delete-everywhere applied
    }

    public sealed class PlatytalkDevice
    {
        public required string DeviceId { get; init; }
        public required string Name { get; set; }
        public required byte[] IdentityKeyPublic { get; init; }
        public DateTime LinkedUtc { get; init; } = DateTime.UtcNow;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public bool IsCurrent { get; init; }
    }
}
