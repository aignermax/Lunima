namespace CAP.Avalonia.ViewModels.Export.OpenEbl;

/// <summary>
/// One row in the openEBL result error list: a localized category label, the raw
/// (English) script message, and an optional error location in µm.
/// </summary>
public sealed class OpenEblCheckErrorItemViewModel
{
    /// <summary>Initializes a new instance of <see cref="OpenEblCheckErrorItemViewModel"/>.</summary>
    public OpenEblCheckErrorItemViewModel(string categoryLabel, string message, string location)
    {
        CategoryLabel = categoryLabel;
        Message = message;
        Location = location;
    }

    /// <summary>Localized label of the error category.</summary>
    public string CategoryLabel { get; }

    /// <summary>Human-readable error description (verbatim script output).</summary>
    public string Message { get; }

    /// <summary>Formatted error location (e.g. "(12.5 µm, 3 µm)"), empty when the script reported none.</summary>
    public string Location { get; }

    /// <summary>True when a location is shown.</summary>
    public bool HasLocation => Location.Length > 0;
}
