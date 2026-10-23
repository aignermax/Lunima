using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.ViewModels;

public sealed class DocumentProgressiveCommitTests
{
    [Fact]
    public void ComputedBatch_AdvancesOwnerAndRevokesOtherSnapshots()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        using var simulation = document.CaptureOperation();
        var published = 0;
        routing.TryCommitAndAdvance(() => published++).ShouldBeTrue();
        published.ShouldBe(1);
        simulation.IsCurrent.ShouldBeFalse();
        routing.IsCurrent.ShouldBeTrue();
        using var nextSnapshot = document.CaptureOperation();
        routing.TryCommitAndAdvance(() => published++).ShouldBeTrue();
        published.ShouldBe(2);
        nextSnapshot.IsCurrent.ShouldBeFalse();
        routing.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void CommitObservers_CanInvalidateWithoutCancellingTheirOwningBatch()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        using var peer = document.CaptureOperation();
        routing.TryCommitAndAdvance(() =>
        {
            document.Invalidate();
            document.Invalidate();
        }).ShouldBeTrue();
        routing.IsCurrent.ShouldBeTrue();
        routing.CancellationToken.IsCancellationRequested.ShouldBeFalse();
        peer.IsCurrent.ShouldBeFalse();
        document.Invalidate();
        routing.TryCommitAndAdvance(() => throw new Exception("obsolete mutation")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("cancel")]
    [InlineData("retire")]
    public async Task InterveningChange_RejectsNextComputedBatch(string change)
    {
        var document = new DocumentSession();
        using var caller = new CancellationTokenSource();
        var routing = document.CaptureOperation(caller.Token);
        Task? retiring = null;
        try
        {
            routing.TryCommitAndAdvance(() => { }).ShouldBeTrue();
            if (change == "edit") document.Invalidate();
            else if (change == "replace") document.ReplaceDocument();
            else if (change == "scope") document.ChangeScope(Guid.NewGuid());
            else if (change == "cancel") caller.Cancel();
            else retiring = document.RetireAsync();
            routing.TryCommitAndAdvance(() => throw new Exception("obsolete mutation")).ShouldBeFalse();
        }
        finally
        {
            routing.Dispose();
            await (retiring ?? document.RetireAsync()).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void PartiallyFailedCommit_RevokesOwnerAndPeers()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        using var peer = document.CaptureOperation();
        var mutations = 0;
        Should.Throw<InvalidOperationException>(() => routing.TryCommitAndAdvance(() =>
        {
            mutations++;
            throw new InvalidOperationException("apply failed");
        }));
        mutations.ShouldBe(1);
        routing.IsCurrent.ShouldBeFalse();
        peer.IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public void Commit_CannotCaptureAnIncompleteDocumentOrNestAnotherCommit()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        routing.TryCommitAndAdvance(() =>
        {
            Should.Throw<InvalidOperationException>(() => document.CaptureOperation());
            Should.Throw<InvalidOperationException>(() => routing.TryCommitAndAdvance(() => { }));
        }).ShouldBeTrue();
    }

    [Fact]
    public void ReentrantDocumentReplacement_CannotTransferOldBatchPermissionToNewDocument()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        routing.TryCommitAndAdvance(document.ReplaceDocument).ShouldBeTrue();
        routing.IsCurrent.ShouldBeFalse();
        routing.TryCommitAndAdvance(() => throw new Exception("new-document mutation")).ShouldBeFalse();
    }

    [Fact]
    public void Observers_CannotPublishOrAdvertisePartialTopologyAsCurrent()
    {
        var document = new DocumentSession();
        using var routing = document.CaptureOperation();
        using var peer = document.CaptureOperation();
        routing.TryCommitAndAdvance(() =>
        {
            peer.IsCurrent.ShouldBeFalse();
            peer.TryPublish(() => throw new Exception("partial topology")).ShouldBeFalse();
            routing.TryPublish(() => throw new Exception("partial topology")).ShouldBeFalse();
        }).ShouldBeTrue();
        routing.IsCurrent.ShouldBeTrue();
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("cancel")]
    [InlineData("scope")]
    [InlineData("retire")]
    public async Task ReentrantRevocation_CannotBeReversedByAnObserverInvalidation(string change)
    {
        var document = new DocumentSession();
        using var caller = new CancellationTokenSource();
        var routing = document.CaptureOperation(caller.Token);
        try
        {
            routing.TryCommitAndAdvance(() =>
            {
                if (change == "dispose") routing.Dispose();
                else if (change == "cancel") caller.Cancel();
                else if (change == "scope") document.ChangeScope(Guid.NewGuid());
                else _ = document.RetireAsync();
                document.Invalidate();
            }).ShouldBeTrue();
            routing.IsCurrent.ShouldBeFalse();
        }
        finally
        {
            routing.Dispose();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
