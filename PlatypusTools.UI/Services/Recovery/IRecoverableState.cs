using System.Collections.Generic;

namespace PlatypusTools.UI.Services.Recovery
{
    /// <summary>
    /// Phase 1.5 — opt-in contract for tabs that want their state snapshotted to disk
    /// so it can be restored after a crash.
    /// </summary>
    public interface IRecoverableState
    {
        /// <summary>Stable key (e.g. "FileManagement.Robocopy") used as the snapshot file name.</summary>
        string RecoveryKey { get; }

        /// <summary>Returns a key/value snapshot of in-progress state. Null/empty = nothing to save.</summary>
        IDictionary<string, object?>? CaptureState();

        /// <summary>Restore from a previously-captured snapshot. Implementations MUST be idempotent.</summary>
        void RestoreState(IDictionary<string, object?> state);
    }
}
