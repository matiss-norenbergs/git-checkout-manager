using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    /// <summary>
    /// Basic-auth credentials plus the repository URL they may be sent to. <see cref="ScopeUrl"/>
    /// restricts the header to that one server, so a submodule on another host never sees the token.
    /// </summary>
    public sealed record GitAuth(string Username, string Token, string ScopeUrl);

    public sealed record GitResult(int ExitCode, string StdOut, string StdErr);

    public interface IGitService
    {
        Task<List<string>?> GetSparseCheckoutPathsAsync(string localRepoPath);

        Task<GitResult> RunAsync(IEnumerable<string> args, string? workingDirectory = null,
                                 GitAuth? auth = null, CancellationToken ct = default);

        Task<List<Branch>> ListRemoteBranchesAsync(string repoUrl, GitAuth? auth, CancellationToken ct = default);
    }
}
