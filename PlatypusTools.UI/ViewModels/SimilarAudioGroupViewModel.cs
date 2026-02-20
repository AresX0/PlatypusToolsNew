using System;
using System.Collections.ObjectModel;
using System.IO;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a group of similar audio files.
    /// </summary>
    public class SimilarAudioGroupViewModel : BindableBase
    {
        public SimilarAudioGroupViewModel(SimilarAudioGroup group)
        {
            AudioFiles = new ObservableCollection<SimilarAudioViewModel>();
            foreach (var audio in group.AudioFiles)
            {
                AudioFiles.Add(new SimilarAudioViewModel(audio));
            }
        }

        public ObservableCollection<SimilarAudioViewModel> AudioFiles { get; }
        
        public int AudioCount => AudioFiles.Count;
        
        public string Summary => $"{AudioFiles.Count} similar sounds";
    }

    /// <summary>
    /// ViewModel for a single similar audio file.
    /// </summary>
    public class SimilarAudioViewModel : BindableBase
    {
        private bool _isSelected;
        private readonly SimilarAudioInfo _info;

        public SimilarAudioViewModel(SimilarAudioInfo info)
        {
            _info = info;
        }

        public string FilePath => _info.FilePath;
        
        public string FileName => Path.GetFileName(_info.FilePath);
        
        public double Similarity => _info.SimilarityPercent;
        
        public string SimilarityText => $"{Similarity:F1}%";
        
        public string Duration => _info.Duration;
        
        public double DurationSeconds => _info.DurationSeconds;
        
        public long FileSize => _info.FileSize;
        
        public string FileSizeText => FormatFileSize(_info.FileSize);
        
        public int SampleRate => _info.SampleRate;
        
        public string SampleRateText => $"{SampleRate / 1000.0:F1} kHz";
        
        public int Channels => _info.Channels;
        
        public string ChannelInfo => _info.ChannelInfo;
        
        public string BitrateText => _info.BitrateText;
        
        public string Codec => _info.Codec;
        
        public string Title => !string.IsNullOrWhiteSpace(_info.Title) ? _info.Title : FileName;
        
        public string Artist => _info.Artist;
        
        public string DisplayTitle => !string.IsNullOrWhiteSpace(_info.Artist) 
            ? $"{_info.Artist} - {Title}" 
            : Title;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; RaisePropertyChanged(); }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }
    }
}
