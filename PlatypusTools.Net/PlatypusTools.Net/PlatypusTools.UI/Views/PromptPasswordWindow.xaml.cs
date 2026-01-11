using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class PromptPasswordWindow : Window
    {
        public PromptPasswordWindow(string message = "Enter password:")
        {
            InitializeComponent();
            MessageText.Text = message;
        }

        public string EnteredPassword => PwdBox?.Password ?? string.Empty;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}