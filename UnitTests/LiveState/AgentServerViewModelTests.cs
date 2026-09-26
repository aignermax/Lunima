using System.Net;
using System.Net.Sockets;
using CAP_Core;
using CAP.Avalonia.Services.LiveState;
using CAP.Avalonia.ViewModels.LiveState;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.LiveState;

/// <summary>
/// Tests for <see cref="AgentServerViewModel"/>: start/stop toggling, endpoint
/// display, and the generated MCP config snippet.
/// </summary>
public class AgentServerViewModelTests
{
    private static AgentServerViewModel CreateViewModel()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var errorConsole = new ErrorConsoleService();
        var api = new LiveStateApi(() => vm, errorConsole, new LiveStateCommandExecutor(),
            invokeOnUiThread: handler => handler());
        return new AgentServerViewModel(new LiveStateHttpServer(api, errorConsole));
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public void Toggle_StartsThenStopsServer()
    {
        var viewModel = CreateViewModel();
        viewModel.Port = GetFreePort();

        viewModel.ToggleServerCommand.Execute(null);
        viewModel.IsRunning.ShouldBeTrue();
        viewModel.StatusText.ShouldContain(viewModel.EndpointUrl);

        viewModel.ToggleServerCommand.Execute(null);
        viewModel.IsRunning.ShouldBeFalse();
        viewModel.StatusText.ShouldBe("Stopped");
    }

    [Fact]
    public void EndpointUrl_ReflectsPort()
    {
        var viewModel = CreateViewModel();
        viewModel.Port = 6100;
        viewModel.EndpointUrl.ShouldBe("http://127.0.0.1:6100");
    }

    [Fact]
    public void McpConfigSnippet_ContainsBridgePathAndPort()
    {
        var viewModel = CreateViewModel();
        viewModel.Port = 6200;
        viewModel.McpConfigSnippet.ShouldContain("mcp-servers/livestate/server.py");
        viewModel.McpConfigSnippet.ShouldContain("\"6200\"");
    }

    [Fact]
    public void AutoStart_WithoutEnvVariable_StaysStopped()
    {
        Environment.SetEnvironmentVariable(AgentServerViewModel.AutoStartEnvironmentVariable, null);
        var viewModel = CreateViewModel();

        viewModel.AutoStartFromEnvironment();

        viewModel.IsRunning.ShouldBeFalse();
    }
}
