using System.Diagnostics;

namespace GitCheckoutManager.Tests;

/// <summary>Fact that skips itself when git isn't on PATH.</summary>
public sealed class RequiresGitFactAttribute : FactAttribute
{
    public RequiresGitFactAttribute()
    {
        if (!GitFixture.GitAvailable) Skip = "git is not available on PATH";
    }
}

/// <summary>A scratch folder, deleted on dispose, with helpers to drive real git.</summary>
public sealed class GitFixture : IDisposable
{
    public static readonly bool GitAvailable = Probe();

    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "gsm-tests-" + Guid.NewGuid().ToString("N"));

    public GitFixture() => Directory.CreateDirectory(Root);

    public string Sub(string name)
    {
        var p = Path.Combine(Root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Deterministic identity; the file transport is only enabled for these throwaway repos.
        foreach (var a in new[]
        {
            "-c", "user.name=Test", "-c", "user.email=test@example.com",
            "-c", "protocol.file.allow=always", "-c", "commit.gpgsign=false",
            "-c", "core.autocrlf=false", "-c", "init.defaultBranch=main",
        }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var proc = Process.Start(psi)!;
        var err = proc.StandardError.ReadToEndAsync();
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {err.Result}");
        return output;
    }

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static bool Probe()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try
        {
            // git marks object files read-only, which blocks Directory.Delete on Windows.
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch { /* best effort */ }
    }
}
