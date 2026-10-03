using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class CommandGeneratorTests
{
    private readonly CommandGenerator _gen = new();

    private static string[] Lines(string s) => s.Split("\r\n");

    [Fact]
    public void Bat_sets_utf8_codepage_on_line_two()
    {
        var bat = _gen.GenerateBatScript("https://x/r.git", "main", new[] { "apps" }, "r");
        var lines = Lines(bat);
        Assert.Equal("@echo off", lines[0]);
        Assert.Equal("chcp 65001 >nul", lines[1]);
    }

    [Fact]
    public void Bat_doubles_percent_signs_and_quotes_branch()
    {
        var bat = _gen.GenerateBatScript("https://x/r%20.git", "feat/100%", new[] { "a%b" }, "dir%1", "new%branch");

        Assert.Contains("\"https://x/r%%20.git\"", bat);
        Assert.Contains("git checkout \"feat/100%%\"", bat);
        Assert.Contains("git checkout -b \"new%%branch\"", bat);
        Assert.Contains("\"a%%b\"", bat);
        Assert.Contains("\"dir%%1\"", bat);
    }

    [Fact]
    public void Bat_checks_errorlevel_after_commands_and_has_failed_label()
    {
        var bat = _gen.GenerateBatScript("https://x/r.git", "main", new[] { "apps" }, "r");

        Assert.True(Lines(bat).Count(l => l == "if errorlevel 1 goto :failed") >= 4);
        Assert.Contains(":failed", bat);
        Assert.Contains("exit /b 1", bat);
    }

    [Fact]
    public void Bat_exit_code_2_block_only_when_submodules_enabled()
    {
        var without = _gen.GenerateBatScript("https://x/r.git", "main", new[] { "apps" }, "r");
        var with = _gen.GenerateBatScript("https://x/r.git", "main", new[] { "apps" }, "r", initSubmodules: true);

        Assert.DoesNotContain("exit /b 2", without);
        Assert.Contains("if defined SUBFAIL (", with);
        Assert.Contains("exit /b 2", with);
    }

    [Fact]
    public void Sh_has_no_carriage_returns_and_single_quotes_values()
    {
        var sh = _gen.GenerateShScript("https://x/r.git", "feature/x", new[] { "apps/web" }, "r", "topic", initSubmodules: true);

        Assert.DoesNotContain('\r', sh);
        Assert.StartsWith("#!/bin/bash\n", sh);
        Assert.Contains("git clone --filter=blob:none --no-checkout 'https://x/r.git' 'r' || fail", sh);
        Assert.Contains("git checkout 'feature/x' || fail", sh);
        Assert.Contains("git checkout -b 'topic' || fail", sh);
        Assert.Contains("'apps/web'", sh);
        Assert.Contains("exit 2", sh);
    }

    [Fact]
    public void Sh_escapes_single_quotes()
    {
        var sh = _gen.GenerateShScript("https://x/r.git", "it's", new[] { "o'neil" }, "r");

        Assert.Contains("'it'\\''s'", sh);
        Assert.Contains("'o'\\''neil'", sh);
    }

    [Fact]
    public void Full_bat_is_a_plain_clone_of_the_branch()
    {
        var bat = _gen.GenerateBatScript("https://x/r.git", "main", Array.Empty<string>(), "r", "topic", fullClone: true);

        Assert.Contains("git clone --branch \"main\" \"https://x/r.git\" \"r\"", bat);
        Assert.Contains("cd /d \"r\"", bat);
        Assert.Contains("git checkout -b \"topic\"", bat);
        Assert.DoesNotContain("sparse-checkout", bat);
        Assert.DoesNotContain("--filter", bat);
        Assert.DoesNotContain("--no-checkout", bat);
        Assert.DoesNotContain("--recurse-submodules", bat);
        Assert.Equal("chcp 65001 >nul", Lines(bat)[1]);
    }

    [Fact]
    public void Full_scripts_include_submodule_loop_only_when_enabled()
    {
        var batOff = _gen.GenerateBatScript("https://x/r.git", "main", Array.Empty<string>(), "r", fullClone: true);
        var batOn = _gen.GenerateBatScript("https://x/r.git", "main", Array.Empty<string>(), "r", initSubmodules: true, fullClone: true);
        var shOff = _gen.GenerateShScript("https://x/r.git", "main", Array.Empty<string>(), "r", fullClone: true);
        var shOn = _gen.GenerateShScript("https://x/r.git", "main", Array.Empty<string>(), "r", initSubmodules: true, fullClone: true);

        Assert.DoesNotContain(".gitmodules", batOff);
        Assert.DoesNotContain(".gitmodules", shOff);
        Assert.Contains("if exist .gitmodules (", batOn);
        Assert.Contains("WARNING:", batOn);
        Assert.Contains("exit /b 2", batOn);
        Assert.Contains("if [ -f .gitmodules ]", shOn);
        Assert.Contains("exit 2", shOn);
    }

    [Fact]
    public void Full_sh_has_no_carriage_returns_and_no_sparse_commands()
    {
        var sh = _gen.GenerateShScript("https://x/r.git", "it's", Array.Empty<string>(), "r", "topic", initSubmodules: true, fullClone: true);

        Assert.DoesNotContain('\r', sh);
        Assert.Contains("git clone --branch 'it'\\''s' 'https://x/r.git' 'r' || fail", sh);
        Assert.Contains("git checkout -b 'topic' || fail", sh);
        Assert.DoesNotContain("sparse-checkout", sh);
        Assert.DoesNotContain("--filter", sh);
    }

    [Fact]
    public void Manage_scripts_follow_same_rules()
    {
        var bat = _gen.GenerateManageBatScript(@"C:\repo%1", new[] { "apps" }, removedPaths: new[] { "docs" });
        var sh = _gen.GenerateManageShScript("/repo", new[] { "apps" }, removedPaths: new[] { "it's" });

        Assert.Equal("chcp 65001 >nul", Lines(bat)[1]);
        Assert.Contains("cd /d \"C:\\repo%%1\"", bat);
        Assert.DoesNotContain('\r', sh);
        Assert.Contains("'it'\\''s'", sh);
    }
}
