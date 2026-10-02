using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public interface ILocalScanService
    {
        /// <summary>Scans <paramref name="localPath"/> up to <paramref name="maxDepth"/> levels deep.
        /// Folders at the boundary that have children are flagged HasUnscannedChildren = true.</summary>
        Task<List<TreeNode>> ScanAsync(string localPath, int maxDepth = 4);

        /// <summary>Scans only the contents of a single sub-folder (<paramref name="relativeFolderPath"/>)
        /// up to <paramref name="expansionDepth"/> levels deep, returning nodes with paths
        /// relative to <paramref name="rootPath"/>.</summary>
        Task<List<TreeNode>> ScanSubfolderAsync(string rootPath, string relativeFolderPath, int expansionDepth = 2);
    }
}
