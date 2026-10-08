using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal list of a checkout's submodules, with actions to initialize the selected ones. Bound to a <see cref="SubmodulesViewModel"/> by its caller.</summary>
    public partial class SubmodulesWindow : Window
    {
        public SubmodulesWindow()
        {
            InitializeComponent();
            WindowSizing.FitToWorkArea(this, 1400, 820);
            // Cancel comes first while git is running, so a half-finished update is never abandoned silently.
            Closing += (_, e) => e.Cancel = (DataContext as SubmodulesViewModel)?.IsRunning == true;
            Closed += (_, _) => (DataContext as SubmodulesViewModel)?.Cancel();
        }

        /// <summary>Ctrl+F focuses the filter box.</summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        /// <summary>
        /// Esc clears the filter when it has text. The window's Close button is IsCancel, so this must be handled
        /// here (marked handled) or the window would close; with an empty box Esc behaves as before.
        /// </summary>
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;
            SearchBox.Clear();
            e.Handled = true;
        }

        /// <summary>Keeps the header as wide as the rows: the gutter takes whatever the vertical scrollbar uses.</summary>
        private void RowsScroll_Changed(object sender, RoutedEventArgs e) =>
            HeaderGutter.Width = Math.Max(0, RowsScroll.ActualWidth - RowsScroll.ViewportWidth);

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

    /// <summary>
    /// Radio button helper: IsChecked is true when the bound enum equals the ConverterParameter. Unchecking
    /// is ignored (Binding.DoNothing) so the other radio's check is the only thing that changes the value.
    /// </summary>
    public sealed class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && parameter != null && value.Equals(parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true && parameter != null ? parameter : Binding.DoNothing;
    }
}
