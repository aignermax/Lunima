using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Preferences;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;

namespace UnitTests.AgentBridge;

internal static class AiAsyncToolFixture
{
    internal static AiGridService Grid(DesignCanvasViewModel canvas, SimulationService simulation)
    {
        var library = new Mock<IGroupLibrary>();
        library.SetupGet(x => x.UserTemplates).Returns(Array.Empty<GroupTemplate>());
        library.SetupGet(x => x.PdkTemplates).Returns(Array.Empty<GroupTemplate>());
        var panel = new LeftPanelViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory), canvas, library.Object, Mock.Of<IPdkReader>(),
            Mock.Of<IPdkLibraryPreferences>(), Mock.Of<IPanelLayoutPreferences>(),
            new HierarchyPanelViewModel(canvas), new PdkManagerViewModel(),
            new ComponentLibraryViewModel(library.Object));
        return new AiGridService(canvas, panel, simulation);
    }

    internal static AgentToolCatalog Catalog(IAiTool tool) =>
        new([], new AiToolRegistry([tool]), new InlineUiDispatcher());

    internal static Task Revoke(string change, DesignCanvasViewModel canvas,
        AgentToolCatalog catalog, CancellationTokenSource caller)
    {
        switch (change)
        {
            case "replace": canvas.Document.ReplaceDocument(); break;
            case "scope": canvas.Document.ChangeScope(Guid.NewGuid()); break;
            case "edit": canvas.Document.Invalidate(); break;
            case "close": return catalog.CloseAdmissionAsync();
            case "cancel": return caller.CancelAsync();
        }
        return Task.CompletedTask;
    }

    internal static object? PublishNewerOverlay(DesignCanvasViewModel canvas,
        Component source, Component output, WaveguideConnection wire)
    {
        canvas.PowerFlowVisualizer.UpdateFromSimulation([wire], [source, output],
            new Dictionary<Guid, Complex> { [output.PhysicalPins[1].LogicalPin.IDOutFlow] = new(0.9, 0) });
        return canvas.PowerFlowVisualizer.CurrentResult;
    }

    internal static async Task DrainAsync(Task running, AgentToolCatalog catalog, DesignCanvasViewModel canvas)
    {
        try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { }
        await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        canvas.DisposeDocumentBindings();
    }
}

internal sealed class HeldAiSimulation : SimulationService
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken WorkerToken { get; private set; }
    internal SimulationSnapshotResult? Computed { get; private set; }

    protected override async Task<SimulationSnapshotResult> SolveSnapshotAsync(
        SimulationInputSnapshot snapshot, CancellationToken cancellationToken)
    {
        WorkerToken = cancellationToken;
        Entered.TrySetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Finish the actual detached calculation even when revoked, exercising publication admission.
        return Computed = await SimulationSnapshotSolver.RunAsync(snapshot);
    }
}
