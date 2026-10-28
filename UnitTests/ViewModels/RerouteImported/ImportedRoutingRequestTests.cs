using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Documents;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [AvaloniaTheory]
    [InlineData("unrelated", 2)]
    [InlineData("repeated", 2)]
    [InlineData("target", 1)]
    [InlineData("replace", 1)]
    [InlineData("stop", 1)]
    public async Task ImportedRequest_RetriesOnlyAnUnchangedIntent_AtMostOnce(string edit, int expectedPasses)
    {
        AttemptRoutingWorker? worker = null;
        var canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 },
            (router, manager, components, connections, document) =>
                worker = new AttemptRoutingWorker(router, manager, components, connections, document));
        var (start, end) = AddComponentPair(canvas, 0);
        var target = ConnectFrozenImported(canvas, start, end);
        target.Connection.IsRouteFrozen = false;
        target.Connection.InvalidateRoute();
        canvas.Routing.InitializeAStarRouting(-100, -100, 1200, 1200);
        var stopped = false;
        worker!.OnStart = pass =>
        {
            if (pass > 1 && edit != "repeated") return;
            if (edit == "target") target.Connection.BendRadiusMicrometers += 1;
            if (edit == "replace") canvas.Document.ReplaceDocument();
            else canvas.Document.Invalidate();
            if (edit == "stop") stopped = true;
        };

        var running = ImportedRoutingRequest.RunAsync(canvas, [target], () => stopped);
        try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally
        {
            stopped = true;
            canvas.Routing.CancelRouting();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }

        worker.Passes.ShouldBe(expectedPasses);
        if (edit == "unrelated") target.Connection.RoutedPath.ShouldNotBeNull();
        else target.Connection.RoutedPath.ShouldBeNull("revocation must not publish an obsolete calculation");
    }

    private sealed class AttemptRoutingWorker(WaveguideRouter router, WaveguideConnectionManager manager,
        ObservableCollection<ComponentViewModel> components, ObservableCollection<WaveguideConnectionViewModel> connections,
        DocumentSession document) : RoutingOrchestrator(router, manager, components, connections, document)
    {
        internal int Passes { get; private set; }
        internal Action<int>? OnStart { get; set; }

        protected override async Task<RoutingPassResult?> RunSnapshotAsync(RoutingInputSnapshot input,
            RoutingProgressMailbox progress, CancellationToken token, bool calculateRoutes = true)
        {
            await Task.Yield();
            OnStart?.Invoke(++Passes);
            return await base.RunSnapshotAsync(input, progress, token, calculateRoutes);
        }
    }
}
