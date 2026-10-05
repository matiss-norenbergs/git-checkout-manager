using System.Windows;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal, read-only list of a checkout's local changes. Bound to a <see cref="LocalChangesViewModel"/> by its caller.</summary>
    public partial class LocalChangesWindow : Window
    {
        public LocalChangesWindow()
        {
            InitializeComponent();
            WindowSizing.FitToWorkArea(this, 820, 620);
            // Closing the window stops a running git status.
            Closed += (_, _) => (DataContext as LocalChangesViewModel)?.Cancel();
        }
    }
}
