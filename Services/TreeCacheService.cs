using System.IO;
using System.Text.Json;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public class TreeCacheService : ITreeCacheService
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        public void SaveLocalCache(string localPath, List<TreeNode> nodes)
        {
            var cache = new LocalScanCache
            {
                ScanPath  = localPath,
                ScannedAt = DateTime.UtcNow,
                Nodes     = nodes
            };
            var json = JsonSerializer.Serialize(cache, WriteOptions);
            File.WriteAllText(Path.Combine(localPath, "sparse-checkout-cache.json"), json);
        }

        public LocalScanCache? LoadLocalCache(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath)) return null;
            try
            {
                var json = File.ReadAllText(jsonFilePath);
                return JsonSerializer.Deserialize<LocalScanCache>(json);
            }
            catch { return null; }
        }
    }
}
