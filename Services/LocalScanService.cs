using System.IO;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public class LocalScanService : ILocalScanService
    {
        public Task<List<TreeNode>> ScanAsync(string localPath, int maxDepth = 4)
        {
            return Task.Run(() =>
            {
                var nodes = new List<TreeNode>();
                ScanDirectory(localPath, localPath, nodes, 1, maxDepth);
                return nodes;
            });
        }

        public Task<List<TreeNode>> ScanSubfolderAsync(
            string rootPath, string relativeFolderPath, int expansionDepth = 2)
        {
            return Task.Run(() =>
            {
                var absolutePath = Path.Combine(
                    rootPath,
                    relativeFolderPath.Replace('/', Path.DirectorySeparatorChar));
                var nodes = new List<TreeNode>();
                ScanDirectory(rootPath, absolutePath, nodes, 1, expansionDepth);
                return nodes;
            });
        }

        private static void ScanDirectory(
            string rootPath, string currentPath,
            List<TreeNode> nodes, int currentDepth, int maxDepth)
        {
            IEnumerable<string> dirs;
            IEnumerable<string> files;

            try
            {
                dirs  = Directory.EnumerateDirectories(currentPath)
                                  .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                                  .ToList();                            // materialise before iterating
                files = Directory.EnumerateFiles(currentPath)
                                  .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
            }
            catch (UnauthorizedAccessException) { return; }
            catch (DirectoryNotFoundException)  { return; }

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    continue;

                var relativePath = Path.GetRelativePath(rootPath, dir).Replace('\\', '/');

                if (currentDepth >= maxDepth)
                {
                    // At depth boundary – O(1) probe; no full recursion
                    bool hasChildren;
                    try   { hasChildren = Directory.EnumerateFileSystemEntries(dir).Any(); }
                    catch { hasChildren = false; }

                    nodes.Add(new TreeNode
                    {
                        Id   = relativePath,
                        Name = name,
                        Type = "tree",
                        Path = relativePath,
                        HasUnscannedChildren = hasChildren
                    });
                }
                else
                {
                    nodes.Add(new TreeNode
                    {
                        Id   = relativePath,
                        Name = name,
                        Type = "tree",
                        Path = relativePath
                    });
                    ScanDirectory(rootPath, dir, nodes, currentDepth + 1, maxDepth);
                }
            }

            foreach (var file in files)
            {
                var name         = Path.GetFileName(file);
                var relativePath = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                nodes.Add(new TreeNode
                {
                    Id   = relativePath,
                    Name = name,
                    Type = "blob",
                    Path = relativePath
                });
            }
        }
    }
}

