namespace GitSparseManager.Models
{
    public class LocalScanCache
    {
        public string ScanPath { get; set; } = string.Empty;
        public DateTime ScannedAt { get; set; }
        public List<TreeNode> Nodes { get; set; } = new();
    }
}
