using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public class GitLabHostService : IGitHostService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://gitlab.local";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public GitHostType HostType => GitHostType.GitLab;

        public string GitHttpUsername => "oauth2";

        public GitLabHostService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public void Configure(string serverUrl, string token)
        {
            _baseUrl = serverUrl.TrimEnd('/');
            _httpClient.DefaultRequestHeaders.Remove("PRIVATE-TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                _httpClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
        }

        public async Task<List<Repository>> GetRepositoriesAsync(CancellationToken ct = default)
        {
            var url = $"{_baseUrl}/api/v4/projects?membership=true&per_page=100&order_by=last_activity_at";

            var dtos = new List<GitLabProjectDto>();
            await FetchAllPagesAsync(url, dtos, ct);

            return dtos.Select(d => new Repository
            {
                Id = d.Id,
                Name = d.Name,
                HttpUrlToRepo = d.HttpUrlToRepo,
                PathWithNamespace = d.PathWithNamespace,
                DefaultBranch = d.DefaultBranch,
                WebUrl = d.WebUrl
            }).ToList();
        }

        private async Task FetchAllPagesAsync<T>(string initialUrl, List<T> results, CancellationToken ct)
        {
            // Fetch first page and determine total page count
            var firstResponse = await _httpClient.GetAsync(initialUrl, ct);
            firstResponse.EnsureSuccessStatusCode();

            var firstJson = await firstResponse.Content.ReadAsStringAsync(ct);
            var firstItems = JsonSerializer.Deserialize<List<T>>(firstJson, JsonOptions);
            if (firstItems != null)
                results.AddRange(firstItems);

            int totalPages = 1;
            if (firstResponse.Headers.TryGetValues("X-Total-Pages", out var totalVals))
                int.TryParse(totalVals.FirstOrDefault(), out totalPages);

            if (totalPages <= 1) return;

            // Fetch remaining pages with bounded concurrency to avoid rate-limiting
            var semaphore = new SemaphoreSlim(8);
            var pageTasks = Enumerable.Range(2, totalPages - 1)
                .Select(async p =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var r = await _httpClient.GetAsync(SetPageParameter(initialUrl, p.ToString()), ct);
                        r.EnsureSuccessStatusCode();
                        return await r.Content.ReadAsStringAsync(ct);
                    }
                    finally { semaphore.Release(); }
                });

            var jsonPages = await Task.WhenAll(pageTasks);

            foreach (var pageJson in jsonPages)
            {
                var items = JsonSerializer.Deserialize<List<T>>(pageJson, JsonOptions);
                if (items != null)
                    results.AddRange(items);
            }
        }

        private static string SetPageParameter(string url, string page)
        {
            var pagePattern = new Regex(@"([&?])page=\d+");
            if (pagePattern.IsMatch(url))
                return pagePattern.Replace(url, $"$1page={page}");

            var sep = url.Contains('?') ? "&" : "?";
            return $"{url}{sep}page={page}";
        }

        private sealed class GitLabProjectDto
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("http_url_to_repo")]
            public string HttpUrlToRepo { get; set; } = string.Empty;

            [JsonPropertyName("path_with_namespace")]
            public string PathWithNamespace { get; set; } = string.Empty;

            [JsonPropertyName("default_branch")]
            public string DefaultBranch { get; set; } = string.Empty;

            [JsonPropertyName("web_url")]
            public string WebUrl { get; set; } = string.Empty;
        }
    }
}
