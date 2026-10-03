using System.Windows;

namespace GitSparseManager.Views
{
    /// <summary>Modal window for app-wide settings. Bound to a <c>SettingsViewModel</c> by its caller.</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }
    }
}
