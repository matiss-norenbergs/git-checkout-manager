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

        /// <summary>Which button closed a three-way dialog (<see cref="ThreeWayChoice.Cancel"/> for Cancel/Esc/close).</summary>
        public Services.ThreeWayChoice Choice { get; private set; } = Services.ThreeWayChoice.Cancel;

        private bool _threeWay;

        /// <summary>Turns the dialog into "primary / secondary / Cancel".</summary>
        public void UseThreeWay(string primary, string? secondary)
        {
            _threeWay = true;
            PrimaryButton.Content = primary;
            if (secondary != null)
            {
                MiddleButton.Content = secondary;
                MiddleButton.Style = (Style)FindResource("ActionButton");
                MiddleButton.Visibility = Visibility.Visible;
            }
            SecondaryButton.Content = "Cancel";
            SecondaryButton.Visibility = Visibility.Visible;
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_threeWay) Choice = Services.ThreeWayChoice.Primary;
            DialogResult = true;
        }

        private void MiddleButton_Click(object sender, RoutedEventArgs e)
        {
            Choice = Services.ThreeWayChoice.Secondary;
            DialogResult = true;
        }
    }
}
