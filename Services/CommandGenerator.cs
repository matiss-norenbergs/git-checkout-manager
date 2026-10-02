using System.Text;

namespace GitSparseManager.Services
{
    public class CommandGenerator : ICommandGenerator
    {
        public string GenerateBatScript(
            string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder,
            string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null)
        {
            var paths = sparsePaths.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("@echo off");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                sb.AppendLine($"cd /d \"{workingDirectory}\"");
                sb.AppendLine();
            }

            var cloneArgs = string.IsNullOrWhiteSpace(targetFolder)
                ? $"git clone --filter=blob:none --no-checkout \"{repoUrl}\""
                : $"git clone --filter=blob:none --no-checkout \"{repoUrl}\" \"{targetFolder}\"";
            sb.AppendLine(cloneArgs);
            sb.AppendLine();

            var cdTarget = string.IsNullOrWhiteSpace(targetFolder)
                ? RepoFolderName(repoUrl)
                : targetFolder;
            sb.AppendLine($"cd /d \"{cdTarget}\"");
            sb.AppendLine();

            sb.AppendLine("git sparse-checkout init --cone");
            sb.AppendLine();

            if (paths.Count > 0)
            {
                sb.Append("git sparse-checkout set");
                foreach (var p in paths)
                {
                    sb.AppendLine(" ^");
                    sb.Append($"  \"{p}\"");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine($"git checkout {branch}");

            if (!string.IsNullOrWhiteSpace(newBranch))
            {
                sb.AppendLine();
                sb.AppendLine($"git checkout -b {newBranch}");
            }

            if (initSubmodules)
            {
                sb.AppendLine();
                sb.AppendLine("for /f \"usebackq tokens=2\" %%p in (`git submodule status`) do (");
                sb.AppendLine("    if exist \"%%p\\\" git submodule update --init --remote --recursive \"%%p\"");
                sb.AppendLine(")");
            }

            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("pause");
            }

            return sb.ToString();
        }

        public string GenerateShScript(
            string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder,
            string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null)
        {
            var paths = sparsePaths.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("#!/bin/bash");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                sb.AppendLine($"cd \"{workingDirectory}\"");
                sb.AppendLine();
            }

            var cloneArgs = string.IsNullOrWhiteSpace(targetFolder)
                ? $"git clone --filter=blob:none --no-checkout \"{repoUrl}\""
                : $"git clone --filter=blob:none --no-checkout \"{repoUrl}\" \"{targetFolder}\"";
            sb.AppendLine(cloneArgs);
            sb.AppendLine();

            var cdTarget = string.IsNullOrWhiteSpace(targetFolder)
                ? RepoFolderName(repoUrl)
                : targetFolder;
            sb.AppendLine($"cd \"{cdTarget}\"");
            sb.AppendLine();

            sb.AppendLine("git sparse-checkout init --cone");
            sb.AppendLine();

            if (paths.Count > 0)
            {
                sb.Append("git sparse-checkout set");
                foreach (var p in paths)
                {
                    sb.AppendLine(" \\");
                    sb.Append($"  \"{p}\"");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine($"git checkout {branch}");

            if (!string.IsNullOrWhiteSpace(newBranch))
            {
                sb.AppendLine();
                sb.AppendLine($"git checkout -b {newBranch}");
            }

            if (initSubmodules)
            {
                sb.AppendLine();
                sb.AppendLine("git submodule status | awk '{print $2}' | while read p; do");
                sb.AppendLine("    [ -d \"$p\" ] && git submodule update --init --remote --recursive \"$p\"");
                sb.AppendLine("done");
            }

            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("read -rsp $'\\nPress any key to continue...\\n' -n1");
            }

            return sb.ToString();
        }

        public string GenerateManageBatScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null)
        {
            var paths = allDesiredPaths.ToList();
            var removed = removedPaths?.ToList() ?? new List<string>();
            var sb = new StringBuilder();

            sb.AppendLine("@echo off");
            sb.AppendLine();
            sb.AppendLine($"cd /d \"{localRepoPath}\"");
            sb.AppendLine();

            if (removed.Count > 0)
            {
                sb.Append("git clean -ffdxn");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.AppendLine("pause");
                sb.Append("git restore --");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.Append("git clean -ffdx");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.AppendLine();
            }

            if (paths.Count > 0)
            {
                sb.Append("git sparse-checkout set");
                foreach (var p in paths)
                {
                    sb.AppendLine(" ^");
                    sb.Append($"  \"{p}\"");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("git sparse-checkout set");
            }

            if (removed.Count > 0)
            {
                sb.AppendLine();
                // PowerShell Remove-Item handles read-only files and nested .git dirs that rmdir cannot
                foreach (var p in removed)
                    sb.AppendLine($"if exist \"{p}\" powershell -NoProfile -Command \"Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path '{p}'\"");
            }

            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("pause");
            }

            return sb.ToString();
        }

        public string GenerateManageShScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null)
        {
            var paths = allDesiredPaths.ToList();
            var removed = removedPaths?.ToList() ?? new List<string>();
            var sb = new StringBuilder();

            sb.AppendLine("#!/bin/bash");
            sb.AppendLine();
            sb.AppendLine($"cd \"{localRepoPath}\"");
            sb.AppendLine();

            if (removed.Count > 0)
            {
                sb.Append("git clean -ffdxn");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.AppendLine("read -rsp $'\\nFiles listed above will be removed. Press any key to continue or Ctrl+C to abort...\\n' -n1");
                sb.Append("git restore --");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.Append("git clean -ffdx");
                foreach (var p in removed) sb.Append($" \"{p}\"");
                sb.AppendLine();
                sb.AppendLine();
            }

            if (paths.Count > 0)
            {
                sb.Append("git sparse-checkout set");
                foreach (var p in paths)
                {
                    sb.AppendLine(" \\");
                    sb.Append($"  \"{p}\"");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("git sparse-checkout set");
            }

            if (removed.Count > 0)
            {
                sb.AppendLine();
                foreach (var p in removed)
                    sb.AppendLine($"[ -d \"{p}\" ] && rm -rf \"{p}\"");
            }

            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("read -rsp $'\\nPress any key to continue...\\n' -n1");
            }

            return sb.ToString();
        }

        private static string RepoFolderName(string repoUrl) =>
            repoUrl.TrimEnd('/').Split('/').Last().Replace(".git", "", StringComparison.OrdinalIgnoreCase);
    }
}
