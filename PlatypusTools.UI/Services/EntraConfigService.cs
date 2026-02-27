using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Manages Entra ID (Azure AD) app registration configuration.
    /// Stores the user's app registration IDs in the app's data directory
    /// so they are never committed to source control.
    /// </summary>
    public class EntraConfigService
    {
        private static EntraConfigService? _instance;
        public static EntraConfigService Instance => _instance ??= new EntraConfigService();

        private const string ConfigFileName = "entra-config.json";

        /// <summary>
        /// Generic/placeholder client ID used in source code. Not tied to any tenant.
        /// </summary>
        public const string GenericClientId = "00000000-0000-0000-0000-000000000000";

        /// <summary>
        /// Well-known Microsoft Graph PowerShell client ID. Public, not tenant-specific.
        /// </summary>
        public const string MsGraphPowerShellClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";

        private EntraConfig? _cachedConfig;

        /// <summary>
        /// Gets the full path to the config file in the app's data directory.
        /// </summary>
        public string ConfigFilePath => Path.Combine(SettingsManager.DataDirectory, ConfigFileName);

        /// <summary>
        /// Loads the Entra config from the data directory.
        /// Returns a default config if the file doesn't exist.
        /// </summary>
        public EntraConfig Load()
        {
            if (_cachedConfig != null) return _cachedConfig;

            try
            {
                var path = ConfigFilePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _cachedConfig = JsonSerializer.Deserialize<EntraConfig>(json) ?? new EntraConfig();
                    return _cachedConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Entra config: {ex.Message}");
            }

            _cachedConfig = new EntraConfig();
            return _cachedConfig;
        }

        /// <summary>
        /// Saves the Entra config to the data directory.
        /// </summary>
        public void Save(EntraConfig config)
        {
            try
            {
                var dir = SettingsManager.DataDirectory;
                Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
                _cachedConfig = config;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving Entra config: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the configured client ID, or the generic placeholder if not set.
        /// </summary>
        public string GetClientId()
        {
            var config = Load();
            return string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId == GenericClientId
                ? string.Empty
                : config.ClientId;
        }

        /// <summary>
        /// Gets the configured API scope ID, or empty if not set.
        /// </summary>
        public string GetApiScopeId()
        {
            var config = Load();
            return string.IsNullOrWhiteSpace(config.ApiScopeId)
                ? string.Empty
                : config.ApiScopeId;
        }

        /// <summary>
        /// Gets the configured tenant ID, defaulting to "common" if not set.
        /// </summary>
        public string GetTenantId()
        {
            var config = Load();
            return string.IsNullOrWhiteSpace(config.TenantId) ? "common" : config.TenantId;
        }

        /// <summary>
        /// Gets the client ID for Microsoft Graph operations.
        /// Returns the user-configured Entra client ID if set, otherwise falls back
        /// to the well-known Microsoft Graph PowerShell client ID.
        /// </summary>
        public string GetGraphClientId()
        {
            var config = Load();
            return string.IsNullOrWhiteSpace(config.GraphClientId) || config.GraphClientId == GenericClientId
                ? MsGraphPowerShellClientId
                : config.GraphClientId;
        }

        /// <summary>
        /// Checks whether the user has configured their app registration.
        /// </summary>
        public bool IsConfigured()
        {
            var config = Load();
            return !string.IsNullOrWhiteSpace(config.ClientId)
                   && config.ClientId != GenericClientId;
        }

        /// <summary>
        /// Clears the cached config, forcing a reload from disk on next access.
        /// </summary>
        public void ClearCache()
        {
            _cachedConfig = null;
        }
    }

    /// <summary>
    /// Configuration model for Entra ID app registration settings.
    /// Stored in the app's data directory (not in source control).
    /// </summary>
    public class EntraConfig
    {
        /// <summary>
        /// The Application (client) ID from your Azure AD app registration.
        /// Used for Platypus Remote authentication.
        /// </summary>
        public string ClientId { get; set; } = "";

        /// <summary>
        /// The Directory (tenant) ID. Use "common" for multi-tenant.
        /// </summary>
        public string TenantId { get; set; } = "common";

        /// <summary>
        /// The API scope ID exposed in Azure Portal -> Expose an API.
        /// Format: api://{ApiScopeId}/access_as_user
        /// Often the same as ClientId, but can differ.
        /// </summary>
        public string ApiScopeId { get; set; } = "";

        /// <summary>
        /// Optional: Custom client ID for Microsoft Graph operations.
        /// Leave empty to use the well-known Microsoft Graph PowerShell client ID.
        /// </summary>
        public string GraphClientId { get; set; } = "";
    }
}
