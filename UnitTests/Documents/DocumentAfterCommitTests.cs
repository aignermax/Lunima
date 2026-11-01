using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.Documents;

/// <summary>Observers that bind to the document run after a commit that triggered them, not inside it.</summary>
public sealed class DocumentAfterCommitTests
{
    [Fact]
    public void OutsideACommit_TheReactionRunsImmediately()
    {
        var document = new DocumentSession();
        var ran = false;

        document.AfterCommit(() => ran = true);

        ran.ShouldBeTrue();
    }

    [Fact]
    public void InsideACommit_TheReactionWaitsForTheCommit_AndCanBindTheDocument()
    {
        var document = new DocumentSession();
        using var operation = document.CaptureOperation();
        var order = new List<string>();
        DocumentIdentity? bound = null;

        operation.TryCommitAndAdvance(() =>
        {
            Should.Throw<InvalidOperationException>(() => document.CaptureIdentity());
            document.AfterCommit(() => { order.Add("reaction"); bound = document.CaptureIdentity(); });
            order.Add("commit");
        }).ShouldBeTrue();

        order.ShouldBe(new[] { "commit", "reaction" });
        bound.ShouldNotBeNull();
        document.IsCurrentIdentity(bound.Value).ShouldBeTrue();
    }

    [Fact]
    public void Reactions_RunInOrder_AndOnlyOnce()
    {
        var document = new DocumentSession();
        using var operation = document.CaptureOperation();
        var calls = new List<int>();

        operation.TryCommitAndAdvance(() =>
        {
            document.AfterCommit(() => calls.Add(1));
            document.AfterCommit(() => calls.Add(2));
        });
        using var next = document.CaptureOperation();
        next.TryCommitAndAdvance(() => { });

        calls.ShouldBe(new[] { 1, 2 });
    }
}
