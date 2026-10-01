using System.Globalization;
using CAP_Core.Logic.Isa;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// Photonic half of <see cref="IsaPlaygroundViewModel"/> (issue #1215, rung 5
/// slice 3; extended to NOT in issue #1275): the photonic toggle swaps the emulator's
/// ALU from the golden C# model to a <see cref="CompositeIsaAlu"/> that routes ADD
/// and NOT onto the logic network the Logic tab last assembled (handed over through
/// the shared <see cref="CAP.Avalonia.Services.BuiltLogicNetworkProvider"/>) —
/// <see cref="PhotonicAdderAlu"/> for a network with the adder signals (A0–A3,
/// B0–B3, Cin in, S0–S3 out), <see cref="PhotonicNotAlu"/> for one with the NOT
/// signals (A0–A3 in, Y0–Y3 out); whichever operation the network cannot compute
/// falls back to the golden model. The toggle is enabled while the built network is
/// accepted by either ALU; otherwise a hint points at both shipped examples (4-bit
/// adder, NOT 4-bit). Toggling recreates the machine at power-on state, and every
/// photonic operation leaves a status line with operand and result in binary plus
/// the gate count (photonic ADDs also name the light-travel time of that addition,
/// <see cref="PhotonicAdderAlu.LastAddTrace"/>, issue #1227). Evaluation is a
/// truth-table walk plus one event-timeline pass over the network — microseconds,
/// far under the 100 ms UI budget, pinned by a budget test — so stepping stays on
/// the UI thread.
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
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _usePhotonicAdder;

    [ObservableProperty]
    private string _photonicStatusText = string.Empty;

    private int _photonicGateCount;
    private PhotonicAdderAlu? _photonicAlu;
    private PhotonicNotAlu? _photonicNotAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic ADD ALU while the toggle is on.</summary>
    internal PhotonicAdderAlu? PhotonicAlu => _photonicAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic NOT ALU while the toggle is on.</summary>
    internal PhotonicNotAlu? PhotonicNotAlu => _photonicNotAlu;

    /// <summary>
    /// True while the built network can run at least one operation on the photonic
    /// chip — the adder signals or the NOT signals.
    /// </summary>
    public bool IsAnyPhotonicAvailable => IsPhotonicAddAvailable || IsPhotonicNotAvailable;

    /// <summary>
    /// The toggle can be flipped while an accepted network is available and the
    /// machine is not auto-stepping (flipping mid-run would reset the machine under
    /// the timer).
    /// </summary>
    public bool IsPhotonicToggleEnabled => IsAnyPhotonicAvailable && !IsRunning;

    /// <summary>
    /// The toggle's label, naming the operation the current network would run on
    /// light: the adder label when the network exposes the adder signals, the NOT
    /// label when it only exposes the NOT signals.
    /// </summary>
    public string PhotonicToggleLabel =>
        Translate(IsPhotonicAddAvailable || !IsPhotonicNotAvailable
            ? "IsaPlayground.PhotonicAdderToggle"
            : "IsaPlayground.PhotonicNotToggle");

    /// <summary>
    /// The header title, naming the ALU the machine actually uses so the window
    /// never claims "golden model" (or "photonic adder") while a different engine
    /// computes on the photonic chip.
    /// </summary>
    public string HeaderTitle =>
        Translate(UsePhotonicAdder
            ? (_photonicAlu is null && _photonicNotAlu is not null
                ? "IsaPlayground.TitlePhotonicNot"
                : "IsaPlayground.TitlePhotonic")
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
        IsPhotonicNotAvailable = PhotonicNotAlu.Accepts(network);
    }

    /// <summary>
    /// Creates a fresh machine for the assembled program on the currently selected
    /// ALU: a composite that routes ADD and NOT onto the photonic network wherever
    /// the network exposes the operation's signals (golden model for the rest) when
    /// the toggle is on, the golden model alone otherwise.
    /// </summary>
    private IsaEmulator CreateEmulator()
    {
        if (UsePhotonicAdder && _builtNetworkProvider?.Network is { } network)
        {
            _photonicGateCount = network.Gates.Count;
            _photonicAlu = PhotonicAdderAlu.Accepts(network) ? new PhotonicAdderAlu(network) : null;
            _photonicNotAlu = PhotonicNotAlu.Accepts(network) ? new PhotonicNotAlu(network) : null;
            var golden = new GoldenIsaAlu();
            return new IsaEmulator(_assembledWords, new CompositeIsaAlu(
                _photonicAlu ?? (IIsaAlu)golden,
                _photonicNotAlu ?? (IIsaAlu)golden));
        }

        _photonicAlu = null;
        _photonicNotAlu = null;
        return new IsaEmulator(_assembledWords);
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
