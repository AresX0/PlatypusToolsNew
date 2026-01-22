using System;
using System.Collections.Generic;
using System.IO;
using PlatypusTools.UI.Services;

namespace SamplePlugin
{
    /// <summary>
    /// Sample file processor plugin that demonstrates how to create a plugin
    /// that processes specific file types.
    /// </summary>
    public class TextFileProcessorPlugin : PluginBase, IFileProcessorPlugin
    {
        public override string Id => "com.platypustools.sample.textprocessor";
        public override string Name => "Text File Word Counter";
        public override string Description => "A sample file processor plugin that counts words in text files.";
        public override string Version => "1.0.0";
        public override string Author => "PlatypusTools Team";

        public string[] SupportedExtensions => new[] { ".txt", ".md", ".log" };

        public bool CanProcess(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(SupportedExtensions, ext => ext == extension);
        }

        public void Process(string filePath, IDictionary<string, object>? options = null)
        {
            if (!CanProcess(filePath))
            {
                LoggingService.Instance.Warning($"Cannot process file: {filePath}");
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                var wordCount = CountWords(content);
                var lineCount = content.Split('\n').Length;
                var charCount = content.Length;

                LoggingService.Instance.Info(
                    $"Text File Analysis for {Path.GetFileName(filePath)}:\n" +
                    $"  Words: {wordCount:N0}\n" +
                    $"  Lines: {lineCount:N0}\n" +
                    $"  Characters: {charCount:N0}");

                // If caller provided a callback option, invoke it
                if (options?.TryGetValue("OnComplete", out var callback) == true && callback is Action<int, int, int> onComplete)
                {
                    onComplete(wordCount, lineCount, charCount);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error($"Failed to process {filePath}: {ex.Message}");
            }
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            
            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length;
        }

        public override void Initialize()
        {
            LoggingService.Instance.Info("Text File Word Counter plugin initialized!");
        }

        public override void Shutdown()
        {
            LoggingService.Instance.Info("Text File Word Counter plugin shutdown!");
        }
    }
}
