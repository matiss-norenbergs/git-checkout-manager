using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using GitSparseManager.Models;

namespace GitSparseManager.ViewModels
{
    /// <summary>
    /// Holds the tree and branch data for a loaded repository.
    /// Used by MainViewModel as a composable sub-ViewModel.
    /// </summary>
    public partial class RepositoryViewModel : ObservableObject
    {
        public Repository Repository { get; }

        [ObservableProperty]
        private ObservableCollection<Branch> _branches = new();

        [ObservableProperty]
        private Branch? _selectedBranch;

        [ObservableProperty]
        private ObservableCollection<TreeNodeViewModel> _treeNodes = new();

        public RepositoryViewModel(Repository repository)
        {
            Repository = repository;
        }
    }
}
