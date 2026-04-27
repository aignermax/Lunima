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
    /// <param name="formats">All formats to expose in the Export menu. Must be non-null and contain no null entries.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="formats"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="formats"/> contains a null entry.</exception>
    public ExportMenuViewModel(IEnumerable<IExportFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var list = new List<IExportFormat>(formats);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is null)
                throw new ArgumentException($"Export formats collection contains a null entry at index {i}.", nameof(formats));
        }
        Formats = list;
    }
}
