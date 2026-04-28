// Phase 4.5 / 4.4 — Opaque clipboard blob store for /api/v1/clipboard endpoints.
namespace PlatypusTools.Remote.Server
{
    internal static class ClipboardStore
    {
        public static object? Latest;
        public static string Plain = string.Empty;
    }
}
