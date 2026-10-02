using System.Text.Json.Serialization;

namespace GitSparseManager.Models
{
    public class Branch
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;
    }
}
