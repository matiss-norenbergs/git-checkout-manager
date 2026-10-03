using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public interface IPresetService
    {
        List<TreePreset> GetPresets(string scanPath);
        void SavePresets(string scanPath, IEnumerable<TreePreset> presets);
    }
}
