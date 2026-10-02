using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GitSparseManager.Models;

namespace GitSparseManager.Services
{
    public class GitService : IGitService
    {
        public async Task<List<string>?> GetSparseCheckoutPathsAsync(string localRepoPath)
        {
            try
            {
                var result = await RunAsync(new[] { "sparse-checkout", "list" }, localRepoPath);
                if (result.ExitCode != 0) return null;

                return result.StdOut
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        public async Task<GitResult> RunAsync(IEnumerable<string> args, string? workingDirectory = null,
                                              GitAuth? auth = null, CancellationToken ct = default,
                                              bool allowInteractiveAuth = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            if (!string.IsNullOrWhiteSpace(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            // There is no console to type into, so git itself never prompts. Credential Manager has its
            // own window and is only silenced unless the caller allows a login window.
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            if (!allowInteractiveAuth)
                psi.Environment["GCM_INTERACTIVE"] = "Never";

            if (auth != null)
            {
                // Credentials travel only via environment, so they stay out of process
                // listings, out of .git/config and out of any generated script.
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Token}"));
                psi.Environment["GIT_CONFIG_COUNT"] = "1";
                psi.Environment["GIT_CONFIG_KEY_0"] = BuildExtraHeaderKey(auth.ScopeUrl);
                psi.Environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}";
            }

            Process process;
            try
            {
                process = Process.Start(psi) ?? throw new InvalidOperationException("Git was not found on PATH.");
            }
            catch (Win32Exception)
            {
                throw new InvalidOperationException("Git was not found on PATH.");
            }

            using (process)
            {
                // Read both streams concurrently so large output cannot deadlock the pipe
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { /* already exited */ }
                    throw;
                }

                var stdOut = await stdOutTask;
                var stdErr = await stdErrTask;
                return new GitResult(process.ExitCode, stdOut, stdErr);
            }
        }

        /// <summary>
        /// Builds a URL-scoped config key such as <c>http.https://host:8443/.extraHeader</c>. Git only
        /// sends a scoped header to that server, so the token cannot reach any other host (for example
        /// a submodule hosted elsewhere).
        /// </summary>
        internal static string BuildExtraHeaderKey(string scopeUrl)
        {
            if (!Uri.TryCreate(scopeUrl?.Trim(), UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Cannot scope credentials to '{scopeUrl}': it is not an absolute URL.");

            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"http.{uri.Scheme}://{uri.Host}{port}/.extraHeader";
        }

        public async Task<List<Branch>> ListRemoteBranchesAsync(string repoUrl, GitAuth? auth, CancellationToken ct = default)
        {
            var result = await RunAsync(new[] { "ls-remote", "--heads", repoUrl }, null, auth, ct);

            if (result.ExitCode != 0)
            {
                var firstLine = result.StdErr
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0) ?? "git ls-remote failed.";
                throw new InvalidOperationException(firstLine);
            }

            const string prefix = "refs/heads/";
            return result.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => l.Split('\t'))
                .Where(parts => parts.Length == 2 && parts[1].StartsWith(prefix, StringComparison.Ordinal))
                .Select(parts => new Branch { Name = parts[1][prefix.Length..] })
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
