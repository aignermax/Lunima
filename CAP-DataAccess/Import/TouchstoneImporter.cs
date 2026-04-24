using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CAP_DataAccess.Import;

/// <summary>
/// Parses Touchstone S-parameter files (.sNp where N is the port count).
///
/// Supports:
/// <list type="bullet">
///   <item><description>Frequency units: Hz, KHz, MHz, GHz (case-insensitive)</description></item>
///   <item><description>Data formats: MA (magnitude-angle°), DB (dB-angle°), RI (real-imaginary)</description></item>
///   <item><description>Parameter type: S (S-parameters only; Y and Z are rejected)</description></item>
/// </list>
///
/// Reference impedance (R line) is parsed but not used in conversion.
/// </summary>
public class TouchstoneImporter : ISParameterImporter
{
    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions { get; } = BuildExtensions();

    /// <inheritdoc/>
    public async Task<ImportedSParameters> ImportAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new SParameterImportException($"File not found: {filePath}");

        int portCount = DetectPortCount(filePath);
        var lines = await File.ReadAllLinesAsync(filePath);
        return Parse(lines, portCount, filePath);
    }

    // ── Port count from extension (.s2p → 2, .s4p → 4) ──────────────────────

    private static int DetectPortCount(string filePath)
    {
        var m = Regex.Match(Path.GetExtension(filePath), @"\.s(\d+)p", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 2;
    }

    // ── Main parser ──────────────────────────────────────────────────────────

    private static ImportedSParameters Parse(string[] lines, int portCount, string filePath)
    {
        double freqMultiplier = 1.0;
        string dataFormat = "MA";

        var dataRows = new List<double[]>();
        var currentRow = new List<double>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Strip inline comment
            var bangIdx = line.IndexOf('!');
            if (bangIdx >= 0)
                line = line[..bangIdx].Trim();

            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith('#'))
            {
                // Option line: # <FreqUnit> <ParamType> <DataFormat> R <Impedance>
                var parts = line[1..].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) freqMultiplier = FreqUnitMultiplier(parts[0]);
                if (parts.Length >= 2)
                {
                    // Only S parameters are supported. Silently accepting Y/Z
                    // would produce physically wrong simulation (admittance or
                    // impedance treated as scattering) — reject explicitly.
                    var paramType = parts[1].ToUpperInvariant();
                    if (paramType != "S")
                        throw new SParameterImportException(
                            $"Unsupported parameter type '{parts[1]}'. Only S-parameters are supported; Y and Z must be converted before import.");
                }
                if (parts.Length >= 3) dataFormat = ValidateDataFormat(parts[2]);
                continue;
            }

            // Data line — may span multiple lines for large matrices
            var nums = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var num in nums)
            {
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    currentRow.Add(v);
            }

            // A complete row has 1 (freq) + 2*N*N values
            int expectedPerRow = 1 + 2 * portCount * portCount;
            if (currentRow.Count >= expectedPerRow)
            {
                var completedRow = currentRow.Take(expectedPerRow).ToArray();

                // Touchstone v1 allows noise-parameter blocks after the last
                // S-parameter row. They re-use the frequency column but use a
                // different (smaller) column layout. We detect the start of
                // noise data as "frequency went backwards" — real sweeps are
                // monotonically increasing — and stop parsing there rather
                // than corrupting the S-matrix with noise numbers silently.
                if (dataRows.Count > 0 && completedRow[0] <= dataRows[^1][0])
                    break;

                dataRows.Add(completedRow);
                currentRow = new List<double>(currentRow.Skip(expectedPerRow));
            }
        }

        if (dataRows.Count == 0)
            throw new SParameterImportException("No data rows found in Touchstone file.");

        return BuildResult(dataRows, portCount, freqMultiplier, dataFormat, filePath);
    }

    private static ImportedSParameters BuildResult(
        List<double[]> dataRows, int n, double freqMult, string format, string filePath)
    {
        var portNames = Enumerable.Range(1, n).Select(i => $"port {i}").ToList();

        var result = new ImportedSParameters
        {
            SourceFormat = $"Touchstone S{n}P",
            SourceFilePath = filePath,
            PortCount = n,
            PortNames = portNames,
        };

        result.Metadata["tool"] = "Touchstone";
        result.Metadata["dataFormat"] = format;

        foreach (var row in dataRows)
        {
            double freqHz = row[0] * freqMult;
            if (!double.IsFinite(freqHz) || freqHz <= 0)
                throw new SParameterImportException(
                    $"Invalid frequency {row[0]} {format} — must be finite and positive.");

            int wavelengthNm = (int)Math.Round(299_792_458.0 / freqHz * 1e9);

            var matrix = new Complex[n, n];

            // Touchstone v1 ordering: S11, S21, S31... S12, S22, S32... (column-major)
            for (int col = 0; col < n; col++)
            {
                for (int row2 = 0; row2 < n; row2++)
                {
                    int pairIdx = col * n + row2;
                    int baseIdx = 1 + pairIdx * 2;
                    double a = row[baseIdx];
                    double b = row[baseIdx + 1];

                    matrix[row2, col] = ToComplex(a, b, format);
                }
            }

            result.SMatricesByWavelengthNm[wavelengthNm] = matrix;
        }

        return result;
    }

    private static Complex ToComplex(double a, double b, string format)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b))
            throw new SParameterImportException(
                $"Non-finite S-parameter component ({a}, {b}) in {format} format — file contains NaN or Infinity.");

        return format switch
        {
            "MA" => Complex.FromPolarCoordinates(a, b * Math.PI / 180.0),
            "DB" => Complex.FromPolarCoordinates(Math.Pow(10, a / 20.0), b * Math.PI / 180.0),
            "RI" => new Complex(a, b),
            _ => throw new SParameterImportException(
                $"Unknown data format '{format}'. Expected MA, DB, or RI.")
        };
    }

    /// <summary>
    /// Validates the data-format token on the Touchstone option line.
    /// Unknown tokens throw — silently defaulting to RI would turn a typo'd
    /// "# HZ S MAG R 50" file into garbage values without the user noticing.
    /// </summary>
    private static string ValidateDataFormat(string raw)
    {
        var format = raw.ToUpperInvariant();
        if (format is not ("MA" or "DB" or "RI"))
            throw new SParameterImportException(
                $"Unknown data format '{raw}' on Touchstone option line. Expected MA, DB, or RI.");
        return format;
    }

    private static double FreqUnitMultiplier(string unit) => unit.ToUpperInvariant() switch
    {
        "HZ"  => 1.0,
        "KHZ" => 1e3,
        "MHZ" => 1e6,
        "GHZ" => 1e9,
        _ => throw new SParameterImportException(
            $"Unknown frequency unit '{unit}' on Touchstone option line. Expected Hz, kHz, MHz, or GHz.")
    };

    private static IReadOnlyList<string> BuildExtensions()
    {
        // .s1p through .s9p are the common ones; .s1p and .s2p most frequent
        return Enumerable.Range(1, 9).Select(i => $".s{i}p").ToList();
    }
}
