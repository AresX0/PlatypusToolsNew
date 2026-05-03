using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Sender-key (group-ratchet) state.
    ///
    /// Protocol (Signal-style, simplified):
    ///  • A sender owns one chain key per (groupId, senderDeviceId, generation).
    ///  • Each outgoing group message advances the chain by HMAC-SHA256:
    ///        msgKey  = HMAC(chainKey, 0x01)
    ///        nextCk  = HMAC(chainKey, 0x02)
    ///  • The initial 32-byte chain key is fanned out to every recipient
    ///    device, encrypted under the same X3DH-lite root key used for 1:1
    ///    messages. Recipients store the chain key and run the same advance
    ///    function to derive each generation's per-message key.
    ///  • Generation rotates whenever group membership changes.
    /// </summary>
    internal sealed class PlatytalkSenderKeyState
    {
        public required string GroupId { get; init; }
        public required string SenderDeviceId { get; init; }
        public required long Generation { get; init; }
        public byte[] ChainKey { get; set; } = Array.Empty<byte>();
        public long Counter { get; set; }
    }

    internal static class PlatytalkSenderKeys
    {
        public static byte[] GenerateChainKey() => RandomNumberGenerator.GetBytes(32);

        public static (byte[] MessageKey, byte[] NextChainKey) AdvanceChain(byte[] chainKey)
        {
            using var mac = new HMACSHA256(chainKey);
            var msgKey = mac.ComputeHash(new byte[] { 0x01 });
            var nextCk = mac.ComputeHash(new byte[] { 0x02 });
            return (msgKey, nextCk);
        }

        /// <summary>Derives a per-message key for a given counter without
        /// mutating the caller's chain. Used on receive when an out-of-order
        /// message arrives.</summary>
        public static byte[] DeriveMessageKeyAt(byte[] initialChainKey, long counter)
        {
            var ck = (byte[])initialChainKey.Clone();
            byte[] msgKey = Array.Empty<byte>();
            for (long i = 0; i <= counter; i++)
            {
                var (mk, next) = AdvanceChain(ck);
                msgKey = mk;
                ck = next;
            }
            return msgKey;
        }
    }

    /// <summary>In-memory cache of sender-key state. Persistence is handled
    /// out-of-band by the encrypted backup snapshot; for now lives only in
    /// the running process.</summary>
    internal sealed class PlatytalkSenderKeyCache
    {
        private readonly ConcurrentDictionary<string, PlatytalkSenderKeyState> _outgoing = new();
        private readonly ConcurrentDictionary<string, PlatytalkSenderKeyState> _incoming = new();

        public PlatytalkSenderKeyState GetOrCreateOutgoing(string groupId, string deviceId, long generation)
        {
            var key = $"{groupId}|{deviceId}|{generation}";
            return _outgoing.GetOrAdd(key, _ => new PlatytalkSenderKeyState
            {
                GroupId = groupId,
                SenderDeviceId = deviceId,
                Generation = generation,
                ChainKey = PlatytalkSenderKeys.GenerateChainKey(),
                Counter = 0,
            });
        }

        public void StoreIncoming(string groupId, string senderUserId, string senderDeviceId, long generation, byte[] chainKey)
        {
            var key = $"{groupId}|{senderUserId}|{senderDeviceId}|{generation}";
            _incoming[key] = new PlatytalkSenderKeyState
            {
                GroupId = groupId,
                SenderDeviceId = senderDeviceId,
                Generation = generation,
                ChainKey = chainKey,
                Counter = 0,
            };
        }

        public PlatytalkSenderKeyState? FindIncoming(string groupId, string senderUserId, string senderDeviceId, long generation)
        {
            _incoming.TryGetValue($"{groupId}|{senderUserId}|{senderDeviceId}|{generation}", out var s);
            return s;
        }

        public IReadOnlyCollection<PlatytalkSenderKeyState> AllOutgoing() => _outgoing.Values.ToArray();

        public void Clear() { _outgoing.Clear(); _incoming.Clear(); }
    }
}
