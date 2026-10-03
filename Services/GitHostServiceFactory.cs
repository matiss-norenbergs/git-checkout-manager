using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public class GitHostServiceFactory
    {
        public IGitHostService Create(GitHostType type) => type switch
        {
            GitHostType.GitHub => new GitHubHostService(),
            _ => new GitLabHostService()
        };
    }
}
