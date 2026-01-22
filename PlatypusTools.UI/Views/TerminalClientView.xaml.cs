using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class TerminalClientView : UserControl
    {
        public TerminalClientView()
        {
            InitializeComponent();
            
            // Auto-scroll terminal output
            if (DataContext is ViewModels.TerminalClientViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(vm.OutputText))
                    {
                        OutputScroller.ScrollToEnd();
                    }
                };
            }

            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ViewModels.TerminalClientViewModel newVm)
                {
                    newVm.PropertyChanged += (s2, e2) =>
                    {
                        if (e2.PropertyName == nameof(newVm.OutputText))
                        {
                            OutputScroller.ScrollToEnd();
                        }
                    };
                }
            };
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.TerminalClientViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }
    }
}
