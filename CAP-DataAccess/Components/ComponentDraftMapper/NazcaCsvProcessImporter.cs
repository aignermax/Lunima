using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Imports a <see cref="ProcessDefinition"/> from the CSV tables a Nazca foundry
    /// PDK ships (table_layers.csv, table_xsections.csv, table_parameters.csv — the
    /// layout used by e.g. the HHI PDK). This is the Nazca/Bright-Photonics
    /// convention, one of several source formats. Reads from a licensed PDK directory
    /// at runtime; no proprietary foundry data is bundled with the app.
    /// </summary>
    public class NazcaCsvProcessImporter : IProcessImporter
    {
        private const string LayersTable = "table_layers.csv";

        /// <inheritdoc/>
        public string FormatName => "Nazca CSV tables";

        /// <inheritdoc/>
        public bool CanImport(string path)
        {
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return false;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            return dir != null && File.Exists(Path.Combine(dir, LayersTable));
        }

        /// <inheritdoc/>
        public ProcessDefinition Import(string path)
        {
            // The user picks any CSV in the PDK folder; we read the sibling tables.
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
            return ImportDirectory(dir);
        }

        /// <summary>Reads the CSV tables in <paramref name="pdkDirectory"/> into a process.</summary>
        public ProcessDefinition ImportDirectory(string pdkDirectory, string? processName = null)
        {
            var process = new ProcessDefinition
            {
                Name = processName ?? new DirectoryInfo(pdkDirectory).Name,
            };

            ImportLayers(Path.Combine(pdkDirectory, LayersTable), process);
            ImportXsections(Path.Combine(pdkDirectory, "table_xsections.csv"), process);
            EnrichFromParameters(Path.Combine(pdkDirectory, "table_parameters.csv"), process);
            return process;
        }

        private static void ImportLayers(string path, ProcessDefinition process)
        {
            if (!File.Exists(path)) return;
            foreach (var row in ReadRows(path, out var col))
            {
                var name = Get(row, col, "layer_name");
                if (name.Length == 0 || !int.TryParse(Get(row, col, "layer"), out var layer)) continue;
                int.TryParse(Get(row, col, "datatype"), out var datatype);
                process.Layers.Add(new ProcessLayer
                {
                    Name = name,
                    Layer = layer,
                    Datatype = datatype,
                    Field = NullIfEmpty(Get(row, col, "field")),
                    Description = NullIfEmpty(Get(row, col, "description")),
                });
            }
        }

        private static void ImportXsections(string path, ProcessDefinition process)
        {
            if (!File.Exists(path)) return;
            foreach (var row in ReadRows(path, out var col))
            {
                var name = Get(row, col, "xsection");
                if (name.Length == 0) continue;
                if (name.Contains("stub", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("CellBoundary", StringComparison.OrdinalIgnoreCase)) continue;

                process.Xsections.Add(new ProcessXsection
                {
                    Name = name,
                    Kind = name.StartsWith("Metal", StringComparison.OrdinalIgnoreCase)
                        ? XsectionKind.Metal : XsectionKind.Optical,
                    Description = NullIfEmpty(Get(row, col, "description")),
                });
            }
        }

        private static void EnrichFromParameters(string path, ProcessDefinition process)
        {
            if (!File.Exists(path)) return;
            foreach (var row in ReadRows(path, out var col))
            {
                var name = Get(row, col, "name");
                if (!TryDouble(Get(row, col, "value"), out var value)) continue;
                var xsection = Get(row, col, "xsection");
                TryDouble(Get(row, col, "recommended"), out var recommended);

                if (name.StartsWith("arc_", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var xs in MatchXsections(process, xsection))
                    {
                        xs.MinRadiusUm = value;
                        xs.RecommendedRadiusUm = recommended > 0 ? recommended : value;
                    }
                }
                else if (name.Equals("width", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var xs in process.Xsections.Where(x => x.Kind == XsectionKind.Optical && x.WidthUm == 0))
                        xs.WidthUm = value;
                }
                else if (name.StartsWith("width_metal", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var xs in process.Xsections.Where(x => x.Kind == XsectionKind.Metal && x.WidthUm == 0))
                        xs.WidthUm = value;
                }
            }
        }

        private static IEnumerable<ProcessXsection> MatchXsections(ProcessDefinition process, string xsection)
        {
            if (xsection.Length == 0) return Enumerable.Empty<ProcessXsection>();
            var exact = process.Xsections.Where(x => x.Name.Equals(xsection, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            if (xsection.Equals("Metal", StringComparison.OrdinalIgnoreCase))
                return process.Xsections.Where(x => x.Kind == XsectionKind.Metal);
            return Enumerable.Empty<ProcessXsection>();
        }

        // ── tiny quote-aware CSV reader ──────────────────────────────────────────
        private static IEnumerable<string[]> ReadRows(string path, out Dictionary<string, int> columns)
        {
            var lines = File.ReadAllLines(path);
            columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (lines.Length == 0) return Enumerable.Empty<string[]>();

            var header = SplitCsvLine(lines[0]);
            for (int i = 0; i < header.Length; i++)
                columns[header[i].Trim()] = i;

            var rows = new List<string[]>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                rows.Add(SplitCsvLine(lines[i]));
            }
            return rows;
        }

        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static string Get(string[] row, Dictionary<string, int> col, string name) =>
            col.TryGetValue(name, out var i) && i < row.Length ? row[i].Trim() : string.Empty;

        private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

        private static bool TryDouble(string s, out double value) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
