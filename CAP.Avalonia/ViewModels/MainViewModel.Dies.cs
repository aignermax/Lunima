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

    /// <summary>The die the user currently works with: the open die, or the die selected on the assembly.</summary>
    public ComponentGroup? ScopeDie =>
        Canvas.Dies.ActiveDie
        ?? (Canvas.Dies.IsAssemblyRoot && Canvas.SelectedComponent?.Component is ComponentGroup { IsDie: true } die ? die : null);

    private void InitializeDies()
    {
        IReadOnlyList<ProcessGroup> Catalog() => ProcessCatalog.BuildGroups(LeftPanel.GetLoadedPdkProcessEntries());
        DieProperties = new DiePropertiesViewModel(Canvas, CommandManager, Catalog)
        {
            OpenDieRequested = OpenDie,
        };
        NewDie = new NewDieViewModel(Canvas, CommandManager, Catalog, () => FileOperations.ActiveProcess)
        {
            DieCreated = OnDieCreated,
        };

        DieLinkProperties = new DieLinkPropertiesViewModel(Canvas, CommandManager);

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

    /// <summary>Opens <paramref name="die"/> for editing (undoable like a double-click).</summary>
    public void OpenDie(ComponentGroup die)
    {
        if (Canvas.IsInGroupEditMode)
            Canvas.ExitToRoot();
        CommandManager.ExecuteCommand(new EnterGroupEditModeCommand(Canvas, die));
        LeftPanel.HierarchyPanel?.RebuildTree();
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
        var die = ScopeDie;
        var state = die == null ? ((string?, string)?)null
            : (die.ProcessBinding?.DisplayName, string.Join("|", die.Die?.EnabledPdkNames ?? Array.Empty<string>()));
        if (!force && ReferenceEquals(die, _scopedDie) && state == _scopedDieState)
            return;

        bool hadScope = _scopedDie != null;
        _scopedDie = die;
        _scopedDieState = state;
        if (!ReferenceEquals(DieProperties.Die, die))
            DieProperties.Show(die);
        OnPropertyChanged(nameof(ScopeDie));

        if (die != null)
            LeftPanel.ApplyDieScope(die.ProcessBinding, die.Die?.EnabledPdkNames);
        else if (hadScope)
            RefreshProcessIndicator();
    }
}
