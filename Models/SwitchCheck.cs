namespace GitCheckoutManager.Models
{
    /// <summary>What switching a submodule's checkout could cost.</summary>
    /// <param name="DirtyFiles"><c>git status --porcelain</c> lines; any entry blocks the switch.</param>
    /// <param name="UnreferencedCommits">HEAD is detached, on no branch and not the recorded commit: it gets hard to find after switching.</param>
    public sealed record SwitchCheck(IReadOnlyList<string> DirtyFiles, bool UnreferencedCommits)
    {
        public bool IsDirty => DirtyFiles.Count > 0;
    }
}
