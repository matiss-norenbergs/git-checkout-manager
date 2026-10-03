using System.IO;
using System.Text.Json;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public class PresetService : IPresetService
    {
        private static readonly string FilePath = AppPaths.PresetsFile;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly Dictionary<string, List<TreePreset>> _data;

        public PresetService()
        {
            _data = Load();
        }

        public List<TreePreset> GetPresets(string scanPath)
        {
            var key = NormalizeKey(scanPath);
            if (!_data.TryGetValue(key, out var list)) return new List<TreePreset>();
            return list.Select(p => new TreePreset { Name = p.Name, Paths = new List<string>(p.Paths) }).ToList();
        }

        public void SavePresets(string scanPath, IEnumerable<TreePreset> presets)
        {
            var key = NormalizeKey(scanPath);
            _data[key] = presets.ToList();
            Persist();
        }

        private static string NormalizeKey(string scanPath) =>
            // "remote:<url>" keys identify a repository rather than a folder, so leave them as-is.
            scanPath.StartsWith("remote:", StringComparison.OrdinalIgnoreCase)
                ? scanPath
                : Path.GetFullPath(scanPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static Dictionary<string, List<TreePreset>> Load()
        {
            if (!File.Exists(FilePath))
                return new Dictionary<string, List<TreePreset>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var json = File.ReadAllText(FilePath);
                var raw = JsonSerializer.Deserialize<Dictionary<string, List<TreePreset>>>(json, JsonOptions);
                if (raw == null)
                    return new Dictionary<string, List<TreePreset>>(StringComparer.OrdinalIgnoreCase);
                return new Dictionary<string, List<TreePreset>>(raw, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, List<TreePreset>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void Persist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(_data, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
