using CommunityToolkit.Mvvm.ComponentModel;

namespace GitCheckoutManager.Models
{
    public partial class TreePreset : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        public List<string> Paths { get; set; } = new();
    }
}
