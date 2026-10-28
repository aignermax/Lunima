using CAP.Avalonia.Services.AgentBridge.Registration;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class ClaudeRegistrationAdmissionTests
{
    [Fact]
    public async Task PreCanceledConnect_DoesNotEnableHostOrStartRegistration()
    {
        using var fixture = new ClaudeRegistrationLifetimeFixture();
        fixture.Preferences.SetAgentBridgeEnabled(false);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Connector.ConnectAsync(stop.Token));
        fixture.Preferences.GetAgentBridgeEnabled().ShouldBeFalse();
        fixture.Host.Calls.ShouldBe(0);
        fixture.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CanceledWhileHostApplies_DoesNotAdmitFirstCliSideEffect()
    {
        using var fixture = new ClaudeRegistrationLifetimeFixture();
        fixture.Host.Hold = true;
        using var stop = new CancellationTokenSource();
        var running = fixture.Connector.ConnectAsync(stop.Token);
        try
        {
            await fixture.Host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            running.IsCompleted.ShouldBeFalse("the accepted host operation still owns its cleanup");
            fixture.Host.Release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Calls.ShouldBeEmpty();
        }
        finally
        {
            fixture.Host.Release.TrySetResult();
            await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData("register")]
    [InlineData("unregister")]
    [InlineData("inspect")]
    public async Task PreCanceledRegistrar_RejectsEachCliEntryPoint(string action)
    {
        using var fixture = new ClaudeRegistrationLifetimeFixture();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Func<Task> operation = action switch
        {
            "register" => () => fixture.Registrar.RegisterAsync(39271, null, stop.Token),
            "unregister" => () => fixture.Registrar.UnregisterAsync(stop.Token),
            _ => () => fixture.Registrar.IsRegisteredAsync(stop.Token)
        };
        await Should.ThrowAsync<OperationCanceledException>(operation);
        fixture.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellationBetweenRemoveAndAdd_DoesNotAdmitAnotherCliProcess()
    {
        using var fixture = new ClaudeRegistrationLifetimeFixture();
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Run = async (_, token) =>
        {
            token.ShouldBe(stop.Token);
            entered.TrySetResult();
            await release.Task;
            return new CliRunResult(0, "remove completed after cancellation");
        };
        var running = fixture.Registrar.RegisterAsync(39271, null, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            fixture.Calls.ShouldHaveSingleItem().ArgumentList.ShouldContain("remove");
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task CanceledAfterLastCliCompletion_DoesNotReturnSuccessfulRegistration()
    {
        using var fixture = new ClaudeRegistrationLifetimeFixture();
        using var stop = new CancellationTokenSource();
        fixture.Run = (info, _) =>
        {
            if (info.ArgumentList.Contains("add")) stop.Cancel();
            return Task.FromResult(new CliRunResult(0, "completed"));
        };
        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Registrar.RegisterAsync(39271, null, stop.Token));
        fixture.Calls.Count.ShouldBe(2);
    }
}
