using System.Windows;
using PlatypusTools.UI.ViewModels;
using WinForms = System.Windows.Forms;

namespace PlatypusTools.UI.Views
{
    public partial class HiderEditWindow : Window
    {
        public HiderEditWindow()
        {
            InitializeComponent();
            Loaded += HiderEditWindow_Loaded;
        }

        private void HiderEditWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is HiderEditViewModel vm && PwdBox != null)
            {
                // Bind PasswordBox manually (PasswordBox doesn't support two-way binding to plain property)
                PwdBox.Password = vm.Password ?? string.Empty;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is HiderEditViewModel vm && PwdBox != null)
            {
                vm.Password = PwdBox.Password;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DialogResult == true && DataContext is HiderEditViewModel vm && PwdBox != null)
            {
                vm.Password = PwdBox.Password;
            }
            base.OnClosed(e);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.ShowNewFolderButton = true;
                if (DataContext is HiderEditViewModel vm)
                {
                    if (!string.IsNullOrWhiteSpace(vm.FolderPath)) dlg.SelectedPath = vm.FolderPath;
                }
                var res = dlg.ShowDialog();
                if (res == WinForms.DialogResult.OK && DataContext is HiderEditViewModel vm2)
                {
                    vm2.FolderPath = dlg.SelectedPath;
                }
            }
        }
    }
}