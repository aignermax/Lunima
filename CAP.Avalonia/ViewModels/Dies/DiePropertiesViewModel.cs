using System.Collections.ObjectModel;
using Avalonia.Media;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Dies;

/// <summary>
/// Properties of the selected die (Properties panel): name, outline size (process presets or
/// custom), process, the PDKs enabled on this die and the edge-zone width. Every edit is one
/// undoable <see cref="UpdateDieCommand"/>; the die's process and PDKs scope the component
/// library while the die is selected or open.
/// </summary>
public partial class DiePropertiesViewModel : ObservableObject
{
    private const double MicrometersPerMillimeter = 1000.0;

    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private readonly Func<IReadOnlyList<ProcessGroup>> _catalog;
    private bool _loading;

    /// <summary>Creates the view model over the canvas, undo stack and live process catalog.</summary>
    public DiePropertiesViewModel(DesignCanvasViewModel canvas, CommandManager commands, Func<IReadOnlyList<ProcessGroup>> catalog)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>The die shown, or null (section hidden).</summary>
    [ObservableProperty] private ComponentGroup? _die;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _widthMillimeters;
    [ObservableProperty] private double _heightMillimeters;
    [ObservableProperty] private double _edgeZoneMicrometers;
    [ObservableProperty] private DieSizeOption? _selectedSize;
    [ObservableProperty] private DieProcessOption? _selectedProcess;
    [ObservableProperty] private string _statusText = "";

    /// <summary>True while a die is shown.</summary>
    public bool IsVisible => Die != null;

    /// <summary>Size choices for the die's process.</summary>
    public ObservableCollection<DieSizeOption> SizeOptions { get; } = new();

    /// <summary>Processes the die can be switched to.</summary>
    public ObservableCollection<DieProcessOption> ProcessOptions { get; } = new();

    /// <summary>PDKs of the die's process with their per-die enabled state.</summary>
    public ObservableCollection<DiePdkOption> Pdks { get; } = new();

    /// <summary>The die's process color.</summary>
    public IBrush ProcessBrush => DieProcessColors.BrushFor(Die?.ProcessBinding);

    /// <summary>"3 of 4 PDKs enabled" style summary.</summary>
    public string PdkSummary => string.Format(LocalizationService.Instance.Translate("Dies.Properties.PdkSummary"),
        Pdks.Count(p => p.IsEnabled), Pdks.Count);

    /// <summary>Opens the die (shows only its content) when requested from the panel.</summary>
    public Action<ComponentGroup>? OpenDieRequested { get; set; }

    /// <summary>Shows <paramref name="die"/> (or hides the section for null).</summary>
    public void Show(ComponentGroup? die)
    {
        Die = die is { IsDie: true } ? die : null;
        Reload();
    }

    /// <summary>Re-reads the shown die (after undo/redo or an edit elsewhere).</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            if (Die?.Die is { } definition)
                Load(Die, definition);
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(ProcessBrush));
            OnPropertyChanged(nameof(PdkSummary));
        }
        finally
        {
            _loading = false;
        }
    }

    private void Load(ComponentGroup die, DieDefinition definition)
    {
        Name = die.GroupName;
        WidthMillimeters = definition.WidthMicrometers / MicrometersPerMillimeter;
        HeightMillimeters = definition.HeightMicrometers / MicrometersPerMillimeter;
        EdgeZoneMicrometers = definition.EdgeZoneMicrometers;

        ProcessOptions.Clear();
        foreach (var option in DieProcessOption.FromCatalog(_catalog()))
            ProcessOptions.Add(option);
        SelectedProcess = ProcessOptions.FirstOrDefault(o => SameProcess(o.Selection, die.ProcessBinding))
                          ?? AddCurrentProcess(die.ProcessBinding);

        SizeOptions.Clear();
        foreach (var option in DieSizeOption.For(die.ProcessBinding, LocalizationService.Instance.Translate("Dies.Size.Custom")))
            SizeOptions.Add(option);
        SelectedSize = SizeOptions.FirstOrDefault(o => o.Preset is { } p
                           && Math.Abs(p.WidthMicrometers - definition.WidthMicrometers) < 1e-6
                           && Math.Abs(p.HeightMicrometers - definition.HeightMicrometers) < 1e-6)
                       ?? SizeOptions.LastOrDefault();

        Pdks.Clear();
        foreach (var pdk in die.ProcessBinding?.MemberPdkNames ?? Array.Empty<string>())
            Pdks.Add(new DiePdkOption(pdk, definition.AllProcessPdksEnabled
                || definition.EnabledPdkNames!.Contains(pdk, StringComparer.OrdinalIgnoreCase), OnPdkToggled));
    }

    private DieProcessOption? AddCurrentProcess(ActiveProcessSelection? process)
    {
        if (process == null)
            return null;
        var option = new DieProcessOption(process.DisplayName, process);
        ProcessOptions.Add(option);
        return option;
    }

    partial void OnNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Commit(state => state with { Name = value.Trim() });
    }

    partial void OnWidthMillimetersChanged(double value) => CommitSize();

    partial void OnHeightMillimetersChanged(double value) => CommitSize();

    partial void OnEdgeZoneMicrometersChanged(double value) =>
        Commit(state => state with { Definition = state.Definition with { EdgeZoneMicrometers = Math.Max(0, value) } });

    partial void OnSelectedSizeChanged(DieSizeOption? value)
    {
        if (_loading || value?.Preset is not { } preset)
            return;
        Commit(state => state with
        {
            Definition = state.Definition with
            {
                WidthMicrometers = preset.WidthMicrometers,
                HeightMicrometers = preset.HeightMicrometers,
            },
        });
    }

    partial void OnSelectedProcessChanged(DieProcessOption? value)
    {
        if (value == null)
            return;
        Commit(state => state with { Process = value.Selection, Definition = state.Definition with { EnabledPdkNames = null } });
    }

    private void CommitSize()
    {
        double width = WidthMillimeters * MicrometersPerMillimeter, height = HeightMillimeters * MicrometersPerMillimeter;
        if (!DieDefinition.IsValidDimension(width) || !DieDefinition.IsValidDimension(height))
            return;
        Commit(state => state with { Definition = state.Definition with { WidthMicrometers = width, HeightMicrometers = height } });
    }

    private void OnPdkToggled()
    {
        if (_loading)
            return;
        var enabled = Pdks.Where(p => p.IsEnabled).Select(p => p.Name).ToList();
        if (enabled.Count == 0)
        {
            StatusText = LocalizationService.Instance.Translate("Dies.Properties.NeedOnePdk");
            Reload();
            return;
        }
        Commit(state => state with
        {
            Definition = state.Definition with { EnabledPdkNames = enabled.Count == Pdks.Count ? null : enabled },
        });
    }

    private void Commit(Func<DieState, DieState> edit)
    {
        if (_loading || Die is not { IsDie: true } die)
            return;
        var command = UpdateDieCommand.TryCreate(_canvas, die, edit(DieState.Of(die)), out var refusal);
        if (command != null)
        {
            _commands.ExecuteCommand(command);
            StatusText = "";
        }
        else if (refusal != null)
        {
            StatusText = refusal;
        }
        Reload();
    }

    /// <summary>Opens the die so only its content is shown and editable.</summary>
    [RelayCommand]
    private void OpenDie()
    {
        if (Die != null)
            OpenDieRequested?.Invoke(Die);
    }

    /// <summary>Deletes the die with all its content (undoable).</summary>
    [RelayCommand]
    private void DeleteDie()
    {
        var vm = _canvas.Components.FirstOrDefault(c => c.Component == Die);
        if (vm == null)
            return;
        _commands.ExecuteCommand(new DeleteDieCommand(_canvas, vm));
        Show(null);
    }

    private static bool SameProcess(ActiveProcessSelection a, ActiveProcessSelection? b) =>
        b != null && string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
}
