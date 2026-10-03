namespace GitCheckoutManager.Models
{
    public class Repository
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string HttpUrlToRepo { get; set; } = string.Empty;

        public string PathWithNamespace { get; set; } = string.Empty;

        public string DefaultBranch { get; set; } = string.Empty;

        public string WebUrl { get; set; } = string.Empty;

        public override string ToString() => PathWithNamespace;
    }
}
