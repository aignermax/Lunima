using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Applies workspace and process policies before placing components or saved groups.</summary>
internal sealed class CanvasPlacementActions
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly ComponentLibraryViewModel? _libraryViewModel;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly Action<string> _status;

    public CanvasPlacementActions(DesignCanvasViewModel canvas, CommandManager commands,
        ComponentLibraryViewModel? library, ErrorConsoleService? errors, Action<string> status)
    {
        _canvas = canvas;
        _commandManager = commands;
        _libraryViewModel = library;
        _errorConsole = errors;
        _status = status;
    }

    public PlacementPolicyContext PlacementContext { get; set; } = PlacementPolicyContext.Unrestricted;
    public Func<ComponentTemplate, string?>? ComponentPlacementRefusal { get; set; }
    public Func<GroupTemplate, string?>? GroupPlacementRefusal { get; set; }

    private bool IsRefused(string? refusal)
    {
        if (refusal == null) return false;
        _status(refusal);
        return true;
    }

    public void PlaceComponent(ComponentTemplate? template, double x, double y)
    {
        if (template == null || IsRefused(ComponentPlacementRefusal?.Invoke(template))) return;

        if (_canvas.Dies.IsAssemblyRoot)
        {
            PlaceComponentTemplateOnDie(template, x, y);
            return;
        }

        // Per-chiplet scope (issue #935): dropping onto a process-bound chiplet resolves
        // against that chiplet's process; everywhere else the canvas-level process applies.
        var (isAllowed, blockReason) = PlacementContext.CheckPlacementAt(
            template.PdkSource, ChipletAt(x, y));
        if (!isAllowed)
        {
            _status(blockReason ?? "Process mismatch — cannot place component.");
            return;
        }

        double centeredX = x - template.WidthMicrometers / 2;
        double centeredY = y - template.HeightMicrometers / 2;

        var cmd = PlaceComponentCommand.TryCreate(_canvas, template, centeredX, centeredY);
        if (cmd == null)
        {
            _status("No space available on chip for this component");
            return;
        }
        SnapToActiveDieEdge(cmd.PlacedComponent);

        _commandManager.ExecuteCommand(cmd);
        _status($"Placed {template.Name} at ({x:F0}, {y:F0})µm");
    }

    /// <summary>
    /// Placement while looking at a die assembly: a part always lands on a die (it belongs
    /// to that die's process); dropping between dies places nothing and says why.
    /// </summary>
    private void PlaceComponentTemplateOnDie(ComponentTemplate template, double x, double y)
    {
        var die = CAP_Core.Components.Dies.DieAssembly.DieAt(_canvas.Components.Select(c => c.Component), x, y);
        if (die == null)
        {
            _status(Services.Localization.LocalizationService.Instance.Translate("Dies.Status.DropInsideDie"));
            return;
        }

        var (isAllowed, blockReason) = PlacementContext.CheckPlacementOnDie(template.PdkSource, die);
        if (!isAllowed)
        {
            _status(blockReason ?? "Process mismatch — cannot place component.");
            return;
        }

        var partProcess = PlacementContext.ProcessCatalog
            .Where(g => g.MemberPdkNames.Contains(template.PdkSource ?? "", StringComparer.OrdinalIgnoreCase))
            .Select(CAP_Core.Components.Process.ActiveProcessSelection.ForGroup)
            .FirstOrDefault();
        var cmd = Commands.Dies.PlaceComponentInDieCommand.TryCreate(_canvas, die, template, x, y, partProcess);
        if (cmd == null)
        {
            _status($"No free space on {die.GroupName} for {template.Name}");
            return;
        }
        _commandManager.ExecuteCommand(cmd);
        _status($"Placed {template.Name} on {die.GroupName}");
    }

    /// <summary>
    /// Inside a die, an edge coupler dropped within the edge zone lands on the die edge with
    /// its facet facing out — before it is placed, so placement and snap are one undo step.
    /// </summary>
    private void SnapToActiveDieEdge(CAP_Core.Components.Core.Component? component)
    {
        if (component == null || _canvas.Dies.ActiveDie is not { Die: { } die } activeDie
            || !CAP_Core.Components.Dies.EdgeCouplerFacet.IsEdgeCoupler(component))
            return;
        var snap = CAP_Core.Components.Dies.DieEdgeSnapper.Plan(
            component, CAP_Core.Components.Dies.DieAssembly.OutlineOf(activeDie), die.EdgeZoneMicrometers);
        if (snap != null)
            CAP_Core.Components.Dies.DieEdgeSnapper.Apply(component, snap);
    }

    public void PlaceGroup(GroupTemplate? groupTemplate, double x, double y)
    {
        if (groupTemplate == null || _libraryViewModel == null) return;
        if (IsRefused(GroupPlacementRefusal?.Invoke(groupTemplate))) return;

        // On a die assembly a saved group lands on the die under the pointer, in place.
        var targetDie = _canvas.Dies.IsAssemblyRoot
            ? CAP_Core.Components.Dies.DieAssembly.DieAt(_canvas.Components.Select(c => c.Component), x, y)
            : null;
        if (_canvas.Dies.IsAssemblyRoot && targetDie == null)
        {
            _status(Services.Localization.LocalizationService.Instance.Translate("Dies.Status.DropInsideDie"));
            return;
        }

        // Debug: Check if TemplateGroup is loaded
        if (groupTemplate.TemplateGroup == null)
        {
            _status($"ERROR: Template '{groupTemplate.Name}' not loaded! TemplateGroup is null.");
            return;
        }

        // Single-process enforcement over the group's children (issue #653): a group has no
        // PdkSource of its own, so a foreign-process child must not slip in via grouping.
        // Per-chiplet scope (issue #935): onto a bound chiplet the chiplet's process decides;
        // at canvas level a uniformly foreign-process group is placeable as its own chiplet
        // and gets that process pinned as its binding.
        var (isAllowed, blockReason, derivedBinding) = PlacementContext.CheckGroupPlacementAt(
            groupTemplate.TemplateGroup,
            ChipletAt(x, y),
            groupTemplate.Name);
        if (!isAllowed)
        {
            _status(blockReason ?? "Process mismatch — cannot place group.");
            return;
        }

        var libraryManager = _libraryViewModel.GetLibraryManager();
        var cmd = PlaceGroupTemplateCommand.TryCreate(
            _canvas, libraryManager, groupTemplate, x, y, out var physicsRejection);

        if (physicsRejection != null)
        {
            // Physics guard (round-4 hotfix): the template's frozen S-matrix data would
            // fabricate energy. Abort the placement cleanly — localized guard message to
            // the Error Console and the status bar, never an app-killing exception.
            var message = CAP.Avalonia.ViewModels.Analysis.NonConvergentCircuitMessageFormatter.Format(physicsRejection);
            _errorConsole?.LogError(
                $"Group '{groupTemplate.Name}' was not placed: {message}");
            _status(message);
            return;
        }

        if (cmd == null)
        {
            _status("No space available on chip for this group or template not loaded");
            return;
        }

        // Pin the chiplet process binding resolved by the policy check onto the instance
        // (on a die the die's process is the scope, the group needs no binding of its own).
        if (derivedBinding != null && targetDie == null)
        {
            cmd.GroupToPlace.ProcessBinding = derivedBinding;
        }

        _commandManager.ExecuteCommand(targetDie == null
            ? cmd
            : new Commands.Dies.DieSurfaceCommand(_canvas, () => cmd, Array.Empty<ComponentViewModel>(), targetDie, cmd.Description));
        _status($"Placed group '{groupTemplate.Name}' at ({x:F0}, {y:F0})µm");
    }

    /// <summary>
    /// The topmost group whose bounds contain the point — the chiplet a placement at that
    /// position targets (issue #935); null when the drop lands on ungrouped canvas.
    /// </summary>
    private ComponentGroup? ChipletAt(double x, double y) =>
        _canvas.Components
            .Where(c => c.Component is ComponentGroup
                        && x >= c.X && x <= c.X + c.Width
                        && y >= c.Y && y <= c.Y + c.Height)
            .Select(c => (ComponentGroup)c.Component)
            .LastOrDefault();

}
