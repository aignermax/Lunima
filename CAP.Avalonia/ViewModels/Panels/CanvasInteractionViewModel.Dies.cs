using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// Die rules of <see cref="CanvasInteractionViewModel"/>: dies are the top level of an
/// assembly, never members of a group, and they carry no S-matrix — so group-only and
/// component-only actions (Create group, Ungroup, Rename group, Make gate, Edit component) are
/// unavailable on a selection holding a die, each with a short reason in the status bar.
/// </summary>
public partial class CanvasInteractionViewModel
{
    /// <summary>True when the primary selection is a die (the context menu hides component-only entries).</summary>
    public bool IsDieSelected => IsDie(SelectedComponent);

    /// <summary>True when any selected item is a die (the selection set, else the primary selection).</summary>
    public bool SelectionHasDie =>
        _canvas.Selection.SelectedComponents.Count > 0 ? _canvas.Selection.SelectedComponents.Any(IsDie) : IsDieSelected;

    /// <summary>Why Create group is unavailable for the current selection, or null when it is not blocked by a die.</summary>
    public string? GroupBlockedReason =>
        SelectionHasDie ? LocalizationService.Instance.Translate("Dies.Status.NoGroupingDies") : null;

    /// <summary>True when <paramref name="item"/> stands for a die of an assembly.</summary>
    public static bool IsDie(ComponentViewModel? item) => item?.Component is ComponentGroup { IsDie: true };

    /// <summary>Re-evaluates the die-dependent menu state whenever the selection changes.</summary>
    private void InitializeDieRules()
    {
        _canvas.Selection.SelectedComponents.CollectionChanged += (_, _) => RefreshDieRules();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedComponent))
                RefreshDieRules();
        };
    }

    private void RefreshDieRules()
    {
        OnPropertyChanged(nameof(IsDieSelected));
        OnPropertyChanged(nameof(SelectionHasDie));
        OnPropertyChanged(nameof(GroupBlockedReason));
        CreateGroupCommand.NotifyCanExecuteChanged();
        UngroupCommand.NotifyCanExecuteChanged();
        RenameGroupCommand.NotifyCanExecuteChanged();
        OpenSelectedComponentSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The chip die "Die 1" of a single-die design under the point — selectable in the System
    /// workspace, where the die is the unit of work — or null.
    /// </summary>
    private ComponentViewModel? ChipDieAt(double x, double y) =>
        _canvas.Surface.IsContentLocked && _canvas.ChipDie.Contains(x, y) ? _canvas.ChipDie.ViewModel : null;

    /// <summary>
    /// True for a die Delete must leave alone: outside System dies are background, and the
    /// chip die of a single-die design is the chip itself (a design keeps at least one die).
    /// </summary>
    private bool IsUndeletableDie(ComponentViewModel item) =>
        _canvas.Surface.IsInertDie(item.Component) || _canvas.ChipDie.IsProxy(item.Component);

    /// <summary>Why Delete left the dies among <paramref name="targets"/> alone, or null when none was refused.</summary>
    private string? DieDeleteRefusal(IReadOnlyCollection<ComponentViewModel> targets)
    {
        var loc = LocalizationService.Instance;
        if (targets.Any(c => _canvas.Surface.IsInertDie(c.Component)))
            return loc.Translate("Dies.Status.SystemOnly");
        return targets.Any(c => _canvas.ChipDie.IsProxy(c.Component)) ? loc.Translate("Dies.Status.KeepOneDie") : null;
    }

    /// <summary>Refuses Create group on a selection holding a die (reason in the status bar); true when refused.</summary>
    private bool RefuseGroupingDies()
    {
        if (!SelectionHasDie)
            return false;
        UpdateStatus?.Invoke(GroupBlockedReason!);
        return true;
    }

    /// <summary>
    /// Refuses Ungroup / Rename group on a die — a die is not a group, it is edited in its
    /// properties (System). True when refused.
    /// </summary>
    private bool RefuseOnDie(ComponentGroup? target)
    {
        if (target is not { IsDie: true })
            return false;
        UpdateStatus?.Invoke(LocalizationService.Instance.Translate("Dies.Status.NotAGroup"));
        return true;
    }
}
