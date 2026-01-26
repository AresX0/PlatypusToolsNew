using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing application localization.
    /// Provides dynamic language switching and string resource access.
    /// </summary>
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
        public static LocalizationService Instance => _instance.Value;

        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;
        private readonly Dictionary<string, string> _cachedStrings = new();

        /// <summary>
        /// Supported languages with display names.
        /// </summary>
        public static readonly Dictionary<string, string> SupportedLanguages = new()
        {
            { "en-US", "English (US)" },
            { "en-GB", "English (UK)" },
            { "es-ES", "Español" },
            { "fr-FR", "Français" },
            { "de-DE", "Deutsch" },
            { "it-IT", "Italiano" },
            { "pt-BR", "Português (Brasil)" },
            { "ja-JP", "日本語" },
            { "ko-KR", "한국어" },
            { "zh-CN", "中文 (简体)" },
            { "zh-TW", "中文 (繁體)" },
            { "ru-RU", "Русский" },
            { "ar-SA", "العربية" },
            { "hi-IN", "हिन्दी" },
            { "nl-NL", "Nederlands" },
            { "pl-PL", "Polski" },
            { "tr-TR", "Türkçe" },
            { "vi-VN", "Tiếng Việt" },
            { "th-TH", "ไทย" },
            { "sv-SE", "Svenska" }
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? LanguageChanged;

        private LocalizationService()
        {
            _resourceManager = new ResourceManager("PlatypusTools.UI.Resources.Strings", typeof(LocalizationService).Assembly);
            _currentCulture = CultureInfo.CurrentUICulture;
        }

        /// <summary>
        /// Gets or sets the current UI culture.
        /// </summary>
        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            set
            {
                if (_currentCulture.Name != value.Name)
                {
                    _currentCulture = value;
                    Thread.CurrentThread.CurrentUICulture = value;
                    CultureInfo.DefaultThreadCurrentUICulture = value;
                    _cachedStrings.Clear();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentLanguageCode));
                    OnPropertyChanged(nameof(CurrentLanguageName));
                    LanguageChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Gets the current language code (e.g., "en-US").
        /// </summary>
        public string CurrentLanguageCode => _currentCulture.Name;

        /// <summary>
        /// Gets the current language display name.
        /// </summary>
        public string CurrentLanguageName => 
            SupportedLanguages.TryGetValue(_currentCulture.Name, out var name) 
                ? name 
                : _currentCulture.DisplayName;

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The resource key.</param>
        /// <returns>The localized string, or the key if not found.</returns>
        public string this[string key] => GetString(key);

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            // Check cache first
            var cacheKey = $"{_currentCulture.Name}:{key}";
            if (_cachedStrings.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var value = _resourceManager.GetString(key, _currentCulture);
                if (value != null)
                {
                    _cachedStrings[cacheKey] = value;
                    return value;
                }
            }
            catch (MissingManifestResourceException)
            {
                // Resource file not found for this culture
            }

            // Return key as fallback (helpful for debugging)
            return $"[{key}]";
        }

        /// <summary>
        /// Gets a formatted localized string.
        /// </summary>
        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        /// Sets the language by culture code.
        /// </summary>
        public void SetLanguage(string cultureCode)
        {
            try
            {
                CurrentCulture = new CultureInfo(cultureCode);
            }
            catch (CultureNotFoundException)
            {
                // Invalid culture code, use English as fallback
                CurrentCulture = new CultureInfo("en-US");
            }
        }

        /// <summary>
        /// Saves the current language setting.
        /// </summary>
        public void SaveLanguageSetting()
        {
            var settings = SettingsManager.Current;
            settings.Language = CurrentLanguageCode;
            SettingsManager.SaveCurrent();
        }

        /// <summary>
        /// Loads the language from settings.
        /// </summary>
        public void LoadLanguageSetting()
        {
            var settings = SettingsManager.Current;
            if (!string.IsNullOrEmpty(settings.Language))
            {
                SetLanguage(settings.Language);
            }
        }

        /// <summary>
        /// Gets all supported language codes.
        /// </summary>
        public IEnumerable<string> GetSupportedLanguageCodes() => SupportedLanguages.Keys;

        /// <summary>
        /// Gets language display name by code.
        /// </summary>
        public string GetLanguageName(string cultureCode)
        {
            return SupportedLanguages.TryGetValue(cultureCode, out var name) 
                ? name 
                : cultureCode;
        }

        /// <summary>
        /// Checks if a resource file exists for the given culture.
        /// </summary>
        public bool HasResourcesForCulture(string cultureCode)
        {
            try
            {
                var culture = new CultureInfo(cultureCode);
                var resourceSet = _resourceManager.GetResourceSet(culture, true, false);
                return resourceSet != null;
            }
            catch
            {
                return false;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// XAML extension for localized strings.
    /// Usage: Text="{loc:Loc Key=MenuFile}"
    /// </summary>
    public class LocExtension : System.Windows.Markup.MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public LocExtension() { }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return LocalizationService.Instance.GetString(Key);
        }
    }
}
