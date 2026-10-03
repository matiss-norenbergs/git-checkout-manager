using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal list of a checkout's submodules, with actions to initialize the selected ones. Bound to a <see cref="SubmodulesViewModel"/> by its caller.</summary>
    public partial class SubmodulesWindow : Window
    {
        public SubmodulesWindow()
        {
            InitializeComponent();
            // Cancel comes first while git is running, so a half-finished update is never abandoned silently.
            Closing += (_, e) => e.Cancel = (DataContext as SubmodulesViewModel)?.IsRunning == true;
            Closed += (_, _) => (DataContext as SubmodulesViewModel)?.Cancel();
        }

        private void ActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button) return;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Turns a theme brush key such as "SuccessBrush" into that brush from the active theme.</summary>
    public sealed class ResourceBrushConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string key ? Application.Current.TryFindResource(key) : null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
