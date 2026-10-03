using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public interface ISettingsService
    {
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
        string? GetDecryptedToken(AppSettings settings);
        void SetToken(AppSettings settings, string token);
        string? GetDecryptedToken(AppSettings settings, GitHostType hostType);
        void SetToken(AppSettings settings, GitHostType hostType, string token);
    }
}
