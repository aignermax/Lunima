using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// ViewModel for general application preferences that do not belong to a
/// dedicated category. Currently a placeholder — the custom Python path lives
/// on the dedicated Python Environment page to avoid a dual-write / desync
/// hazard with <see cref="Export.GdsExportViewModel"/>. Additional general
/// preferences can be surfaced here as they come up.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
}
