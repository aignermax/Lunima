using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class CircuitOptimizationPublicationTests
{
    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("replace", false)]
    [InlineData("remove", false)]
    [InlineData("target", false)]
    [InlineData("replace", true)]
    public async Task ChangedContext_RejectsLateVariantsAndFailure(string change, bool fail)
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.FailOnRelease = fail;
        var running = fixture.RunAsync();
        string status = "";
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "edit") fixture.Canvas.Document.Invalidate();
            if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
            if (change == "remove") fixture.Canvas.Components.Remove(fixture.Target);
            if (change == "target") fixture.Panel.SelectedTarget = new OptimizationTargetOption("other", [Guid.NewGuid()]);
            status = fixture.Panel.StatusText;
        }
        finally { await fixture.FinishAsync(running); }

        fixture.Panel.Variants.ShouldBeEmpty();
        fixture.Panel.BaselineText.ShouldBeEmpty();
        fixture.Panel.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task RetainedOldVariant_CannotApplyItsValuesToTheNextRunsComponentBindings()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Release.TrySetResult();
        await fixture.RunAsync();
        var previous = fixture.Panel.Variants.Single();
        var replacement = new DesignCanvasViewModel();
        try
        {
            var nextTarget = CircuitOptimizationLifetimeFixture.Populate(replacement, 0.4);
            fixture.Panel.ConfigureForCanvas(replacement);
            await fixture.RunAsync();
            fixture.Panel.Variants.ShouldNotBeEmpty();

            previous.ApplyCommand.Execute(null);

            nextTarget.SliderValue.ShouldBe(0.4, "the old row must never use another run's component mapping");
            fixture.Target.SliderValue.ShouldBe(0.2);
            previous.IsApplied.ShouldBeFalse();
        }
        finally { await replacement.Document.RetireAsync(); replacement.DisposeDocumentBindings(); }
    }

    [AvaloniaFact]
    public async Task CompletedRow_CannotApplyAfterItsDocumentWasReplaced()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Release.TrySetResult();
        await fixture.RunAsync();
        var variant = fixture.Panel.Variants.Single();
        fixture.Canvas.Document.ReplaceDocument();

        variant.ApplyCommand.Execute(null);

        fixture.Target.SliderValue.ShouldBe(0.2);
        variant.IsApplied.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task CurrentVariant_AppliesAndRemainsUndoable()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Release.TrySetResult();
        await fixture.RunAsync();
        var variant = fixture.Panel.Variants.Single();

        variant.ApplyCommand.Execute(null);

        fixture.Target.SliderValue.ShouldBe(0.8);
        variant.IsApplied.ShouldBeTrue();
        fixture.Commands.Undo();
        fixture.Target.SliderValue.ShouldBe(0.2);
    }
}
