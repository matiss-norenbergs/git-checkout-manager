using GitCheckoutManager.Models;
using GitCheckoutManager.ViewModels;

namespace GitCheckoutManager.Services
{
    public interface IDialogService
    {
        string? ShowSaveFileDialog(string filter, string defaultExtension, string defaultFileName);
        string? ShowOpenFileDialog(string filter, string title = "Open File");
        string? ShowOpenFolderDialog(string title = "Select Folder", string? initialPath = null);

        /// <summary>Yes/No question. <paramref name="details"/> (e.g. a file list) shows in a scrollable box; <paramref name="destructive"/> makes Yes the red button.</summary>
        bool ShowConfirmation(string title, string message, string? details = null, bool destructive = false);
        string? ShowInputDialog(string title, string prompt, string defaultValue = "");

        /// <summary>Returns the user's per-group deletion choices, or null when the dialog was cancelled.</summary>
        RemovalReviewChoices? ShowRemovalReview(RemovalReviewModel model);

        /// <summary>Shows the modal Settings window bound to <paramref name="viewModel"/>.</summary>
        void ShowSettings(SettingsViewModel viewModel);

        /// <summary>Shows the modal, read-only Submodules window and returns when it is closed.</summary>
        void ShowSubmodules(SubmodulesViewModel vm);

        /// <summary>Shows the repository picker and returns the chosen, reachable URL, or null when cancelled.</summary>
        string? ShowSubmoduleUrl(SubmoduleUrlModel model);

        /// <summary>Shows the remote-branch picker and returns the chosen branch, or null when cancelled.</summary>
        string? ShowSubmoduleBranch(SubmoduleBranchModel model);

        /// <summary>Shows a message with an OK button and optional scrollable details.</summary>
        void ShowMessage(string title, string message, string? details = null);
    }
}
