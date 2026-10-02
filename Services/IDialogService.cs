using GitSparseManager.Models;
using GitSparseManager.ViewModels;

namespace GitSparseManager.Services
{
    public interface IDialogService
    {
        string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName);
        string? ShowOpenFileDialog(string filter, string title = "Open File");
        string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null);
        bool ShowConfirmation(string message, string title);
        string? ShowInputDialog(string title, string prompt, string defaultValue = "");

        /// <summary>Returns the user's per-group deletion choices, or null when the dialog was cancelled.</summary>
        RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model);

        /// <summary>Shows the modal Settings window bound to <paramref name="viewModel"/>.</summary>
        void ShowSettings(SettingsViewModel viewModel);
    }
}
