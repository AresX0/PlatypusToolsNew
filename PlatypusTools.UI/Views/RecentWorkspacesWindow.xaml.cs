using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    public partial class RecentWorkspacesWindow : Window
    {
        private readonly RecentWorkspacesService _service;
        
        public string? SelectedPath { get; private set; }
        public bool IsFile { get; private set; }
        
        public RecentWorkspacesWindow()
        {
            InitializeComponent();
            
            _service = RecentWorkspacesService.Instance;
            RefreshLists();
        }
        
        private void RefreshLists()
        {
            WorkspacesList.ItemsSource = _service.RecentWorkspaces;
            FilesList.ItemsSource = _service.RecentFiles;
            PinnedList.ItemsSource = _service.PinnedPaths;
        }
        
        private void OnWorkspaceSearchChanged(object sender, TextChangedEventArgs e)
        {
            var filter = WorkspaceSearchBox.Text?.ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(filter))
            {
                WorkspacesList.ItemsSource = _service.RecentWorkspaces;
            }
            else
            {
                WorkspacesList.ItemsSource = _service.RecentWorkspaces
                    .Where(w => w.Name.ToLowerInvariant().Contains(filter) || 
                               w.Path.ToLowerInvariant().Contains(filter));
            }
        }
        
        private void OnClearWorkspaceSearch(object sender, RoutedEventArgs e)
        {
            WorkspaceSearchBox.Text = "";
        }
        
        private void OnFileSearchChanged(object sender, TextChangedEventArgs e)
        {
            var filter = FileSearchBox.Text?.ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(filter))
            {
                FilesList.ItemsSource = _service.RecentFiles;
            }
            else
            {
                FilesList.ItemsSource = _service.RecentFiles
                    .Where(f => f.Name.ToLowerInvariant().Contains(filter) || 
                               f.Path.ToLowerInvariant().Contains(filter));
            }
        }
        
        private void OnClearFileSearch(object sender, RoutedEventArgs e)
        {
            FileSearchBox.Text = "";
        }
        
        private void OnWorkspaceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WorkspacesList.SelectedItem is RecentWorkspace ws)
            {
                SelectedPath = ws.Path;
                IsFile = false;
                DialogResult = true;
                Close();
            }
        }
        
        private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FilesList.SelectedItem is RecentFile file)
            {
                SelectedPath = file.Path;
                IsFile = true;
                DialogResult = true;
                Close();
            }
        }
        
        private void OnPinnedDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PinnedList.SelectedItem is string path)
            {
                SelectedPath = path;
                IsFile = File.Exists(path);
                DialogResult = true;
                Close();
            }
        }
        
        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            var tab = MainTabs.SelectedIndex;
            
            switch (tab)
            {
                case 0 when WorkspacesList.SelectedItem is RecentWorkspace ws:
                    SelectedPath = ws.Path;
                    IsFile = false;
                    DialogResult = true;
                    Close();
                    break;
                    
                case 1 when FilesList.SelectedItem is RecentFile file:
                    SelectedPath = file.Path;
                    IsFile = true;
                    DialogResult = true;
                    Close();
                    break;
                    
                case 2 when PinnedList.SelectedItem is string path:
                    SelectedPath = path;
                    IsFile = File.Exists(path);
                    DialogResult = true;
                    Close();
                    break;
            }
        }
        
        private void OnPinClick(object sender, RoutedEventArgs e)
        {
            string? pathToPin = null;
            
            if (MainTabs.SelectedIndex == 0 && WorkspacesList.SelectedItem is RecentWorkspace ws)
            {
                pathToPin = ws.Path;
            }
            else if (MainTabs.SelectedIndex == 1 && FilesList.SelectedItem is RecentFile file)
            {
                pathToPin = file.Path;
            }
            
            if (!string.IsNullOrEmpty(pathToPin))
            {
                _service.PinPath(pathToPin);
                RefreshLists();
            }
        }
        
        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (MainTabs.SelectedIndex == 0 && WorkspacesList.SelectedItem is RecentWorkspace ws)
            {
                _service.RemoveWorkspace(ws.Path);
                RefreshLists();
            }
            else if (MainTabs.SelectedIndex == 1 && FilesList.SelectedItem is RecentFile file)
            {
                _service.RemoveFile(file.Path);
                RefreshLists();
            }
            else if (MainTabs.SelectedIndex == 2 && PinnedList.SelectedItem is string path)
            {
                _service.UnpinPath(path);
                RefreshLists();
            }
        }
        
        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all recent items?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                _service.ClearAll();
                RefreshLists();
            }
        }
        
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}