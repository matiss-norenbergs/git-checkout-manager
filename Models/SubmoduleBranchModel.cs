namespace GitCheckoutManager.Models
{
    /// <summary>Input of the branch picker shown by Switch branch….</summary>
    public sealed class SubmoduleBranchModel
    {
        public required string DisplayPath { get; init; }

        /// <summary>Branch HEAD is on now; null when detached.</summary>
        public string? CurrentBranch { get; init; }

        /// <summary>Loads the remote branch names. Throws with a readable message on failure.</summary>
        public required Func<CancellationToken, Task<List<string>>> LoadBranchesAsync { get; init; }
    }
}
