namespace GitSparseManager.Services
{
    /// <summary>One submodule section of .gitmodules.</summary>
    public sealed record ModuleConfig(string? Path, string? Url, string? Branch);

    /// <summary>Parses <c>git config -f .gitmodules -z --get-regexp ^submodule\.</c> output.</summary>
    public static class GitmodulesParser
    {
        /// <summary>Returns the entries indexed by submodule path (forward slashes, no trailing slash).</summary>
        public static Dictionary<string, (string Name, ModuleConfig Config)> ParseByPath(string zOutput)
        {
            var byPath = new Dictionary<string, (string, ModuleConfig)>(StringComparer.Ordinal);

            const string prefix = "submodule.";
            var byName = new Dictionary<string, ModuleConfig>(StringComparer.Ordinal);

            foreach (var entry in zOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // "key\nvalue"; a key without a value has no newline.
                var nl = entry.IndexOf('\n');
                var key = nl < 0 ? entry : entry[..nl];
                var value = nl < 0 ? string.Empty : entry[(nl + 1)..];

                // The name may contain dots: strip the leading prefix, then split at the last dot.
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = key[prefix.Length..];
                var dot = rest.LastIndexOf('.');
                if (dot <= 0) continue;

                var name = rest[..dot];
                var field = rest[(dot + 1)..];

                var cfg = byName.GetValueOrDefault(name) ?? new ModuleConfig(null, null, null);
                cfg = field.ToLowerInvariant() switch
                {
                    "path" => cfg with { Path = value },
                    "url" => cfg with { Url = value },
                    "branch" => cfg with { Branch = value },
                    _ => cfg
                };
                byName[name] = cfg;
            }

            foreach (var (name, cfg) in byName)
            {
                if (string.IsNullOrWhiteSpace(cfg.Path)) continue;
                var path = cfg.Path.Trim().Replace('\\', '/').TrimEnd('/');
                byPath.TryAdd(path, (name, new ModuleConfig(path, cfg.Url, cfg.Branch)));
            }

            return byPath;
        }
    }
}
