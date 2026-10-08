using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GitCheckoutManager.Controls
{
    /// <summary>Visible when every bound value is true, otherwise Collapsed (AND of several flags).</summary>
    public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
