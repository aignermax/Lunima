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
/// RAM readout carries an "on light" chip while it does. On the combined ALU + RAM
/// chip the data memory answers to the <c>RAM.</c>-prefixed signal map (default map
/// first, prefixed as fallback), so the adder and the memory run on light together
/// and toggle label, header and unit-chip row name both (issue #1468). The combined
/// ALU + RAM + ACC chip adds the 4-bit accumulator register
/// (<c>ACC.D0</c>–<c>ACC.D3</c>, <c>ACC.LOAD</c> → <c>ACC.Q0</c>–<c>ACC.Q3</c>):
/// every instruction then clocks the machine's working register through a
/// <see cref="PhotonicAccumulator"/>, so all three units run on light on one chip
/// and toggle label, header and unit-chip row name all three (issue #1479). The toggle
/// is enabled while the built network is accepted by any ALU, the zero flag, the
/// data memory or the accumulator; otherwise a hint points at the shipped examples (4-bit adder, NOT
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
    [NotifyPropertyChangedFor(nameof(IsAnyPhotonicAvailable))]
    [NotifyPropertyChangedFor(nameof(IsPhotonicToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(PhotonicToggleLabel))]
    private bool _isPhotonicAccumulatorAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private bool _usePhotonicAdder;

    [ObservableProperty]
    private string _photonicStatusText = string.Empty;

    /// <summary>
    /// The prefix the shipped ALU + RAM chip puts its RAM signals under (issue
    /// #1463), so the data memory can live next to the adder's plain names.
    /// </summary>
    private const string RamSignalPrefix = "RAM.";

    private int _photonicGateCount;
    private PhotonicAdderAlu? _photonicAlu;
    private PhotonicNotAlu? _photonicNotAlu;
    private PhotonicAndAlu? _photonicAndAlu;
    private PhotonicZeroFlag? _photonicZeroFlag;
    private PhotonicDataMemory? _photonicDataMemory;
    private PhotonicAccumulator? _photonicAccumulator;
    private IsaDataMemorySignalMap? _dataMemoryMap;

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
    /// Test seam (InternalsVisibleTo UnitTests): the photonic accumulator while the
    /// toggle is on. Named <c>PhotonicAccumulator</c>, not <c>Accumulator</c>, because
    /// the playground already publishes the machine's ACC value under that name.
    /// </summary>
    internal PhotonicAccumulator? PhotonicAccumulator => _photonicAccumulator;

    /// <summary>
    /// The "what runs on light" row (issue #1456): one chip per machine unit —
    /// ALU, Z (zero flag), RAM, ACC, PC — green while that unit is computed by the
    /// photonic network, grey while it is simulated electronically. PC is always
    /// electronic for now; the ACC chip lights up while a
    /// <see cref="PhotonicAccumulator"/> clocks the machine's working register
    /// (issue #1479); ALU counts as on light when any photonic ALU
    /// half (adder, NOT or AND) is active.
    /// </summary>
    public IReadOnlyList<IsaUnitChipViewModel> UnitChips { get; } = new IsaUnitChipViewModel[]
    {
        new("ALU"),
        new("Z"),
        new("RAM"),
        new("ACC"),
        new("PC"),
    };

    /// <summary>
    /// Reflects the freshly created emulator's photonic parts onto the unit chips;
    /// called by <see cref="CreateEmulator"/> on every machine (re)creation, so the
    /// row always names what actually computes — toggle off means all electronic.
    /// </summary>
    private void UpdateUnitChips()
    {
        UnitChips[0].IsOnLight = _photonicAlu is not null || _photonicNotAlu is not null || _photonicAndAlu is not null;
        UnitChips[1].IsOnLight = _photonicZeroFlag is not null;
        UnitChips[2].IsOnLight = _photonicDataMemory is not null;
        UnitChips[3].IsOnLight = _photonicAccumulator is not null;
    }

    /// <summary>
    /// True while the built network can run at least one operation on the photonic
    /// chip — the adder signals, the NOT signals, the AND signals, the zero-flag
    /// signals (A0–A3 in, Z out), the RAM 4x4 signals (A0/A1, LOAD, D0–D3 in,
    /// Q0–Q3 out) that let the program's data memory live on light (issue #1446),
    /// or the accumulator signals (ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3) that
    /// clock the machine's working register on light (issue #1479).
    /// </summary>
    public bool IsAnyPhotonicAvailable =>
        IsPhotonicAddAvailable || IsPhotonicNotAvailable || IsPhotonicAndAvailable
        || IsPhotonicZeroFlagAvailable || IsPhotonicDataMemoryAvailable
        || IsPhotonicAccumulatorAvailable;

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
    /// 4-bit chip, issue #1322) — and the three-unit label when the network runs
    /// the adder, the data RAM and the accumulator on light together (the combined
    /// ALU + RAM + ACC chip, issue #1479).
    /// </summary>
    public string PhotonicToggleLabel =>
        Translate(IsPhotonicAddAvailable && IsPhotonicDataMemoryAvailable && IsPhotonicAccumulatorAvailable
            ? "IsaPlayground.PhotonicAdderDataMemoryAccumulatorToggle"
            : IsPhotonicAddAvailable && IsPhotonicDataMemoryAvailable
                ? "IsaPlayground.PhotonicAdderDataMemoryToggle"
                : IsPhotonicAddAvailable
                    || (!IsPhotonicNotAvailable && !IsPhotonicAndAvailable && !IsPhotonicZeroFlagAvailable
                        && !IsPhotonicDataMemoryAvailable && !IsPhotonicAccumulatorAvailable)
                    ? "IsaPlayground.PhotonicAdderToggle"
                    : IsPhotonicZeroFlagAvailable && !IsPhotonicAndAvailable && !IsPhotonicNotAvailable
                        ? "IsaPlayground.PhotonicZeroFlagToggle"
                        : IsPhotonicAndAvailable && IsPhotonicNotAvailable
                            ? "IsaPlayground.PhotonicAndNotToggle"
                            : IsPhotonicAndAvailable
                                ? "IsaPlayground.PhotonicAndToggle"
                                : IsPhotonicNotAvailable
                                    ? "IsaPlayground.PhotonicNotToggle"
                                    : IsPhotonicDataMemoryAvailable
                                        ? "IsaPlayground.PhotonicDataMemoryToggle"
                                        : "IsaPlayground.PhotonicAccumulatorToggle");

    /// <summary>
    /// The header title, naming the ALU the machine actually uses so the window
    /// never claims "golden model" (or "photonic adder") while a different engine
    /// computes on the photonic chip.
    /// </summary>
    public string HeaderTitle =>
        Translate(UsePhotonicAdder
            ? (_photonicAlu is not null && _photonicDataMemory is not null && _photonicAccumulator is not null
                ? "IsaPlayground.TitlePhotonicAdderDataMemoryAccumulator"
                : _photonicAlu is not null && _photonicDataMemory is not null
                    ? "IsaPlayground.TitlePhotonicAdderDataMemory"
                    : _photonicAlu is not null
                        ? "IsaPlayground.TitlePhotonic"
                        : _photonicAndAlu is not null && _photonicNotAlu is not null
                            ? "IsaPlayground.TitlePhotonicAndNot"
                            : _photonicAndAlu is not null
                                ? "IsaPlayground.TitlePhotonicAnd"
                                : _photonicNotAlu is not null
                                    ? "IsaPlayground.TitlePhotonicNot"
                                    : _photonicDataMemory is not null
                                        ? "IsaPlayground.TitlePhotonicDataMemory"
                                        : _photonicAccumulator is not null
                                            ? "IsaPlayground.TitlePhotonicAccumulator"
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
        // Default (unprefixed) map first; the ALU + RAM chip keeps its RAM under the
        // 'RAM.' prefix, so the prefixed map is the fallback (issue #1468).
        _dataMemoryMap = ResolveDataMemoryMap(network);
        IsPhotonicDataMemoryAvailable = _dataMemoryMap is not null;
        IsPhotonicAccumulatorAvailable = CAP_Core.Logic.Isa.PhotonicAccumulator.Accepts(network);
    }

    /// <summary>
    /// The signal map the network's data memory answers to: the shipped RAM 4x4
    /// names when they are exposed, otherwise the <c>RAM.</c>-prefixed names of the
    /// combined ALU + RAM chip, otherwise null (no photonic data memory).
    /// </summary>
    private static IsaDataMemorySignalMap? ResolveDataMemoryMap(LogicNetworkEvaluator? network)
    {
        if (PhotonicDataMemory.Accepts(network))
        {
            return IsaDataMemorySignalMap.Default;
        }

        var prefixed = IsaDataMemorySignalMap.WithPrefix(RamSignalPrefix);
        return PhotonicDataMemory.Accepts(network, prefixed) ? prefixed : null;
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
    /// alongside AND (issue #1295). The data memory and the accumulator ride the same
    /// network when it exposes their signals, so on the combined ALU + RAM + ACC chip
    /// every instruction runs end to end on light (issue #1479).
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
            _photonicDataMemory = _dataMemoryMap is not null && PhotonicDataMemory.Accepts(network, _dataMemoryMap)
                ? new PhotonicDataMemory(network, _dataMemoryMap)
                : null;
            _photonicAccumulator = CAP_Core.Logic.Isa.PhotonicAccumulator.Accepts(network)
                ? new CAP_Core.Logic.Isa.PhotonicAccumulator(network)
                : null;
            var golden = new GoldenIsaAlu();
            var emulator = new IsaEmulator(_assembledWords, new CompositeIsaAlu(
                _photonicAlu ?? (IIsaAlu)golden,
                _photonicNotAlu ?? (IIsaAlu)golden,
                _photonicAndAlu ?? (IIsaAlu)golden),
                _photonicZeroFlag,
                _photonicDataMemory,
                _photonicAccumulator);
            OnPropertyChanged(nameof(IsPhotonicDataMemoryActive));
            UpdateUnitChips();
            return emulator;
        }

        _photonicAlu = null;
        _photonicNotAlu = null;
        _photonicAndAlu = null;
        _photonicZeroFlag = null;
        _photonicDataMemory = null;
        _photonicAccumulator = null;
        OnPropertyChanged(nameof(IsPhotonicDataMemoryActive));
        UpdateUnitChips();
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
}
