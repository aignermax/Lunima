using CAP.Avalonia.Services.WaveguideCompanions;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Routing.MetalRouting;
using CAP_DataAccess.Components.ComponentDraftMapper;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Live process policy shared by placement, routing, validation and export.</summary>
public sealed class ProcessRoutingContext
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly LeftPanelViewModel _library;
    private readonly Func<ActiveProcessSelection?> _designProcess;

    public ProcessRoutingContext(DesignCanvasViewModel canvas, LeftPanelViewModel library,
        Func<ActiveProcessSelection?> designProcess)
    {
        _canvas = canvas;
        _library = library;
        _designProcess = designProcess;
        Placement = new PlacementPolicyContext(
            getActiveProcess: () => canvas.Dies.ActiveDie?.ProcessBinding ?? designProcess(),
            getProcessAgnosticPdkNames: library.GetProcessAgnosticPdkNames,
            resolveComponentPdkSource: component => ComponentPdkSourceResolver.Resolve(component, library.AllTemplates),
            resolveLiveMemberPdkNames: () => canvas.Dies.ActiveDie is { ProcessBinding: { } process } die
                ? library.DieScopePdkNames(process, die.Die?.EnabledPdkNames)
                : designProcess() is { } active ? library.ResolveLiveMemberPdkNames(active) : null,
            getProcessCatalog: BuildCatalog);
        CompanionLayers = new WaveguideCompanionLayerProvider(
            () => library.AllTemplates, library.GetLoadedPdkDrafts,
            () => WaveguideCompanionSpecResolver.FromActiveProcess(
                designProcess(), library.GetLoadedPdkDrafts(), LiveMemberPdkNames()));
    }

    // Assembly roots have no canvas-wide routing/check process. Placement still uses the
    // design process there; this distinction is intentional and must not be flattened.
    public ActiveProcessSelection? CanvasScopeProcess =>
        _canvas.Dies.ActiveDie is { } die ? die.ProcessBinding
        : _canvas.Dies.IsAssemblyRoot ? null : _designProcess();

    public PlacementPolicyContext Placement { get; }
    public WaveguideCompanionLayerProvider CompanionLayers { get; }
    public IReadOnlyList<ProcessGroup> BuildCatalog() => ProcessCatalog.BuildGroups(_library.GetLoadedPdkProcessEntries());
    public IReadOnlyCollection<string>? LiveMemberPdkNames() =>
        CanvasScopeProcess is { } process ? _library.ResolveLiveMemberPdkNames(process) : null;
    public MetalRoutingSpec MetalSpec() => MetalRoutingSpecFactory.FromActiveProcess(
        CanvasScopeProcess, _library.GetLoadedPdkDrafts(), LiveMemberPdkNames());
    public double MinBendRadius() => WaveguideBendRadiusResolver.Resolve(
        CanvasScopeProcess, _library.GetLoadedPdkDrafts(), LiveMemberPdkNames());

    /// <summary>Capture library inputs on the UI thread before routing visits connections.</summary>
    public Func<PhysicalPin, PhysicalPin, double?> BuildConnectionFloorProvider()
    {
        var templates = _library.AllTemplates.ToList();
        var drafts = _library.GetLoadedPdkDrafts().ToList();
        return (start, end) => WaveguideBendRadiusResolver.ResolveForEndpointPdkNames(SourceOf(start), SourceOf(end), drafts);
        string? SourceOf(PhysicalPin pin) => pin.ParentComponent is { } component
            ? ComponentPdkSourceResolver.Resolve(component, templates) : null;
    }
}
