using System.Diagnostics;
using System.Text;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

/// <summary>Skips unless git is available and the tests run on Windows (the .bat is run through cmd).</summary>
public sealed class RequiresGitWindowsFactAttribute : FactAttribute
{
    public RequiresGitWindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "the .bat script only runs on Windows";
        else if (!GitFixture.GitAvailable) Skip = "git is not available on PATH";
    }
}

/// <summary>Skips unless git and a real bash are available on PATH.</summary>
public sealed class RequiresGitBashFactAttribute : FactAttribute
{
    public RequiresGitBashFactAttribute()
    {
        if (!GitFixture.GitAvailable) Skip = "git is not available on PATH";
        else if (ScriptExitCodeTests.Bash == null) Skip = "bash is not available on PATH";
    }
}

/// <summary>
/// Runs the generated clone scripts for real. The post-clone bar in the Submodules window depends on
/// exit code 2 ("cloned, but some submodule failed"), and exit code 1 for a failed clone.
/// </summary>
public class ScriptExitCodeTests
{
    static ScriptExitCodeTests()
    {
        // The scripts' git children inherit this: newer git blocks the file transport for submodule clones.
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    private static readonly CommandGenerator Gen = new();

    /// <summary>First bash on PATH, ignoring the WSL launcher in System32 (it can't read Windows paths).</summary>
    public static readonly string? Bash = FindBash();

    private static string? FindBash()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "bash.exe" } : new[] { "bash" };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (dir.Length == 0 || dir.Contains("System32", StringComparison.OrdinalIgnoreCase) ||
                dir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string Origin(GitFixture fx, string name)
    {
        var repo = fx.Sub(name);
        GitFixture.Git(repo, "init");
        GitFixture.Write(Path.Combine(repo, "a.txt"), "a");
        GitFixture.Git(repo, "add", ".");
        GitFixture.Git(repo, "commit", "-m", "first");
        return repo;
    }

    /// <summary>A main repo with one submodule that clones fine and one whose .gitmodules URL points nowhere.</summary>
    private static string MainWithHealthyAndBrokenSubmodule(GitFixture fx)
    {
        var main = fx.Sub("main-origin");
        GitFixture.Git(main, "init");
        GitFixture.Write(Path.Combine(main, "m.txt"), "m");
        GitFixture.Git(main, "add", ".");
        GitFixture.Git(main, "commit", "-m", "m");
        GitFixture.Git(main, "submodule", "add", new Uri(Origin(fx, "healthy-origin")).AbsoluteUri, "external/healthy");
        GitFixture.Git(main, "submodule", "add", new Uri(Origin(fx, "broken-origin")).AbsoluteUri, "external/broken");
        GitFixture.Git(main, "config", "-f", ".gitmodules", "submodule.external/broken.url",
            new Uri(Path.Combine(fx.Root, "gone.git")).AbsoluteUri);
        GitFixture.Git(main, "add", ".gitmodules");
        GitFixture.Git(main, "commit", "-m", "add submodules");
        return main;
    }

    private static (int ExitCode, string Output) Run(ProcessStartInfo psi)
    {
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close(); // like "< NUL": pause and read return at once
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            proc.Kill(entireProcessTree: true);
            Assert.Fail("The script did not finish within 3 minutes.");
        }
        proc.WaitForExit();
        return (proc.ExitCode, stdout.Result + stderr.Result);
    }

    private static (int ExitCode, string Output) RunBat(string bat)
    {
        // cmd strips the outer pair of quotes, leaving "<bat>".
        var psi = new ProcessStartInfo("cmd.exe") { Arguments = $"/c \"\"{bat}\"\"" };
        return Run(psi);
    }

    private static (int ExitCode, string Output) RunSh(string sh)
    {
        var psi = new ProcessStartInfo(Bash!);
        psi.ArgumentList.Add(sh.Replace('\\', '/'));
        return Run(psi);
    }

    private static string WriteScript(GitFixture fx, string name, string content)
    {
        var path = Path.Combine(fx.Root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static void AssertHealthyPopulatedAndBrokenNot(string checkout)
    {
        Assert.True(Directory.Exists(checkout), "the checkout folder should exist");
        Assert.True(File.Exists(Path.Combine(checkout, "m.txt")));
        Assert.True(File.Exists(Path.Combine(checkout, "external", "healthy", "a.txt")), "the healthy submodule should be populated");
        Assert.False(File.Exists(Path.Combine(checkout, "external", "broken", "a.txt")));
    }

    // ── .bat ──────────────────────────────────────────────────────────────

    [RequiresGitWindowsFact]
    public void Bat_exits_with_2_when_one_submodule_fails_and_keeps_the_healthy_one()
    {
        using var fx = new GitFixture();
        var main = MainWithHealthyAndBrokenSubmodule(fx);

        var script = Gen.GenerateBatScript(new Uri(main).AbsoluteUri, "main", new[] { "external" }, "checkout",
            initSubmodules: true, workingDirectory: fx.Root);
        var (exit, output) = RunBat(WriteScript(fx, "run.bat", script));

        Assert.True(exit == 2, $"expected exit code 2, got {exit}:{Environment.NewLine}{output}");
        AssertHealthyPopulatedAndBrokenNot(Path.Combine(fx.Root, "checkout"));
    }

    [RequiresGitWindowsFact]
    public void Bat_exits_with_0_when_every_submodule_works()
    {
        using var fx = new GitFixture();
        var main = fx.Sub("main-origin");
        GitFixture.Git(main, "init");
        GitFixture.Write(Path.Combine(main, "m.txt"), "m");
        GitFixture.Git(main, "add", ".");
        GitFixture.Git(main, "commit", "-m", "m");
        GitFixture.Git(main, "submodule", "add", new Uri(Origin(fx, "healthy-origin")).AbsoluteUri, "external/healthy");
        GitFixture.Git(main, "commit", "-m", "add submodule");

        var script = Gen.GenerateBatScript(new Uri(main).AbsoluteUri, "main", new[] { "external" }, "checkout",
            initSubmodules: true, workingDirectory: fx.Root);
        var (exit, output) = RunBat(WriteScript(fx, "run.bat", script));

        Assert.True(exit == 0, $"expected exit code 0, got {exit}:{Environment.NewLine}{output}");
        Assert.True(File.Exists(Path.Combine(fx.Root, "checkout", "external", "healthy", "a.txt")));
    }

    [RequiresGitWindowsFact]
    public void Bat_exits_with_1_when_the_main_repository_is_unreachable()
    {
        using var fx = new GitFixture();

        var script = Gen.GenerateBatScript(new Uri(Path.Combine(fx.Root, "gone.git")).AbsoluteUri, "main",
            new[] { "external" }, "checkout", initSubmodules: true, workingDirectory: fx.Root);
        var (exit, output) = RunBat(WriteScript(fx, "run.bat", script));

        Assert.True(exit == 1, $"expected exit code 1, got {exit}:{Environment.NewLine}{output}");
        Assert.False(Directory.Exists(Path.Combine(fx.Root, "checkout")));
    }

    // ── .sh ───────────────────────────────────────────────────────────────

    private static string ForBash(string path) => path.Replace('\\', '/');

    [RequiresGitBashFact]
    public void Sh_exits_with_2_when_one_submodule_fails_and_keeps_the_healthy_one()
    {
        using var fx = new GitFixture();
        var main = MainWithHealthyAndBrokenSubmodule(fx);

        var script = Gen.GenerateShScript(new Uri(main).AbsoluteUri, "main", new[] { "external" }, "checkout",
            initSubmodules: true, workingDirectory: ForBash(fx.Root));
        var (exit, output) = RunSh(WriteScript(fx, "run.sh", script));

        Assert.True(exit == 2, $"expected exit code 2, got {exit}:{Environment.NewLine}{output}");
        AssertHealthyPopulatedAndBrokenNot(Path.Combine(fx.Root, "checkout"));
    }

    [RequiresGitBashFact]
    public void Sh_exits_with_1_when_the_main_repository_is_unreachable()
    {
        using var fx = new GitFixture();

        var script = Gen.GenerateShScript(new Uri(Path.Combine(fx.Root, "gone.git")).AbsoluteUri, "main",
            new[] { "external" }, "checkout", initSubmodules: true, workingDirectory: ForBash(fx.Root));
        var (exit, output) = RunSh(WriteScript(fx, "run.sh", script));

        Assert.True(exit == 1, $"expected exit code 1, got {exit}:{Environment.NewLine}{output}");
        Assert.False(Directory.Exists(Path.Combine(fx.Root, "checkout")));
    }
}
