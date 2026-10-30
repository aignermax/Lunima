using Avalonia.Headless.XUnit;
using CAP_Core.Components.Core;
using CAP_Core.Documents;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentSettingsAdmissionTests
{
    [AvaloniaTheory]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("retire", false)]
    [InlineData("retire", true)]
    public async Task OldSettingsWindow_RejectsNewWorkForObsoleteTarget(string change, bool import)
    {
        var fixture = new ComponentJobFixture("unused");
        var members = new HashSet<Component>(ReferenceEqualityComparer.Instance) { fixture.Target };
        var document = new DocumentSession();
        var model = fixture.Settings(document, isComponentPresent: members.Contains);
        try
        {
            if (change == "replace") document.ReplaceDocument();
            else if (change == "delete") { members.Remove(fixture.Target); document.Invalidate(); }
            else await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));

            if (import) await model.LoadFromFileCommand.ExecuteAsync(null);
            else await model.RecalculateSMatrixCommand.ExecuteAsync(null);

            fixture.Solver.Verify(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Files.Invocations.ShouldBeEmpty();
            fixture.Store.ShouldBeEmpty();
            fixture.Changes.ShouldBe(0);
            fixture.Propagations.ShouldBe(0);
        }
        finally
        {
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("edit", true)]
    [InlineData("navigation", false)]
    [InlineData("navigation", true)]
    public async Task CurrentTarget_AllowsNewWorkAfterEditsAndScopeRoundTrip(string change, bool import)
    {
        var fixture = new ComponentJobFixture("unused");
        var members = new HashSet<Component>(ReferenceEqualityComparer.Instance) { fixture.Target };
        var document = new DocumentSession();
        var model = fixture.Settings(document, isComponentPresent: members.Contains);
        try
        {
            if (change == "edit") document.Invalidate();
            else { document.ChangeScope(Guid.NewGuid()); document.ChangeScope(null); }
            if (import)
            {
                await model.LoadFromFileCommand.ExecuteAsync(null);
                fixture.Files.Verify(service => service.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            }
            else
            {
                await model.RecalculateSMatrixCommand.ExecuteAsync(null);
                fixture.Solver.Verify(service => service.CheckAvailabilityAsync(It.IsAny<CancellationToken>()), Times.Once);
                fixture.Store.ShouldContainKey("component");
            }
        }
        finally
        {
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaFact]
    public async Task ConfiguredIdleWindow_DoesNotKeepDocumentRetirementAlive()
    {
        var fixture = new ComponentJobFixture("unused");
        var document = new DocumentSession();
        var model = fixture.Settings(document);
        try { await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await model.StopAsync(); }
    }
}
