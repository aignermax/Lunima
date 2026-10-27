using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using UnitTests.Analysis.Snapshots;

namespace UnitTests.Analysis.Lifetime;

internal sealed class TruthTableLifetimeFixture : IAsyncDisposable
{
    internal DesignCanvasViewModel Canvas { get; } = new();
    internal ComponentGroup Group { get; } = LogicSnapshotFixture.Gate("original");
    internal ComponentGroup OtherGroup { get; } = LogicSnapshotFixture.Gate("replacement");
    internal ComponentViewModel Component { get; }
    internal ComponentViewModel Other { get; }
    internal MakeGateLifetimeFixture.HeldExtractor Extractor { get; } = new();
    internal TruthTableViewModel Panel { get; }

    internal TruthTableLifetimeFixture()
    {
        OtherGroup.TruthTablePinAssignment = new()
        {
            InputPinNames = ["b"], OutputPinNames = ["y"], Threshold = 0.4,
            InputSignalNames = new() { ["b"] = "replacement-input" }
        };
        Component = new(Group);
        Other = new(OtherGroup);
        Canvas.Components.Add(Component);
        Canvas.Components.Add(Other);
        Panel = new(Extractor);
        ConfigureOriginal();
    }

    internal void ConfigureOriginal()
    {
        Canvas.Selection.SelectSingle(Component);
        Panel.ConfigureForSelection(Component, Canvas);
    }

    internal void ConfigureReplacement()
    {
        Canvas.Selection.SelectSingle(Other);
        Panel.ConfigureForSelection(Other, Canvas);
    }

    internal Task RunAsync() => Panel.ExtractCommand.ExecuteAsync(null);

    internal async Task FinishAsync(Task running)
    {
        Extractor.Release.TrySetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        Extractor.Release.TrySetResult();
        if (Panel.ExtractCommand.ExecutionTask is { } running) await running.WaitAsync(TimeSpan.FromSeconds(10));
        if (Panel is IAsyncDisposable owner) await owner.DisposeAsync();
        await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Canvas.DisposeDocumentBindings();
    }
}
