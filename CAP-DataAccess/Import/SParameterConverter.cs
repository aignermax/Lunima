using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Import;

/// <summary>
/// Converts <see cref="ImportedSParameters"/> to <see cref="ComponentSMatrixData"/>
/// for storage in the .lun PIR section.
/// </summary>
public static class SParameterConverter
{
    /// <summary>
    /// Converts parsed S-parameter data to the PIR storage format.
    /// </summary>
    /// <param name="imported">Parsed S-parameters from an importer.</param>
    /// <param name="sourceNote">Optional note to embed (defaults to format + filename).</param>
    /// <returns>A <see cref="ComponentSMatrixData"/> ready for <c>DesignFileData.SMatrices</c>.</returns>
    public static ComponentSMatrixData ToComponentSMatrixData(
        ImportedSParameters imported,
        string? sourceNote = null)
    {
        var data = new ComponentSMatrixData
        {
            SourceNote = sourceNote
                ?? $"{imported.SourceFormat} — {Path.GetFileName(imported.SourceFilePath)}"
        };

        foreach (var (wavelengthNm, matrix) in imported.SMatricesByWavelengthNm)
        {
            int n = imported.PortCount;
            var entry = new SMatrixWavelengthEntry
            {
                Rows = n,
                Cols = n,
                PortNames = new List<string>(imported.PortNames),
            };

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    var v = matrix[r, c];
                    entry.Real.Add(v.Real);
                    entry.Imag.Add(v.Imaginary);
                }
            }

            data.Wavelengths[wavelengthNm.ToString()] = entry;
        }

        return data;
    }
}
