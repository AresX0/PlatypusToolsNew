using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Services.AI;

namespace PlatypusTools.UI.ViewModels
{
    public class AudioTranscriptionViewModel : BindableBase
    {
        private readonly LocalWhisperService _whisper = new();
        private CancellationTokenSource? _cts;

        public AudioTranscriptionViewModel()
        {
            TranscribeCommand = new RelayCommand(async _ => await TranscribeAsync(), _ => !IsTranscribing && !string.IsNullOrEmpty(InputFile));
            BrowseInputCommand = new RelayCommand(_ => BrowseInput());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsTranscribing);
            CopyResultCommand = new RelayCommand(_ => { if (!string.IsNullOrEmpty(ResultText)) System.Windows.Clipboard.SetText(ResultText); });
            SaveResultCommand = new RelayCommand(async _ => await SaveResultAsync(), _ => !string.IsNullOrEmpty(ResultText));
            CheckModelCommand = new RelayCommand(async _ => await CheckModelAsync());
            DownloadModelCommand = new RelayCommand(async _ => await CheckModelAsync());

            // Populate model and language lists
            AvailableModels = new ObservableCollection<string> { "tiny", "base", "small", "medium", "large" };
            Languages = new ObservableCollection<string> { "auto", "en", "es", "fr", "de", "it", "pt", "nl", "ja", "ko", "zh", "ru", "ar", "hi", "pl", "sv", "da", "no", "fi", "tr" };
            SelectedLanguage = "auto";
            SelectedModel = "base";
        }

        private string _inputFile = "";
        public string InputFile { get => _inputFile; set { SetProperty(ref _inputFile, value); OnPropertyChanged(nameof(InputFilePath)); } }

        /// <summary>XAML alias for InputFile</summary>
        public string InputFilePath { get => InputFile; set => InputFile = value; }

        private string _outputFile = "";
        public string OutputFile { get => _outputFile; set => SetProperty(ref _outputFile, value); }

        private string _selectedModel = "base";
        public string SelectedModel { get => _selectedModel; set => SetProperty(ref _selectedModel, value); }

        private string _selectedLanguage = "auto";
        public string SelectedLanguage { get => _selectedLanguage; set => SetProperty(ref _selectedLanguage, value); }

        private string _outputFormat = "srt";
        public string OutputFormat { get => _outputFormat; set => SetProperty(ref _outputFormat, value); }

        private bool _isTranscribing;
        public bool IsTranscribing { get => _isTranscribing; set => SetProperty(ref _isTranscribing, value); }

        private int _progress;
        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private string _statusMessage = "Ready — Select an audio or video file to transcribe";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _resultText = "";
        public string ResultText { get => _resultText; set { SetProperty(ref _resultText, value); OnPropertyChanged(nameof(TranscriptionResult)); } }

        /// <summary>XAML alias for ResultText</summary>
        public string TranscriptionResult { get => ResultText; set => ResultText = value; }

        private bool _translateToEnglish;
        public bool TranslateToEnglish { get => _translateToEnglish; set => SetProperty(ref _translateToEnglish, value); }

        private bool _modelReady;
        public bool ModelReady { get => _modelReady; set => SetProperty(ref _modelReady, value); }

        private string _logOutput = "";
        public string LogOutput { get => _logOutput; set => SetProperty(ref _logOutput, value); }

        public ObservableCollection<string> Languages { get; }

        /// <summary>XAML alias for Languages</summary>
        public ObservableCollection<string> AvailableLanguages => Languages;

        /// <summary>Available Whisper model sizes</summary>
        public ObservableCollection<string> AvailableModels { get; }

        public ICommand TranscribeCommand { get; }
        public ICommand BrowseInputCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyResultCommand { get; }
        public ICommand SaveResultCommand { get; }
        public ICommand CheckModelCommand { get; }
        public ICommand DownloadModelCommand { get; }

        private async Task TranscribeAsync()
        {
            if (string.IsNullOrEmpty(InputFile) || !File.Exists(InputFile)) return;

            IsTranscribing = true;
            _cts = new CancellationTokenSource();
            ResultText = "";
            LogOutput = "";

            try
            {
                StatusMessage = "Checking Whisper installation...";
                AddLog("[START] Checking whisper.cpp...");

                if (!_whisper.IsAvailable)
                {
                    AddLog("[INSTALL] Downloading whisper.cpp...");
                    StatusMessage = "Downloading whisper.cpp...";
                    var installed = await _whisper.InstallWhisperAsync(
                        new Progress<double>(p => Progress = (int)(p * 20)),
                        _cts.Token);
                    if (!installed)
                    {
                        StatusMessage = "Failed to install whisper.cpp";
                        AddLog("[ERROR] Installation failed");
                        return;
                    }
                }

                AddLog($"[MODEL] Checking model: {SelectedModel}");
                StatusMessage = $"Ensuring model '{SelectedModel}' is available...";
                Progress = 20;

                var modelOk = await _whisper.DownloadModelAsync(SelectedModel,
                    new Progress<double>(p => Progress = 20 + (int)(p * 30)),
                    _cts.Token);
                if (!modelOk)
                {
                    StatusMessage = "Failed to download model";
                    AddLog("[ERROR] Model download failed");
                    return;
                }

                AddLog($"[MODEL] Model ready: {SelectedModel}");
                ModelReady = true;
                Progress = 50;

                StatusMessage = "Transcribing...";
                AddLog($"[TRANSCRIBE] Input: {Path.GetFileName(InputFile)}");
                AddLog($"  Language: {SelectedLanguage}, Format: {OutputFormat}");

                var language = SelectedLanguage == "auto" ? "auto" : SelectedLanguage;
                var captions = await _whisper.TranscribeAsync(InputFile, language,
                    new Progress<double>(p => Progress = 50 + (int)(p * 50)),
                    _cts.Token);

                if (captions != null && captions.Count > 0)
                {
                    // Format captions based on output format
                    ResultText = FormatCaptions(captions);
                    Progress = 100;

                    if (string.IsNullOrEmpty(OutputFile))
                        OutputFile = Path.ChangeExtension(InputFile, OutputFormat);

                    StatusMessage = $"Transcription complete — {captions.Count} segments";
                    AddLog($"[DONE] Transcription complete: {captions.Count} segments");
                }
                else
                {
                    StatusMessage = "Transcription returned empty result";
                    AddLog("[WARN] Empty result from whisper");
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled";
                AddLog("[CANCELLED]");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AddLog($"[ERROR] {ex.Message}");
            }
            finally
            {
                IsTranscribing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private string FormatCaptions(System.Collections.Generic.List<PlatypusTools.Core.Models.Video.Caption> captions)
        {
            var sb = new System.Text.StringBuilder();

            if (OutputFormat == "srt")
            {
                for (int i = 0; i < captions.Count; i++)
                {
                    var c = captions[i];
                    sb.AppendLine((i + 1).ToString());
                    sb.AppendLine($"{FormatSrtTime(c.StartTime)} --> {FormatSrtTime(c.EndTime)}");
                    sb.AppendLine(c.Text?.Trim());
                    sb.AppendLine();
                }
            }
            else if (OutputFormat == "vtt")
            {
                sb.AppendLine("WEBVTT");
                sb.AppendLine();
                foreach (var c in captions)
                {
                    sb.AppendLine($"{FormatVttTime(c.StartTime)} --> {FormatVttTime(c.EndTime)}");
                    sb.AppendLine(c.Text?.Trim());
                    sb.AppendLine();
                }
            }
            else // txt
            {
                foreach (var c in captions)
                    sb.AppendLine(c.Text?.Trim());
            }

            return sb.ToString();
        }

        private static string FormatSrtTime(TimeSpan ts) =>
            $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";

        private static string FormatVttTime(TimeSpan ts) =>
            $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";

        private async Task SaveResultAsync()
        {
            if (string.IsNullOrEmpty(ResultText)) return;

            if (string.IsNullOrEmpty(OutputFile))
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "SRT files (*.srt)|*.srt|VTT files (*.vtt)|*.vtt|Text files (*.txt)|*.txt",
                    FileName = Path.GetFileNameWithoutExtension(InputFile)
                };
                if (dlg.ShowDialog() != true) return;
                OutputFile = dlg.FileName;
            }

            await File.WriteAllTextAsync(OutputFile, ResultText);
            StatusMessage = $"Saved to {Path.GetFileName(OutputFile)}";
        }

        private async Task CheckModelAsync()
        {
            StatusMessage = "Checking model availability...";
            try
            {
                var ok = await _whisper.DownloadModelAsync(SelectedModel, cancellationToken: CancellationToken.None);
                ModelReady = ok;
                StatusMessage = ModelReady ? $"Model '{SelectedModel}' is ready" : $"Model '{SelectedModel}' download failed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void BrowseInput()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Media files|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.wma;*.mp4;*.mkv;*.avi;*.mov;*.webm|All files|*.*",
                Title = "Select Audio/Video File"
            };
            if (dlg.ShowDialog() == true)
            {
                InputFile = dlg.FileName;
                OutputFile = Path.ChangeExtension(dlg.FileName, OutputFormat);
            }
        }

        private void BrowseOutput()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SRT files (*.srt)|*.srt|VTT files (*.vtt)|*.vtt|Text files (*.txt)|*.txt",
                FileName = Path.GetFileNameWithoutExtension(InputFile)
            };
            if (dlg.ShowDialog() == true) OutputFile = dlg.FileName;
        }

        private void AddLog(string message)
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        }
    }
}
