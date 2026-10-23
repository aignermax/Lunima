using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.Snapshots;
using CAP_Core.Documents;

namespace UnitTests.ViewModels.Canvas;

/// <summary>Controls the worker progress source without blocking UI-side model capture.</summary>
internal sealed class ReportedRoutingWorker(WaveguideRouter router, WaveguideConnectionManager manager,
    ObservableCollection<ComponentViewModel> components, ObservableCollection<WaveguideConnectionViewModel> connections,
    DocumentSession? document = null)
    : RoutingOrchestrator(router, manager, components, connections, document)
{
    internal static DesignCanvasViewModel CreateCanvas() => new(new WaveguideRouter(),
        static (router, manager, components, connections, document) => new ReportedRoutingWorker(
            router, manager, components, connections, document));
    internal bool BlockWorker { get; set; }
    internal bool ReportComplex { get; set; }
    internal Action? AfterRelease { get; set; }
    internal RoutingPhase ReportedPhase { get; set; }
    internal RoutingProgressMailbox? Mailbox { get; private set; }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<RoutingPassResult?> RunSnapshotAsync(RoutingInputSnapshot input,
        RoutingProgressMailbox progress, CancellationToken token, bool calculateRoutes = true)
    {
        Mailbox = progress;
        if (BlockWorker && calculateRoutes)
        {
            progress.PhaseChanged(ReportedPhase);
            if (ReportComplex) progress.ComplexStarted();
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (AfterRelease != null) await Task.Run(AfterRelease);
        }
        return await base.RunSnapshotAsync(input, progress, token, calculateRoutes);
    }
}
