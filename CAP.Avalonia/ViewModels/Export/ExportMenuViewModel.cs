namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for the unified Export menu flyout.
/// Holds the ordered list of registered <see cref="IExportFormat"/> implementations.
/// Add a new format by creating a class that implements <see cref="IExportFormat"/>
/// and passing it here — no changes to MainWindow.axaml required.
/// </summary>
public class ExportMenuViewModel
{
    /// <summary>
    /// All registered export formats displayed in the menu, in registration order.
    /// </summary>
    public IReadOnlyList<IExportFormat> Formats { get; }

    /// <summary>
    /// Initializes with the ordered list of export formats.
    /// </summary>
    /// <param name="formats">All formats to expose in the Export menu.</param>
    public ExportMenuViewModel(IEnumerable<IExportFormat> formats)
    {
        Formats = new List<IExportFormat>(formats);
    }
}
