using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class ShellWorkConcurrencyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentAdmissionAndCompletion_PreserveEveryAcceptedJob(bool generic)
    {
        var scope = new ShellWorkScope();
        var start = Signal();
        int entered = 0, cleaned = 0;
        const int producers = 16, jobsPerProducer = 400;
        var running = Enumerable.Range(0, producers).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            for (int job = 0; job < jobsPerProducer; job++)
            {
                if (generic)
                {
                    var result = await scope.RunAsync(async token =>
                    {
                        token.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref entered);
                        try { await Task.Yield(); return 42; }
                        finally { Interlocked.Increment(ref cleaned); }
                    });
                    result.ShouldBe(42);
                }
                else
                {
                    await scope.RunAsync(async token =>
                    {
                        token.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref entered);
                        try { await Task.Yield(); }
                        finally { Interlocked.Increment(ref cleaned); }
                    });
                }
            }
        })).ToArray();

        start.TrySetResult();
        try
        {
            await Task.WhenAll(running).WaitAsync(Deadline);
            entered.ShouldBe(producers * jobsPerProducer);
            cleaned.ShouldBe(entered);
        }
        finally { await scope.StopAsync().WaitAsync(Deadline); }
    }

    [Fact]
    public async Task ConcurrentStopCalls_ShareOneRetirementAndAwaitHeldCleanup()
    {
        var scope = new ShellWorkScope();
        var cleanup = Signal();
        var cancelled = Signal();
        var start = Signal();
        var running = scope.RunAsync(async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await cleanup.Task;
        });
        // Return the retirement tasks as values; do not unwrap and wait for held cleanup yet.
        var callers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return new Retirement(scope.StopAsync());
        })).ToArray();
        start.TrySetResult();
        try
        {
            var retirements = await Task.WhenAll(callers).WaitAsync(Deadline);
            await cancelled.Task.WaitAsync(Deadline);
            foreach (var retirement in retirements)
            {
                retirement.Task.ShouldBeSameAs(retirements[0].Task);
                retirement.Task.IsCompleted.ShouldBeFalse("accepted cleanup remains held");
            }
            scope.IsStopping.ShouldBeTrue();
            var admitted = false;
            await scope.RunAsync(_ => { admitted = true; return Task.CompletedTask; });
            await Should.ThrowAsync<TaskCanceledException>(() => scope.RunAsync(_ => Task.FromResult(1)));
            admitted.ShouldBeFalse();
        }
        finally
        {
            cleanup.TrySetResult();
            await running.WaitAsync(Deadline);
            await scope.StopAsync().WaitAsync(Deadline);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmissionRacingStop_NeverEscapesRetirementOrUsesDisposedCancellation(bool generic)
    {
        // Each round checks the race once. This is bounded concurrency coverage, not a retry
        // that accepts a successful round after an earlier failure.
        for (int round = 0; round < 40; round++)
        {
            var scope = new ShellWorkScope();
            var start = Signal();
            var cleanup = Signal();
            int entered = 0, cleaned = 0;
            var anchor = scope.RunAsync(async _ => await cleanup.Task);
            var admissions = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                if (generic)
                {
                    try
                    {
                        await scope.RunAsync(async token =>
                        {
                            Interlocked.Increment(ref entered);
                            // Reading the accepted token must remain valid even if Stop wins next.
                            using var registration = token.Register(() => { });
                            try { await cleanup.Task; return 1; }
                            finally { Interlocked.Increment(ref cleaned); }
                        });
                    }
                    catch (TaskCanceledException) { /* Rejected generic admission, as specified. */ }
                }
                else
                {
                    await scope.RunAsync(async token =>
                    {
                        Interlocked.Increment(ref entered);
                        using var registration = token.Register(() => { });
                        try { await cleanup.Task; }
                        finally { Interlocked.Increment(ref cleaned); }
                    });
                }
            })).ToArray();
            var stopping = Task.Run(async () =>
            {
                await start.Task;
                return new Retirement(scope.StopAsync());
            });
            start.TrySetResult();
            try
            {
                var retirement = await stopping.WaitAsync(Deadline);
                retirement.Task.IsCompleted.ShouldBeFalse("the anchor keeps retirement open while admissions settle");
                cleanup.TrySetResult();
                await retirement.Task.WaitAsync(Deadline);
                var enteredAtRetirement = Volatile.Read(ref entered);
                Volatile.Read(ref cleaned).ShouldBe(enteredAtRetirement,
                    "every accepted job must finish its actual cleanup before retirement returns");
                await Task.WhenAll(admissions).WaitAsync(Deadline);
                Volatile.Read(ref entered).ShouldBe(enteredAtRetirement,
                    "no previously racing admission may begin after retirement returned");
            }
            finally
            {
                cleanup.TrySetResult();
                await Task.WhenAll(admissions.Append(anchor)).WaitAsync(Deadline);
                await scope.StopAsync().WaitAsync(Deadline);
            }
        }
    }

    private sealed record Retirement(Task Task);
}
