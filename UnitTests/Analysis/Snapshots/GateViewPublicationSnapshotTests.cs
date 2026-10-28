using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView;
using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Snapshots;

public sealed class GateViewPublicationSnapshotTests
{
    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("replace")]
    [InlineData("scope")]
    public async Task HeldClassification_ObsoleteDocumentCannotPublishCallerBindings(string change)
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        shell.Canvas.Components.Add(new ComponentViewModel(LogicSnapshotFixture.Gate("gate")));
        var extractor = new GateViewSnapshotFixture.CountingExtractor(holdFirst: true);
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () => 1550,
            classifier: new GateFunctionClassifier(extractor));
        var running = view.RebuildNowAsync();
        try
        {
            try
            {
                await extractor.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                switch (change)
                {
                    case "edit": shell.Canvas.Document.Invalidate(); break;
                    case "replace": shell.Canvas.Document.ReplaceDocument(); break;
                    case "scope": shell.Canvas.Document.ChangeScope(Guid.NewGuid()); break;
                }
            }
            finally { await GateViewSnapshotFixture.FinishAsync(extractor, running); }

            view.Graph.ShouldBeSameAs(GateViewGraph.Empty,
                "an obsolete capture must not attach its live groups to the visible graph");
            view.IsStale.ShouldBeTrue();
        }
        finally { await view.StopAsync(); }
    }

    [AvaloniaFact]
    public async Task AdmittedGraph_BindsNestedGateToOriginalLiveGroups()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        var gate = LogicSnapshotFixture.Gate("gate");
        var wrapper = new ComponentGroup("wrapper");
        wrapper.AddChild(gate);
        shell.Canvas.Components.Add(new ComponentViewModel(wrapper));
        var extractor = new GateViewSnapshotFixture.CountingExtractor(holdFirst: true);
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () => 1550,
            classifier: new GateFunctionClassifier(extractor));
        var running = view.RebuildNowAsync();
        try
        {
            try { await extractor.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { await GateViewSnapshotFixture.FinishAsync(extractor, running); }
            var node = view.Graph.Nodes.ShouldHaveSingleItem();
            node.Group.ShouldBeSameAs(gate, "drawing and selection must retain the real admitted gate");
            node.TopLevelGroup.ShouldBeSameAs(wrapper, "nested gate selection targets its real enclosing cell");
            node.Kind.ShouldBe(GateSymbolKind.Or);
            view.IsStale.ShouldBeFalse();
        }
        finally { await view.StopAsync(); }
    }

    [AvaloniaFact]
    public async Task Stop_WaitsForWorkerAndRejectsLateBinding()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        shell.Canvas.Components.Add(new ComponentViewModel(LogicSnapshotFixture.Gate("gate")));
        var extractor = new GateViewSnapshotFixture.CountingExtractor(holdFirst: true);
        var view = new GateViewViewModel(shell.Canvas, shell.WorkspaceSwitch, () => 1550,
            classifier: new GateFunctionClassifier(extractor));
        var running = view.RebuildNowAsync();
        Task? stopping = null;
        try
        {
            try
            {
                await extractor.Barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                stopping = view.StopAsync();
                stopping.IsCompleted.ShouldBeFalse("accepted worker cleanup must complete before StopAsync");
            }
            finally { await GateViewSnapshotFixture.FinishAsync(extractor, running); }
            await stopping!.WaitAsync(TimeSpan.FromSeconds(5));
            view.Graph.ShouldBeSameAs(GateViewGraph.Empty);
            await view.RebuildNowAsync();
            extractor.Calls.ShouldBe(1, "terminal retirement refuses a later rebuild");
        }
        finally { await view.StopAsync(); }
    }
}
