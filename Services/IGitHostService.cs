using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public interface IGitHostService
    {
        GitHostType HostType { get; }

        /// <summary>Username used for git-over-HTTP basic auth against this host.</summary>
        string GitHttpUsername { get; }

        void Configure(string serverUrl, string token);

        Task<List<Repository>> GetRepositoriesAsync(CancellationToken ct = default);
    }
}
