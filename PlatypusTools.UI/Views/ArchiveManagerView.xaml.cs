using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Archive Manager view for browsing, extracting, and creating archives.
    /// </summary>
    public partial class ArchiveManagerView : UserControl
    {
        public ArchiveManagerView()
        {
            InitializeComponent();
        }

        private void OnOpenArchiveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.OpenArchive();
                RefreshUI();
            }
        }

        private void OnCreateArchiveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.CreateArchive();
            }
        }

        private async void OnExtractAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                await vm.ExtractAllAsync();
            }
        }

        private async void OnExtractSelectedClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                await vm.ExtractSelectedAsync();
            }
        }

        private void OnAddFilesClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.AddFiles();
                RefreshUI();
            }
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                await vm.TestArchiveAsync();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.Cancel();
            }
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.SelectAll();
                RefreshUI();
            }
        }

        private void OnSelectNoneClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                vm.SelectNone();
                RefreshUI();
            }
        }

        private void RefreshUI()
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm)
            {
                EntriesDataGrid.ItemsSource = null;
                EntriesDataGrid.ItemsSource = vm.Entries;
                SelectionInfoText.Text = $"Selected: {vm.SelectedEntries.Count} of {vm.Entries.Count}";
            }
        }
        
        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ArchiveManagerViewModel vm && sender is PasswordBox pb)
            {
                vm.Password = pb.Password;
            }
        }
    }
}
