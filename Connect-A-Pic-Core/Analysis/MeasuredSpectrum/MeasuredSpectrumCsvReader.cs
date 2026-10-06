using System.Globalization;

namespace CAP_Core.Analysis.MeasuredSpectrum
{
    /// <summary>Unit of the power column in a measured-spectrum CSV.</summary>
    public enum PowerUnit
    {
        /// <summary>Linear power / transmission.</summary>
        Linear,

        /// <summary>Power in decibel (dB); converted with 10^(dB/10).</summary>
        Decibel,
    }

    /// <summary>
    /// Reads a two-column CSV (wavelength, power) as exported by typical lab
    /// setups (e.g. the SiEPIC/Phot1x data files). Header line is optional;
    /// ',', ';' and tab separators are auto-detected. Wavelength may be given
    /// in nm or in m — auto-detected by magnitude (values &lt; 0.1 are treated
    /// as meters, as silicon-photonics wavelengths are ~1550 nm / 1.55e-6 m).
    /// All parsing uses <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public static class MeasuredSpectrumCsvReader
    {
        /// <summary>Wavelengths below this value are interpreted as meters (SI export).</summary>
        public const double MeterDetectionThreshold = 0.1;

        private static readonly char[] Separators = { ',', ';', '\t' };

        /// <summary>Reads and parses a CSV file into a <see cref="MeasuredSpectrum"/>.</summary>
        public static MeasuredSpectrum ReadFile(string path, PowerUnit powerUnit)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must not be empty.", nameof(path));
            string text = File.ReadAllText(path);
            return Parse(text, powerUnit, Path.GetFileName(path));
        }

        /// <summary>Parses CSV text into a <see cref="MeasuredSpectrum"/>.</summary>
        /// <exception cref="FormatException">Thrown with the offending line number on malformed rows.</exception>
        public static MeasuredSpectrum Parse(string csvText, PowerUnit powerUnit, string label = "")
        {
            if (string.IsNullOrWhiteSpace(csvText)) throw new ArgumentException("CSV text must not be empty.", nameof(csvText));

            var wavelengths = new List<double>();
            var powers = new List<double>();
            var lines = csvText.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                    throw Malformed(i, line, "expected two columns (wavelength, power)");

                if (!TryParseDouble(parts[0], out double wavelength) || !TryParseDouble(parts[1], out double power))
                {
                    // A non-numeric first data row is treated as a header; later rows are errors.
                    if (wavelengths.Count == 0 && !IsNumericStart(parts[0]))
                        continue;
                    throw Malformed(i, line, "columns must be numeric");
                }

                wavelengths.Add(wavelength);
                powers.Add(power);
            }

            if (wavelengths.Count == 0)
                throw new FormatException("CSV contains no data rows.");

            double scale = wavelengths.Max() < MeterDetectionThreshold ? 1e9 : 1.0;
            var wl = wavelengths.Select(w => w * scale).ToArray();
            var pw = powers.Select(p => powerUnit == PowerUnit.Decibel ? DecibelToLinear(p) : p).ToArray();

            return new MeasuredSpectrum(wl, pw, label);
        }

        /// <summary>Converts dB to linear power: 10^(dB/10).</summary>
        public static double DecibelToLinear(double decibel) => Math.Pow(10.0, decibel / 10.0);

        private static bool TryParseDouble(string token, out double value) =>
            double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static bool IsNumericStart(string token) =>
            token.Length > 0 && (char.IsDigit(token[0]) || token[0] is '-' or '+' or '.');

        private static FormatException Malformed(int zeroBasedLine, string line, string reason) =>
            new($"Malformed CSV at line {zeroBasedLine + 1}: {reason}. Offending row: '{line}'.");
    }
}
