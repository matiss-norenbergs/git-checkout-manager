using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GitCheckoutManager.Models;
using GitCheckoutManager.Services;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            Views.WindowSizing.FitToWorkArea(this, 1400, 820);
            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnWindowPreviewKeyDown;

            var hostFactory     = new GitHostServiceFactory();
            var commandGen      = new CommandGenerator();
            var settingsService = new SettingsService();
            var clipboard       = new ClipboardService();
            var dialogs         = new DialogService();
            var presetService   = new PresetService();
            var gitService      = new GitService();
            var remoteTree      = new RemoteTreeService(gitService);
            var checkoutService = new CheckoutService(gitService);
            var submoduleService = new SubmoduleService(gitService);
            var updateService   = new UpdateService();
            var shellLauncher   = new ShellLauncher();

            _viewModel = new MainViewModel(hostFactory, commandGen, settingsService, clipboard, dialogs, presetService, gitService, remoteTree, checkoutService, submoduleService, updateService, shellLauncher);
            DataContext = _viewModel;
            ApplySplitRatio(_viewModel.MainSplitRatio);
            Loaded += async (_, _) => await _viewModel.CheckForUpdatesOnStartupAsync();
            Closed += (_, _) => _viewModel.StopUpdateTimer();

            // PasswordBox cannot bind via XAML – mirror the VM's Token manually
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.MatchNavigated += OnMatchNavigated;
            _viewModel.TreeRefreshStarting += () => SaveTreeAnchor();
            _viewModel.TreeRefreshed += RestoreTreeScroll;
            if (!string.IsNullOrEmpty(_viewModel.Token))
                TokenBox.Password = _viewModel.Token;
        }

        /// <summary>Manage tab, folders only. The on-disk check runs here, each time the menu opens.</summary>
        private void TreeNodeMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TreeNodeViewModel { IsFolder: true } node } || !_viewModel.IsManageMode)
            {
                e.Handled = true; // no menu
                return;
            }
            _viewModel.RefreshNodeOnDisk(node);
        }

        private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F &&
                System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control &&
                TreeSearchBox.IsVisible)
            {
                TreeSearchBox.Focus();
                TreeSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void TreeSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            var backward = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
            if (backward) _viewModel.PreviousMatchCommand.Execute(null);
            else _viewModel.NextMatchCommand.Execute(null);
            e.Handled = true;
        }

        private string? _savedTreeAnchorPath;

        /// <summary>Remembers the folder or file shown at the top of the tree, so a refresh can scroll back to it.</summary>
        private void SaveTreeAnchor()
        {
            _savedTreeAnchorPath = null;
            if (FindDescendant<ScrollViewer>(MainTree) is not { VerticalOffset: > 0 }) return;

            // The row under a point just inside the top-left corner of the tree.
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(MainTree, new Point(40, 6));
            DependencyObject? visual = hit?.VisualHit;
            while (visual != null && visual is not TreeViewItem)
                visual = System.Windows.Media.VisualTreeHelper.GetParent(visual);

            if (visual is TreeViewItem { DataContext: TreeNodeViewModel node })
                _savedTreeAnchorPath = node.FullPath;
        }

        /// <summary>
        /// After a refresh rebuilt the tree: bring the remembered top row back into view, once the new items have
        /// been laid out. The scroll offset itself can't be reused because the virtualized tree only knows the
        /// size of the rows it has realized, so the same offset lands somewhere else.
        /// </summary>
        private void RestoreTreeScroll()
        {
            var path = _savedTreeAnchorPath;
            if (path == null) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                MainTree.UpdateLayout();
                var node = FindNodeByPath(_viewModel.TreeNodes, path);
                if (node != null) OnMatchNavigated(node);
            }));
        }

        private static TreeNodeViewModel? FindNodeByPath(IEnumerable<TreeNodeViewModel> roots, string path)
        {
            var stack = new Stack<TreeNodeViewModel>(roots);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.FullPath == path) return node;
                // Only a folder whose path is a prefix of the target can contain it.
                if (node.IsFolder && path.StartsWith(node.FullPath + "/", StringComparison.Ordinal))
                    foreach (var child in node.Children) stack.Push(child);
            }
            return null;
        }

        /// <summary>
        /// Brings a match into view. The tree is virtualized, so each level's container may not exist yet:
        /// scroll the parent's panel to the child's index first, then take the realized container.
        /// </summary>
        private void OnMatchNavigated(TreeNodeViewModel node)
        {
            var chain = new List<TreeNodeViewModel>();
            for (var n = node; n != null; n = n.Parent) chain.Insert(0, n);

            ItemsControl current = MainTree;
            TreeViewItem? container = null;
            foreach (var item in chain)
            {
                current.UpdateLayout();
                container = current.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container == null)
                {
                    var index = current.Items.IndexOf(item);
                    if (index >= 0 && FindItemsPanel(current) is VirtualizingStackPanel panel)
                    {
                        panel.BringIndexIntoViewPublic(index);
                        current.UpdateLayout();
                        container = current.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                    }
                }
                if (container == null) return;
                current = container;
            }

            container?.BringIntoView();
        }

        private static Panel? FindItemsPanel(ItemsControl control)
        {
            var presenter = FindDescendant<ItemsPresenter>(control);
            return presenter != null && System.Windows.Media.VisualTreeHelper.GetChildrenCount(presenter) > 0
                ? System.Windows.Media.VisualTreeHelper.GetChild(presenter, 0) as Panel
                : null;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainViewModel.Token)) return;

            if (TokenBox.Password != _viewModel.Token)
                TokenBox.Password = _viewModel.Token;
        }

        private void OnSourceInitialized(object? sender, System.EventArgs e)
        {
            var settings = new SettingsService().LoadSettings();
            var isDark = settings.ThemeMode == ThemeMode.Dark ||
                         (settings.ThemeMode == ThemeMode.System && App.IsDarkModeEnabled());
            TitleBarColorizer.Apply(this, isDark);
        }

        private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Token = TokenBox.Password;
        }

        private void PresetMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = PresetMenuButton.ContextMenu;
            if (menu == null) return;

            menu.DataContext = DataContext;
            menu.PlacementTarget = PresetMenuButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OpenMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button) return;

            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private const double MinSplitRatio = 0.4;
        private const double MaxSplitRatio = 0.8;

        private static double ClampRatio(double ratio) =>
            double.IsNaN(ratio) ? AppSettings.DefaultMainSplitRatio : Math.Clamp(ratio, MinSplitRatio, MaxSplitRatio);

        private void ApplySplitRatio(double ratio)
        {
            ratio = ClampRatio(ratio);
            TreeColumn.Width = new GridLength(ratio, GridUnitType.Star);
            RightColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
        }

        private void MainSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            var total = TreeColumn.ActualWidth + RightColumn.ActualWidth;
            if (total <= 0) return;

            var ratio = ClampRatio(TreeColumn.ActualWidth / total);
            ApplySplitRatio(ratio);
            _viewModel.SaveMainSplitRatio(ratio);
        }

        /// <summary>Sizes the right panel's script row: compact when collapsed, resizable when expanded.</summary>
        private void ScriptExpander_Changed(object sender, RoutedEventArgs e)
        {
            var expanded = ((Expander)sender).IsExpanded;

            ScriptSplitter.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ScriptRow.MinHeight = expanded ? 120 : 0;
            ScriptRow.Height = expanded ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;
        }

        private void CheckoutCombo_DropDownOpened(object sender, System.EventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.RefreshRecentCheckoutState();
        }

        private void CheckoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not RecentCheckout entry) return;

            // Let the ComboBox finish its selection cycle before a dialog or a selection reset runs.
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => vm.SelectRecentCheckoutCommand.Execute(entry)));
        }
    }
}
