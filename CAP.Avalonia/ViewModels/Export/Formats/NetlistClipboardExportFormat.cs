using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.Formats;

/// <summary>
/// Export-menu entry next to <see cref="NetlistExportFormat"/> that puts the gdsfactory YAML
/// netlist on the clipboard instead of into a file — often all that is needed to paste it
/// into a notebook, a script or a chat.
/// </summary>
public class NetlistClipboardExportFormat : IExportFormat
{
    private readonly IAsyncRelayCommand _exportCommand;

    /// <inheritdoc/>
    public string Name => "Netlist (YAML) → Clipboard";

    /// <inheritdoc/>
    public string Icon => "📋";

    /// <inheritdoc/>
    public string Description => "Copy the circuit topology as a gdsfactory YAML netlist to the clipboard";

    /// <inheritdoc/>
    public string Background => "#3d4d4d";

    /// <inheritdoc/>
    public IAsyncRelayCommand ExportCommand => _exportCommand;

    /// <summary>Initializes with the command that copies the netlist and reports the result.</summary>
    /// <param name="exportCommand">The async command that performs the copy.</param>
    public NetlistClipboardExportFormat(IAsyncRelayCommand exportCommand)
    {
        _exportCommand = exportCommand;
    }
}
