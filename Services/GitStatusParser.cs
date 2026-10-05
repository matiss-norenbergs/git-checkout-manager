using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    /// <summary>
    /// Parses <c>git status --porcelain=v2 -z --untracked-files=all</c>. Entries are NUL-separated and paths are
    /// never quoted, so spaces, unicode and <c>%</c> arrive as-is. Headers (<c>#</c>) and ignored entries (<c>!</c>)
    /// are skipped. A file gets exactly one row, so a staged-and-modified file is never counted twice.
    /// </summary>
    public static class GitStatusParser
    {
        private const string GitlinkMode = "160000";
        private const string SubmoduleHint = "Use the Submodules window.";

        public static LocalChangeSet Parse(string output)
        {
            var changes = new List<LocalChange>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var tokens = output.Split('\0');

            for (var i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t.Length < 3 || t[1] != ' ') continue;

                LocalChange? change = null;

                switch (t[0])
                {
                    case '1': // 1 XY sub mH mI mW hH hI path
                    {
                        var f = t.Split(' ', 9);
                        if (f.Length == 9)
                            change = Classify(f[8], null, f[1], f[2], f[3], f[4], f[5]);
                        break;
                    }
                    case '2': // 2 XY sub mH mI mW hH hI Xscore path NUL origPath
                    {
                        var f = t.Split(' ', 10);
                        if (f.Length == 10)
                        {
                            // The original path is its own NUL-terminated token; always consume it.
                            var original = i + 1 < tokens.Length ? tokens[++i] : null;
                            change = Classify(f[9], original, f[1], f[2], f[3], f[4], f[5]);
                        }
                        break;
                    }
                    case 'u': // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    {
                        var f = t.Split(' ', 11);
                        if (f.Length == 11)
                            change = Conflict(f[10], f[1], f[2], f[3], f[4], f[5], f[6]);
                        break;
                    }
                    case '?':
                    {
                        // A path deleted from the index but present again on disk is listed twice; keep one row.
                        var path = t[2..];
                        if (!seen.Contains(path))
                            change = new LocalChange(path, null, LocalChangeGroup.Untracked, "Untracked", false);
                        break;
                    }
                }

                if (change != null && seen.Add(change.Path))
                    changes.Add(change);
            }

            return new LocalChangeSet(changes);
        }

        private static bool IsGitlink(string sub, params string[] modes) =>
            modes.Any(m => m == GitlinkMode) || (sub.Length > 0 && sub[0] != 'N');

        private static string? SubmoduleHintFor(string sub)
        {
            var details = new List<string>();
            if (sub.Length == 4 && sub[0] == 'S')
            {
                if (sub[1] == 'C') details.Add("new commits");
                if (sub[2] == 'M') details.Add("modified content");
                if (sub[3] == 'U') details.Add("untracked files");
            }
            return details.Count == 0 ? SubmoduleHint : $"{string.Join(", ", details)}. {SubmoduleHint}";
        }

        private static LocalChange Classify(string path, string? original, string xy, string sub,
            string mH, string mI, string mW)
        {
            var x = xy[0];
            var y = xy[1];
            var submodule = IsGitlink(sub, mH, mI, mW);
            var renamed = x is 'R' or 'C';

            LocalChangeGroup group;
            if (renamed) group = LocalChangeGroup.Renamed;
            else if (x == 'D' || y == 'D') group = LocalChangeGroup.Deleted;
            else if (x != '.') group = LocalChangeGroup.Staged;
            else if (y == 'A') group = LocalChangeGroup.Staged; // intent-to-add
            else group = LocalChangeGroup.Modified;

            var label = submodule ? "Submodule" : Label(x, y);
            return new LocalChange(path, renamed ? original : null, group, label, submodule,
                submodule ? SubmoduleHintFor(sub) : null);
        }

        private static string Label(char x, char y)
        {
            var staged = x != '.';
            var unstaged = y != '.';

            if (staged && unstaged)
            {
                var head = x switch { 'A' => "Added", 'R' => "Renamed", 'C' => "Copied", _ => "Staged" };
                return $"{head} + {Unstaged(y)}";
            }

            if (staged)
                return x switch
                {
                    'A' => "Added",
                    'M' => "Staged (modified)",
                    'T' => "Staged (type changed)",
                    'D' => "Deleted (staged)",
                    'R' => "Renamed",
                    'C' => "Copied",
                    _ => "Staged"
                };

            var text = Unstaged(y);
            return char.ToUpperInvariant(text[0]) + text[1..];
        }

        private static string Unstaged(char y) => y switch
        {
            'M' => "modified",
            'T' => "type changed",
            'D' => "deleted",
            'A' => "intent to add",
            _ => "changed"
        };

        private static LocalChange Conflict(string path, string xy, string sub, string m1, string m2, string m3, string mW)
        {
            var label = xy switch
            {
                "DD" => "Conflict: both deleted",
                "AU" => "Conflict: added by us",
                "UD" => "Conflict: deleted by them",
                "UA" => "Conflict: added by them",
                "DU" => "Conflict: deleted by us",
                "AA" => "Conflict: both added",
                "UU" => "Conflict: both modified",
                _ => "Conflict"
            };

            var submodule = IsGitlink(sub, m1, m2, m3, mW);
            return new LocalChange(path, null, LocalChangeGroup.Conflicted, submodule ? "Submodule" : label,
                submodule, submodule ? $"{label}. {SubmoduleHint}" : null);
        }
    }
}
