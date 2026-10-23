using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Die-aware selection restrictions and the command scope for editing parts on dies.</summary>
internal sealed class CanvasDieInteractionRules(DesignCanvasViewModel canvas)
{
    public static bool IsDie(ComponentViewModel? item) => item?.Component is ComponentGroup { IsDie: true };

    public bool SelectionHasDie(ComponentViewModel? primary) => canvas.Selection.SelectedComponents.Count > 0
        ? canvas.Selection.SelectedComponents.Any(IsDie) : IsDie(primary);

    public string? GroupBlockedReason(ComponentViewModel? primary) => SelectionHasDie(primary)
        ? LocalizationService.Instance.Translate("Dies.Status.NoGroupingDies") : null;

    public ComponentViewModel? ChipDieAt(double x, double y) =>
        canvas.Surface.IsContentLocked && canvas.ChipDie.Contains(x, y) ? canvas.ChipDie.ViewModel : null;

    public bool IsUndeletableDie(ComponentViewModel item) =>
        canvas.Surface.IsInertDie(item.Component) || canvas.ChipDie.IsProxy(item.Component);

    public string? DeleteRefusal(IReadOnlyCollection<ComponentViewModel> targets)
    {
        if (targets.Any(c => canvas.Surface.IsInertDie(c.Component)))
            return LocalizationService.Instance.Translate("Dies.Status.SystemOnly");
        return targets.Any(c => canvas.ChipDie.IsProxy(c.Component))
            ? LocalizationService.Instance.Translate("Dies.Status.KeepOneDie") : null;
    }

    public IUndoableCommand OnDieSurface(IReadOnlyList<ComponentViewModel> targets,
        Func<IUndoableCommand> build, string description)
    {
        var onDies = targets.Where(canvas.Surface.IsItem).ToList();
        return onDies.Count == 0 ? build() : new Commands.Dies.DieSurfaceCommand(
            canvas, build, onDies, canvas.Surface.DieOf(onDies[0]), description);
    }

    public ComponentGroup? DieUnder(Component part)
    {
        var (x, y, width, height) = DiePlacement.FootprintOf(part);
        return DieAssembly.DieAt(canvas.Components.Select(c => c.Component), x + width / 2, y + height / 2);
    }
}
