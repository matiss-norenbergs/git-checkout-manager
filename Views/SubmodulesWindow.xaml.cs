using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GitSparseManager.ViewModels;

namespace GitSparseManager.Views
{
    /// <summary>Modal, read-only list of a checkout's submodules. Bound to a <see cref="SubmodulesViewModel"/> by its caller.</summary>
    public partial class SubmodulesWindow : Window
    {
        public SubmodulesWindow()
        {
            InitializeComponent();
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
