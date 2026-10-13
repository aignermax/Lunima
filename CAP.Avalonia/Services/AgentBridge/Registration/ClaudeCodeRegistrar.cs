using System.Diagnostics;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;

namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>Outcome of registering or unregistering the bridge with Claude Code.</summary>
/// <param name="Success">Whether the <c>claude</c> CLI reported success.</param>
/// <param name="CliFound">False when the <c>claude</c> CLI is not installed (the caller shows the manual command).</param>
/// <param name="Output">What the CLI printed, or why nothing ran.</param>
public sealed record ClaudeRegistrationResult(bool Success, bool CliFound, string Output);

/// <summary>
/// Registers the agent bridge as the user-scope MCP server <c>lunima</c> in Claude Code by
/// running its CLI (<c>claude mcp remove</c>, then <c>claude mcp add</c>, so a port or token
/// change re-registers cleanly). Every launch goes through <see cref="ProcessLaunchFactory"/>
/// with the absolute path from <see cref="ClaudeCliLocator"/>; the home-folder variables are
/// passed explicitly so the CLI writes the right user's config even from an installer.
/// </summary>
public sealed class ClaudeCodeRegistrar
{
    /// <summary>Runs one CLI process; replaced in tests.</summary>
    public delegate Task<CliRunResult> CliRunner(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Longest a single <c>claude mcp …</c> call may take.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly ProcessLaunchFactory _launchFactory;
    private readonly ClaudeCliLocator _locator;
    private readonly CliRunner _run;

    /// <summary>Creates the registrar.</summary>
    /// <param name="launchFactory">Builds every process start (cross-platform rule).</param>
    /// <param name="locator">Finds the <c>claude</c> executable; defaults to one using the factory's PATH augmentation.</param>
    /// <param name="run">Runs a built process; defaults to <see cref="CliProcessRunner.RunAsync"/>.</param>
    public ClaudeCodeRegistrar(ProcessLaunchFactory launchFactory, ClaudeCliLocator? locator = null, CliRunner? run = null)
    {
        _launchFactory = launchFactory;
        _locator = locator ?? new ClaudeCliLocator(launchFactory.Prober.AugmentedPath);
        _run = run ?? CliProcessRunner.RunAsync;
    }

    /// <summary>The <c>claude</c> executable, or null when Claude Code is not installed.</summary>
    public string? FindCli() => _locator.Locate();

    /// <summary>
    /// Registers <c>lunima</c> → <c>http://127.0.0.1:port/mcp</c> for the current user, replacing
    /// any earlier registration. The <c>Authorization</c> header is only sent when <paramref name="token"/> is set.
    /// </summary>
    public async Task<ClaudeRegistrationResult> RegisterAsync(int port, string? token, CancellationToken cancellationToken = default)
    {
        var cli = FindCli();
        if (cli == null)
            return NotFound();

        await RunAsync(cli, RemoveArguments, cancellationToken);
        var added = await RunAsync(cli, AgentBridgeHost.ClaudeCodeAddArguments(port, token), cancellationToken);
        return new ClaudeRegistrationResult(added.Succeeded, true, added.Output);
    }

    /// <summary>Removes the <c>lunima</c> registration for the current user (best effort).</summary>
    public async Task<ClaudeRegistrationResult> UnregisterAsync(CancellationToken cancellationToken = default)
    {
        var cli = FindCli();
        if (cli == null)
            return NotFound();

        var removed = await RunAsync(cli, RemoveArguments, cancellationToken);
        return new ClaudeRegistrationResult(removed.Succeeded, true, removed.Output);
    }

    /// <summary>Whether Claude Code already knows <c>lunima</c>; null when the CLI is missing.</summary>
    public async Task<bool?> IsRegisteredAsync(CancellationToken cancellationToken = default)
    {
        var cli = FindCli();
        if (cli == null)
            return null;
        var result = await RunAsync(cli, new[] { "mcp", "get", AgentBridgeHost.McpServerName }, cancellationToken);
        return result.Succeeded;
    }

    private static IReadOnlyList<string> RemoveArguments =>
        new[] { "mcp", "remove", "--scope", "user", AgentBridgeHost.McpServerName };

    private static ClaudeRegistrationResult NotFound() =>
        new(false, false, "The Claude Code CLI (claude) was not found.");

    private async Task<CliRunResult> RunAsync(string cli, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!_launchFactory.TryBuild(cli, arguments, HomeDirectory(), HomeEnvironment(), out var startInfo, out var error))
            return new CliRunResult(CliProcessRunner.StartFailedExitCode, error ?? "Could not build the process.");
        return await _run(startInfo, CallTimeout, cancellationToken);
    }

    private static string? HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : null;
    }

    /// <summary>
    /// The current user's home folders, resolved from the process token rather than inherited
    /// variables — an installer's impersonated custom action can inherit the service's profile.
    /// </summary>
    private static IReadOnlyDictionary<string, string> HomeEnvironment()
    {
        var environment = new Dictionary<string, string>();
        AddFolder(environment, OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME", Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            AddFolder(environment, "APPDATA", Environment.SpecialFolder.ApplicationData);
            AddFolder(environment, "LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);
        }
        return environment;
    }

    private static void AddFolder(Dictionary<string, string> environment, string name, Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        if (!string.IsNullOrEmpty(path))
            environment[name] = path;
    }
}
