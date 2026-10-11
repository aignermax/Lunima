using System.Globalization;
using CAP_Core.Logic.Isa;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// Status-line half of the photonic playground (split out of
/// IsaPlaygroundViewModel.Photonic.cs under the 500-line architecture limit):
/// the "next step runs photonically" probes the step/run commands consult before
/// executing, and the status lines plus Logic-panel badge bits they publish after
/// a photonic ADD, AND or NOT. Machine construction and unit availability stay in
/// IsaPlaygroundViewModel.Photonic.cs.
/// </summary>
public partial class IsaPlaygroundViewModel
{

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
