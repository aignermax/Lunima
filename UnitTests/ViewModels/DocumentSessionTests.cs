using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class DocumentSessionTests
{
    [Theory]
    [InlineData("edit")]
    [InlineData("scope")]
    [InlineData("replace")]
    public async Task ChangedDocument_RejectsOlderResultButAcceptsNewOperation(string change)
    {
        var document = new DocumentSession();
        using var old = document.CaptureOperation();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = old.CancellationToken.Register(() => cancelled.TrySetResult());
        if (change == "edit") document.Invalidate();
        else if (change == "scope") document.ChangeScope(Guid.NewGuid());
        else document.ReplaceDocument();
        var published = false;

        old.TryPublish(() => published = true).ShouldBeFalse();
        old.IsCurrent.ShouldBeFalse();
        published.ShouldBeFalse();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var current = document.CaptureOperation();
        current.TryPublish(() => published = true).ShouldBeTrue();
        published.ShouldBeTrue();
    }

    [Fact]
    public async Task Retirement_SealsImmediatelyAndDrainsActualOwnership()
    {
        var document = new DocumentSession();
        var operation = document.CaptureOperation();
        var stopping = document.RetireAsync();
        try
        {
            stopping.IsCompleted.ShouldBeFalse();
            operation.TryPublish(() => throw new Exception("late write")).ShouldBeFalse();
            Should.Throw<OperationCanceledException>(() => document.CaptureOperation());
        }
        finally { operation.Dispose(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));
        document.RetireAsync().ShouldBeSameAs(stopping);
    }

    [Fact]
    public void CancelledAndDisposedOperations_CannotPublish()
    {
        var document = new DocumentSession();
        using var cancellation = new CancellationTokenSource();
        using var operation = document.CaptureOperation(cancellation.Token);
        cancellation.Cancel();
        operation.TryPublish(() => throw new Exception("late write")).ShouldBeFalse();
        var completed = document.CaptureOperation();
        completed.Dispose();
        completed.TryPublish(() => throw new Exception("completed write")).ShouldBeFalse();
    }

    [Fact]
    public void ReenteringSameScope_DoesNotInvalidateCurrentResult()
    {
        var document = new DocumentSession();
        var scope = Guid.NewGuid();
        document.ChangeScope(scope);
        using var operation = document.CaptureOperation();
        document.ChangeScope(scope);
        operation.TryPublish(() => { }).ShouldBeTrue();
    }

    [Fact]
    public void DocumentWideOperation_SurvivesNavigationButNotDocumentEdits()
    {
        var document = new DocumentSession();
        using var save = document.CaptureOperation(scopeBound: false);
        using var scoped = document.CaptureOperation();
        document.ChangeScope(Guid.NewGuid());
        document.ChangeScope(null);
        scoped.IsCurrent.ShouldBeFalse("leaving and returning must not revive an obsolete operation");
        save.IsCurrent.ShouldBeTrue();
        document.Invalidate();
        save.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task ThrowingCancellationCallback_DoesNotPreventDrainingOtherOperations()
    {
        var document = new DocumentSession();
        var first = document.CaptureOperation();
        var second = document.CaptureOperation();
        using var broken = first.CancellationToken.Register(() => throw new InvalidOperationException("callback"));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observed = second.CancellationToken.Register(() => cancelled.TrySetResult());
        var stop = document.RetireAsync();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            stop.IsCompleted.ShouldBeFalse();
        }
        finally { first.Dispose(); second.Dispose(); }
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        document.CancellationError.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExternalCancellationCallback_IsOwnedUntilItActuallyReturns()
    {
        var document = new DocumentSession();
        using var caller = new CancellationTokenSource();
        var operation = document.CaptureOperation(caller.Token);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = operation.CancellationToken.Register(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("callback release");
        });
        var cancelling = Task.Run(caller.Cancel);
        Task? stopping = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            operation.Dispose();
            stopping = document.RetireAsync();
            stopping.IsCompleted.ShouldBeFalse("external cancellation callbacks belong to the accepted operation");
        }
        finally
        {
            release.Set();
            operation.Dispose();
            await cancelling.WaitAsync(TimeSpan.FromSeconds(10));
            await (stopping ?? document.RetireAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task CallerCancellation_RevokesPublicationBeforeRegisteredCallbacksAreDelivered()
    {
        var document = new DocumentSession();
        using var caller = new CancellationTokenSource();
        var operation = document.CaptureOperation(caller.Token);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocking = caller.Token.Register(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("callback release");
        });
        // A dedicated thread makes the cancellation-order fixture independent
        // of thread-pool injection; assertions still use actual token callbacks.
        var cancelling = Task.Factory.StartNew(caller.Cancel, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            operation.CancellationToken.IsCancellationRequested.ShouldBeFalse();
            operation.IsCurrent.ShouldBeFalse("the caller has cancelled even while propagation is queued");
            operation.TryPublish(() => throw new Exception("late result")).ShouldBeFalse();
            operation.TryCommitAndAdvance(() => throw new Exception("late mutation")).ShouldBeFalse();
        }
        finally
        {
            release.Set();
            await cancelling.WaitAsync(TimeSpan.FromSeconds(10));
            operation.Dispose();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
