using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services.TabConfig
{
    /// <summary>
    /// Phase 1.4 — handles serialization and file-dialog interaction for <see cref="ITabConfigProvider"/>.
    /// </summary>
    public static class TabConfigService
    {
        public const string BundleVersion = "1";
        public const string FileExtension = ".platypuscfg";
        private const string Filter = "PlatypusTools tab config (*.platypuscfg)|*.platypuscfg|JSON (*.json)|*.json|All files (*.*)|*.*";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static bool Export(ITabConfigProvider provider)
        {
            if (provider == null) return false;
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = Filter,
                    FileName = $"{provider.TabKey}{FileExtension}",
                    Title = $"Export {provider.TabDisplayName} config"
                };
                if (dlg.ShowDialog() != true) return false;

                var bundle = new Dictionary<string, object?>
                {
                    ["bundleVersion"] = BundleVersion,
                    ["tab"] = provider.TabKey,
                    ["exportedAt"] = DateTimeOffset.Now.ToString("o"),
                    ["payload"] = provider.ExportConfig()
                };
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(bundle, JsonOpts));
                ToastNotificationService.Instance.ShowSuccess($"Exported {provider.TabDisplayName} config.");
                return true;
            }
            catch (Exception ex)
            {
                ToastNotificationService.Instance.ShowError($"Export failed: {ex.Message}");
                return false;
            }
        }

        public static bool Import(ITabConfigProvider provider)
        {
            if (provider == null) return false;
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = Filter,
                    Title = $"Import {provider.TabDisplayName} config"
                };
                if (dlg.ShowDialog() != true) return false;

                using var doc = JsonDocument.Parse(File.ReadAllText(dlg.FileName));
                var root = doc.RootElement;

                if (root.TryGetProperty("tab", out var tab))
                {
                    var tabKey = tab.GetString();
                    if (!string.IsNullOrEmpty(tabKey) && !string.Equals(tabKey, provider.TabKey, StringComparison.OrdinalIgnoreCase))
                    {
                        var ok = MessageBox.Show(
                            $"This bundle was exported from '{tabKey}' but you are importing into '{provider.TabKey}'. Continue?",
                            "Tab mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (ok != MessageBoxResult.Yes) return false;
                    }
                }

                if (!root.TryGetProperty("payload", out var payload)) return false;
                var dict = JsonElementToDictionary(payload);
                provider.ImportConfig(dict);
                ToastNotificationService.Instance.ShowSuccess($"Imported {provider.TabDisplayName} config.");
                return true;
            }
            catch (Exception ex)
            {
                ToastNotificationService.Instance.ShowError($"Import failed: {ex.Message}");
                return false;
            }
        }

        public static bool Reset(ITabConfigProvider provider)
        {
            if (provider == null) return false;
            var ok = MessageBox.Show(
                $"Reset {provider.TabDisplayName} to defaults?\nThis cannot be undone.",
                "Reset config", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return false;
            try
            {
                provider.ResetConfig();
                ToastNotificationService.Instance.ShowSuccess($"{provider.TabDisplayName} reset to defaults.");
                return true;
            }
            catch (Exception ex)
            {
                ToastNotificationService.Instance.ShowError($"Reset failed: {ex.Message}");
                return false;
            }
        }

        private static IDictionary<string, object?> JsonElementToDictionary(JsonElement element)
        {
            var dict = new Dictionary<string, object?>();
            if (element.ValueKind != JsonValueKind.Object) return dict;
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = JsonElementToValue(prop.Value);
            }
            return dict;
        }

        private static object? JsonElementToValue(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => ArrayToList(element),
            _ => element.ToString()
        };

        private static List<object?> ArrayToList(JsonElement array)
        {
            var list = new List<object?>();
            foreach (var item in array.EnumerateArray()) list.Add(JsonElementToValue(item));
            return list;
        }

        public static T? GetValue<T>(IDictionary<string, object?> config, string key, T? defaultValue = default)
        {
            if (!config.TryGetValue(key, out var raw) || raw == null) return defaultValue;
            try
            {
                if (raw is T tv) return tv;
                if (typeof(T).IsEnum && raw is string s) return (T)Enum.Parse(typeof(T), s, ignoreCase: true);
                return (T)Convert.ChangeType(raw, typeof(T));
            }
            catch { return defaultValue; }
        }
    }
}
