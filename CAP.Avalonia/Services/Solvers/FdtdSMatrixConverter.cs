using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services.Solvers;

/// <summary>
/// Converts an <see cref="FdtdSMatrixResult"/> (keyed "out@mode,in@mode" with
/// complex values per wavelength) into the <see cref="ComponentSMatrixData"/>
/// shape the Component Settings dialog stores and applies — the same target type
/// the file importers produce, so the FDTD result flows through the existing
/// store-and-apply path.
/// </summary>
public static class FdtdSMatrixConverter
{
    /// <summary>
    /// Builds per-wavelength row-major S-matrices from the solver result.
    /// Only the fundamental mode (index 0) is used.
    /// </summary>
    /// <param name="result">A successful FDTD result.</param>
    /// <param name="sourceNote">Provenance note stored on the data (e.g. "FDTD Meep 2D").</param>
    public static ComponentSMatrixData ToComponentSMatrixData(FdtdSMatrixResult result, string sourceNote)
    {
        var ports = result.Ports.ToList();
        var index = new Dictionary<string, int>();
        for (int i = 0; i < ports.Count; i++)
            index[ports[i]] = i;

        var n = ports.Count;
        var data = new ComponentSMatrixData { SourceNote = sourceNote };

        for (int w = 0; w < result.Wavelengths.Count; w++)
        {
            var entry = new SMatrixWavelengthEntry
            {
                Rows = n,
                Cols = n,
                PortNames = ports,
                Real = new List<double>(new double[n * n]),
                Imag = new List<double>(new double[n * n]),
            };

            foreach (var s in result.Entries)
            {
                if (!TryParseKey(s.Key, out var outPort, out var inPort)) continue;
                if (!index.TryGetValue(outPort, out var r) || !index.TryGetValue(inPort, out var c)) continue;
                if (w >= s.Values.Count) continue;

                var flat = r * n + c;
                entry.Real[flat] = s.Values[w].Real;
                entry.Imag[flat] = s.Values[w].Imaginary;
            }

            var nm = ((int)Math.Round(result.Wavelengths[w] * 1000.0)).ToString();
            data.Wavelengths[nm] = entry;
        }

        return data;
    }

    /// <summary>Parses an "out@mode,in@mode" key into its output and input port names.</summary>
    private static bool TryParseKey(string key, out string outPort, out string inPort)
    {
        outPort = inPort = string.Empty;
        var comma = key.Split(',');
        if (comma.Length != 2) return false;
        outPort = comma[0].Split('@')[0];
        inPort = comma[1].Split('@')[0];
        return outPort.Length > 0 && inPort.Length > 0;
    }
}
