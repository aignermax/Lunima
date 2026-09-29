using System.Collections.ObjectModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Logic.Isa;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// ViewModel of the ISA playground tool window (issue #1194): edit a 4-bit
/// assembly program (or pick a shipped sample), assemble it into the golden
/// <see cref="IsaEmulator"/>, and step through it while the machine state and
/// the current source line stay visible. Editing the text marks the assembled
/// state stale until <see cref="AssembleCommand"/> runs again.
/// </summary>
public partial class IsaPlaygroundViewModel : ObservableObject
{
    private const int AccumulatorBits = 4;

    private readonly IsaAssembler _assembler = new();
    private IsaEmulator? _emulator;
    private IReadOnlyList<int> _instructionLineNumbers = Array.Empty<int>();

    [ObservableProperty]
    private string _programText = string.Empty;

    [ObservableProperty]
    private IsaSampleProgram? _selectedSample;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool _isAssembled;

    [ObservableProperty]
    private int _programCounter;

    [ObservableProperty]
    private int _accumulator;

    [ObservableProperty]
    private string _accumulatorBinary = string.Empty;

    [ObservableProperty]
    private string _ramText = string.Empty;

    [ObservableProperty]
    private string _machineStatusText = string.Empty;

    /// <summary>Creates the playground with the samples discovered next to the app (or the repo).</summary>
    public IsaPlaygroundViewModel()
        : this(IsaSampleProgramCatalog.LoadDefault())
    {
    }

    /// <summary>Creates the playground with an explicit sample catalog (test seam).</summary>
    internal IsaPlaygroundViewModel(IsaSampleProgramCatalog catalog)
    {
        Samples = catalog.Samples;
        if (Samples.Count == 0)
        {
            ErrorText = Translate("IsaPlayground.NoSamplesFound");
            return;
        }

        // Loads the sample's source into the editor and auto-assembles it,
        // so the window opens demo-ready.
        SelectedSample = Samples[0];
    }

    /// <summary>The sample programs shown in the picker.</summary>
    public IReadOnlyList<IsaSampleProgram> Samples { get; }

    /// <summary>All source lines with line numbers; the executed line is highlighted.</summary>
    public ObservableCollection<IsaTraceLineViewModel> TraceLines { get; } = new();

    /// <summary>Selecting a sample loads its source into the editor and assembles it.</summary>
    partial void OnSelectedSampleChanged(IsaSampleProgram? value)
    {
        if (value == null)
        {
            return;
        }

        ProgramText = value.Source;
        Assemble();
    }

    /// <summary>Edits make the assembled state stale: disable stepping and drop the highlight.</summary>
    partial void OnProgramTextChanged(string value)
    {
        IsAssembled = false;
        HighlightCurrentLine(currentLine: null);
    }

    /// <summary>Assembles the editor text; on success resets the machine with the new program.</summary>
    [RelayCommand]
    private void Assemble()
    {
        try
        {
            var result = _assembler.AssembleWithSourceMap(ProgramText);
            _emulator = new IsaEmulator(result.Words);
            _instructionLineNumbers = result.InstructionLineNumbers;
            ErrorText = string.Empty;
            IsAssembled = true;
            RebuildTraceLines();
            UpdateState();
        }
        catch (IsaAssemblerException ex)
        {
            ErrorText = string.Format(
                CultureInfo.InvariantCulture,
                Translate("IsaPlayground.ErrorFormat"),
                ex.LineNumber,
                StripLinePrefix(ex));
            IsAssembled = false;
            _emulator = null;
            _instructionLineNumbers = Array.Empty<int>();
            TraceLines.Clear();
            ZeroState();
        }
    }

    /// <summary>Executes the single instruction at the program counter.</summary>
    [RelayCommand(CanExecute = nameof(IsAssembled))]
    private void Step()
    {
        if (_emulator is null)
        {
            return;
        }

        try
        {
            _emulator.Step();
        }
        catch (InvalidOperationException ex)
        {
            ErrorText = ex.Message;
        }

        UpdateState();
    }

    /// <summary>Restores the power-on state; the assembled program stays loaded.</summary>
    [RelayCommand(CanExecute = nameof(IsAssembled))]
    private void Reset()
    {
        _emulator?.Reset();
        UpdateState();
    }

    private void UpdateState()
    {
        if (_emulator is null)
        {
            ZeroState();
            return;
        }

        ProgramCounter = _emulator.ProgramCounter;
        Accumulator = _emulator.Accumulator;
        AccumulatorBinary = Convert.ToString(_emulator.Accumulator, 2).PadLeft(AccumulatorBits, '0');
        RamText = string.Join("  ", _emulator.Ram);
        MachineStatusText = Translate(_emulator.IsHalted ? "IsaPlayground.StatusHalted" : "IsaPlayground.StatusRunning");
        HighlightCurrentLine(FindCurrentLine());
    }

    /// <summary>The source line the program counter points at, or null when halted/past the program.</summary>
    private int? FindCurrentLine()
    {
        if (_emulator is { IsHalted: false } && _emulator.ProgramCounter < _instructionLineNumbers.Count)
        {
            return _instructionLineNumbers[_emulator.ProgramCounter];
        }

        return null;
    }

    private void HighlightCurrentLine(int? currentLine)
    {
        foreach (var line in TraceLines)
        {
            line.IsCurrent = line.LineNumber == currentLine;
        }
    }

    /// <summary>Rebuilds the trace listing from every editor line, so numbers match the editor.</summary>
    private void RebuildTraceLines()
    {
        TraceLines.Clear();
        var rawLines = ProgramText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < rawLines.Length; i++)
        {
            TraceLines.Add(new IsaTraceLineViewModel(i + 1, rawLines[i].TrimEnd()));
        }
    }

    private void ZeroState()
    {
        ProgramCounter = 0;
        Accumulator = 0;
        AccumulatorBinary = string.Empty;
        RamText = string.Empty;
        MachineStatusText = string.Empty;
    }

    /// <summary>Drops the "Line N: " prefix the exception message already carries.</summary>
    private static string StripLinePrefix(IsaAssemblerException ex)
    {
        var prefix = string.Format(CultureInfo.InvariantCulture, "Line {0}: ", ex.LineNumber);
        return ex.Message.StartsWith(prefix, StringComparison.Ordinal) ? ex.Message[prefix.Length..] : ex.Message;
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}
