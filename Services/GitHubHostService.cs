using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GitCheckoutManager.Models;

namespace GitCheckoutManager.Services
{
    public class GitHubHostService : IGitHostService
    {
        private const string PublicApiBase = "https://api.github.com";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Regex NextLinkPattern =
            new(@"<(?<url>[^>]+)>\s*;\s*rel\s*=\s*""next""", RegexOptions.IgnoreCase);

        private readonly HttpClient _httpClient;
        private string _apiBase = PublicApiBase;

        public GitHostType HostType => GitHostType.GitHub;

        public string GitHttpUsername => "x-access-token";

        public GitHubHostService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public void Configure(string serverUrl, string token)
        {
            _apiBase = ResolveApiBase(serverUrl);

            var headers = _httpClient.DefaultRequestHeaders;
            headers.Authorization = string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);

            headers.Accept.Clear();
            headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            headers.Remove("X-GitHub-Api-Version");
            headers.Add("X-GitHub-Api-Version", "2022-11-28");

            // GitHub rejects API requests that have no User-Agent
            headers.UserAgent.Clear();
            headers.UserAgent.Add(new ProductInfoHeaderValue("GitCheckoutManager", "1.0"));
        }

        private static string ResolveApiBase(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                return PublicApiBase;

            var trimmed = serverUrl.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return PublicApiBase;

            var host = uri.Host;
            if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
                return PublicApiBase;

            return $"{trimmed}/api/v3";
        }

        public async Task<List<Repository>> GetRepositoriesAsync(CancellationToken ct = default)
        {
            var url = $"{_apiBase}/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member";

            var result = new List<Repository>();

            // Follow Link rel="next" sequentially to stay friendly with rate limits
            while (!string.IsNullOrEmpty(url))
            {
                using var response = await _httpClient.GetAsync(url, ct);
                EnsureSuccess(response);

                var json = await response.Content.ReadAsStringAsync(ct);
                var page = JsonSerializer.Deserialize<List<GitHubRepoDto>>(json, JsonOptions);
                if (page != null)
                {
                    foreach (var d in page)
                    {
                        result.Add(new Repository
                        {
                            Id = d.Id,
                            Name = d.Name,
                            PathWithNamespace = d.FullName,
                            HttpUrlToRepo = d.CloneUrl,
                            DefaultBranch = d.DefaultBranch,
                            WebUrl = d.HtmlUrl
                        });
                    }
                }

                url = GetNextLink(response);
            }

            return result;
        }

        private static void EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("GitHub rejected the token (401)");

            if (response.StatusCode == HttpStatusCode.Forbidden &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                remaining.FirstOrDefault() == "0")
                throw new InvalidOperationException("GitHub API rate limit reached");

            response.EnsureSuccessStatusCode();
        }

        private static string? GetNextLink(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Link", out var linkValues))
                return null;

            foreach (var value in linkValues)
            {
                var match = NextLinkPattern.Match(value);
                if (match.Success)
                    return match.Groups["url"].Value;
            }

            return null;
        }

        private sealed class GitHubRepoDto
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("full_name")]
            public string FullName { get; set; } = string.Empty;

            [JsonPropertyName("clone_url")]
            public string CloneUrl { get; set; } = string.Empty;

            [JsonPropertyName("default_branch")]
            public string DefaultBranch { get; set; } = string.Empty;

            [JsonPropertyName("html_url")]
            public string HtmlUrl { get; set; } = string.Empty;
        }
    }
}
