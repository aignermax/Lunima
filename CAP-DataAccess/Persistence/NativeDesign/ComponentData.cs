using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

public class ComponentData
{
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// PDK source name (e.g. "Built-in", "Demo PDK").
    /// Used to disambiguate templates with the same name from different PDKs.
    /// Null in old files — falls back to name-only lookup.
    /// </summary>
    public string? PdkSource { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public string Identifier { get; set; } = "";
    public int Rotation { get; set; }

    /// <summary>
    /// Exact continuous rotation in degrees for non-cardinal placements (GDS
    /// import). Null in old files and for cardinal rotations — <see cref="Rotation"/>
    /// alone restores those.
    /// </summary>
    public double? RotationDegrees { get; set; }

    /// <summary>
    /// True when the pins were mirrored across the local horizontal centerline
    /// (GDS STRANS-reflected instance). Null in old files — no mirror.
    /// </summary>
    public bool? Mirrored { get; set; }

    /// <summary>
    /// True for imported background geometry (die frames, logos): no routing obstacle,
    /// no placement collisions, lowest click priority. Null — a regular component.
    /// </summary>
    public bool? IsBackground { get; set; }

    public double? SliderValue { get; set; }

    /// <summary>
    /// All slider values keyed by slider number. Supersedes
    /// <see cref="SliderValue"/> for multi-parameter components; null in old files.
    /// </summary>
    public Dictionary<int, double>? SliderValues { get; set; }

    public int? LaserWavelengthNm { get; set; }
    public double? LaserPower { get; set; }

    /// <summary>Per-coupler laser on/off (#690). Null in old files — treated as on.</summary>
    public bool? LaserEnabled { get; set; }

    /// <summary>Spectral line shape name (#819). Null in old files — ideal source.</summary>
    public string? LaserLineShape { get; set; }

    /// <summary>Linewidth (FWHM) in nm (#819). Null in old files — editor default.</summary>
    public double? LaserLinewidthFwhmNm { get; set; }

    /// <summary>Relative intensity noise in dB/Hz (#819). Null in old files — default RIN.</summary>
    public double? LaserRinDbPerHz { get; set; }

    public bool? IsLocked { get; set; }

    /// <summary>
    /// True only for crossing components the crossing-insertion pass placed
    /// automatically (#705); null otherwise and in old files. Used after load
    /// to rebuild the crossings' dissolution records.
    /// </summary>
    public bool? IsInsertedCrossing { get; set; }

    public string? HumanReadableName { get; set; }
}
