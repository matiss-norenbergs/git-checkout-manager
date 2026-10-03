namespace GitCheckoutManager.Models
{
    /// <summary>Input of the repository picker shown by Set URL… and Clone manually….</summary>
    public sealed class SubmoduleUrlModel
    {
        public required string Path { get; init; }

        /// <summary>The URL .gitmodules has for this path, if it has an entry.</summary>
        public string? GitmodulesUrl { get; init; }

        /// <summary>Repositories of the connected server; empty when not connected.</summary>
        public required IReadOnlyList<Repository> Repositories { get; init; }

        /// <summary>Checks that a URL is reachable (with the right credentials). Returns the first error line, or null on success.</summary>
        public required Func<string, CancellationToken, Task<string?>> TestUrlAsync { get; init; }
    }
}
