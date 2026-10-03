using System.Text.RegularExpressions;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>Builds the default clone folder name from the pattern tokens {repo}, {branch} and {base}.</summary>
    public static class FolderNameBuilder
    {
        public static string Build(string pattern, string? repo, string? newBranch, string? baseBranch)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = AppSettings.DefaultFolderNamePattern;
            var branch = string.IsNullOrWhiteSpace(newBranch) ? baseBranch : newBranch;

            var name = pattern
                .Replace("{repo}", repo ?? string.Empty)
                .Replace("{branch}", branch ?? string.Empty)
                .Replace("{base}", baseBranch ?? string.Empty);

            foreach (var c in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
                name = name.Replace(c, '-');

            return Regex.Replace(name, "-{2,}", "-").TrimEnd('.', ' ');
        }
    }
}
