using System.Globalization;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// Photonic half of <see cref="IsaPlaygroundViewModel"/> (issue #1215, rung 5
/// slice 3; extended to NOT in issue #1275 and to AND in issue #1284): the photonic
/// toggle swaps the emulator's ALU from the golden C# model to a
/// <see cref="CompositeIsaAlu"/> that routes ADD, NOT and AND onto the logic network
/// the Logic tab last assembled (handed over through the shared
/// <see cref="CAP.Avalonia.Services.BuiltLogicNetworkProvider"/>) —
/// <see cref="PhotonicAdderAlu"/> for a network with the adder signals (A0–A3,
/// B0–B3, Cin in, S0–S3 out), <see cref="PhotonicNotAlu"/> for one with the NOT
/// signals (A0–A3 in, Y0–Y3 out), <see cref="PhotonicAndAlu"/> for one with the AND
/// signals (A0–A3, B0–B3 in, Y0–Y3 out); whichever operation the network cannot
/// compute falls back to the golden model. A plain default-map NOT is never routed
/// onto a network the AND unit accepts: the AND chip also exposes A0–A3 in / Y0–Y3
/// out, so <see cref="PhotonicNotAlu.Accepts"/> alone would match it and compute
/// A &amp; 0 instead of ~A (issue #1284). The combined logic-unit chip is the one
/// exception: it keeps AND on Y0–Y3 and moves NOT to its own taps N0–N3
/// (<see cref="IsaAluSignalMap.CombinedLogicUnitNot"/>), so on it AND and NOT both
/// run on light (issue #1295) and the toggle label, header and status line name
/// both operations. The zero-detect network (A0–A3 in, Z out) is no ALU, so with
/// the toggle on it instead moves the machine's one branch decision onto light:
/// every <c>JZ</c> asks a <see cref="PhotonicZeroFlag"/> whether the accumulator is
/// zero (issue #1322), and toggle label and header name the zero flag. The RAM 4x4
/// network (A0/A1, LOAD, D0–D3 in, Q0–Q3 out) moves the machine's data memory onto
/// light (issue #1446): every STORE clocks a photonic register through
/// <see cref="PhotonicDataMemory"/> and every RAM-operand read taps Q0–Q3, and the
/// RAM readout carries an "on light" chip while it does. The toggle
/// is enabled while the built network is accepted by any ALU, the zero flag or the
/// data memory; otherwise a hint points at the shipped examples (4-bit adder, NOT
/// 4-bit, AND 4-bit, Logic Unit 4-bit, Zero Detect 4-bit, RAM 4x4). Toggling recreates the machine at
/// power-on state, and every photonic operation leaves a status line with operands and result
/// in binary plus the gate count (photonic ADDs also name the light-travel time of
/// that addition, <see cref="PhotonicAdderAlu.LastAddTrace"/>, issue #1227).
/// Evaluation is a truth-table walk plus one event-timeline pass over the network —
/// microseconds, far under the 100 ms UI budget, pinned by a budget test — so
/// stepping stays on the UI thread.
/// </summary>
public partial class IsaPlaygroundViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicAddAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicNotAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicAndAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicZeroFlagAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicDataMemoryAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _usePhotonicAdder;

    [ObservableProperty]
    private string _photonicStatusText = string.Empty;

    private int _photonicGateCount;
    private PhotonicAdderAlu? _photonicAlu;
    private PhotonicNotAlu? _photonicNotAlu;
    private PhotonicAndAlu? _photonicAndAlu;
    private PhotonicZeroFlag? _photonicZeroFlag;
    private PhotonicDataMemory? _photonicDataMemory;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic ADD ALU while the toggle is on.</summary>
    internal PhotonicAdderAlu? PhotonicAlu => _photonicAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic NOT ALU while the toggle is on.</summary>
    internal PhotonicNotAlu? PhotonicNotAlu => _photonicNotAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic AND ALU while the toggle is on.</summary>
    internal PhotonicAndAlu? PhotonicAndAlu => _photonicAndAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic zero flag while the toggle is on.</summary>
    internal PhotonicZeroFlag? ZeroFlag => _photonicZeroFlag;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic data memory while the toggle is on.</summary>
    internal PhotonicDataMemory? DataMemory => _photonicDataMemory;

    /// <summary>
    /// True while the built network can run at least one operation on the photonic
    /// chip — the adder signals, the NOT signals, the AND signals, the zero-flag
    /// signals (A0–A3 in, Z out) or the RAM 4x4 signals (A0/A1, LOAD, D0–D3 in,
    /// Q0–Q3 out) that let the program's data memory live on light (issue #1446).
    /// </summary>
    public bool IsAnyPhotonicAvailable =>
        IsPhotonicAddAvailable || IsPhotonicNotAvailable || IsPhotonicAndAvailable
        || IsPhotonicZeroFlagAvailable || IsPhotonicDataMemoryAvailable;

    /// <summary>
    /// True while the emulator's data RAM lives on the photonic RAM 4x4 network —
    /// drives the "on light" chip next to the RAM readout (issue #1446).
    /// </summary>
    public bool IsPhotonicDataMemoryActive => _photonicDataMemory is not null;

    /// <summary>
    /// The toggle can be flipped while an accepted network is available and the
    /// machine is not auto-stepping (flipping mid-run would reset the machine under
    /// the timer).
    /// </summary>
    public bool IsPhotonicToggleEnabled => IsAnyPhotonicAvailable && !IsRunning;

    /// <summary>
    /// The toggle's label, naming the operation the current network would run on
    /// light: the adder label when the network exposes the adder signals, the AND
    /// label when it exposes the AND signals, the NOT label when it only exposes
    /// the NOT signals — and the combined label when the network runs both AND and
    /// NOT on light (the Logic Unit 4-bit chip, issue #1295) — and the zero-flag
    /// label when the network only decides <c>JZ</c> on light (the Zero Detect
    /// 4-bit chip, issue #1322).
    /// </summary>
    public string PhotonicToggleLabel =>
        Translate(IsPhotonicAddAvailable
                || (!IsPhotonicNotAvailable && !IsPhotonicAndAvailable && !IsPhotonicZeroFlagAvailable
                    && !IsPhotonicDataMemoryAvailable)
            ? "IsaPlayground.PhotonicAdderToggle"
            : IsPhotonicZeroFlagAvailable && !IsPhotonicAndAvailable && !IsPhotonicNotAvailable
                ? "IsaPlayground.PhotonicZeroFlagToggle"
                : IsPhotonicAndAvailable && IsPhotonicNotAvailable
                    ? "IsaPlayground.PhotonicAndNotToggle"
                    : IsPhotonicAndAvailable
                        ? "IsaPlayground.PhotonicAndToggle"
                        : IsPhotonicNotAvailable
                            ? "IsaPlayground.PhotonicNotToggle"
                            : "IsaPlayground.PhotonicDataMemoryToggle");

    /// <summary>
    /// The header title, naming the ALU the machine actually uses so the window
    /// never claims "golden model" (or "photonic adder") while a different engine
    /// computes on the photonic chip.
    /// </summary>
    public string HeaderTitle =>
        Translate(UsePhotonicAdder
            ? (_photonicAlu is not null
                ? "IsaPlayground.TitlePhotonic"
                : _photonicAndAlu is not null && _photonicNotAlu is not null
                    ? "IsaPlayground.TitlePhotonicAndNot"
                    : _photonicAndAlu is not null
                        ? "IsaPlayground.TitlePhotonicAnd"
                        : _photonicNotAlu is not null
                            ? "IsaPlayground.TitlePhotonicNot"
                            : _photonicDataMemory is not null
                                ? "IsaPlayground.TitlePhotonicDataMemory"
                                : "IsaPlayground.TitlePhotonicZeroFlag")
            : "IsaPlayground.Title");

    /// <summary>Flipping the toggle resets the machine to power-on state with the chosen ALU.</summary>
    partial void OnUsePhotonicAdderChanged(bool value)
    {
        if (value && !IsAnyPhotonicAvailable)
        {
            UsePhotonicAdder = false;
            return;
        }

        PhotonicStatusText = string.Empty;
        if (!IsAssembled)
        {
            return;
        }

        _emulator = CreateEmulator();
        UpdateState();
    }

    /// <summary>
    /// The network the Logic tab handed over changed: re-evaluate whether the toggle
    /// is offered. Losing every accepted network while the toggle is on falls back
    /// to the golden ALU (with the same machine reset a manual toggle would cause).
    /// </summary>
    private void OnBuiltNetworkChanged()
    {
        RefreshPhotonicAvailability();
        if (!IsAnyPhotonicAvailable && UsePhotonicAdder)
        {
            UsePhotonicAdder = false;
        }
    }

    private void RefreshPhotonicAvailability()
    {
        var network = _builtNetworkProvider?.Network;
        IsPhotonicAddAvailable = PhotonicAdderAlu.Accepts(network);
        IsPhotonicAndAvailable = PhotonicAndAlu.Accepts(network);
        // A default-map NOT must never ride on a network the AND unit accepts (the
        // A&0 trap, issue #1284); the combined logic-unit chip is safe because its
        // NOT reads its own N0–N3 taps instead of the AND chip's Y0–Y3 (issue #1295).
        IsPhotonicNotAvailable =
            (PhotonicNotAlu.Accepts(network) && !IsPhotonicAndAvailable)
            || PhotonicNotAlu.Accepts(network, IsaAluSignalMap.CombinedLogicUnitNot);
        IsPhotonicZeroFlagAvailable = PhotonicZeroFlag.Accepts(network);
        IsPhotonicDataMemoryAvailable = PhotonicDataMemory.Accepts(network);
    }

    /// <summary>
    /// Creates a fresh machine for the assembled program on the currently selected
    /// ALU: a composite that routes ADD, NOT and AND onto the photonic network
    /// wherever the network exposes the operation's signals (golden model for the
    /// rest) when the toggle is on, the golden model alone otherwise. The NOT unit
    /// stays golden on a network the AND unit accepts, because that network's Y
    /// taps carry A &amp; B (with B tied to 0), not ~A (issue #1284) — unless the
    /// network exposes the combined logic-unit NOT taps N0–N3, in which case NOT
    /// runs on light through <see cref="IsaAluSignalMap.CombinedLogicUnitNot"/>
    /// alongside AND (issue #1295).
    /// </summary>
    private IsaEmulator CreateEmulator()
    {
        if (UsePhotonicAdder && _builtNetworkProvider?.Network is { } network)
        {
            _photonicGateCount = network.Gates.Count;
            _photonicAlu = PhotonicAdderAlu.Accepts(network) ? new PhotonicAdderAlu(network) : null;
            _photonicAndAlu = PhotonicAndAlu.Accepts(network) ? new PhotonicAndAlu(network) : null;
            _photonicNotAlu = CreatePhotonicNotAlu(network);
            _photonicZeroFlag = PhotonicZeroFlag.Accepts(network) ? new PhotonicZeroFlag(network) : null;
            _photonicDataMemory = PhotonicDataMemory.Accepts(network) ? new PhotonicDataMemory(network) : null;
            var golden = new GoldenIsaAlu();
            var emulator = new IsaEmulator(_assembledWords, new CompositeIsaAlu(
                _photonicAlu ?? (IIsaAlu)golden,
                _photonicNotAlu ?? (IIsaAlu)golden,
                _photonicAndAlu ?? (IIsaAlu)golden),
                _photonicZeroFlag,
                _photonicDataMemory);
            OnPropertyChanged(nameof(IsPhotonicDataMemoryActive));
            return emulator;
        }

        _photonicAlu = null;
        _photonicNotAlu = null;
        _photonicAndAlu = null;
        _photonicZeroFlag = null;
        _photonicDataMemory = null;
        OnPropertyChanged(nameof(IsPhotonicDataMemoryActive));
        return new IsaEmulator(_assembledWords);
    }

    /// <summary>
    /// The photonic NOT unit for <paramref name="network"/>, or null when NOT stays
    /// golden: the default-map unit only on networks the AND unit does not accept,
    /// the combined logic-unit map (N0–N3 taps) when the network exposes them.
    /// </summary>
    private PhotonicNotAlu? CreatePhotonicNotAlu(LogicNetworkEvaluator network)
    {
        if (_photonicAndAlu is null)
        {
            return PhotonicNotAlu.Accepts(network) ? new PhotonicNotAlu(network) : null;
        }

        return PhotonicNotAlu.Accepts(network, IsaAluSignalMap.CombinedLogicUnitNot)
            ? new PhotonicNotAlu(network, IsaAluSignalMap.CombinedLogicUnitNot)
            : null;
    }

    /// <summary>True when the next instruction to execute is an ADD and it will run photonically.</summary>
    private bool NextStepIsPhotonicAdd() =>
        UsePhotonicAdder
        && _photonicAlu is not null
        && _emulator is { IsHalted: false }
        && _emulator.ProgramCounter < _assembledWords.Length
        && IsaInstruction.Decode(_assembledWords[_emulator.ProgramCounter], out _)?.Opcode == IsaOpcode.Add;

    /// <summary>True when the next instruction to execute is a NOT and it will run photonically.</summary>
    private bool NextStepIsPhotonicNot() =>
        UsePhotonicAdder
        && _photonicNotAlu is not null
        && _emulator is { IsHalted: false }
        && _emulator.ProgramCounter < _assembledWords.Length
        && IsaInstruction.Decode(_assembledWords[_emulator.ProgramCounter], out _)?.Opcode == IsaOpcode.Not;

    /// <summary>
    /// Status line left after a photonic ADD: operands and result in binary, the
    /// light-travel time of that addition in ps, and the network's gate count.
    /// </summary>
    private void ReportPhotonicAdd()
    {
        if (_photonicAlu?.LastAddTrace is not { } trace)
        {
            return;
        }

        PhotonicStatusText = string.Format(
            CultureInfo.InvariantCulture,
            Translate("IsaPlayground.StatusPhotonicAdd"),
            ToBinary(trace.A),
            ToBinary(trace.B),
            ToBinary(trace.Sum),
            trace.LightTravelPicoseconds,
            _photonicGateCount);
    }

    /// <summary>
    /// True when the next instruction to execute is an AND and it will run
    /// photonically; then <paramref name="operandA"/> is ACC and
    /// <paramref name="operandB"/> the RAM word the AND reads (both captured before
    /// the step, because the step overwrites ACC with the result).
    /// </summary>
    private bool NextStepIsPhotonicAnd(out int operandA, out int operandB)
    {
        operandA = 0;
        operandB = 0;
        if (!UsePhotonicAdder
            || _photonicAndAlu is null
            || _emulator is not { IsHalted: false } emulator
            || emulator.ProgramCounter >= _assembledWords.Length
            || IsaInstruction.Decode(_assembledWords[emulator.ProgramCounter], out var address)?.Opcode
                != IsaOpcode.And)
        {
            return false;
        }

        operandA = emulator.Accumulator;
        operandB = emulator.Ram[address];
        return true;
    }

    /// <summary>
    /// Status line left after a photonic AND (issue #1284): both operands captured
    /// before the step, the result (ACC after the step), all in binary, and the
    /// network's gate count.
    /// </summary>
    private void ReportPhotonicAnd(int operandA, int operandB)
    {
        if (_emulator is null)
        {
            return;
        }

        PhotonicStatusText = string.Format(
            CultureInfo.InvariantCulture,
            Translate("IsaPlayground.StatusPhotonicAnd"),
            ToBinary(operandA),
            ToBinary(operandB),
            ToBinary(_emulator.Accumulator),
            _photonicGateCount);
    }

    /// <summary>
    /// Status line left after a photonic NOT (issue #1275): the operand captured
    /// before the step, the result (ACC after the step), both in binary, and the
    /// network's gate count.
    /// </summary>
    private void ReportPhotonicNot(int operand)
    {
        if (_emulator is null)
        {
            return;
        }

        PhotonicStatusText = string.Format(
            CultureInfo.InvariantCulture,
            Translate("IsaPlayground.StatusPhotonicNot"),
            ToBinary(operand),
            ToBinary(_emulator.Accumulator),
            _photonicGateCount);
    }

    /// <summary>
    /// Publishes the operand bits of the last photonic ADD (A0–A3 = ACC, B0–B3 =
    /// RAM[operand], Cin = 0) through the shared provider, so the Logic panel mirrors
    /// them onto its input toggles and the canvas badges show the addition (issue
    /// #1240). Golden-model ADDs never reach here — the call sites gate on
    /// <see cref="NextStepIsPhotonicAdd"/>.
    /// </summary>
    private void PublishDrivenInputs()
    {
        if (_photonicAlu?.LastAddTrace is not { } trace)
        {
            return;
        }

        var bits = new Dictionary<string, bool>(2 * AccumulatorBits + 1);
        for (var bit = 0; bit < AccumulatorBits; bit++)
        {
            bits[$"A{bit}"] = ((trace.A >> bit) & 1) == 1;
            bits[$"B{bit}"] = ((trace.B >> bit) & 1) == 1;
        }

        bits["Cin"] = false;
        _builtNetworkProvider?.DriveInputs(bits);
    }

    private static string ToBinary(int value) =>
        Convert.ToString(value, 2).PadLeft(AccumulatorBits, '0');
}
