using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class MakeGateReentrancyLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("selection")]
    public async Task MatchObserver_RevokesTheFollowingSummaryPublication(string change)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Panel.SelectedFunction = fixture.Panel.Functions.Single(option => option.Function == GateFunction.Or);
        fixture.Extractor.Release.TrySetResult();
        var revoked = false;
        string? summaryAfterRevocation = null;
        fixture.Panel.PropertyChanged += (_, args) =>
        {
            if (revoked || args.PropertyName != nameof(fixture.Panel.MatchesFunction)
                || !fixture.Panel.MatchesFunction) return;
            revoked = true;
            fixture.ChangeContext(change);
            summaryAfterRevocation = fixture.Panel.MatchText;
        };

        await fixture.Panel.SimulateCommand.ExecuteAsync(null);

        revoked.ShouldBeTrue("the real combiner must first classify as the selected OR function");
        fixture.Panel.MatchText.ShouldBe(summaryAfterRevocation,
            "the revoked result cannot publish its summary after the match observer returns");
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("selection")]
    public async Task RowObserver_RevokesRemainingResultPublication(string change)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        var revoked = false;
        var rowsAfterRevocation = -1;
        var statusAfterRevocation = "";
        fixture.Panel.Rows.CollectionChanged += (_, args) =>
        {
            if (revoked || args.Action != NotifyCollectionChangedAction.Add) return;
            revoked = true;
            fixture.ChangeContext(change);
            rowsAfterRevocation = fixture.Panel.Rows.Count;
            statusAfterRevocation = fixture.Panel.StatusText;
        };
        fixture.Panel.Rows.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add) return;
            args.NewStartingIndex.ShouldBeInRange(0, fixture.Panel.Rows.Count - 1);
            fixture.Panel.Rows[args.NewStartingIndex].ShouldBeSameAs(args.NewItems![0]);
        };

        await fixture.Panel.SimulateCommand.ExecuteAsync(null);

        revoked.ShouldBeTrue();
        if (change == "selection")
        {
            fixture.Panel.Rows.ShouldBeEmpty("the replacement display reset completes after the old notification");
            fixture.Panel.ConfiguredGroup!.GroupName.ShouldBe("other");
            fixture.Panel.IsActive.ShouldBeTrue();
            fixture.Panel.StatusText.ShouldBe(CAP.Avalonia.Services.Localization.LocalizationService.Instance.Translate("MakeGate.PinsHint"));
        }
        else
        {
            fixture.Panel.Rows.Count.ShouldBe(rowsAfterRevocation);
            fixture.Panel.StatusText.ShouldBe(statusAfterRevocation);
        }
        fixture.Panel.HasResult.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData(NotifyCollectionChangedAction.Reset)]
    [InlineData(NotifyCollectionChangedAction.Add)]
    public async Task PinObserver_RetirementDuringSaveStopsFurtherEditorPublication(NotifyCollectionChangedAction trigger)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        fixture.Panel.GateName = "saved";
        var retired = false;
        Task? stopping = null;
        fixture.Panel.Pins.CollectionChanged += (_, args) =>
        {
            if (retired || args.Action != trigger) return;
            retired = true;
            stopping = fixture.Panel.StopAsync();
        };

        var error = Record.Exception(() => fixture.Panel.SaveCommand.Execute(null));

        retired.ShouldBeTrue();
        error.ShouldBeNull("retirement can complete synchronously inside a collection observer");
        await stopping!.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Panel.Pins.ShouldBeEmpty();
        fixture.Panel.IsSaved.ShouldBeFalse();
        fixture.Panel.HasResult.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task GateSavedObserver_CloseRevokesTheSameSaveToLibraryRequest()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        var writes = 0;
        fixture.Panel.SaveToLibraryRequested = (_, _) => { writes++; return true; };
        fixture.Panel.GateSaved += (_, _) => fixture.Panel.CloseCommand.Execute(null);

        fixture.Panel.SaveToLibraryCommand.Execute(null);

        fixture.Panel.IsActive.ShouldBeFalse();
        writes.ShouldBe(0, "the library stage cannot acquire fresh permission after this request was revoked");
    }

    [AvaloniaFact]
    public async Task DefinitionObserver_CloseRevokesTheFollowingRepaint()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        var paints = 0;
        var paintsAtClose = -1;
        fixture.Canvas.RepaintRequested += () => paints++;
        fixture.Panel.GateDefinitionChanged += (_, _) =>
        {
            fixture.Panel.CloseCommand.Execute(null);
            paintsAtClose = paints;
        };

        fixture.Panel.SaveCommand.Execute(null);

        paintsAtClose.ShouldBeGreaterThanOrEqualTo(0);
        paints.ShouldBe(paintsAtClose);
    }

    [AvaloniaFact]
    public async Task RetainedUndoCommand_DoesNotRetainTheRetiredEditor()
    {
        var (editor, command) = await CreateRetiredEditorWithRetainedCommandAsync();
        Dispatcher.UIThread.RunJobs();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        editor.IsAlive.ShouldBeFalse("the retained undo callback must contain only a weak editor reference");
        command.Undo();
        command.Execute();
        GC.KeepAlive(command);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Editor, IUndoableCommand Command)> CreateRetiredEditorWithRetainedCommandAsync()
    {
        var fixture = new MakeGateLifetimeFixture();
        var retained = new CommandHolder();
        fixture.Panel.ExecuteCommand = retained.Execute;
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        fixture.Panel.SaveCommand.Execute(null);
        var reference = new WeakReference(fixture.Panel);
        await fixture.DisposeAsync();
        fixture = null!;
        return (reference, retained.Command.ShouldNotBeNull());
    }

    private sealed class CommandHolder
    {
        internal IUndoableCommand? Command { get; private set; }
        internal void Execute(IUndoableCommand command) { Command = command; command.Execute(); }
    }
}
