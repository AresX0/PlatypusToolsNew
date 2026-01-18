using System.Windows.Controls;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Status bar control displaying operation progress, elapsed time, and cancel button.
    /// Uses singleton StatusBarViewModel for global status updates.
    /// </summary>
    public partial class StatusBarControl : UserControl
    {
        public StatusBarControl()
        {
            InitializeComponent();
        }
    }
}
