namespace CAP_DataAccess.Import;

/// <summary>
/// Parses S-parameter data from an external simulation file into <see cref="ImportedSParameters"/>.
/// </summary>
public interface ISParameterImporter
{
    /// <summary>
    /// File extensions this importer handles (lower-case, with dot, e.g. ".sparam").
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Parse the given file and return structured S-parameter data.
    /// </summary>
    /// <param name="filePath">Absolute path to the source file.</param>
    /// <returns>Parsed S-parameters, or throws <see cref="SParameterImportException"/> on format errors.</returns>
    Task<ImportedSParameters> ImportAsync(string filePath);
}

/// <summary>
/// Thrown when an S-parameter file cannot be parsed due to format errors.
/// </summary>
public class SParameterImportException : Exception
{
    /// <inheritdoc/>
    public SParameterImportException(string message) : base(message) { }

    /// <inheritdoc/>
    public SParameterImportException(string message, Exception inner) : base(message, inner) { }
}
