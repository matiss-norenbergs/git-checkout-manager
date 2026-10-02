namespace GitSparseManager.Models
{
    public class TreeCache
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public DateTime CachedAt { get; set; }
        public List<TreeNode> Nodes { get; set; } = new();
    }
}
