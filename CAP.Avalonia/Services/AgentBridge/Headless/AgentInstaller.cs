using System.Text;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.Services.AgentBridge.Registration;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>Where the launcher was written and whether a shell finds it by name.</summary>
/// <param name="LauncherPath">The <c>lunima</c> launcher file.</param>
/// <param name="OnPath">Whether its folder is on <c>PATH</c>.</param>
public sealed record AgentInstallResult(string LauncherPath, bool OnPath);

/// <summary>
/// <c>lunima agent install</c>: makes the headless engine callable as <c>lunima</c> — a small
/// launcher that forwards to this executable (<c>~/.local/bin/lunima</c> on Linux/macOS,
/// <c>%LOCALAPPDATA%\Lunima\bin\lunima.cmd</c> on Windows) — and optionally registers the stdio
/// MCP server with Claude Code. The Lunima Claude Code plugin starts <c>lunima mcp</c>.
/// </summary>
public static class AgentInstaller
{
    /// <summary>Claude Code server name of the headless engine (the desktop bridge is <c>lunima</c>).</summary>
    public const string HeadlessServerName = "lunima-headless";

    private const string LauncherName = "lunima";

    /// <summary>Writes the launcher for <paramref name="executable"/>, replacing an older one.</summary>
    /// <param name="executable">The Lunima executable the launcher forwards to.</param>
    /// <param name="folder">Target folder; <see cref="LauncherFolder"/> when null.</param>
    public static AgentInstallResult InstallLauncher(string executable, string? folder = null)
    {
        folder ??= LauncherFolder();
        Directory.CreateDirectory(folder);
        string path;
        if (OperatingSystem.IsWindows())
        {
            path = Path.Combine(folder, LauncherName + ".cmd");
            File.WriteAllText(path, $"@echo off\r\n\"{executable}\" %*\r\n", new UTF8Encoding(false));
        }
        else
        {
            path = Path.Combine(folder, LauncherName);
            File.WriteAllText(path, $"#!/bin/sh\nexec \"{executable}\" \"$@\"\n", new UTF8Encoding(false));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return new AgentInstallResult(path, IsOnPath(folder));
    }

    /// <summary>Registers <c>lunima-headless</c> → <paramref name="executable"/> <c>mcp</c> with Claude Code.</summary>
    public static Task<ClaudeRegistrationResult> RegisterAsync(ClaudeCodeRegistrar registrar, string executable) =>
        registrar.RegisterStdioAsync(HeadlessServerName, executable, new[] { AgentCliCommand.McpVerb });

    /// <summary>Folder of the launcher on this OS.</summary>
    public static string LauncherFolder() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "bin")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    private static bool IsOnPath(string folder) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(entry => string.Equals(Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
}
