using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.ViewModels
{
    /// <summary>One row of the Local changes window.</summary>
    public sealed record LocalChangeRow(string Path, string Label, string? Detail, string ToolTip);

    /// <summary>One non-empty group of the Local changes window.</summary>
    public sealed record LocalChangeGroupView(string Title, int Total, IReadOnlyList<LocalChangeRow> Rows)
    {
        public string Header => $"{Title} ({Total})";
    }

    /// <summary>
    /// Read-only list of a checkout's local changes. The window shows the snapshot it was given, and
    /// <see cref="RefreshCommand"/> re-runs <c>git status</c> (cancellable; <see cref="Cancel"/> on close).
    /// </summary>
    public partial class LocalChangesViewModel : ObservableObject
    {
        /// <summary>More files than this are summarised as "…and N more" instead of being listed.</summary>
        public const int MaxRows = 2000;

        private readonly string _root;
        private readonly Func<CancellationToken, Task<LocalChangeSet>> _load;
        private readonly Action<LocalChangeSet>? _onLoaded;
        private CancellationTokenSource? _cts;

        [ObservableProperty] private string _headerText = string.Empty;
        [ObservableProperty] private string _moreText = string.Empty;
        [ObservableProperty] private string _errorText = string.Empty;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private ObservableCollection<LocalChangeGroupView> _groups = new();

        public bool HasMore => MoreText.Length > 0;
        public bool HasError => ErrorText.Length > 0;
        public bool IsEmpty => Groups.Count == 0;

        public LocalChangesViewModel(string root, LocalChangeSet initial,
            Func<CancellationToken, Task<LocalChangeSet>> load, Action<LocalChangeSet>? onLoaded = null)
        {
            _root = root;
            _load = load;
            _onLoaded = onLoaded;
            Show(initial);
        }

        partial void OnMoreTextChanged(string value) => OnPropertyChanged(nameof(HasMore));
        partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

        [RelayCommand]
        private async Task RefreshAsync()
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();

            IsLoading = true;
            ErrorText = string.Empty;
            try
            {
                var set = await _load(cts.Token);
                if (cts.IsCancellationRequested) return;

                Show(set);
                _onLoaded?.Invoke(set);
            }
            catch (OperationCanceledException)
            {
                // Closed or superseded by a newer refresh.
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                    ErrorText = $"Could not read the local changes: {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(_cts, cts)) IsLoading = false;
            }
        }

        /// <summary>Cancels a running status; called when the window closes.</summary>
        public void Cancel() => _cts?.Cancel();

        private void Show(LocalChangeSet set)
        {
            HeaderText = set.SummaryText;
            var (groups, more) = BuildGroups(_root, set, MaxRows);
            Groups = new ObservableCollection<LocalChangeGroupView>(groups);
            MoreText = more > 0 ? $"…and {more:N0} more" : string.Empty;
            OnPropertyChanged(nameof(IsEmpty));
        }

        /// <summary>Groups the set in display order, keeping at most <paramref name="max"/> rows in total; returns how many were left out.</summary>
        public static (List<LocalChangeGroupView> Groups, int More) BuildGroups(string root, LocalChangeSet set, int max)
        {
            var groups = new List<LocalChangeGroupView>();
            var remaining = max;

            foreach (var group in Enum.GetValues<LocalChangeGroup>())
            {
                var changes = set.Changes.Where(c => c.Group == group).ToList();
                if (changes.Count == 0) continue;

                // The full count stays in the title, even if only some rows fit.
                var rows = changes.Take(Math.Max(remaining, 0)).Select(c => ToRow(root, c)).ToList();
                remaining -= rows.Count;
                if (rows.Count > 0)
                    groups.Add(new LocalChangeGroupView(Title(group), changes.Count, rows));
            }

            return (groups, set.Count - (max - Math.Max(remaining, 0)));
        }

        private static LocalChangeRow ToRow(string root, LocalChange c)
        {
            var full = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(root, c.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            var detail = c.OriginalPath != null ? $"from {c.OriginalPath}" : c.Hint;

            var tip = c.OriginalPath != null ? $"{full}\nRenamed from {c.OriginalPath}" : full;
            if (c.Hint != null) tip += $"\n{c.Hint}";
            return new LocalChangeRow(c.Path, c.Label, detail, tip);
        }

        private static string Title(LocalChangeGroup g) => g.ToString();
    }
}
