using System.Windows;
using System.Windows.Controls;
using GitSparseManager.Models;

namespace GitSparseManager.Views
{
    /// <summary>
    /// Shows what a Manage apply would delete inside the folders being removed, and lets the user pick
    /// which groups may actually be deleted from disk.
    /// </summary>
    public partial class RemovalReviewWindow : Window
    {
        /// <summary>Long lists are truncated so the dialog stays responsive.</summary>
        private const int MaxListedFiles = 500;

        public RemovalReviewChoices? Choices { get; private set; }

        public RemovalReviewWindow(RemovalReviewModel model)
        {
            InitializeComponent();

            RemovedFoldersList.ItemsSource = model.RemovedFolders;

            FillSection(IgnoredSection, IgnoredHeader, IgnoredItems, IgnoredDelete,
                "Ignored files", model.IgnoredFiles, deleteByDefault: true);
            FillSection(UntrackedSection, UntrackedHeader, UntrackedItems, UntrackedDelete,
                "Untracked files", model.UntrackedFiles, deleteByDefault: false);
            FillSection(ChangedSection, ChangedHeader, ChangedItems, ChangedDelete,
                "Changed files", model.ChangedFiles, deleteByDefault: false);
        }

        private static void FillSection(UIElement section, TextBlock header, ItemsControl list,
            CheckBox deleteBox, string title, IReadOnlyList<string> files, bool deleteByDefault)
        {
            if (files.Count == 0)
            {
                section.Visibility = Visibility.Collapsed;
                deleteBox.IsChecked = false;
                return;
            }

            header.Text = $"{title} ({files.Count})";
            deleteBox.IsChecked = deleteByDefault;

            var shown = files.Take(MaxListedFiles).ToList();
            if (files.Count > MaxListedFiles)
                shown.Add($"\u2026and {files.Count - MaxListedFiles} more");

            list.ItemsSource = shown;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            Choices = new RemovalReviewChoices(
                IgnoredDelete.IsChecked == true,
                UntrackedDelete.IsChecked == true,
                ChangedDelete.IsChecked == true);

            DialogResult = true;
        }
    }
}
