namespace GitCheckoutManager.Models
{
    /// <summary>A branch offered in multi-row mode: how many of the target rows have it on their remote and how many are on it now.</summary>
    public sealed record BranchOption(string Name, int OnCount, int CurrentCount);

    /// <summary>Input of the branch picker shown by Switch branch….</summary>
    public sealed class SubmoduleBranchModel
    {
        /// <summary>The submodule's path (single mode); in multi mode only a label.</summary>
        public required string DisplayPath { get; init; }

        /// <summary>Single mode: replaces the default "Switch the branch of {DisplayPath}" header (the Manage tab switches the checkout itself).</summary>
        public string? Header { get; init; }

        /// <summary>Branch HEAD is on now; null when detached. Single mode only.</summary>
        public string? CurrentBranch { get; init; }

        /// <summary>Single mode: loads the remote branch names. Throws with a readable message on failure.</summary>
        public Func<CancellationToken, Task<List<string>>> LoadBranchesAsync { get; init; } =
            _ => Task.FromResult(new List<string>());

        /// <summary>Number of submodules the branch applies to. 2 or more switches the picker to multi mode.</summary>
        public int RowCount { get; init; } = 1;

        public bool IsMulti => RowCount > 1;

        /// <summary>Multi mode: the union of the rows' remote branches with their counts. Throws with a readable message on failure.</summary>
        public Func<CancellationToken, Task<List<BranchOption>>>? LoadBranchOptionsAsync { get; init; }
    }
}
