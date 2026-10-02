using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public interface IPresetService
    {
        List<TreePreset> GetPresets(string scanPath);
        void SavePresets(string scanPath, IEnumerable<TreePreset> presets);
    }
}
