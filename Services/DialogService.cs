using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;
using GitCheckoutManager.Views;

namespace GitCheckoutManager.Services
{
    public class DialogService : IDialogService
    {
        public string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExtension,
                FileName = defaultFileName
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? ShowOpenFileDialog(string filter, string title = "Open File")
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                Title  = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (!string.IsNullOrWhiteSpace(initialPath))
                dialog.InitialDirectory = initialPath;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        public bool ShowConfirmation(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            == MessageBoxResult.Yes;

        public RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model)
        {
            var window = new RemovalReviewWindow(model) { Owner = Application.Current?.MainWindow };
            return window.ShowDialog() == true ? window.Choices : null;
        }

        public void ShowSettings(SettingsViewModel viewModel)
        {
            var window = new SettingsWindow { DataContext = viewModel, Owner = Application.Current?.MainWindow };
            window.ShowDialog();
        }

        public void ShowSubmodules(SubmodulesViewModel vm)
        {
            var window = new SubmodulesWindow { DataContext = vm, Owner = Application.Current?.MainWindow };
            window.ShowDialog();
        }

        public string? ShowSubmoduleUrl(SubmoduleUrlModel model)
        {
            // Owner is the Submodules window that is currently active, falling back to the main window.
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current?.MainWindow;
            var window = new SubmoduleUrlWindow(model) { Owner = owner };
            return window.ShowDialog() == true ? window.ChosenUrl : null;
        }

        public string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(4),
                MinWidth = 280
            };

            string? result = null;

            var okButton = new Button { Content = "OK", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 72, IsCancel = true };

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttonRow.Children.Add(okButton);
            buttonRow.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(textBox);
            panel.Children.Add(buttonRow);

            var window = new Window
            {
                Title = title,
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            okButton.Click += (_, _) => { result = textBox.Text; window.DialogResult = true; };
            textBox.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };

            window.ShowDialog();
            return result;
        }
    }
}
