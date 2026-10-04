using System.Windows;

namespace GitCheckoutManager.Views
{
    /// <summary>Themed replacement for MessageBox: OK, or Yes/No (<see cref="Window.DialogResult"/> true = Yes/OK).</summary>
    public partial class MessageWindow : Window
    {
        public MessageWindow(string title, string message, string? details, bool confirm, bool destructive)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;

            if (!string.IsNullOrEmpty(details))
            {
                DetailsBox.Text = details;
                DetailsBox.Visibility = Visibility.Visible;
            }

            PrimaryButton.Content = confirm ? "Yes" : "OK";
            PrimaryButton.Style = (Style)FindResource(destructive ? "DangerActionButton" : "AccentActionButton");
            SecondaryButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;

            // The No button is collapsed for OK-only dialogs, so Esc is handled here to close them too.
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                DialogResult = false;
                e.Handled = true;
            };
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
