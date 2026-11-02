using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using CAP.Avalonia;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP.Avalonia.Services.AgentBridge.Registration;

namespace CAP.Desktop;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Fix de-DE (and any locale that uses ',' as decimal separator) slider-serialization bug:
        // force numeric parse/format on all threads to use '.' so exported values round-trip
        // correctly regardless of the OS locale.
        // DefaultThreadCurrentUICulture is intentionally left alone so that date, time, and
        // number *display* in the UI stays in the user's own locale — only the export/parse
        // path needs to be locale-independent.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        // Installer hooks (MSI "Connect Claude Code" feature): run headless and exit.
        if (AgentBridgeCommandLine.IsRegistrationCommand(args))
            return AgentBridgeCommandLine.RunAsync(args).GetAwaiter().GetResult();

        // Agent interface without a window: `mcp` (stdio MCP server) and `agent …` (one-shot CLI).
        if (AgentCliCommand.IsAgentCommand(args))
        {
            AttachToCallingConsole();
            return HeadlessAgentProgram.Run(args, BuildHeadlessAvaloniaApp);
        }

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    /// <summary>
    /// On Windows this executable is a GUI-subsystem app: started from cmd/PowerShell without
    /// redirection its output would vanish. Attach to the caller's console then; piped use
    /// (Claude Code, scripts, MCP) is already redirected and stays untouched.
    /// </summary>
    private static void AttachToCallingConsole()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
            AttachConsole(AttachParentProcess);
    }

    /// <summary>The app on Avalonia's headless platform, with Skia so screenshots have real pixels.</summary>
    private static AppBuilder BuildHeadlessAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .LogToTrace();
}
