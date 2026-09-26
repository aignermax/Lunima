using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using CAP_Core;
using CAP.Avalonia.Services.LiveState;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.LiveState;

/// <summary>
/// End-to-end tests for <see cref="LiveStateHttpServer"/>: real localhost HTTP
/// round-trips against a live <see cref="LiveStateApi"/>.
/// </summary>
public class LiveStateHttpServerTests
{
    private static LiveStateHttpServer CreateServer(out ErrorConsoleService errorConsole)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        errorConsole = new ErrorConsoleService();
        var api = new LiveStateApi(() => vm, errorConsole, new LiveStateCommandExecutor(),
            invokeOnUiThread: handler => handler());
        return new LiveStateHttpServer(api, errorConsole);
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public async Task StartServeStop_RoundTripsStatusRequest()
    {
        using var server = CreateServer(out _);
        int port = GetFreePort();

        server.Start(port).ShouldBeTrue();
        server.IsRunning.ShouldBeTrue();
        server.BaseUrl.ShouldBe($"http://127.0.0.1:{port}");

        using var client = new HttpClient();
        var response = await client.GetAsync($"{server.BaseUrl}/status");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Lunima");

        server.Stop();
        server.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var server = CreateServer(out _);
        server.Start(GetFreePort()).ShouldBeTrue();

        using var client = new HttpClient();
        var response = await client.GetAsync($"{server.BaseUrl}/does-not-exist");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Start_PortAlreadyTaken_ReturnsFalseAndLogs()
    {
        using var first = CreateServer(out _);
        using var second = CreateServer(out var errorConsole);
        int port = GetFreePort();

        first.Start(port).ShouldBeTrue();
        second.Start(port).ShouldBeFalse();
        second.IsRunning.ShouldBeFalse();
        errorConsole.Entries.ShouldContain(e => e.Message.Contains("could not bind"));
    }

    [Fact]
    public void ResolveConfiguredPort_InvalidEnvValue_FallsBackToDefault()
    {
        Environment.SetEnvironmentVariable(LiveStateHttpServer.PortEnvironmentVariable, "not-a-port");
        try
        {
            LiveStateHttpServer.ResolveConfiguredPort().ShouldBe(LiveStateHttpServer.DefaultPort);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveStateHttpServer.PortEnvironmentVariable, null);
        }
    }
}
