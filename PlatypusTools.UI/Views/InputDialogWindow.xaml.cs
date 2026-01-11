using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class InputDialogWindow : Window
    {
        public InputDialogWindow(string message, string defaultText = "")
        {
            InitializeComponent();
            MessageText.Text = message;
            InputBox.Text = defaultText;
            Loaded += (s, e) => InputBox.Focus();
        }

        public string EnteredText => InputBox?.Text ?? string.Empty;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; Close();
        }
    }
}