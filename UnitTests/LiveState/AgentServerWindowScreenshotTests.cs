using System.Net;
using System.Net.Sockets;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP_Core;
using CAP.Avalonia.Services.LiveState;
using CAP.Avalonia.ViewModels.LiveState;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.UI;
using Xunit;

namespace UnitTests.LiveState;

/// <summary>
/// Headless render of <see cref="AgentServerWindow"/> in stopped and running state.
/// Catches AXAML/binding regressions; PNGs land in UI_SHOT_DIR when set (opt-in,
/// same convention as <see cref="UnitTests.UI.UiScreenshotTests"/>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class AgentServerWindowScreenshotTests
{
    [AvaloniaFact]
    public void CaptureAgentServerWindowStates()
    {
        var outputDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(outputDir))
            return;
        Directory.CreateDirectory(outputDir);

        var mainVm = MainViewModelTestHelper.CreateMainViewModel();
        var errorConsole = new ErrorConsoleService();
        var api = new LiveStateApi(() => mainVm, errorConsole, new LiveStateCommandExecutor(),
            invokeOnUiThread: handler => handler());
        using var server = new LiveStateHttpServer(api, errorConsole);
        var viewModel = new AgentServerViewModel(server);

        var window = new AgentServerWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDir, "01-agent-server-stopped.png"));

        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        viewModel.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        viewModel.ToggleServerCommand.Execute(null);
        viewModel.IsRunning.ShouldBeTrue();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(outputDir, "02-agent-server-running.png"));

        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(AgentServerWindow window, string path)
    {
        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"Render miss for {path}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap, path);
        new FileInfo(path).Length.ShouldBeGreaterThan(0);
    }
}
