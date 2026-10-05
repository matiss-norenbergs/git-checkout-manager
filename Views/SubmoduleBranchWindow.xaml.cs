using System.Windows;
using System.Windows.Controls;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal remote-branch picker. <see cref="ChosenBranch"/> is set when the user clicks Switch.</summary>
    public partial class SubmoduleBranchWindow : Window
    {
        /// <summary>
        /// <paramref name="IsCurrent"/>: nothing to switch (the one row, or every target row, is on it already).
        /// <paramref name="Note"/>: multi mode counts, e.g. "on 3/5" or "2 already on it".
        /// </summary>
        private sealed record BranchItem(string Name, bool IsCurrent, string Note = "", bool IsMissingSomewhere = false)
        {
            public string Label => IsCurrent ? $"{Name}  (current)" : Note.Length > 0 ? $"{Name}  ({Note})" : Name;
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

            if (model.IsMulti)
            {
                Title = "Switch branch";
                HeaderText.Text = $"Switch {model.RowCount} submodules to a branch";
                CurrentText.Visibility = Visibility.Collapsed;
                HintText.Text = "Submodules already on the chosen branch are fast-forwarded to its tip. " +
                                "Submodules whose remote lacks it are skipped.";
                HintText.Visibility = Visibility.Visible;
                ShowMissingCheck.Visibility = Visibility.Visible;
            }
            else
            {
                HeaderText.Text = $"Switch the branch of {model.DisplayPath}";
                CurrentText.Text = model.CurrentBranch is { Length: > 0 }
                    ? $"Currently on {model.CurrentBranch}"
                    : "Currently detached (not on a branch)";
            }

            StatusText.Text = "Loading branches…";
            Loaded += async (_, _) => await LoadAsync();
            // Closing while git ls-remote runs just cancels the load.
            Closed += (_, _) => _cts.Cancel();
        }

        private async Task LoadAsync()
        {
            try
            {
                if (_model.IsMulti && _model.LoadBranchOptionsAsync is { } loadOptions)
                {
                    var options = await loadOptions(_cts.Token);
                    _items = options.Select(o => ToItem(o, _model.RowCount)).ToList();
                }
                else
                {
                    var names = await _model.LoadBranchesAsync(_cts.Token);
                    _items = names.Select(n => new BranchItem(n, n == _model.CurrentBranch)).ToList();
                }
                StatusText.Text = _items.Count == 0 ? "The remote has no branches."
                    : _items.All(i => i.IsMissingSomewhere) ? "No branch exists on every submodule's remote. Tick the box to see the rest."
                    : string.Empty;
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

        /// <summary>Disabled only when every target row is on the branch already.</summary>
        private static BranchItem ToItem(BranchOption o, int rows)
        {
            var notes = new List<string>();
            if (o.OnCount < rows) notes.Add($"on {o.OnCount}/{rows}");
            if (o.CurrentCount > 0 && o.CurrentCount < rows) notes.Add($"{o.CurrentCount} already on it");
            return new BranchItem(o.Name, o.CurrentCount == rows, string.Join(", ", notes), o.OnCount < rows);
        }

        private void ApplyFilter()
        {
            var text = FilterBox.Text.Trim();
            // By default only branches every submodule's remote has; the checkbox adds the rest.
            var showMissing = ShowMissingCheck.IsChecked == true;
            BranchList.ItemsSource = _items
                .Where(i => showMissing || !i.IsMissingSomewhere)
                .Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void ShowMissingCheck_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

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
