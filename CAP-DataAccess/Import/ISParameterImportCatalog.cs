namespace CAP_DataAccess.Import;

/// <summary>How ambiguous text files are identified before selecting a configured parser.</summary>
public enum SParameterImportDetection
{
    ExtensionOnly,
    VerifyAmbiguousText
}

/// <summary>Selects an S-parameter parser from the application's configured format readers.</summary>
public interface ISParameterImportCatalog
{
    Task<ISParameterImporter?> FindAsync(string path, SParameterImportDetection detection,
        CancellationToken cancellationToken = default);
}
