using GitCheckoutManager.Services;

namespace GitCheckoutManager.Models
{
    /// <summary>Why a switch ended the way it did, when the exit code alone doesn't say.</summary>
    public enum SwitchOutcome
    {
        None,
        /// <summary>The local branch has commits that aren't on origin: it was checked out but not fast-forwarded. Exit code is still 0.</summary>
        LeftAsIs,
        /// <summary>The fetch failed because origin has no such branch. Exit code is non-zero.</summary>
        BranchNotOnRemote
    }

    /// <summary>Outcome of <c>SwitchBranchAsync</c>: a git result plus an explicit <see cref="SwitchOutcome"/>, so callers never parse text.</summary>
    public sealed record SwitchResult(int ExitCode, string StdOut, string StdErr, SwitchOutcome Outcome = SwitchOutcome.None)
    {
        public static SwitchResult From(GitResult git, SwitchOutcome outcome = SwitchOutcome.None) =>
            new(git.ExitCode, git.StdOut, git.StdErr, outcome);

        public GitResult ToGitResult() => new(ExitCode, StdOut, StdErr);
    }
}
