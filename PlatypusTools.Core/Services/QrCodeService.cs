using System;
using System.IO;
using QRCoder;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service to generate QR codes from text, URLs, WiFi configs, vCards, etc.
    /// Uses the QRCoder NuGet package.
    /// </summary>
    public class QrCodeService
    {
        /// <summary>
        /// Generates a QR code as a PNG byte array.
        /// </summary>
        public byte[] GenerateQrCode(string content, int pixelsPerModule = 20, 
            string darkColorHex = "#000000", string lightColorHex = "#FFFFFF",
            QRCodeGenerator.ECCLevel eccLevel = QRCodeGenerator.ECCLevel.Q)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("Content cannot be empty.", nameof(content));

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(content, eccLevel);
            using var qrCode = new PngByteQRCode(qrCodeData);

            var darkColor = HexToColorBytes(darkColorHex);
            var lightColor = HexToColorBytes(lightColorHex);

            return qrCode.GetGraphic(pixelsPerModule, darkColor, lightColor);
        }

        /// <summary>
        /// Generates a WiFi QR code (scanned by phones to auto-connect).
        /// </summary>
        public byte[] GenerateWifiQrCode(string ssid, string password, string authType = "WPA",
            bool hidden = false, int pixelsPerModule = 20)
        {
            // WiFi QR code format: WIFI:T:{type};S:{ssid};P:{password};H:{hidden};;
            var escapedSsid = EscapeSpecialChars(ssid);
            var escapedPassword = EscapeSpecialChars(password);
            var content = $"WIFI:T:{authType};S:{escapedSsid};P:{escapedPassword};H:{(hidden ? "true" : "false")};;";

            return GenerateQrCode(content, pixelsPerModule);
        }

        /// <summary>
        /// Generates a vCard QR code (for contact info).
        /// </summary>
        public byte[] GenerateVCardQrCode(string firstName, string lastName, string phone = "",
            string email = "", string organization = "", string title = "", int pixelsPerModule = 20)
        {
            var vcard = $"""
                BEGIN:VCARD
                VERSION:3.0
                N:{lastName};{firstName}
                FN:{firstName} {lastName}
                """;

            if (!string.IsNullOrEmpty(organization))
                vcard += $"\nORG:{organization}";
            if (!string.IsNullOrEmpty(title))
                vcard += $"\nTITLE:{title}";
            if (!string.IsNullOrEmpty(phone))
                vcard += $"\nTEL:{phone}";
            if (!string.IsNullOrEmpty(email))
                vcard += $"\nEMAIL:{email}";

            vcard += "\nEND:VCARD";

            return GenerateQrCode(vcard, pixelsPerModule);
        }

        /// <summary>
        /// Saves QR code bytes to a file.
        /// </summary>
        public void SaveToFile(byte[] qrCodeBytes, string filePath)
        {
            File.WriteAllBytes(filePath, qrCodeBytes);
        }

        private static byte[] HexToColorBytes(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                return new byte[]
                {
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)
                };
            }
            return new byte[] { 0, 0, 0 };
        }

        private static string EscapeSpecialChars(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace(":", "\\:")
                .Replace("\"", "\\\"");
        }
    }
}
