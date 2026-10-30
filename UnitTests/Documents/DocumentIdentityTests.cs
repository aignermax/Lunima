using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.Documents;

public sealed class DocumentIdentityTests
{
    [Fact]
    public void EditsAndScopeNavigation_DoNotChangeDocumentIdentity()
    {
        var document = new DocumentSession();
        var identity = document.CaptureIdentity();
        document.Invalidate();
        document.ChangeScope(Guid.NewGuid());
        document.ChangeScope(null);
        document.IsCurrentIdentity(identity).ShouldBeTrue();
        using var current = document.CaptureOperation(identity);
        current.TryPublish(() => { }).ShouldBeTrue();
    }

    [Fact]
    public void Replacement_RejectsNewWorkFromAnOlderConfiguredFeature()
    {
        var document = new DocumentSession();
        var identity = document.CaptureIdentity();
        document.ReplaceDocument();
        document.IsCurrentIdentity(identity).ShouldBeFalse();
        Should.Throw<OperationCanceledException>(() => document.CaptureOperation(identity));
        using var current = document.CaptureOperation(document.CaptureIdentity());
        current.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task IdentityDoesNotOwnWork_ButRetirementRevokesAdmission()
    {
        var document = new DocumentSession();
        var identity = document.CaptureIdentity();
        await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        document.IsCurrentIdentity(identity).ShouldBeFalse();
        Should.Throw<OperationCanceledException>(() => document.CaptureOperation(identity));
        Should.Throw<OperationCanceledException>(() => document.CaptureIdentity());
    }

    [Fact]
    public void DefaultOrForeignIdentity_CannotAdmitWork()
    {
        var document = new DocumentSession();
        var foreign = new DocumentSession().CaptureIdentity();
        document.IsCurrentIdentity(default).ShouldBeFalse();
        document.IsCurrentIdentity(foreign).ShouldBeFalse();
        Should.Throw<OperationCanceledException>(() => document.CaptureOperation(foreign));
        Should.Throw<OperationCanceledException>(() => document.CaptureOperation(default(DocumentIdentity)));
    }
}
