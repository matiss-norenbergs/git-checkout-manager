using System.Text;

namespace GitSparseManager.Services
{
    public class CommandGenerator : ICommandGenerator
    {
        private const string BatCheck = "if errorlevel 1 goto :failed";

        public string GenerateBatScript(
            string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder,
            string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null)
        {
            var paths = sparsePaths.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                sb.AppendLine($"cd /d \"{BatEscape(workingDirectory)}\"");
                sb.AppendLine(BatCheck);
                sb.AppendLine();
            }

            var cloneArgs = string.IsNullOrWhiteSpace(targetFolder)
                ? $"git clone --filter=blob:none --no-checkout \"{BatEscape(repoUrl)}\""
                : $"git clone --filter=blob:none --no-checkout \"{BatEscape(repoUrl)}\" \"{BatEscape(targetFolder)}\"";
            sb.AppendLine(cloneArgs);
            sb.AppendLine(BatCheck);
            sb.AppendLine();

            var cdTarget = string.IsNullOrWhiteSpace(targetFolder)
                ? RepoFolderName(repoUrl)
                : targetFolder;
            sb.AppendLine($"cd /d \"{BatEscape(cdTarget)}\"");
            sb.AppendLine(BatCheck);
            sb.AppendLine();

            sb.AppendLine("git sparse-checkout init --cone");
            sb.AppendLine(BatCheck);
            sb.AppendLine();

            if (paths.Count > 0)
            {
                AppendBatSparseSet(sb, paths);
                sb.AppendLine(BatCheck);
            }

            sb.AppendLine();
            sb.AppendLine($"git checkout \"{BatEscape(branch)}\"");
            sb.AppendLine(BatCheck);

            if (!string.IsNullOrWhiteSpace(newBranch))
            {
                sb.AppendLine();
                sb.AppendLine($"git checkout -b \"{BatEscape(newBranch)}\"");
                sb.AppendLine(BatCheck);
            }

            if (initSubmodules)
            {
                // Driven by .gitmodules so one broken entry can't hide the healthy ones; failures never abort.
                sb.AppendLine();
                sb.AppendLine("set \"SUBFAIL=\"");
                sb.AppendLine("if exist .gitmodules (");
                sb.AppendLine("    for /f \"usebackq tokens=1*\" %%a in (`git config -f .gitmodules --get-regexp \"^submodule\\..*\\.path$\"`) do (");
                sb.AppendLine("        if exist \"%%b\\\" ( git submodule update --init --remote --recursive -- \"%%b\" || set SUBFAIL=1 )");
                sb.AppendLine("    )");
                sb.AppendLine(")");
            }

            AppendBatEnd(sb, keepWindowOpen, initSubmodules);
            return sb.ToString();
        }

        public string GenerateShScript(
            string repoUrl, string branch, IEnumerable<string> sparsePaths, string targetFolder,
            string? newBranch = null, bool initSubmodules = false, bool keepWindowOpen = false, string? workingDirectory = null)
        {
            var paths = sparsePaths.ToList();
            var sb = new StringBuilder();

            AppendShHeader(sb, keepWindowOpen);

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                sb.AppendLine($"cd {ShQuote(workingDirectory)} || fail");
                sb.AppendLine();
            }

            var cloneArgs = string.IsNullOrWhiteSpace(targetFolder)
                ? $"git clone --filter=blob:none --no-checkout {ShQuote(repoUrl)}"
                : $"git clone --filter=blob:none --no-checkout {ShQuote(repoUrl)} {ShQuote(targetFolder)}";
            sb.AppendLine($"{cloneArgs} || fail");
            sb.AppendLine();

            var cdTarget = string.IsNullOrWhiteSpace(targetFolder)
                ? RepoFolderName(repoUrl)
                : targetFolder;
            sb.AppendLine($"cd {ShQuote(cdTarget)} || fail");
            sb.AppendLine();

            sb.AppendLine("git sparse-checkout init --cone || fail");
            sb.AppendLine();

            if (paths.Count > 0)
                AppendShSparseSet(sb, paths);

            sb.AppendLine();
            sb.AppendLine($"git checkout {ShQuote(branch)} || fail");

            if (!string.IsNullOrWhiteSpace(newBranch))
            {
                sb.AppendLine();
                sb.AppendLine($"git checkout -b {ShQuote(newBranch)} || fail");
            }

            if (initSubmodules)
            {
                // Process substitution (not a pipe) keeps the loop in this shell so SUBFAIL survives.
                sb.AppendLine();
                sb.AppendLine("SUBFAIL=0");
                sb.AppendLine("if [ -f .gitmodules ]; then");
                sb.AppendLine("    while read -r key path; do");
                sb.AppendLine("        [ -d \"$path\" ] && { git submodule update --init --remote --recursive -- \"$path\" || SUBFAIL=1; }");
                sb.AppendLine("    done < <(git config -f .gitmodules --get-regexp '^submodule\\..*\\.path$')");
                sb.AppendLine("fi");
            }

            AppendShEnd(sb, keepWindowOpen, initSubmodules);
            return ToLf(sb);
        }

        public string GenerateManageBatScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null)
        {
            var paths = allDesiredPaths.ToList();
            var removed = removedPaths?.ToList() ?? new List<string>();
            var sb = new StringBuilder();

            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine();
            sb.AppendLine($"cd /d \"{BatEscape(localRepoPath)}\"");
            sb.AppendLine(BatCheck);
            sb.AppendLine();

            if (removed.Count > 0)
            {
                sb.Append("git clean -ffdxn");
                foreach (var p in removed) sb.Append($" \"{BatEscape(p)}\"");
                sb.AppendLine();
                sb.AppendLine("pause");
                sb.Append("git restore --");
                foreach (var p in removed) sb.Append($" \"{BatEscape(p)}\"");
                sb.AppendLine();
                sb.AppendLine(BatCheck);
                sb.Append("git clean -ffdx");
                foreach (var p in removed) sb.Append($" \"{BatEscape(p)}\"");
                sb.AppendLine();
                sb.AppendLine(BatCheck);
                sb.AppendLine();
            }

            if (paths.Count > 0)
                AppendBatSparseSet(sb, paths);
            else
                sb.AppendLine("git sparse-checkout set");
            sb.AppendLine(BatCheck);

            if (removed.Count > 0)
            {
                sb.AppendLine();
                // PowerShell Remove-Item handles read-only files and nested .git dirs that rmdir cannot
                foreach (var p in removed)
                    sb.AppendLine($"if exist \"{BatEscape(p)}\" powershell -NoProfile -Command \"Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -Path '{BatEscape(p)}'\"");
            }

            AppendBatEnd(sb, keepWindowOpen, false);
            return sb.ToString();
        }

        public string GenerateManageShScript(string localRepoPath, IEnumerable<string> allDesiredPaths, bool keepWindowOpen = false, IEnumerable<string>? removedPaths = null)
        {
            var paths = allDesiredPaths.ToList();
            var removed = removedPaths?.ToList() ?? new List<string>();
            var sb = new StringBuilder();

            AppendShHeader(sb, keepWindowOpen);
            sb.AppendLine($"cd {ShQuote(localRepoPath)} || fail");
            sb.AppendLine();

            if (removed.Count > 0)
            {
                sb.Append("git clean -ffdxn");
                foreach (var p in removed) sb.Append($" {ShQuote(p)}");
                sb.AppendLine();
                sb.AppendLine("read -rsp $'\\nFiles listed above will be removed. Press any key to continue or Ctrl+C to abort...\\n' -n1");
                sb.Append("git restore --");
                foreach (var p in removed) sb.Append($" {ShQuote(p)}");
                sb.AppendLine(" || fail");
                sb.Append("git clean -ffdx");
                foreach (var p in removed) sb.Append($" {ShQuote(p)}");
                sb.AppendLine(" || fail");
                sb.AppendLine();
            }

            if (paths.Count > 0)
                AppendShSparseSet(sb, paths);
            else
                sb.AppendLine("git sparse-checkout set || fail");

            if (removed.Count > 0)
            {
                sb.AppendLine();
                foreach (var p in removed)
                    sb.AppendLine($"[ -d {ShQuote(p)} ] && rm -rf {ShQuote(p)}");
            }

            AppendShEnd(sb, keepWindowOpen, false);
            return ToLf(sb);
        }

        private static void AppendBatSparseSet(StringBuilder sb, List<string> paths)
        {
            sb.Append("git sparse-checkout set");
            foreach (var p in paths)
            {
                sb.AppendLine(" ^");
                sb.Append($"  \"{BatEscape(p)}\"");
            }
            sb.AppendLine();
        }

        private static void AppendShSparseSet(StringBuilder sb, List<string> paths)
        {
            sb.Append("git sparse-checkout set");
            foreach (var p in paths)
            {
                sb.AppendLine(" \\");
                sb.Append($"  {ShQuote(p)}");
            }
            sb.AppendLine(" || fail");
        }

        private static void AppendBatEnd(StringBuilder sb, bool keepWindowOpen, bool trackSubmodules)
        {
            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("pause");
            }

            sb.AppendLine();
            if (trackSubmodules)
            {
                // Something to read, so pause even when the success path wouldn't (once, not twice).
                if (keepWindowOpen)
                {
                    sb.AppendLine("if defined SUBFAIL exit /b 2");
                }
                else
                {
                    sb.AppendLine("if defined SUBFAIL (");
                    sb.AppendLine("    echo.");
                    sb.AppendLine("    echo Some submodules failed to initialize — see the output above");
                    sb.AppendLine("    pause");
                    sb.AppendLine("    exit /b 2");
                    sb.AppendLine(")");
                }
            }
            sb.AppendLine("exit /b 0");
            sb.AppendLine();
            sb.AppendLine(":failed");
            sb.AppendLine("echo.");
            sb.AppendLine("echo FAILED — see the output above");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
        }

        private static void AppendShHeader(StringBuilder sb, bool keepWindowOpen)
        {
            sb.AppendLine("#!/bin/bash");
            sb.AppendLine();
            sb.AppendLine("fail() {");
            sb.AppendLine("    echo");
            sb.AppendLine("    echo \"FAILED — see the output above\"");
            sb.AppendLine("    read -rsp $'\\nPress any key to continue...\\n' -n1");
            sb.AppendLine("    exit 1");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        private static void AppendShEnd(StringBuilder sb, bool keepWindowOpen, bool trackSubmodules)
        {
            if (keepWindowOpen)
            {
                sb.AppendLine();
                sb.AppendLine("read -rsp $'\\nPress any key to continue...\\n' -n1");
            }

            sb.AppendLine();
            if (trackSubmodules)
            {
                // Something to read, so pause even when the success path wouldn't (once, not twice).
                sb.AppendLine("if [ \"$SUBFAIL\" = 1 ]; then");
                if (!keepWindowOpen)
                {
                    sb.AppendLine("    echo");
                    sb.AppendLine("    echo \"Some submodules failed to initialize — see the output above\"");
                    sb.AppendLine("    read -rsp $'\\nPress any key to continue...\\n' -n1");
                }
                sb.AppendLine("    exit 2");
                sb.AppendLine("fi");
            }
            sb.AppendLine("exit 0");
        }

        private static string BatEscape(string value) => value.Replace("%", "%%");

        private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

        private static string ToLf(StringBuilder sb) => sb.ToString().Replace("\r\n", "\n");

        private static string RepoFolderName(string repoUrl) =>
            repoUrl.TrimEnd('/').Split('/').Last().Replace(".git", "", StringComparison.OrdinalIgnoreCase);
    }
}
