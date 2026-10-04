using System.Windows;
using System.Windows.Controls;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal remote-branch picker. <see cref="ChosenBranch"/> is set when the user clicks Switch.</summary>
    public partial class SubmoduleBranchWindow : Window
    {
        private sealed record BranchItem(string Name, bool IsCurrent)
        {
            public string Label => IsCurrent ? $"{Name}  (current)" : Name;
            public FontWeight Weight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private readonly SubmoduleBranchModel _model;
        private readonly CancellationTokenSource _cts = new();
        private List<BranchItem> _items = new();

        public string? ChosenBranch { get; private set; }

        public SubmoduleBranchWindow(SubmoduleBranchModel model)
        {
            InitializeComponent();
            _model = model;

            HeaderText.Text = $"Switch the branch of {model.DisplayPath}";
            CurrentText.Text = model.CurrentBranch is { Length: > 0 }
                ? $"Currently on {model.CurrentBranch}"
                : "Currently detached (not on a branch)";

            StatusText.Text = "Loading branches…";
            Loaded += async (_, _) => await LoadAsync();
            // Closing while git ls-remote runs just cancels the load.
            Closed += (_, _) => _cts.Cancel();
        }

        private async Task LoadAsync()
        {
            try
            {
                var names = await _model.LoadBranchesAsync(_cts.Token);
                _items = names.Select(n => new BranchItem(n, n == _model.CurrentBranch)).ToList();
                StatusText.Text = _items.Count == 0 ? "The remote has no branches." : string.Empty;
                FilterBox.IsEnabled = _items.Count > 0;
                ApplyFilter();
                FilterBox.Focus();

                if (_items.FirstOrDefault(i => i.IsCurrent) is { } current) BranchList.ScrollIntoView(current);
            }
            catch (OperationCanceledException)
            {
                // window is closing
            }
            catch (Exception ex)
            {
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
                StatusText.Text = ex.Message;
            }
        }

        private void ApplyFilter()
        {
            var text = FilterBox.Text.Trim();
            BranchList.ItemsSource = _items
                .Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        /// <summary>Switching to the branch that is already checked out would do nothing, so it stays disabled.</summary>
        private void BranchList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            SwitchButton.IsEnabled = BranchList.SelectedItem is BranchItem { IsCurrent: false };

        private void BranchList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SwitchButton.IsEnabled) SwitchButton_Click(sender, e);
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
            if (BranchList.SelectedItem is not BranchItem { IsCurrent: false } item) return;
            ChosenBranch = item.Name;
            DialogResult = true;
        }
    }
}
