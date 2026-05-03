using System;
using QRCoder;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Tiny wrapper around QRCoder so both WPF and Avalonia clients can ask
    /// the Core library for a PNG byte array of an invite link without each
    /// rolling their own.
    /// </summary>
    public static class PlatytalkQr
    {
        /// <summary>
        /// Encodes <paramref name="text"/> as a PNG QR code.
        /// </summary>
        /// <param name="text">Payload (e.g. an invite URL).</param>
        /// <param name="pixelsPerModule">Module size in pixels. 6 ≈ 220 px wide for typical invite link.</param>
        public static byte[] EncodePng(string text, int pixelsPerModule = 6)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            return png.GetGraphic(pixelsPerModule);
        }
    }
}
