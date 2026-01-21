using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a group of similar images.
    /// </summary>
    public class SimilarImageGroupViewModel : BindableBase
    {
        public SimilarImageGroupViewModel(SimilarImageGroup group)
        {
            Images = new ObservableCollection<SimilarImageViewModel>();
            foreach (var img in group.Images)
            {
                Images.Add(new SimilarImageViewModel(img));
            }
        }

        public ObservableCollection<SimilarImageViewModel> Images { get; }
        
        public int ImageCount => Images.Count;
        
        public string Summary => $"{Images.Count} similar images";
    }

    /// <summary>
    /// ViewModel for a single similar image.
    /// </summary>
    public class SimilarImageViewModel : BindableBase
    {
        private bool _isSelected;
        private BitmapImage? _thumbnail;
        private readonly SimilarImageInfo _info;

        public SimilarImageViewModel(SimilarImageInfo info)
        {
            _info = info;
            LoadThumbnail();
        }

        public string FilePath => _info.FilePath;
        
        public string FileName => Path.GetFileName(_info.FilePath);
        
        public double Similarity => _info.SimilarityPercent;
        
        public string SimilarityText => $"{Similarity:F1}%";
        
        public string Dimensions => $"{_info.Width} x {_info.Height}";
        
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
                // Use ImageHelper for memory-efficient thumbnail loading
                if (_info.Thumbnail != null)
                {
                    Thumbnail = ImageHelper.FromDrawingBitmap(_info.Thumbnail, 100);
                }
                else if (File.Exists(FilePath))
                {
                    Thumbnail = ImageHelper.LoadThumbnail(FilePath, 100);
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
