using CAP.Avalonia.Services.AgentBridge.Protocol;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Transport;
using CAP.Avalonia.Services.AiTools;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class TransportAcceptRetirementTests
{
    [Fact]
    public async Task UnexpectedAcceptFailure_StillDrainsAcceptedConnections()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("unexpected accept failure");
        using var session = new McpServerSession(new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0));
        session.AcceptLoop = Task.FromException(failure);
        session.Track(() => release.Task);
        var draining = session.DrainAsync();
        try
        {
            draining.IsCompleted.ShouldBeFalse("accepted connections remain owned after a listener fault");
        }
        finally { release.TrySetResult(); }
        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => draining.WaitAsync(BridgeLifecycleTestHarness.Guard));
        error.ShouldBeSameAs(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopBetweenAcceptCheckAndCall_DrainsAndAllowsRestart(bool restartFirst)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new AgentToolCatalog([], new AiToolRegistry([]), new InlineUiDispatcher());
        var endpoint = new McpHttpEndpoint(new McpJsonRpcDispatcher(catalog, "test"), () => null);
        var hold = !restartFirst;
        var server = new McpHttpServer(endpoint, null, async (listener, token) =>
        {
            if (hold)
            {
                entered.TrySetResult();
                await release.Task;
            }
            return await listener.AcceptTcpClientAsync(token);
        });
        try
        {
            server.Start(0);
            if (restartFirst)
            {
                await server.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);
                hold = true;
                server.Start(0);
            }
            await entered.Task.WaitAsync(BridgeLifecycleTestHarness.Guard);
            var stopping = server.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("shutdown owns the accepted loop until it returns");
            release.TrySetResult();
            await stopping.WaitAsync(BridgeLifecycleTestHarness.Guard);
            server.IsRunning.ShouldBeFalse();
            hold = false;
            server.Start(0);
            server.IsRunning.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await server.StopAsync().WaitAsync(BridgeLifecycleTestHarness.Guard);
        }
    }
}
