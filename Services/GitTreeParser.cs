using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    /// <summary>Parses the output of <c>git ls-tree -r -t -z</c> into a flat list of tree nodes.</summary>
    public static class GitTreeParser
    {
        public static List<TreeNode> Parse(string lsTreeZOutput)
        {
            var nodes = new List<TreeNode>();

            foreach (var entry in lsTreeZOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                // <mode> SP <type> SP <objectId> TAB <path>
                var tab = entry.IndexOf('\t');
                if (tab < 0) continue;

                var meta = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (meta.Length < 2) continue;

                var path = entry[(tab + 1)..];
                if (path.Length == 0) continue;

                var gitType = meta[1];
                var isSubmodule = gitType == "commit";

                var slash = path.LastIndexOf('/');
                nodes.Add(new TreeNode
                {
                    Id = path,
                    Path = path,
                    Name = slash < 0 ? path : path[(slash + 1)..],
                    // A submodule is a gitlink; show it as a folder so it can be selected.
                    Type = gitType == "blob" ? "blob" : "tree",
                    IsSubmodule = isSubmodule,
                    HasUnscannedChildren = false
                });
            }

            return nodes;
        }
    }
}
