using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class TruthTablePublicationOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowObserver_ChangingSelectionCannotWriteOldAssignmentIntoReplacement(bool bindingSubscriber)
    {
        await using var fixture = new TruthTableLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        var originalAssignment = fixture.Group.TruthTablePinAssignment;
        var replacementAssignment = fixture.OtherGroup.TruthTablePinAssignment;
        bool changed = false;
        Exception? observerFailure = null;
        fixture.Panel.Rows.CollectionChanged += (_, args) =>
        {
            if (changed || args.Action != NotifyCollectionChangedAction.Add) return;
            changed = true;
            try { fixture.ConfigureReplacement(); }
            catch (Exception error) { observerFailure = error; }
        };
        if (bindingSubscriber)
            fixture.Panel.Rows.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                args.NewStartingIndex.ShouldBeInRange(0, fixture.Panel.Rows.Count - 1);
                fixture.Panel.Rows[args.NewStartingIndex].ShouldBeSameAs(args.NewItems![0]);
            };

        await fixture.RunAsync();

        changed.ShouldBeTrue("the real completed truth table must reach its first row");
        observerFailure.ShouldBeNull();
        fixture.OtherGroup.TruthTablePinAssignment.ShouldBeSameAs(replacementAssignment,
            "the old extraction cannot persist its roles into the newly selected group");
        fixture.Group.TruthTablePinAssignment.ShouldBeSameAs(originalAssignment);
        fixture.Panel.Rows.ShouldBeEmpty();
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.StatusText.ShouldBeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("selection")]
    public async Task HeldExtraction_CannotPersistOrPublishAfterItsDocumentContextChanges(string change)
    {
        await using var fixture = new TruthTableLifetimeFixture();
        var originalAssignment = fixture.Group.TruthTablePinAssignment;
        var replacementAssignment = fixture.OtherGroup.TruthTablePinAssignment;
        var running = fixture.RunAsync();
        string status = "";
        try
        {
            await fixture.Extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "edit") fixture.Canvas.Document.Invalidate();
            if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
            if (change == "remove") fixture.Canvas.Components.Remove(fixture.Component);
            if (change == "selection") fixture.ConfigureReplacement();
            status = fixture.Panel.StatusText;
        }
        finally { await fixture.FinishAsync(running); }

        fixture.Group.TruthTablePinAssignment.ShouldBeSameAs(originalAssignment);
        fixture.OtherGroup.TruthTablePinAssignment.ShouldBeSameAs(replacementAssignment);
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.Rows.ShouldBeEmpty();
        fixture.Panel.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task CurrentExtraction_PersistsRolesThresholdAndExistingSignalNames()
    {
        await using var fixture = new TruthTableLifetimeFixture();
        fixture.Group.TruthTablePinAssignment!.InputSignalNames = new() { ["a"] = "operand" };
        fixture.ConfigureOriginal();
        fixture.Extractor.Release.TrySetResult();

        await fixture.RunAsync();

        fixture.Panel.HasResult.ShouldBeTrue();
        fixture.Panel.Rows.Count.ShouldBe(4);
        fixture.Group.TruthTablePinAssignment!.InputPinNames.ShouldBe(new[] { "a", "b" });
        fixture.Group.TruthTablePinAssignment.OutputPinNames.ShouldBe(new[] { "y" });
        fixture.Group.TruthTablePinAssignment.Threshold.ShouldBe(fixture.Panel.Threshold);
        fixture.Group.TruthTablePinAssignment.InputSignalNames!["a"].ShouldBe("operand");
    }
}
