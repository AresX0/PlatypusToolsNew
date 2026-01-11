using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class ScriptOutputWindow : Window
    {
        public ScriptOutputWindow(string output)
        {
            InitializeComponent();
            this.DataContext = output;
        }
    }
}