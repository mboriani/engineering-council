using EngineeringCouncil.Infrastructure.Evidence;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Regression tests for <see cref="ExecutableLocator"/> (Milestone 014.1): on Windows, a bare
/// configured executable like <c>opencode</c>/<c>codex</c> resolves to only npm shims on PATH
/// (<c>.cmd</c>), and launching the shim directly would route the child through <c>cmd.exe</c> —
/// re-parsing the command line and corrupting analysis-instruction text. These tests prove the
/// locator unwraps npm shims to a DIRECT invocation (native <c>.exe</c>, or <c>node &lt;script&gt;</c>)
/// and leaves explicit paths and non-Windows behavior untouched.
///
/// They share the NON-PARALLEL process-runner collection because they mutate the process PATH.
/// </summary>
[Collection(ProcessRunnerTestsShared.CollectionName)]
public sealed class ExecutableLocatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ec-locator-" + Guid.NewGuid().ToString("N"));
    private readonly string _savedPath;

    public ExecutableLocatorTests()
    {
        _savedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _savedPath);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string PrependToPath()
    {
        Environment.SetEnvironmentVariable("PATH", _tempDir + Path.PathSeparator + _savedPath);
        return _tempDir;
    }

    [Fact]
    public void Resolves_a_bare_name_to_a_real_exe_on_PATH()
    {
        if (!OperatingSystem.IsWindows()) return;

        var exe = Path.Combine(_tempDir, "tool.exe");
        File.WriteAllText(exe, string.Empty);
        PrependToPath();

        var resolved = ExecutableLocator.Resolve("tool");

        Assert.Equal(exe, resolved.FileName, ignoreCase: true);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Resolves_a_native_npm_shim_to_its_exe_target()
    {
        if (!OperatingSystem.IsWindows()) return;

        var bin = Path.Combine(_tempDir, "node_modules", "tool", "bin");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, "tool.exe");
        File.WriteAllText(exe, string.Empty);
        var shim = Path.Combine(_tempDir, "tool.cmd");
        File.WriteAllText(shim,
            "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\n"
            + "CALL :find_dp0\r\n\"%dp0%\\node_modules\\tool\\bin\\tool.exe\"   %*\r\n");
        PrependToPath();

        var resolved = ExecutableLocator.Resolve("tool");

        Assert.Equal(exe, resolved.FileName, ignoreCase: true);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Resolves_a_node_npm_shim_to_node_with_the_script_as_prefix()
    {
        if (!OperatingSystem.IsWindows()) return;

        var bin = Path.Combine(_tempDir, "node_modules", "tool", "bin");
        Directory.CreateDirectory(bin);
        var script = Path.Combine(bin, "cli.js");
        File.WriteAllText(script, "console.log('ok');\n");
        var shim = Path.Combine(_tempDir, "tool.cmd");
        File.WriteAllText(shim,
            "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\n"
            + "CALL :find_dp0\r\n\r\nIF EXIST \"%dp0%\\node.exe\" (\r\n  SET \"_prog=%dp0%\\node.exe\"\r\n"
            + ") ELSE (\r\n  SET \"_prog=node\"\r\n  SET PATHEXT=%PATHEXT:;.JS;=;%\r\n)\r\n\r\n"
            + "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & \"%_prog%\"  \"%dp0%\\node_modules\\tool\\bin\\cli.js\" %*\r\n");
        PrependToPath();

        var resolved = ExecutableLocator.Resolve("tool");

        Assert.True(File.Exists(resolved.FileName), $"node not resolved, got '{resolved.FileName}'");
        Assert.Equal("node.exe", Path.GetFileName(resolved.FileName), ignoreCase: true);
        Assert.Equal(script, Assert.Single(resolved.PrefixArguments));
    }

    [Fact]
    public void Resolves_an_unknown_shim_to_the_shim_itself()
    {
        if (!OperatingSystem.IsWindows()) return;

        var shim = Path.Combine(_tempDir, "tool.cmd");
        File.WriteAllText(shim, "@echo off\r\ncustom-wrapper %*\r\n");
        PrependToPath();

        var resolved = ExecutableLocator.Resolve("tool");

        Assert.Equal(shim, resolved.FileName, ignoreCase: true);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Passes_through_an_absolute_path_unchanged()
    {
        var resolved = ExecutableLocator.Resolve(@"C:\some\dir\tool.exe");
        Assert.Equal(@"C:\some\dir\tool.exe", resolved.FileName);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Passes_through_a_relative_path_with_a_separator_unchanged()
    {
        var resolved = ExecutableLocator.Resolve("tools/my-tool.exe");
        Assert.Equal("tools/my-tool.exe", resolved.FileName);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void Returns_the_name_unchanged_when_not_on_PATH()
    {
        var name = "no-such-tool-" + Guid.NewGuid().ToString("N");
        var resolved = ExecutableLocator.Resolve(name);
        Assert.Equal(name, resolved.FileName);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public async Task Runner_launches_a_shim_resolved_node_target_without_cmd_parsing_the_prompt()
    {
        // M14.1 end-to-end regression: an npm-installed CLI on Windows is found as a .cmd shim.
        // The runner must unwrap it to `node <script>` so the analysis instruction (which may
        // contain cmd metacharacters) is delivered via ArgumentList intact — never re-parsed by
        // cmd.exe. If the shim were launched directly, the runner's own probe proved the batch
        // line breaks on these characters.
        if (!OperatingSystem.IsWindows()) return;

        var bin = Path.Combine(_tempDir, "node_modules", "tool", "bin");
        Directory.CreateDirectory(bin);
        var script = Path.Combine(bin, "cli.js");
        File.WriteAllText(script, "console.log(process.argv.slice(2).join('\\n'));\n");
        File.WriteAllText(Path.Combine(_tempDir, "tool.cmd"),
            "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\n"
            + "CALL :find_dp0\r\n\"%_prog%\"  \"%dp0%\\node_modules\\tool\\bin\\cli.js\" %*\r\n"
            + "endLocal & goto #_undefined_# 2>NUL || title %COMSPEC%\r\n");
        PrependToPath();

        const string promptWithCmdMetacharacters = "run --model deepseek/v4 100%PATH%&|^!\"<>>test";
        var runner = new OpenCodeProcessRunner();
        var result = await runner.RunAsync(new OpenCodeProcessRequest
        {
            Executable = "tool",
            WorkingDirectory = _tempDir,
            Arguments = [promptWithCmdMetacharacters],
            Timeout = TimeSpan.FromSeconds(30)
        });

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(promptWithCmdMetacharacters, result.StandardOutput, StringComparison.Ordinal);
    }
}
