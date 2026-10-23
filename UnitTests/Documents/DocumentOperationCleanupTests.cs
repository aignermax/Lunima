using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.Documents;

public sealed class DocumentOperationCleanupTests
{
    [Fact]
    public async Task DisposeAsync_WaitsForAnAlreadyRunningCancellationCallback()
    {
        var document = new DocumentSession();
        var operation = document.CaptureOperation();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var registration = operation.CancellationToken.Register(() =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        Task disposing = Task.CompletedTask;
        try
        {
            document.Invalidate();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposing = operation.DisposeAsync().AsTask();
            disposing.IsCompleted.ShouldBeFalse();
            operation.TryPublish(() => throw new InvalidOperationException("revoked")).ShouldBeFalse();
            release.Set();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            await operation.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
