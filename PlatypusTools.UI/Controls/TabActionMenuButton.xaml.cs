using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services.TabConfig;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Phase 1.4 — small "⋯" button that opens an Export/Import/Reset menu for any tab
    /// whose DataContext implements <see cref="ITabConfigProvider"/>.
    /// Drop into any view header, no further wiring needed.
    /// </summary>
    public partial class TabActionMenuButton : UserControl
    {
        public static readonly DependencyProperty ProviderProperty =
            DependencyProperty.Register(nameof(Provider), typeof(ITabConfigProvider),
                typeof(TabActionMenuButton), new PropertyMetadata(null));

        public ITabConfigProvider? Provider
        {
            get => (ITabConfigProvider?)GetValue(ProviderProperty);
            set => SetValue(ProviderProperty, value);
        }

        public TabActionMenuButton() { InitializeComponent(); }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var provider = Provider ?? DataContext as ITabConfigProvider;
            if (provider == null)
            {
                MessageBox.Show("This tab does not yet support config import/export.",
                    "Tab Config", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu { PlacementTarget = MenuButton, IsOpen = true };

            var export = new MenuItem { Header = "💾  Export config…" };
            export.Click += (_, __) => TabConfigService.Export(provider);
            menu.Items.Add(export);

            var import = new MenuItem { Header = "📥  Import config…" };
            import.Click += (_, __) => TabConfigService.Import(provider);
            menu.Items.Add(import);

            menu.Items.Add(new Separator());

            var reset = new MenuItem { Header = "🔄  Reset to defaults" };
            reset.Click += (_, __) => TabConfigService.Reset(provider);
            menu.Items.Add(reset);
        }
    }
}
