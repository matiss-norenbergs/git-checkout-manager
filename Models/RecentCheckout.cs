using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitSparseManager.Models
{
    /// <summary>A checkout folder the user opened before, shown in the Manage dropdown.</summary>
    public sealed class RecentCheckout : ObservableObject
    {
        public string Path { get; set; } = string.Empty;
        public string? RemoteUrl { get; set; }
        public string? Branch { get; set; }
        public DateTime LastOpenedUtc { get; set; }

        private bool _isMissing;

        /// <summary>Recomputed from disk, never persisted.</summary>
        [JsonIgnore]
        public bool IsMissing
        {
            get => _isMissing;
            set
            {
                if (SetProperty(ref _isMissing, value))
                    OnPropertyChanged(nameof(DisplayText));
            }
        }

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                var folder = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(folder)) folder = Path;
                var branch = string.IsNullOrWhiteSpace(Branch) ? "detached" : Branch;
                var text = $"{folder} · {branch} · {Path}";
                return IsMissing ? text + " (missing)" : text;
            }
        }
    }
}
