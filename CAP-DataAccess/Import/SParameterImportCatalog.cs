using System.Globalization;
using CAP_Contracts.Logger;

namespace CAP_DataAccess.Import;

/// <summary>Ordered configured parsers and file probing for ambiguous S-parameter text files.</summary>
public sealed class SParameterImportCatalog : ISParameterImportCatalog
{
    private readonly IReadOnlyList<ISParameterImporter> _importers;
    private readonly IDiagnosticSink? _diagnostics;

    public SParameterImportCatalog(IEnumerable<ISParameterImporter> importers, IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(importers);
        _importers = importers.ToArray();
        _diagnostics = diagnostics;
    }

    public async Task<ISParameterImporter?> FindAsync(string path, SParameterImportDetection detection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".txt" && detection == SParameterImportDetection.VerifyAmbiguousText
            && !await LooksLikeLumericalTextAsync(path, cancellationToken).ConfigureAwait(false))
            return null;
        return _importers.FirstOrDefault(importer => importer.SupportedExtensions.Contains(extension));
    }

    private async Task<bool> LooksLikeLumericalTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(path);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('!')) continue;
                if (trimmed.StartsWith('(')) return true;
                var tokens = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return tokens.Length >= 9 && double.TryParse(tokens[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _);
            }
        }
        catch (IOException ex)
        {
            _diagnostics?.LogWarning($"Could not probe '{path}' for Lumerical .txt format: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _diagnostics?.LogWarning($"Could not probe '{path}' for Lumerical .txt format: {ex.Message}");
        }
        return false;
    }
}
