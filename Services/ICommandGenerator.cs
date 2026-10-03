namespace GitSparseManager.Services
{
    public interface ICommandGenerator
    {
        /// <param name="fullClone">Plain `git clone --branch` of everything; <paramref name="sparsePaths"/> is ignored.</param>
        string GenerateBatScript(string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder, string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null, bool fullClone = false);
        string GenerateShScript(string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder, string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null, bool fullClone = false);
        string GenerateManageBatScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null);
        string GenerateManageShScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null);
    }
}
