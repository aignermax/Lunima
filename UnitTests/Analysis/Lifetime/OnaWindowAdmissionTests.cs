using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class OnaWindowAdmissionTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task OldAnalyzerWindow_RejectsNewWork(string change)
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        BindAnalyzer(fixture);
        fixture.Runner.Barrier.Release.TrySetResult();
        if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
        else
        {
            fixture.Canvas.Components.RemoveAt(0);
            // Leave another source in the visible canvas so absence of all
            // components cannot accidentally satisfy the admission assertion.
            var remaining = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            fixture.Canvas.Components.Add(new CAP.Avalonia.ViewModels.Canvas.ComponentViewModel(remaining));
            fixture.Canvas.Document.Invalidate();
        }

        await fixture.RunAsync();

        fixture.Runner.Calls.ShouldBe(0);
        fixture.HasResult.ShouldBeFalse();
        fixture.Errors.Entries.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("scope")]
    public async Task CurrentAnalyzerWindow_AllowsNewWorkAfterDocumentChanges(string change)
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        BindAnalyzer(fixture);
        fixture.Runner.Barrier.Release.TrySetResult();
        if (change == "edit") fixture.Canvas.Document.Invalidate();
        else
        {
            fixture.Canvas.Document.ChangeScope(Guid.NewGuid());
            fixture.Canvas.Document.ChangeScope(null);
        }

        await fixture.RunAsync();

        fixture.Runner.Calls.ShouldBe(1);
        fixture.HasResult.ShouldBeTrue();
    }

    private static void BindAnalyzer(SweepLifetimeFixture fixture)
    {
        var analyzer = fixture.Canvas.Components[0].Component;
        analyzer.PhysicalPins[0].Name = "source";
        fixture.Ona!.Analyzer = analyzer;
        fixture.Ona.Configure(fixture.Canvas);
    }
}
