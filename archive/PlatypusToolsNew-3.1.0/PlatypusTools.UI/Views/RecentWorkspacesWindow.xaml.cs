using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class RecentWorkspacesWindow : Window
    {
        public string? SelectedWorkspace { get; private set; }
        public RecentWorkspacesWindow()
        {
            InitializeComponent();
            var items = PlatypusTools.UI.Services.WorkspaceManager.LoadRecent();
            foreach (var i in items) List.Items.Add(i);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem == null) return;
            SelectedWorkspace = List.SelectedItem.ToString();
            DialogResult = true;
            Close();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem == null) return;
            var sel = List.SelectedItem.ToString();
            if (sel == null) return;
            var items = PlatypusTools.UI.Services.WorkspaceManager.LoadRecent().Where(x => x != sel);
            PlatypusTools.UI.Services.WorkspaceManager.SaveRecent(items);
            List.Items.Remove(List.SelectedItem);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}