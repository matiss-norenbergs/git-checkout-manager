using System.Text.Json.Serialization;

namespace GitCheckoutManager.Models
{
    public class TreeNode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>"tree" = folder, "blob" = file</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        public bool IsFolder => Type == "tree";

        /// <summary>True for a gitlink entry (mode 160000) pointing at another repository.</summary>
        [JsonPropertyName("isSubmodule")]
        public bool IsSubmodule { get; set; }
    }
}
