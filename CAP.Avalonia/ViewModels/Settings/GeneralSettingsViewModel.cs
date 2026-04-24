using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// ViewModel for general application preferences that do not belong to a
/// dedicated category. Currently a placeholder — the custom Python path
/// lives on the GDS Export settings page (which owns
/// <see cref="Export.GdsExportViewModel"/>) to avoid the dual-write /
/// desync hazard of editing the same backing field from two places.
/// Additional general preferences can be surfaced here as they come up.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
}
