namespace GitSparseManager.Models
{
    public class RepositoryProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string DefaultBranch { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
    }
}
