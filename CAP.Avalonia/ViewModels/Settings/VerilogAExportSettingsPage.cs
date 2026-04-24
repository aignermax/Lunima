using CAP.Avalonia.ViewModels.Export;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page for Verilog-A / SPICE export configuration — circuit name,
/// output directory, simulation wavelength, and whether to emit a SPICE test
/// bench. The export trigger itself lives in the top toolbar; this page
/// owns only the configuration knobs so the right panel can stay focused on
/// analysis and diagnostics.
/// </summary>
public class VerilogAExportSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Verilog-A Export";

    /// <inheritdoc/>
    public string Icon => "⚡";

    /// <inheritdoc/>
    public string? Category => "Export";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="VerilogAExportSettingsPage"/>.
    /// </summary>
    public VerilogAExportSettingsPage(VerilogAExportViewModel verilogAExportViewModel)
    {
        ViewModel = verilogAExportViewModel;
    }
}
