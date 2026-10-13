using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;

namespace CAP.Avalonia.Services.AgentBridge.Registration;

/// <summary>
/// Headless command-line switches used by installers (the Windows MSI's optional
/// "Connect Claude Code" feature) — they run before any window is created and exit:
/// <list type="bullet">
///   <item><c>--register-claude-code</c>: enables the agent bridge in the user's preferences
///   and registers it with Claude Code (when the CLI is installed).</item>
///   <item><c>--unregister-claude-code</c>: removes the registration (best effort).</item>
/// </list>
/// Both always exit with code 0: a missing Claude Code must never fail an install or uninstall.
/// </summary>
public static class AgentBridgeCommandLine
{
    /// <summary>Switch that enables the bridge and registers it with Claude Code.</summary>
    public const string RegisterSwitch = "--register-claude-code";

    /// <summary>Switch that removes the Claude Code registration.</summary>
    public const string UnregisterSwitch = "--unregister-claude-code";

    /// <summary>Whether <paramref name="args"/> asks for a headless registration action.</summary>
    public static bool IsRegistrationCommand(IEnumerable<string> args) =>
        args.Any(a => a is RegisterSwitch or UnregisterSwitch);

    /// <summary>Runs the requested action and returns the process exit code (always 0).</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        var registrar = new ClaudeCodeRegistrar(ProcessLaunchFactory.CreateDefault());
        if (args.Contains(UnregisterSwitch))
        {
            await registrar.UnregisterAsync();
            return 0;
        }

        var preferences = new UserPreferencesService();
        preferences.SetAgentBridgeEnabled(true);
        await registrar.RegisterAsync(preferences.GetAgentBridgePort(), preferences.GetRequiredAgentBridgeToken());
        return 0;
    }
}
