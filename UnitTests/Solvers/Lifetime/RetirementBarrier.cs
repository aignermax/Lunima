using Shouldly;

namespace UnitTests.Solvers.Lifetime;

internal sealed class RetirementBarrier
{
    public TaskCompletionSource Started { get; } = Signal();
    public TaskCompletionSource Cancelled { get; } = Signal();
    public TaskCompletionSource Release { get; } = Signal();

    public async Task WaitAsync(CancellationToken token)
    {
        using var registration = token.Register(() => Cancelled.TrySetResult());
        Started.TrySetResult();
        await Release.Task;
    }

    public async Task AssertDrainingAsync(Task stopping)
    {
        await Task.WhenAny(Cancelled.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Cancelled.Task.IsCompleted.ShouldBeTrue("retirement must reach the actual worker token");
        stopping.IsCompleted.ShouldBeFalse("cancellation is not completed cleanup");
    }

    public async Task AwaitStartAsync(Task running)
    {
        await Task.WhenAny(Started.Task, running).WaitAsync(TimeSpan.FromSeconds(5));
        if (!Started.Task.IsCompleted) await running;
        Started.Task.IsCompleted.ShouldBeTrue("the requested operation must enter its worker");
    }

    public async Task FinishAsync(params Task[] tasks)
    {
        Release.TrySetResult();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
