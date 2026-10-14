using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Dies;

/// <summary>
/// The "New die" flyout of the Chiplets panel: pick a process and a size preset (or type a
/// custom size) and the die is placed to the right of the existing dies. In a single-die
/// design the current chip becomes "Die 1" of the new assembly in the same undo step.
/// </summary>
public partial class NewDieViewModel : ObservableObject
{
    private const double MicrometersPerMillimeter = 1000.0;
    private const double DefaultCustomMillimeters = 2.0;

    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private readonly Func<IReadOnlyList<ProcessGroup>> _catalog;
    private readonly Func<ActiveProcessSelection?> _designProcess;

    /// <summary>Creates the view model.</summary>
    /// <param name="canvas">The design canvas.</param>
    /// <param name="commands">Undo stack.</param>
    /// <param name="catalog">Live process catalog.</param>
    /// <param name="designProcess">The design-level process (becomes Die 1's process on conversion).</param>
    public NewDieViewModel(DesignCanvasViewModel canvas, CommandManager commands,
        Func<IReadOnlyList<ProcessGroup>> catalog, Func<ActiveProcessSelection?> designProcess)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _designProcess = designProcess ?? throw new ArgumentNullException(nameof(designProcess));
    }

    /// <summary>Processes offered for the new die.</summary>
    public ObservableCollection<DieProcessOption> ProcessOptions { get; } = new();

    /// <summary>Size presets of the selected process plus Custom.</summary>
    public ObservableCollection<DieSizeOption> SizeOptions { get; } = new();

    [ObservableProperty] private DieProcessOption? _selectedProcess;
    [ObservableProperty] private DieSizeOption? _selectedSize;
    [ObservableProperty] private double _customWidthMillimeters = DefaultCustomMillimeters;
    [ObservableProperty] private double _customHeightMillimeters = DefaultCustomMillimeters;

    /// <summary>True while the Custom size entry is selected (shows the width/height inputs).</summary>
    public bool IsCustomSize => SelectedSize?.IsCustom == true;

    /// <summary>Raised with the created die after a successful create.</summary>
    public Action<ComponentGroup>? DieCreated { get; set; }

    /// <summary>Refreshes the process list from the live catalog (call when the flyout opens).</summary>
    public void Prepare()
    {
        ProcessOptions.Clear();
        foreach (var option in DieProcessOption.FromCatalog(_catalog()))
            ProcessOptions.Add(option);
        var design = _designProcess();
        SelectedProcess = ProcessOptions.FirstOrDefault(o => o.Name != design?.DisplayName) ?? ProcessOptions.FirstOrDefault();
    }

    partial void OnSelectedProcessChanged(DieProcessOption? value)
    {
        SizeOptions.Clear();
        foreach (var option in DieSizeOption.For(value?.Selection, LocalizationService.Instance.Translate("Dies.Size.Custom")))
            SizeOptions.Add(option);
        SelectedSize = SizeOptions.FirstOrDefault();
    }

    partial void OnSelectedSizeChanged(DieSizeOption? value) => OnPropertyChanged(nameof(IsCustomSize));

    /// <summary>Creates the die (undoable) and selects it.</summary>
    [RelayCommand]
    private void Create()
    {
        var (width, height) = SelectedSize?.Preset is { } preset
            ? (preset.WidthMicrometers, preset.HeightMicrometers)
            : (CustomWidthMillimeters * MicrometersPerMillimeter, CustomHeightMillimeters * MicrometersPerMillimeter);
        if (!DieDefinition.IsValidDimension(width) || !DieDefinition.IsValidDimension(height))
            return;

        if (_canvas.IsInGroupEditMode)
            _canvas.ExitToRoot();
        var command = new CreateDieCommand(_canvas,
            new DieSpec(NextName(), SelectedProcess?.Selection, width, height),
            ExistingChipSpec());
        _commands.ExecuteCommand(command);
        if (command.CreatedDie != null)
            DieCreated?.Invoke(command.CreatedDie);
    }

    /// <summary>How the single-die chip becomes the assembly's first die.</summary>
    private DieSpec ExistingChipSpec()
    {
        var design = _designProcess();
        return new DieSpec(
            DieAssembly.NextDieName(Array.Empty<string>()),
            design is { IsPlayground: false } ? design : null,
            _canvas.Dies.RootChipWidthMicrometers,
            _canvas.Dies.RootChipHeightMicrometers);
    }

    private string NextName()
    {
        var taken = _canvas.Dies.RootDies.Select(d => d.GroupName).ToList();
        if (!_canvas.Dies.IsAssemblyRoot)
            taken.Add(DieAssembly.NextDieName(Array.Empty<string>()));
        return DieAssembly.NextDieName(taken);
    }
}
