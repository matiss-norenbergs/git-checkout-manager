namespace GitSparseManager.Services
{
    public interface ICommandGenerator
    {
        string GenerateBatScript(string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder, string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null);
        string GenerateShScript(string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder, string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null);
        string GenerateManageBatScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null);
        string GenerateManageShScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null);
    }
}
