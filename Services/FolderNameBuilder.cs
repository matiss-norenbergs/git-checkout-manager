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

            // A token the pattern uses but that has no value yet (e.g. branches still loading) means no name yet.
            if (UsesEmpty(pattern, "{repo}", repo) ||
                UsesEmpty(pattern, "{branch}", branch) ||
                UsesEmpty(pattern, "{base}", baseBranch))
                return string.Empty;

            var name = pattern
                .Replace("{repo}", repo)
                .Replace("{branch}", branch)
                .Replace("{base}", baseBranch);

            foreach (var c in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
                name = name.Replace(c, '-');

            return Regex.Replace(name, @"([-_. ])\1+", "$1").Trim('_', '-', '.', ' ');
        }

        private static bool UsesEmpty(string pattern, string token, string? value) =>
            string.IsNullOrWhiteSpace(value) && pattern.Contains(token);
    }
}
