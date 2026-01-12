using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for input validation
    /// </summary>
    public sealed class ValidationService
    {
        private static readonly Lazy<ValidationService> _instance = new(() => new ValidationService());
        public static ValidationService Instance => _instance.Value;

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        private ValidationService() { }

        #region File/Path Validation

        public ValidationResult ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return ValidationResult.Error("File name cannot be empty");

            if (fileName.IndexOfAny(InvalidFileNameChars) >= 0)
                return ValidationResult.Error("File name contains invalid characters");

            if (fileName.Length > 255)
                return ValidationResult.Error("File name is too long (max 255 characters)");

            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4",
                "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();
            if (reserved.Contains(nameWithoutExt))
                return ValidationResult.Error($"'{nameWithoutExt}' is a reserved file name");

            return ValidationResult.Success();
        }

        public ValidationResult ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ValidationResult.Error("Path cannot be empty");

            if (path.IndexOfAny(InvalidPathChars) >= 0)
                return ValidationResult.Error("Path contains invalid characters");

            if (path.Length > 260)
                return ValidationResult.Warning("Path is very long and may cause issues on some systems");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateDirectoryExists(string path)
        {
            var pathResult = ValidatePath(path);
            if (!pathResult.IsValid) return pathResult;

            if (!Directory.Exists(path))
                return ValidationResult.Error("Directory does not exist");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateFileExists(string path)
        {
            var pathResult = ValidatePath(path);
            if (!pathResult.IsValid) return pathResult;

            if (!File.Exists(path))
                return ValidationResult.Error("File does not exist");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateFileExtension(string path, params string[] allowedExtensions)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!allowedExtensions.Any(e => e.ToLowerInvariant() == ext))
            {
                var allowed = string.Join(", ", allowedExtensions);
                return ValidationResult.Error($"Invalid file type. Allowed: {allowed}");
            }
            return ValidationResult.Success();
        }

        #endregion

        #region String Validation

        public ValidationResult ValidateNotEmpty(string value, string fieldName = "Value")
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult.Error($"{fieldName} cannot be empty");
            return ValidationResult.Success();
        }

        public ValidationResult ValidateLength(string value, int minLength, int maxLength, string fieldName = "Value")
        {
            if (string.IsNullOrEmpty(value))
                return minLength > 0 ? ValidationResult.Error($"{fieldName} cannot be empty") : ValidationResult.Success();

            if (value.Length < minLength)
                return ValidationResult.Error($"{fieldName} must be at least {minLength} characters");

            if (value.Length > maxLength)
                return ValidationResult.Error($"{fieldName} cannot exceed {maxLength} characters");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateRegex(string value, string pattern, string errorMessage)
        {
            try
            {
                if (!Regex.IsMatch(value ?? "", pattern))
                    return ValidationResult.Error(errorMessage);
                return ValidationResult.Success();
            }
            catch
            {
                return ValidationResult.Error("Invalid regex pattern");
            }
        }

        public ValidationResult ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return ValidationResult.Error("Email cannot be empty");

            var pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!Regex.IsMatch(email, pattern))
                return ValidationResult.Error("Invalid email format");

            return ValidationResult.Success();
        }

        public ValidationResult ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Error("URL cannot be empty");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ValidationResult.Error("Invalid URL format");

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return ValidationResult.Warning("URL should use HTTP or HTTPS");

            return ValidationResult.Success();
        }

        #endregion

        #region Numeric Validation

        public ValidationResult ValidateNumericRange(int value, int min, int max, string fieldName = "Value")
        {
            if (value < min)
                return ValidationResult.Error($"{fieldName} must be at least {min}");

            if (value > max)
                return ValidationResult.Error($"{fieldName} cannot exceed {max}");

            return ValidationResult.Success();
        }

        public ValidationResult ValidatePositive(int value, string fieldName = "Value")
        {
            if (value <= 0)
                return ValidationResult.Error($"{fieldName} must be a positive number");
            return ValidationResult.Success();
        }

        public ValidationResult ValidateNonNegative(int value, string fieldName = "Value")
        {
            if (value < 0)
                return ValidationResult.Error($"{fieldName} cannot be negative");
            return ValidationResult.Success();
        }

        #endregion

        #region Collection Validation

        public ValidationResult ValidateNotEmpty<T>(IEnumerable<T>? collection, string fieldName = "Collection")
        {
            if (collection == null || !collection.Any())
                return ValidationResult.Error($"{fieldName} cannot be empty");
            return ValidationResult.Success();
        }

        public ValidationResult ValidateCount<T>(IEnumerable<T>? collection, int min, int max, string fieldName = "Collection")
        {
            var count = collection?.Count() ?? 0;

            if (count < min)
                return ValidationResult.Error($"{fieldName} must contain at least {min} items");

            if (count > max)
                return ValidationResult.Error($"{fieldName} cannot exceed {max} items");

            return ValidationResult.Success();
        }

        #endregion

        #region Composite Validation

        public ValidationResult ValidateAll(params ValidationResult[] results)
        {
            var errors = results.Where(r => r.Severity == ValidationSeverity.Error).ToList();
            var warnings = results.Where(r => r.Severity == ValidationSeverity.Warning).ToList();

            if (errors.Count != 0)
                return ValidationResult.Error(string.Join("; ", errors.Select(e => e.Message)));

            if (warnings.Count != 0)
                return ValidationResult.Warning(string.Join("; ", warnings.Select(w => w.Message)));

            return ValidationResult.Success();
        }

        #endregion
    }

    public enum ValidationSeverity { Success, Warning, Error }

    public class ValidationResult
    {
        public bool IsValid => Severity != ValidationSeverity.Error;
        public ValidationSeverity Severity { get; private init; }
        public string Message { get; private init; } = "";

        public static ValidationResult Success() => new() { Severity = ValidationSeverity.Success };
        public static ValidationResult Warning(string message) => new() { Severity = ValidationSeverity.Warning, Message = message };
        public static ValidationResult Error(string message) => new() { Severity = ValidationSeverity.Error, Message = message };
    }
}
