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
/// a status line naming the network's gate count. Evaluation is a truth-table walk
/// over a few dozen gates — microseconds, far under the 100 ms UI budget — so
/// stepping stays on the UI thread.
/// </summary>
public partial class IsaPlaygroundViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    private bool _isPhotonicAddAvailable;

    [ObservableProperty]
    private bool _usePhotonicAdder;

    [ObservableProperty]
    private string _photonicStatusText = string.Empty;

    private int _photonicGateCount;

    /// <summary>
    /// The toggle can be flipped while an adder network is available and the machine
    /// is not auto-stepping (flipping mid-run would reset the machine under the timer).
    /// </summary>
    public bool IsPhotonicToggleEnabled => IsPhotonicAddAvailable && !IsRunning;

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
            return new IsaEmulator(_assembledWords, new PhotonicAdderAlu(network));
        }

        return new IsaEmulator(_assembledWords);
    }

    /// <summary>True when the next instruction to execute is an ADD and it will run photonically.</summary>
    private bool NextStepIsPhotonicAdd() =>
        UsePhotonicAdder
        && _emulator is { IsHalted: false }
        && _emulator.ProgramCounter < _assembledWords.Length
        && IsaInstruction.Decode(_assembledWords[_emulator.ProgramCounter], out _)?.Opcode == IsaOpcode.Add;

    /// <summary>Status line left after a photonic ADD: names the network's gate count.</summary>
    private void ReportPhotonicAdd() =>
        PhotonicStatusText = string.Format(
            CultureInfo.InvariantCulture,
            Translate("IsaPlayground.StatusPhotonicAdd"),
            _photonicGateCount);
}
