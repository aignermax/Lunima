using System.ComponentModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Dies;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Die (chiplet) half of <see cref="MainViewModel"/>: the die properties section, the New die
/// flyout, and the die scope — while a die is selected on the assembly or open for editing,
/// the component library and PDK list show only what that die's process and PDKs allow.
/// </summary>
public partial class MainViewModel
{
    private ComponentGroup? _scopedDie;
    private (string? Process, string Pdks)? _scopedDieState;

    /// <summary>Properties of the selected or open die (Properties panel section).</summary>
    public DiePropertiesViewModel DieProperties { get; private set; } = null!;

    /// <summary>Properties of the selected die-to-die link (Properties panel section).</summary>
    public DieLinkPropertiesViewModel DieLinkProperties { get; private set; } = null!;

    /// <summary>The Chiplets panel's New die flyout.</summary>
    public NewDieViewModel NewDie { get; private set; } = null!;

    /// <summary>
    /// The process that bounds the canvas as a whole (routing floor, metal spec, canvas-wide
    /// design-check limits): the open die's process inside a die, none at the root of a die
    /// assembly (each die answers for its own parts), the design-level process otherwise.
    /// </summary>
    public ActiveProcessSelection? CanvasScopeProcess =>
        Canvas.Dies.ActiveDie is { } die ? die.ProcessBinding
        : Canvas.Dies.IsAssemblyRoot ? null
        : FileOperations.ActiveProcess;

    /// <summary>
    /// Status-bar process chip for a die assembly: the open or selected die's process, else
    /// the number of distinct processes with every die → process in the tooltip. False (and
    /// nothing changed) for a single-die design.
    /// </summary>
    private bool TryDescribeDieProcesses()
    {
        var loc = Services.Localization.LocalizationService.Instance;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        string ProcessName(ComponentGroup d) => d.ProcessBinding?.DisplayName ?? loc.Translate("Workspace.System.NoProcess");
        if (ScopeDie is { } die)
        {
            ActiveProcessLabel = string.Format(culture, loc.Translate("Status.DieProcess"), die.GroupName, ProcessName(die));
            IsPlayground = die.ProcessBinding is not { IsPlayground: false };
            return true;
        }
        var dies = Canvas.Dies.RootDies;
        if (dies.Count == 0)
            return false;
        var processes = dies.Select(ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ActiveProcessLabel = processes.Count == 1
            ? string.Format(culture, loc.Translate("Process.Prefix"), processes[0])
            : string.Format(culture, loc.Translate("Status.ProcessCount"), processes.Count);
        ActiveProcessTip = string.Join(Environment.NewLine, dies.Select(d => $"{d.GroupName} → {ProcessName(d)}"));
        IsPlayground = dies.Any(d => d.ProcessBinding is not { IsPlayground: false });
        return true;
    }

    /// <summary>
    /// The die the user currently works with: the die around the group being edited, the die
    /// selected on the assembly, or the die of the selected part (parts are edited in place).
    /// </summary>
    public ComponentGroup? ScopeDie =>
        Canvas.Dies.ActiveDie
        ?? (Canvas.Dies.IsAssemblyRoot && Canvas.SelectedComponent?.Component is ComponentGroup { IsDie: true } die ? die : null)
        ?? Canvas.Surface.DieOf(Canvas.SelectedComponent);

    /// <summary>
    /// The die whose properties the panel shows: the die itself when it is selected, or the
    /// die being worked inside when nothing is selected. A selected part keeps the panel on
    /// the part — the die of a part never takes over the properties.
    /// </summary>
    private ComponentGroup? PropertiesDie =>
        Canvas.SelectedComponent?.Component is ComponentGroup { IsDie: true } selected ? selected
        : Canvas.SelectedComponent == null ? Canvas.Dies.ActiveDie
        : null;

    private void InitializeDies()
    {
        IReadOnlyList<ProcessGroup> Catalog() => ProcessCatalog.BuildGroups(LeftPanel.GetLoadedPdkProcessEntries());
        DieProperties = new DiePropertiesViewModel(Canvas, CommandManager, Catalog)
        {
            ZoomToDieRequested = ZoomToDie,
            IsEditable = Canvas.Surface.IsContentLocked,
        };
        NewDie = new NewDieViewModel(Canvas, CommandManager, Catalog, () => FileOperations.ActiveProcess)
        {
            DieCreated = OnDieCreated,
        };

        DieLinkProperties = new DieLinkPropertiesViewModel(Canvas, CommandManager);
        Canvas.ChipDie.ProcessSource = () => FileOperations.ActiveProcess;
        ViewportControl.FitsWholeAssembly = () => WorkspaceSwitch.IsSystem;

        Canvas.DiesChanged += OnDiesChanged;
        Canvas.PropertyChanged += OnCanvasPropertyChangedForDies;
        CanvasInteraction.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasInteraction.SelectedWaveguideConnection))
                DieLinkProperties.Show(CanvasInteraction.SelectedWaveguideConnection?.Connection);
        };
        CommandManager.StateChanged += (_, _) =>
        {
            DieProperties.Reload();
            DieLinkProperties.Reload();
        };
    }

    /// <summary>
    /// Canvas context menu "New die": adds a die with the flyout's defaults (another process
    /// than the chip's, its smallest preset) — process and size stay editable in its properties.
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void NewDieQuick()
    {
        NewDie.Prepare();
        NewDie.CreateCommand.Execute(null);
    }

    /// <summary>
    /// Frames <paramref name="die"/> on the canvas and selects it (double-click on a die, the
    /// Chiplets row, the die properties and the context menu). A die is a work surface — its
    /// parts are edited in place on the assembly, so it is never entered like a group.
    /// </summary>
    public void ZoomToDie(ComponentGroup die)
    {
        ArgumentNullException.ThrowIfNull(die);
        if (Canvas.IsInGroupEditMode)
            Canvas.ExitToRoot();
        var outline = CAP_Core.Components.Dies.DieAssembly.OutlineOf(die);
        ViewportControl.ZoomToRect(outline.X, outline.Y, outline.Width, outline.Height);
        var vm = Canvas.ChipDie.IsProxy(die) ? Canvas.ChipDie.ViewModel
            : Canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, die));
        if (vm != null)
        {
            Canvas.Selection.SelectSingle(vm);
            Canvas.SelectedComponent = vm;
        }
        StatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Services.Localization.LocalizationService.Instance.Translate("Dies.Status.ZoomedToDie"), die.GroupName);
    }

    /// <summary>
    /// System workspace: the unit of work is the die — its parts and wires are dimmed and
    /// locked (clicks select the die). Elsewhere the parts on the dies are worked on directly.
    /// A part selected when the lock engages hands its selection over to its die.
    /// </summary>
    private void ApplyDieSurfaceLock(bool locked)
    {
        var selectedDie = Canvas.Surface.DieOf(Canvas.SelectedComponent);
        Canvas.Surface.IsContentLocked = locked;
        if (DieProperties != null)
            DieProperties.IsEditable = locked;
        if (locked)
        {
            if (selectedDie != null && Canvas.Components.FirstOrDefault(c => ReferenceEquals(c.Component, selectedDie)) is { } dieVm)
            {
                Canvas.Selection.SelectSingle(dieVm);
                Canvas.SelectedComponent = dieVm;
            }
            foreach (var part in Canvas.Surface.Items.Where(p => p.IsSelected))
            {
                part.IsSelected = false;
                Canvas.Selection.SelectedComponents.Remove(part);
            }
        }
        Canvas.RepaintRequested?.Invoke();
    }

    private void OnDieCreated(ComponentGroup die)
    {
        RefreshSystemOverview();
        var vm = Canvas.Components.FirstOrDefault(c => c.Component == die);
        if (vm != null)
        {
            Canvas.Selection.SelectSingle(vm);
            Canvas.SelectedComponent = vm;
        }
        StatusText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Services.Localization.LocalizationService.Instance.Translate("Dies.Status.Created"), die.GroupName);
        UpdateDieScope();
    }

    private void OnDiesChanged()
    {
        // The chip die stands for the chip only while the design is single-die.
        if (Canvas.ChipDie.IsSelected && !Canvas.ChipDie.IsShown)
        {
            Canvas.Selection.ClearSelection();
            Canvas.SelectedComponent = null;
        }
        RefreshSystemOverview();
        DieProperties.Reload();
        DieLinkProperties.Reload();
        UpdateDieScope(force: true);
    }

    private void OnCanvasPropertyChangedForDies(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Canvas.SelectedComponent) or nameof(Canvas.IsInGroupEditMode))
            UpdateDieScope();
    }

    /// <summary>
    /// Follows the die the user works with: shows its properties and scopes the library and
    /// PDK list to it; with no die in scope the design-level process lock applies again.
    /// </summary>
    private void UpdateDieScope(bool force = false)
    {
        var shown = PropertiesDie;
        if (!ReferenceEquals(DieProperties.Die, shown))
            DieProperties.Show(shown);
        var die = ScopeDie;
        if (die != null)
            Canvas.Surface.LastWorkedDie = die;
        var state = die == null ? ((string?, string)?)null
            : (die.ProcessBinding?.DisplayName, string.Join("|", die.Die?.EnabledPdkNames ?? Array.Empty<string>()));
        if (!force && ReferenceEquals(die, _scopedDie) && state == _scopedDieState)
            return;

        bool hadScope = _scopedDie != null;
        _scopedDie = die;
        _scopedDieState = state;
        OnPropertyChanged(nameof(ScopeDie));

        if (die != null)
            LeftPanel.ApplyDieScope(die.ProcessBinding, die.Die?.EnabledPdkNames);
        else if (hadScope)
            RefreshProcessIndicator();
        UpdateActiveProcessLabel(FileOperations.ActiveProcess);
    }
}
