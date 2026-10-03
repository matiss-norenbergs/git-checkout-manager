using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GitSparseManager.ViewModels;

namespace GitSparseManager.Views
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
