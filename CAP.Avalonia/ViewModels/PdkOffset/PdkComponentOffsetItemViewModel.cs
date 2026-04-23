using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.PdkOffset;

/// <summary>
/// Offset verification status for a PDK component's NazcaOriginOffset.
/// </summary>
public enum OffsetStatus
{
    /// <summary>nazcaOriginOffsetX or nazcaOriginOffsetY is null in the JSON.</summary>
    Missing,

    /// <summary>Both offsets are present but both are zero — may need calibration.</summary>
    ZeroOffset,

    /// <summary>At least one offset is explicitly non-zero — likely calibrated.</summary>
    Set
}

/// <summary>
/// List-item ViewModel for one PDK component in the PDK Offset Editor.
/// Exposes name, status badge, and the current offset values for display.
/// </summary>
public partial class PdkComponentOffsetItemViewModel : ObservableObject
{
    /// <summary>Display name of the component.</summary>
    public string ComponentName { get; }

    /// <summary>Name of the PDK this component belongs to.</summary>
    public string PdkName { get; }

    /// <summary>Reference to the mutable draft — used for save-back.</summary>
    public PdkComponentDraft Draft { get; }

    /// <summary>Offset X value in micrometers, null when missing from JSON.</summary>
    public double? OffsetX => Draft.NazcaOriginOffsetX;

    /// <summary>Offset Y value in micrometers, null when missing from JSON.</summary>
    public double? OffsetY => Draft.NazcaOriginOffsetY;

    /// <summary>Computed status based on whether offsets are present and non-zero.</summary>
    public OffsetStatus Status { get; private set; }

    /// <summary>Unicode badge for the current <see cref="Status"/>.</summary>
    public string StatusBadge => Status switch
    {
        OffsetStatus.Missing     => "❌",
        OffsetStatus.ZeroOffset  => "⚠️",
        OffsetStatus.Set         => "✅",
        _                        => "?"
    };

    /// <summary>One-line description shown in the component list tooltip.</summary>
    public string StatusDescription => Status switch
    {
        OffsetStatus.Missing    => "NazcaOriginOffset missing — GDS export will fail",
        OffsetStatus.ZeroOffset => "Offset present but both X and Y are zero — may need calibration",
        OffsetStatus.Set        => $"Offset set: X={OffsetX:F3} µm, Y={OffsetY:F3} µm",
        _                       => ""
    };

    /// <summary>
    /// Initializes a new instance of <see cref="PdkComponentOffsetItemViewModel"/>.
    /// </summary>
    public PdkComponentOffsetItemViewModel(PdkComponentDraft draft, string pdkName)
    {
        Draft = draft;
        ComponentName = draft.Name;
        PdkName = pdkName;
        RefreshStatus();
    }

    /// <summary>
    /// Epsilon in µm below which an offset is treated as "exactly zero" for
    /// the purposes of the ZeroOffset status. PDK offsets are calibrated in
    /// whole or fractional µm, so 1e-9 µm (≈ sub-atomic scale) is
    /// unambiguously floating-point noise from a GUI round-trip.
    /// </summary>
    private const double ZeroOffsetEpsilonMicrometers = 1e-9;

    /// <summary>
    /// Re-evaluates the offset status from the current draft values.
    /// Call after editing the offset so the badge updates.
    /// </summary>
    public void RefreshStatus()
    {
        if (Draft.NazcaOriginOffsetX == null || Draft.NazcaOriginOffsetY == null)
        {
            Status = OffsetStatus.Missing;
        }
        else if (Math.Abs(Draft.NazcaOriginOffsetX.Value) < ZeroOffsetEpsilonMicrometers
                 && Math.Abs(Draft.NazcaOriginOffsetY.Value) < ZeroOffsetEpsilonMicrometers)
        {
            Status = OffsetStatus.ZeroOffset;
        }
        else
        {
            Status = OffsetStatus.Set;
        }

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(OffsetX));
        OnPropertyChanged(nameof(OffsetY));
    }
}
