using System;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a group of similar videos.
    /// </summary>
    public class SimilarVideoGroupViewModel : BindableBase
    {
        public SimilarVideoGroupViewModel(SimilarVideoGroup group)
        {
            Videos = new ObservableCollection<SimilarVideoViewModel>();
            foreach (var video in group.Videos)
            {
                Videos.Add(new SimilarVideoViewModel(video));
            }
        }

        public ObservableCollection<SimilarVideoViewModel> Videos { get; }
        
        public int VideoCount => Videos.Count;
        
        public string Summary => $"{Videos.Count} similar videos";
    }

    /// <summary>
    /// ViewModel for a single similar video.
    /// </summary>
    public class SimilarVideoViewModel : BindableBase
    {
        private bool _isSelected;
        private BitmapImage? _thumbnail;
        private readonly SimilarVideoInfo _info;

        public SimilarVideoViewModel(SimilarVideoInfo info)
        {
            _info = info;
            LoadThumbnail();
        }

        public string FilePath => _info.FilePath;
        
        public string FileName => Path.GetFileName(_info.FilePath);
        
        public double Similarity => _info.SimilarityPercent;
        
        public string SimilarityText => $"{Similarity:F1}%";
        
        public string Dimensions => $"{_info.Width} x {_info.Height}";
        
        public string Duration => _info.Duration;
        
        public double DurationSeconds => _info.DurationSeconds;
        
        public double FrameRate => _info.FrameRate;
        
        public string FrameRateText => $"{FrameRate:F1} fps";
        
        public long FileSize => _info.FileSize;
        
        public string FileSizeText => FormatFileSize(_info.FileSize);

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; RaisePropertyChanged(); }
        }

        public BitmapImage? Thumbnail
        {
            get => _thumbnail;
            private set { _thumbnail = value; RaisePropertyChanged(); }
        }

        private void LoadThumbnail()
        {
            try
            {
                if (_info.Thumbnail != null)
                {
                    // Convert System.Drawing.Bitmap to BitmapImage
                    using var ms = new MemoryStream();
                    _info.Thumbnail.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Thumbnail = bitmap;
                }
            }
            catch
            {
                // Thumbnail loading failed, leave as null
            }
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
