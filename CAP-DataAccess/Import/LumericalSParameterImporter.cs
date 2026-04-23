using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CAP_DataAccess.Import;

/// <summary>
/// Parses Lumerical INTERCONNECT S-parameter files in SiEPIC/EBeam PDK format.
///
/// Supported file formats:
/// <list type="bullet">
///   <item><description>.sparam – Blocked format: header line, shape line, frequency/magnitude/phase data</description></item>
///   <item><description>.dat – Same blocked format with .dat extension</description></item>
///   <item><description>.txt – GC packed format: one row per frequency with all Sij columns</description></item>
/// </list>
///
/// The blocked format header: ('port X','MODE',mode_id,'port Y',mode_id,'transmission')
/// The data columns are: frequency_Hz  magnitude  phase_radians
/// </summary>
public class LumericalSParameterImporter : ISParameterImporter
{
    private const double SpeedOfLightMs = 299_792_458.0;

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".sparam", ".dat", ".txt" };

    /// <inheritdoc/>
    public async Task<ImportedSParameters> ImportAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new SParameterImportException($"File not found: {filePath}");

        var lines = await File.ReadAllLinesAsync(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var blocks = ext == ".txt"
            ? ParseGcTxtFormat(lines)
            : ParseSparamFormat(lines);

        return BuildResult(blocks, filePath, ext);
    }

    // ── Blocked (.sparam / .dat) parser ──────────────────────────────────────

    private static List<SparamBlock> ParseSparamFormat(string[] lines)
    {
        var blocks = new List<SparamBlock>();
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('(') && line.Contains("transmission", StringComparison.OrdinalIgnoreCase))
            {
                var (outPort, inPort, mode, ok) = ParseBlockHeader(line);
                if (!ok) { i++; continue; }

                i++;
                if (i >= lines.Length) break;

                var shapeMatch = Regex.Match(lines[i].Trim(), @"\((\d+)\s*,\s*3\)");
                int numPoints = shapeMatch.Success ? int.Parse(shapeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;

                var data = new List<(double FreqHz, double Mag, double PhaseRad)>(numPoints);
                for (int j = 0; j < numPoints && i + 1 + j < lines.Length; j++)
                {
                    var parts = lines[i + 1 + j].Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 &&
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mag) &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var phase))
                    {
                        data.Add((freq, mag, phase));
                    }
                }

                blocks.Add(new SparamBlock(outPort, inPort, mode, data));
                i += 1 + numPoints;
            }
            else
            {
                i++;
            }
        }

        return blocks;
    }

    private static (string outPort, string inPort, string mode, bool ok) ParseBlockHeader(string line)
    {
        // Single-quote variant: ('port 1','TE',0,'port 2',0,'transmission')
        var m = Regex.Match(line, @"\('([^']+)','?(\w+)'?,\d+,'([^']+)',\d+,'transmission'\)");
        if (!m.Success)
            m = Regex.Match(line, @"\(""([^""]+)"",""?([^"",]+)""?,\d+,""([^""]+)"",\d+,""transmission""\)");

        if (!m.Success)
            return (string.Empty, string.Empty, string.Empty, false);

        return (m.Groups[1].Value, m.Groups[3].Value, m.Groups[2].Value, true);
    }

    // ── GC packed .txt parser ────────────────────────────────────────────────
    // Row format: freq |S11| ang(S11) |S21| ang(S21) |S12| ang(S12) |S22| ang(S22)

    private static List<SparamBlock> ParseGcTxtFormat(string[] lines)
    {
        var s11 = new List<(double, double, double)>();
        var s21 = new List<(double, double, double)>();
        var s12 = new List<(double, double, double)>();
        var s22 = new List<(double, double, double)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq)) continue;

            static bool TryPair(string[] p, int idx, out double mag, out double phase)
            {
                mag = phase = 0;
                return double.TryParse(p[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out mag)
                    && double.TryParse(p[idx + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out phase);
            }

            if (TryPair(parts, 1, out var m11, out var p11)) s11.Add((freq, m11, p11 * Math.PI / 180));
            if (TryPair(parts, 3, out var m21, out var p21)) s21.Add((freq, m21, p21 * Math.PI / 180));
            if (TryPair(parts, 5, out var m12, out var p12)) s12.Add((freq, m12, p12 * Math.PI / 180));
            if (TryPair(parts, 7, out var m22, out var p22)) s22.Add((freq, m22, p22 * Math.PI / 180));
        }

        return new List<SparamBlock>
        {
            new("port 1", "port 1", "TE", s11),
            new("port 2", "port 1", "TE", s21),
            new("port 1", "port 2", "TE", s12),
            new("port 2", "port 2", "TE", s22)
        };
    }

    // ── Result assembly ──────────────────────────────────────────────────────

    private static ImportedSParameters BuildResult(List<SparamBlock> blocks, string filePath, string ext)
    {
        if (blocks.Count == 0)
            throw new SParameterImportException("No S-parameter blocks found in file.");

        // Filter to TE mode (or take all if no TE found)
        var teBlocks = blocks.Where(b => b.Mode.Equals("TE", StringComparison.OrdinalIgnoreCase)).ToList();
        var useBlocks = teBlocks.Count > 0 ? teBlocks : blocks;

        // Collect all unique port names
        var portNames = useBlocks
            .SelectMany(b => new[] { b.InPort, b.OutPort })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        int n = portNames.Count;

        // Get all wavelengths from first block
        var refBlock = useBlocks[0];
        var wavelengthsNm = refBlock.Data
            .Select(d => FreqToWavelengthNm(d.FreqHz))
            .ToArray();

        // Build S-matrix for each wavelength index
        var result = new ImportedSParameters
        {
            SourceFormat = ext == ".txt" ? "Lumerical GC TXT" : "Lumerical SiEPIC",
            SourceFilePath = filePath,
            PortCount = n,
            PortNames = portNames,
        };

        for (int wIdx = 0; wIdx < wavelengthsNm.Length; wIdx++)
        {
            var matrix = new Complex[n, n];
            foreach (var block in useBlocks)
            {
                int outIdx = portNames.IndexOf(portNames.FirstOrDefault(p => string.Equals(p, block.OutPort, StringComparison.OrdinalIgnoreCase)) ?? "");
                int inIdx = portNames.IndexOf(portNames.FirstOrDefault(p => string.Equals(p, block.InPort, StringComparison.OrdinalIgnoreCase)) ?? "");
                if (outIdx < 0 || inIdx < 0 || wIdx >= block.Data.Count) continue;

                var (_, mag, phase) = block.Data[wIdx];
                matrix[outIdx, inIdx] = Complex.FromPolarCoordinates(mag, phase);
            }

            result.SMatricesByWavelengthNm[wavelengthsNm[wIdx]] = matrix;
        }

        result.Metadata["polarization"] = "TE";
        result.Metadata["tool"] = "Lumerical INTERCONNECT";

        return result;
    }

    private static int FreqToWavelengthNm(double freqHz) =>
        (int)Math.Round(SpeedOfLightMs / freqHz * 1e9);

    private record SparamBlock(
        string OutPort,
        string InPort,
        string Mode,
        List<(double FreqHz, double Mag, double PhaseRad)> Data);
}
