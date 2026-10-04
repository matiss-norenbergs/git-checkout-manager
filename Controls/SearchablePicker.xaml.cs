using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GitCheckoutManager.Controls
{
    /// <summary>One row in the picker list: the original item plus its display text and the active filter.</summary>
    public sealed record PickerEntry(object Item, string Text, string Query);

    /// <summary>
    /// GitHub-style picker: a closed button showing the selection, and a popup with an empty search box
    /// and the full list. Nothing is selected until the user picks an entry.
    /// </summary>
    public partial class SearchablePicker : UserControl
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
            nameof(ItemsSource), typeof(IEnumerable), typeof(SearchablePicker));

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem), typeof(object), typeof(SearchablePicker),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((SearchablePicker)d).UpdateFace()));

        public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
            nameof(DisplayMemberPath), typeof(string), typeof(SearchablePicker),
            new PropertyMetadata(string.Empty, (d, _) => ((SearchablePicker)d).UpdateFace()));

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(SearchablePicker),
            new PropertyMetadata("Select…", (d, _) => ((SearchablePicker)d).UpdateFace()));

        public static readonly DependencyProperty FilterPlaceholderProperty = DependencyProperty.Register(
            nameof(FilterPlaceholder), typeof(string), typeof(SearchablePicker),
            new PropertyMetadata("Filter…"));

        public static readonly DependencyProperty FilterPredicateProperty = DependencyProperty.Register(
            nameof(FilterPredicate), typeof(Func<object, string, bool>), typeof(SearchablePicker));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        /// <summary>Property of each item to display; the item's ToString() when empty.</summary>
        public string DisplayMemberPath
        {
            get => (string)GetValue(DisplayMemberPathProperty);
            set => SetValue(DisplayMemberPathProperty, value);
        }

        /// <summary>Text on the closed control while nothing is selected.</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>Hint shown in the empty search box.</summary>
        public string FilterPlaceholder
        {
            get => (string)GetValue(FilterPlaceholderProperty);
            set => SetValue(FilterPlaceholderProperty, value);
        }

        /// <summary>(item, filter) => matches. Defaults to a case-insensitive substring match on the display text.</summary>
        public Func<object, string, bool>? FilterPredicate
        {
            get => (Func<object, string, bool>?)GetValue(FilterPredicateProperty);
            set => SetValue(FilterPredicateProperty, value);
        }

        private List<object> _all = new();

        public SearchablePicker()
        {
            InitializeComponent();
            UpdateFace();

            Loaded += (_, _) =>
            {
                if (Window.GetWindow(this) is { } owner) { owner.Deactivated -= OnOwnerDeactivated; owner.Deactivated += OnOwnerDeactivated; }
            };
            Unloaded += (_, _) =>
            {
                if (Window.GetWindow(this) is { } owner) owner.Deactivated -= OnOwnerDeactivated;
            };
        }

        // ── Closed state ──────────────────────────────────────────────────────

        private string TextOf(object? item)
        {
            if (item == null) return string.Empty;
            var path = DisplayMemberPath;
            if (!string.IsNullOrEmpty(path))
            {
                var value = item.GetType().GetProperty(path)?.GetValue(item);
                if (value != null) return value.ToString() ?? string.Empty;
            }
            return item.ToString() ?? string.Empty;
        }

        private void UpdateFace()
        {
            if (FaceText == null) return;

            if (SelectedItem == null)
            {
                FaceText.Text = Placeholder;
                FaceText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }
            else
            {
                FaceText.Text = TextOf(SelectedItem);
                FaceText.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundBrush");
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled || PART_Popup.IsOpen) return;

            // Enter/Space are handled by the toggle itself
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsEnabled && (key == Key.F4 || (key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)))
            {
                PART_Popup.IsOpen = true;
                e.Handled = true;
            }
        }

        protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusWithinChanged(e);
            if (IsEnabled)
                BorderBrush = (Brush?)TryFindResource(IsKeyboardFocusWithin || IsMouseOver ? "AccentBorderBrush" : "ControlBorderBrush");
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            BorderBrush = (Brush?)TryFindResource("AccentBorderBrush");
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!IsKeyboardFocusWithin)
                BorderBrush = (Brush?)TryFindResource("ControlBorderBrush");
        }

        // ── Popup ─────────────────────────────────────────────────────────────

        private void Close()
        {
            PART_Popup.IsOpen = false;
        }

        private void PickerPopup_Opened(object? sender, EventArgs e)
        {
            _all = ItemsSource?.Cast<object>().ToList() ?? new List<object>();

            PopupRoot.MinWidth = Math.Max(ActualWidth, 240);
            SearchHint.Text = FilterPlaceholder;
            SearchBox.Text = string.Empty;
            SearchHint.Visibility = Visibility.Visible;
            Rebuild(string.Empty, keepHighlightOn: SelectedItem);

            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                SearchBox.Text = string.Empty;
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                if (List.SelectedItem != null)
                    List.ScrollIntoView(List.SelectedItem);
            });
        }

        private void PickerPopup_Closed(object? sender, EventArgs e)
        {
            if (IsLoaded && IsVisible) FaceToggle.Focus();
        }

        private void OnOwnerDeactivated(object? sender, EventArgs e) => Close();

        private bool Matches(object item, string filter)
        {
            var predicate = FilterPredicate;
            return predicate != null
                ? predicate(item, filter)
                : PickerFilters.Contains(TextOf(item), filter);
        }

        private void Rebuild(string filter, object? keepHighlightOn)
        {
            var entries = new List<PickerEntry>();
            foreach (var item in _all)
            {
                if (filter.Length == 0 || Matches(item, filter))
                    entries.Add(new PickerEntry(item, TextOf(item), filter));
            }

            List.ItemsSource = entries;
            NoMatches.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            List.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            var highlight = keepHighlightOn == null
                ? null
                : entries.FirstOrDefault(en => ReferenceEquals(en.Item, keepHighlightOn) || Equals(en.Item, keepHighlightOn));
            List.SelectedItem = highlight ?? entries.FirstOrDefault();
            if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!PART_Popup.IsOpen) return;
            Rebuild(SearchBox.Text.Trim(), keepHighlightOn: null);
        }

        private void MoveHighlight(int delta)
        {
            var count = List.Items.Count;
            if (count == 0) return;
            var index = Math.Clamp(List.SelectedIndex < 0 ? (delta > 0 ? 0 : count - 1) : List.SelectedIndex + delta, 0, count - 1);
            List.SelectedIndex = index;
            List.ScrollIntoView(List.SelectedItem);
        }

        private void PopupRoot_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: MoveHighlight(1); e.Handled = true; break;
                case Key.Up: MoveHighlight(-1); e.Handled = true; break;
                case Key.PageDown: MoveHighlight(8); e.Handled = true; break;
                case Key.PageUp: MoveHighlight(-8); e.Handled = true; break;
                case Key.Enter:
                    if (List.SelectedItem is PickerEntry entry) Pick(entry);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(List, source) is ListBoxItem { DataContext: PickerEntry entry })
            {
                Pick(entry);
                e.Handled = true;
            }
        }

        private void Pick(PickerEntry entry)
        {
            SelectedItem = entry.Item;
            Close();
        }
    }
}

namespace GitCheckoutManager.Controls
{
    public sealed class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b && !b;
    }
}
