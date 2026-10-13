using System.Globalization;
using Avalonia;
using CAP.Avalonia;
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

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
