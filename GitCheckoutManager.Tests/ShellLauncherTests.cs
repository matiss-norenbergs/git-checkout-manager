using System.Diagnostics;
using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class ShellLauncherTests
{
    private sealed class FakeEnv : IShellEnvironment
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? CodeCmdOnPath { get; set; }
        public List<ProcessStartInfo> Started { get; } = new();
        public Exception? StartThrows { get; set; }

        public bool FileExists(string path) => Files.Contains(path);
        public bool DirectoryExists(string path) => Dirs.Contains(path);
        public string? FindOnPath(string fileName) => fileName == "code.cmd" ? CodeCmdOnPath : null;
        public string? GetEnvironmentVariable(string name) => Vars.GetValueOrDefault(name);

        public void Start(ProcessStartInfo startInfo)
        {
            if (StartThrows != null) throw StartThrows;
            Started.Add(startInfo);
        }
    }

    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gcm-shell"));
    private static string LocalCode => Path.Combine(Root, "Local", "Programs", "Microsoft VS Code", "Code.exe");
    private static string ProgramCode => Path.Combine(Root, "PF", "Microsoft VS Code", "Code.exe");

    private static FakeEnv Env()
    {
        var env = new FakeEnv();
        env.Vars["LocalAppData"] = Path.Combine(Root, "Local");
        env.Vars["ProgramFiles"] = Path.Combine(Root, "PF");
        return env;
    }

    // ── VS Code detection ────────────────────────────────────────────────

    [Fact]
    public void Nothing_found_hides_vscode_and_open_fails_cleanly()
    {
        var env = Env();
        env.Dirs.Add(Root);
        var launcher = new ShellLauncher(env);

        Assert.False(launcher.IsVsCodeAvailable);
        var result = launcher.OpenInVsCode(Root);
        Assert.False(result.Success);
        Assert.Empty(env.Started);
    }

    [Fact]
    public void Code_cmd_on_path_resolves_to_Code_exe_one_folder_up_from_bin()
    {
        var env = Env();
        var install = Path.Combine(Root, "custom", "VS Code");
        env.CodeCmdOnPath = Path.Combine(install, "bin", "code.cmd");
        env.Files.Add(Path.Combine(install, "Code.exe"));
        env.Files.Add(LocalCode); // would lose to PATH
        env.Dirs.Add(Root);

        var launcher = new ShellLauncher(env);
        Assert.True(launcher.IsVsCodeAvailable);
        Assert.True(launcher.OpenInVsCode(Root).Success);

        Assert.Equal(Path.Combine(install, "Code.exe"), env.Started.Single().FileName);
    }

    [Fact]
    public void Never_starts_code_cmd_even_when_Code_exe_is_missing_next_to_it()
    {
        var env = Env();
        env.CodeCmdOnPath = Path.Combine(Root, "x", "bin", "code.cmd");
        env.Dirs.Add(Root);
        var launcher = new ShellLauncher(env);

        Assert.False(launcher.IsVsCodeAvailable);
        launcher.OpenInVsCode(Root);
        Assert.Empty(env.Started);
    }

    [Fact]
    public void Falls_back_to_local_app_data_when_path_has_no_usable_code_cmd()
    {
        var env = Env();
        env.CodeCmdOnPath = Path.Combine(Root, "x", "bin", "code.cmd"); // no Code.exe beside it
        env.Files.Add(LocalCode);
        env.Files.Add(ProgramCode);
        env.Dirs.Add(Root);

        new ShellLauncher(env).OpenInVsCode(Root);
        Assert.Equal(LocalCode, env.Started.Single().FileName);
    }

    [Fact]
    public void Falls_back_to_program_files_last()
    {
        var env = Env();
        env.Files.Add(ProgramCode);
        env.Dirs.Add(Root);

        new ShellLauncher(env).OpenInVsCode(Root);
        Assert.Equal(ProgramCode, env.Started.Single().FileName);
    }

    [Fact]
    public void Detection_result_is_cached()
    {
        var env = Env();
        env.Files.Add(LocalCode);
        var launcher = new ShellLauncher(env);
        Assert.True(launcher.IsVsCodeAvailable);

        env.Files.Clear(); // uninstalled mid-run: the cached answer stands
        Assert.True(launcher.IsVsCodeAvailable);
    }

    // ── Launch arguments ─────────────────────────────────────────────────

    [Fact]
    public void Path_with_percent_ampersand_and_spaces_is_one_argument()
    {
        var env = Env();
        env.Files.Add(LocalCode);
        var folder = Path.Combine(Root, "50% off & more ^ caret", "my repo");
        env.Dirs.Add(folder);
        var launcher = new ShellLauncher(env);

        Assert.True(launcher.OpenInExplorer(folder).Success);
        Assert.True(launcher.OpenInVsCode(folder).Success);

        Assert.Equal(2, env.Started.Count);
        foreach (var psi in env.Started)
        {
            Assert.Equal(folder, Assert.Single(psi.ArgumentList));
            Assert.Equal(string.Empty, psi.Arguments);
            Assert.False(psi.UseShellExecute);
        }
    }

    [Fact]
    public void Unicode_path_is_passed_unchanged()
    {
        var env = Env();
        var folder = Path.Combine(Root, "projekti", "ābolu āķis ☃");
        env.Dirs.Add(folder);

        new ShellLauncher(env).OpenInExplorer(folder);

        Assert.Equal(folder, Assert.Single(env.Started.Single().ArgumentList));
    }

    [Fact]
    public void Explorer_is_started_by_name_and_forward_slashes_are_normalised()
    {
        var env = Env();
        var folder = Path.Combine(Root, "a", "b");
        env.Dirs.Add(folder);

        var launcher = new ShellLauncher(env);
        Assert.True(launcher.OpenInExplorer(Root.Replace('\\', '/') + "/a/b").Success);

        var psi = env.Started.Single();
        Assert.Equal("explorer.exe", psi.FileName);
        Assert.Equal(folder, Assert.Single(psi.ArgumentList));
    }

    [Fact]
    public void Missing_folder_is_an_error_and_nothing_starts()
    {
        var env = Env();
        var result = new ShellLauncher(env).OpenInExplorer(Path.Combine(Root, "gone"));

        Assert.False(result.Success);
        Assert.Contains("doesn't exist", result.Error);
        Assert.Empty(env.Started);
    }

    [Fact]
    public void Launch_failure_becomes_a_message_not_an_exception()
    {
        var env = Env();
        env.Dirs.Add(Root);
        env.StartThrows = new InvalidOperationException("boom");

        var result = new ShellLauncher(env).OpenInExplorer(Root);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
    }

    // ── FolderOpener (shared by the Manage tree and the Submodules window) ──

    private sealed class FakeLauncher : IShellLauncher
    {
        public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Opened { get; } = new();
        public bool IsVsCodeAvailable { get; set; } = true;
        public bool FolderExists(string path) => Folders.Contains(path);
        public LaunchResult OpenInExplorer(string folder) { Opened.Add("explorer:" + folder); return LaunchResult.Ok; }
        public LaunchResult OpenInVsCode(string folder) { Opened.Add("code:" + folder); return LaunchResult.Ok; }
    }

    [Fact]
    public void Folder_not_on_disk_is_reported_as_not_on_disk_and_open_is_refused()
    {
        var launcher = new FakeLauncher();
        var opener = new FolderOpener(launcher);

        Assert.False(opener.IsOnDisk(Root, "src/missing"));
        var message = opener.Open(Root, "src/missing", vsCode: false);

        Assert.NotNull(message);
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public void Tree_node_that_is_not_on_disk_has_a_tooltip_explaining_the_disabled_action()
    {
        var node = new GitCheckoutManager.ViewModels.TreeNodeViewModel(
            new GitCheckoutManager.Models.TreeNode { Path = "src", Name = "src", Type = "tree" });

        Assert.True(node.IsOnDisk); // optimistic until a context menu opens
        Assert.Null(node.OnDiskToolTip);

        node.IsOnDisk = new FolderOpener(new FakeLauncher()).IsOnDisk(Root, node.FullPath);

        Assert.False(node.IsOnDisk);
        Assert.NotNull(node.OnDiskToolTip);
    }

    [Fact]
    public void On_disk_state_is_read_each_time_not_cached()
    {
        var launcher = new FakeLauncher();
        var opener = new FolderOpener(launcher);
        var folder = FolderOpener.Resolve(Root, "src/app");

        Assert.False(opener.IsOnDisk(Root, "src/app"));
        launcher.Folders.Add(folder); // applied since the tree loaded
        Assert.True(opener.IsOnDisk(Root, "src/app"));
    }

    [Fact]
    public void Open_resolves_relative_git_path_under_the_root_and_empty_path_is_the_root()
    {
        var launcher = new FakeLauncher();
        launcher.Folders.Add(Path.Combine(Root, "src", "app"));
        launcher.Folders.Add(Root);
        var opener = new FolderOpener(launcher);

        Assert.Null(opener.Open(Root, "src/app", vsCode: true));
        Assert.Null(opener.Open(Root, "", vsCode: false));

        Assert.Equal(new[] { "code:" + Path.Combine(Root, "src", "app"), "explorer:" + Root }, launcher.Opened);
    }
}
