using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public interface ITreeCacheService
    {
        void SaveLocalCache(string localPath, List<TreeNode> nodes);
        LocalScanCache? LoadLocalCache(string jsonFilePath);
    }
}
