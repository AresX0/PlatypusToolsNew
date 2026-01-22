using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for a favorite/bookmark item.
    /// </summary>
    public class FavoriteViewModel : BindableBase
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Folder { get; set; } = "Favorites";
        public DateTime DateAdded { get; set; }
    }

    /// <summary>
    /// ViewModel for browser history item.
    /// </summary>
    public class HistoryItemViewModel : BindableBase
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime Visited { get; set; }
    }

    /// <summary>
    /// ViewModel for the simple web browser.
    /// Memory-efficient with WebView2.
    /// </summary>
    public class SimpleBrowserViewModel : BindableBase
    {
        private readonly WebBrowserService _browserService;
        private readonly ObservableCollection<HistoryItemViewModel> _history;

        public SimpleBrowserViewModel()
        {
            _browserService = new WebBrowserService();
            _history = new ObservableCollection<HistoryItemViewModel>();

            NavigateCommand = new RelayCommand(_ => Navigate(), _ => !string.IsNullOrWhiteSpace(Url));
            GoBackCommand = new RelayCommand(_ => GoBack?.Invoke(), _ => CanGoBack);
            GoForwardCommand = new RelayCommand(_ => GoForward?.Invoke(), _ => CanGoForward);
            RefreshCommand = new RelayCommand(_ => Refresh?.Invoke());
            StopCommand = new RelayCommand(_ => Stop?.Invoke());
            GoHomeCommand = new RelayCommand(_ => NavigateToUrl(HomePage));
            AddFavoriteCommand = new RelayCommand(_ => AddFavorite(), _ => !string.IsNullOrWhiteSpace(CurrentUrl));
            RemoveFavoriteCommand = new RelayCommand(param => RemoveFavorite(param as FavoriteViewModel));
            NavigateToFavoriteCommand = new RelayCommand(param => NavigateToFavorite(param as FavoriteViewModel));
            ImportFromEdgeCommand = new RelayCommand(_ => ImportFromEdge());
            ExportFavoritesCommand = new RelayCommand(_ => ExportFavorites());
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
            NavigateToHistoryCommand = new RelayCommand(param => NavigateToHistory(param as HistoryItemViewModel));

            LoadFavorites();
        }

        public ObservableCollection<FavoriteViewModel> Favorites { get; } = new();
        public ObservableCollection<string> Folders { get; } = new();
        public ObservableCollection<HistoryItemViewModel> History => _history;

        // Events for WebView2 control actions
        public event Action? GoBack;
        public event Action? GoForward;
        public event Action? Refresh;
        public event Action? Stop;
        public event Action<string>? NavigateToUrlRequested;

        private string _url = "https://www.google.com";
        public string Url
        {
            get => _url;
            set
            {
                SetProperty(ref _url, value);
                ((RelayCommand)NavigateCommand).RaiseCanExecuteChanged();
            }
        }

        private string _currentUrl = string.Empty;
        public string CurrentUrl
        {
            get => _currentUrl;
            set
            {
                SetProperty(ref _currentUrl, value);
                Url = value;
                ((RelayCommand)AddFavoriteCommand).RaiseCanExecuteChanged();
                UpdateIsFavorite();
            }
        }

        private string _pageTitle = "New Tab";
        public string PageTitle { get => _pageTitle; set => SetProperty(ref _pageTitle, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private bool _canGoBack;
        public bool CanGoBack
        {
            get => _canGoBack;
            set
            {
                SetProperty(ref _canGoBack, value);
                ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
            }
        }

        private bool _canGoForward;
        public bool CanGoForward
        {
            get => _canGoForward;
            set
            {
                SetProperty(ref _canGoForward, value);
                ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
            }
        }

        private string _homePage = "https://www.google.com";
        public string HomePage { get => _homePage; set => SetProperty(ref _homePage, value); }

        private bool _isFavorite;
        public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _showFavoritesPanel;
        public bool ShowFavoritesPanel { get => _showFavoritesPanel; set => SetProperty(ref _showFavoritesPanel, value); }

        private bool _showHistoryPanel;
        public bool ShowHistoryPanel { get => _showHistoryPanel; set => SetProperty(ref _showHistoryPanel, value); }

        private string _selectedFolder = "Favorites";
        public string SelectedFolder
        {
            get => _selectedFolder;
            set => SetProperty(ref _selectedFolder, value);
        }

        public ICommand NavigateCommand { get; }
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand GoHomeCommand { get; }
        public ICommand AddFavoriteCommand { get; }
        public ICommand RemoveFavoriteCommand { get; }
        public ICommand NavigateToFavoriteCommand { get; }
        public ICommand ImportFromEdgeCommand { get; }
        public ICommand ExportFavoritesCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand NavigateToHistoryCommand { get; }

        public void Navigate()
        {
            if (string.IsNullOrWhiteSpace(Url)) return;

            var url = Url.Trim();

            // Add protocol if missing
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Check if it looks like a URL or a search query
                if (url.Contains('.') && !url.Contains(' '))
                {
                    url = "https://" + url;
                }
                else
                {
                    // Treat as search query
                    url = $"https://www.google.com/search?q={Uri.EscapeDataString(url)}";
                }
            }

            NavigateToUrl(url);
        }

        public void NavigateToUrl(string url)
        {
            Url = url;
            NavigateToUrlRequested?.Invoke(url);
        }

        /// <summary>
        /// Called when navigation completes - add to history.
        /// </summary>
        public void OnNavigationCompleted(string url, string title)
        {
            CurrentUrl = url;
            PageTitle = title;
            IsLoading = false;

            // Add to history (limit size for memory efficiency)
            _history.Insert(0, new HistoryItemViewModel
            {
                Title = title,
                Url = url,
                Visited = DateTime.Now
            });

            // Keep only last 100 items
            while (_history.Count > 100)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            StatusMessage = "Done";
        }

        public void OnNavigationStarted()
        {
            IsLoading = true;
            StatusMessage = "Loading...";
        }

        private void LoadFavorites()
        {
            Favorites.Clear();
            Folders.Clear();

            var favorites = _browserService.GetFavorites();
            foreach (var fav in favorites)
            {
                Favorites.Add(new FavoriteViewModel
                {
                    Id = fav.Id,
                    Title = fav.Title,
                    Url = fav.Url,
                    Folder = fav.Folder,
                    DateAdded = fav.DateAdded
                });
            }

            var folders = _browserService.GetFolders();
            foreach (var folder in folders)
            {
                Folders.Add(folder);
            }

            UpdateIsFavorite();
        }

        private void AddFavorite()
        {
            if (string.IsNullOrWhiteSpace(CurrentUrl)) return;

            var title = string.IsNullOrWhiteSpace(PageTitle) ? CurrentUrl : PageTitle;
            _browserService.AddFavorite(title, CurrentUrl, SelectedFolder);
            LoadFavorites();
            StatusMessage = "Added to favorites";
        }

        private void RemoveFavorite(FavoriteViewModel? favorite)
        {
            if (favorite == null) return;

            _browserService.RemoveFavorite(favorite.Id);
            LoadFavorites();
            StatusMessage = "Removed from favorites";
        }

        private void NavigateToFavorite(FavoriteViewModel? favorite)
        {
            if (favorite == null) return;
            NavigateToUrl(favorite.Url);
        }

        private void NavigateToHistory(HistoryItemViewModel? item)
        {
            if (item == null) return;
            NavigateToUrl(item.Url);
        }

        private void UpdateIsFavorite()
        {
            IsFavorite = Favorites.Any(f => f.Url.Equals(CurrentUrl, StringComparison.OrdinalIgnoreCase));
        }

        private void ImportFromEdge()
        {
            var count = _browserService.ImportFromEdge();
            LoadFavorites();
            StatusMessage = count > 0 ? $"Imported {count} bookmarks from Edge" : "No bookmarks found in Edge";
        }

        private void ExportFavorites()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "HTML Bookmarks|*.html",
                FileName = "PlatypusTools_Bookmarks.html",
                Title = "Export Favorites"
            };

            if (dialog.ShowDialog() == true)
            {
                _browserService.ExportToHtml(dialog.FileName);
                StatusMessage = "Favorites exported";
            }
        }

        private void ClearHistory()
        {
            _history.Clear();
            StatusMessage = "History cleared";
        }
    }
}
