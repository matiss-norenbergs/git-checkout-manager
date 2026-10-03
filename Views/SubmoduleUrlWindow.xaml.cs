using System.Windows;
using System.Windows.Controls;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Views
{
    /// <summary>Modal repository picker. <see cref="ChosenUrl"/> is set once the URL passed its reachability check.</summary>
    public partial class SubmoduleUrlWindow : Window
    {
        private readonly SubmoduleUrlModel _model;
        private readonly CancellationTokenSource _cts = new();
        private bool _testing;

        public string? ChosenUrl { get; private set; }

        public SubmoduleUrlWindow(SubmoduleUrlModel model)
        {
            InitializeComponent();
            _model = model;

            HeaderText.Text = $"Choose the repository for {model.Path}";
            CurrentUrlText.Text = model.GitmodulesUrl is { Length: > 0 }
                ? $".gitmodules: {model.GitmodulesUrl}"
                : string.Empty;
            CurrentUrlText.Visibility = CurrentUrlText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            var hasRepos = model.Repositories.Count > 0;
            FilterBox.Visibility = hasRepos ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = hasRepos ? Visibility.Collapsed : Visibility.Visible;
            RepoList.Visibility = hasRepos ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();

            Loaded += (_, _) => UrlBox.Focus();
            // Closing while git ls-remote runs just cancels the check.
            Closed += (_, _) => _cts.Cancel();
        }

        private void ApplyFilter()
        {
            var text = FilterBox.Text.Trim();
            RepoList.ItemsSource = _model.Repositories
                .Where(r => text.Length == 0 || r.PathWithNamespace.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.PathWithNamespace, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RepoList.SelectedItem is Repository repo) UrlBox.Text = repo.HttpUrlToRepo;
        }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UseButton.IsEnabled = !_testing && UrlBox.Text.Trim().Length > 0;
            StatusText.Text = string.Empty;
        }

        private async void UseButton_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (url.Length == 0) return;

            _testing = true;
            UseButton.IsEnabled = false;
            UrlBox.IsEnabled = false;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
            StatusText.Text = "Checking…";

            string? error;
            try
            {
                error = await _model.TestUrlAsync(url, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // window is closing
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                _testing = false;
                UrlBox.IsEnabled = true;
            }

            if (error == null)
            {
                ChosenUrl = url;
                DialogResult = true;
                return;
            }

            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusText.Text = error;
            UseButton.IsEnabled = true;
        }
    }
}
