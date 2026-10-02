using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GitSparseManager.Models;
using GitSparseManager.Services;
using GitSparseManager.ViewModels;

namespace GitSparseManager
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;

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

            _viewModel = new MainViewModel(hostFactory, commandGen, settingsService, clipboard, dialogs, presetService, gitService, remoteTree, checkoutService, submoduleService);
            DataContext = _viewModel;

            // PasswordBox cannot bind via XAML – mirror the VM's Token manually
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (!string.IsNullOrEmpty(_viewModel.Token))
                TokenBox.Password = _viewModel.Token;
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
