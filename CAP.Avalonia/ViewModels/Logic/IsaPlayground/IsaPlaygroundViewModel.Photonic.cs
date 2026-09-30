using System.Globalization;
using CAP_Core.Logic.Isa;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// Photonic-ADD half of <see cref="IsaPlaygroundViewModel"/> (issue #1215, rung 5
/// slice 3): the toggle "Compute ADD on the photonic chip" swaps the emulator's ALU
/// from the golden C# model to <see cref="PhotonicAdderAlu"/> wrapping the logic
/// network the Logic tab last assembled (handed over through the shared
/// <see cref="CAP.Avalonia.Services.BuiltLogicNetworkProvider"/>). The toggle is
/// enabled only while a network that exposes the adder signals (A0–A3, B0–B3, Cin
/// in, S0–S3 out) is available; otherwise a hint points at the 4-bit adder example.
/// Toggling recreates the machine at power-on state, and every photonic ADD leaves
/// a status line with the operands and result in binary plus the light-travel time
/// of that addition (<see cref="PhotonicAdderAlu.LastAddTrace"/>, issue #1227).
/// Evaluation is a truth-table walk plus one event-timeline pass over the network
/// (344 gates on the shipped adder) — microseconds, far under the 100 ms UI budget,
/// pinned by a budget test — so stepping stays on the UI thread.
/// </summary>
public partial class IsaPlaygroundViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    private bool _isPhotonicAddAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _usePhotonicAdder;

    [ObservableProperty]
    private string _photonicStatusText = string.Empty;

    private int _photonicGateCount;
    private PhotonicAdderAlu? _photonicAlu;

    /// <summary>Test seam (InternalsVisibleTo UnitTests): the photonic ALU while the toggle is on.</summary>
    internal PhotonicAdderAlu? PhotonicAlu => _photonicAlu;

    /// <summary>
    /// The toggle can be flipped while an adder network is available and the machine
    /// is not auto-stepping (flipping mid-run would reset the machine under the timer).
    /// </summary>
    public bool IsPhotonicToggleEnabled => IsPhotonicAddAvailable && !IsRunning;

    /// <summary>
    /// The header title, naming the active ALU so the window never claims
    /// "golden model" while ADDs run on the photonic chip.
    /// </summary>
    public string HeaderTitle =>
        Translate(UsePhotonicAdder ? "IsaPlayground.TitlePhotonic" : "IsaPlayground.Title");

    /// <summary>Flipping the toggle resets the machine to power-on state with the chosen ALU.</summary>
    partial void OnUsePhotonicAdderChanged(bool value)
    {
        if (value && !IsPhotonicAddAvailable)
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
    /// is offered. Losing the adder network while the toggle is on falls back to the
    /// golden ALU (with the same machine reset a manual toggle would cause).
    /// </summary>
    private void OnBuiltNetworkChanged()
    {
        RefreshPhotonicAvailability();
        if (!IsPhotonicAddAvailable && UsePhotonicAdder)
        {
            UsePhotonicAdder = false;
        }
    }

    private void RefreshPhotonicAvailability() =>
        IsPhotonicAddAvailable = PhotonicAdderAlu.Accepts(_builtNetworkProvider?.Network);

    /// <summary>
    /// Creates a fresh machine for the assembled program on the currently selected ALU:
    /// the photonic adder network when the toggle is on, the golden model otherwise.
    /// </summary>
    private IsaEmulator CreateEmulator()
    {
        if (UsePhotonicAdder && _builtNetworkProvider?.Network is { } network)
        {
            _photonicGateCount = network.Gates.Count;
            _photonicAlu = new PhotonicAdderAlu(network);
            return new IsaEmulator(_assembledWords, _photonicAlu);
        }

        _photonicAlu = null;
        return new IsaEmulator(_assembledWords);
    }

    /// <summary>True when the next instruction to execute is an ADD and it will run photonically.</summary>
    private bool NextStepIsPhotonicAdd() =>
        UsePhotonicAdder
        && _emulator is { IsHalted: false }
        && _emulator.ProgramCounter < _assembledWords.Length
        && IsaInstruction.Decode(_assembledWords[_emulator.ProgramCounter], out _)?.Opcode == IsaOpcode.Add;

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
