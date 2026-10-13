using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Hosting;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.UI;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// Opt-in artifact capture of the "Agent bridge (MCP)" settings page for the PR: renders the
/// real SettingsWindow with the bridge enabled on a free port and writes PNGs + manifest.json
/// to <c>UI_SHOT_DIR/agent-bridge/</c>. No-op unless <c>UI_SHOT_DIR</c> is set.
/// </summary>
[Collection("LocalizationSingleton")]
public class AgentBridgeSettingsScreenshotTests
{
    private const string Manifest = """
        [
          {"file": "01-agent-bridge-settings-en.png", "caption": "Settings → Agent bridge (MCP), enabled: status line, port, masked token, copyable Claude Code connect command."},
          {"file": "02-agent-bridge-settings-de.png", "caption": "The same page in German."}
        ]
        """;

    [Trait("Category", "UiScreenshots")]
    [AvaloniaFact]
    public void CaptureAgentBridgeSettingsPage()
    {
        var shotDir = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(shotDir))
            return;
        var outputDir = Path.Combine(shotDir, "agent-bridge");
        Directory.CreateDirectory(outputDir);

        var preferences = new UserPreferencesService(Path.GetTempFileName());
        preferences.SetAgentBridgePort(FreePort());
        preferences.SetAgentBridgeEnabled(true);
        var host = new AgentBridgeHost(preferences, new McpHttpServer(
            new McpHttpEndpoint(AgentBridgeTestStack.Dispatcher(), preferences.GetOrCreateAgentBridgeToken)));
        host.ApplySettingsAsync().GetAwaiter().GetResult();

        var previous = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            Capture(preferences, host, Path.Combine(outputDir, "01-agent-bridge-settings-en.png"));
            LocalizationService.Instance.SetLanguage(SupportedLanguage.German.Code);
            Capture(preferences, host, Path.Combine(outputDir, "02-agent-bridge-settings-de.png"));
            ScreenshotArtifacts.WriteText(Path.Combine(outputDir, "manifest.json"), Manifest);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
            host.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static void Capture(UserPreferencesService preferences, AgentBridgeHost host, string path)
    {
        using var vm = new AgentBridgeSettingsViewModel(preferences, host);
        var page = new AgentBridgeSettingsPage(vm, LocalizationService.Instance);
        var window = new SettingsWindow { DataContext = new SettingsWindowViewModel(new ISettingsPage[] { page }) };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var bitmap = window.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull();
            ScreenshotArtifacts.SavePng(bitmap!, path).Length.ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
