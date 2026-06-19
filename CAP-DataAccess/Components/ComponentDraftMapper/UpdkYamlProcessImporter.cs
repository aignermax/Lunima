using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using YamlDotNet.Serialization;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Imports a <see cref="ProcessDefinition"/> from an openEPDA uPDK YAML blueprint
    /// — the cross-vendor PDK interchange format (used by HHI, gdsfactory, Luceda, …).
    /// Reads the <c>header</c> metadata and the <c>xsections</c> (per-cross-section
    /// widths). Layer stack, bend radii and material indices are not part of the uPDK
    /// blueprint, so they are filled by another importer or by hand.
    /// </summary>
    public class UpdkYamlProcessImporter : IProcessImporter
    {
        /// <inheritdoc/>
        public string FormatName => "openEPDA uPDK (YAML)";

        /// <inheritdoc/>
        public bool CanImport(string path) =>
            path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public ProcessDefinition Import(string path)
        {
            var root = new DeserializerBuilder().Build()
                .Deserialize<Dictionary<object, object>>(File.ReadAllText(path))
                ?? new Dictionary<object, object>();

            var process = new ProcessDefinition { Name = Path.GetFileNameWithoutExtension(path) };
            ReadHeader(AsMap(Get(root, "header")), process);
            ReadXsections(AsMap(Get(root, "xsections")), process);
            return process;
        }

        private static void ReadHeader(Dictionary<object, object>? header, ProcessDefinition process)
        {
            if (header == null) return;
            var name = Str(Get(header, "pdk_name"));
            if (name.Length > 0) process.Name = name;
            process.Foundry = NullIfEmpty(Str(Get(header, "provider")));
            process.Version = NullIfEmpty(Str(Get(header, "file_version")));
        }

        private static void ReadXsections(Dictionary<object, object>? xsections, ProcessDefinition process)
        {
            if (xsections == null) return;
            foreach (var (key, value) in xsections)
            {
                var name = key?.ToString() ?? string.Empty;
                if (name.Length == 0) continue;
                TryDouble(Str(Get(AsMap(value), "width")), out var width);
                process.Xsections.Add(new ProcessXsection
                {
                    Name = name,
                    Kind = IsOptical(name) ? XsectionKind.Optical : XsectionKind.Metal,
                    WidthUm = width,
                });
            }
        }

        /// <summary>
        /// Optical cross-sections in the openEPDA naming: etch windows (E&lt;n&gt;),
        /// active (ACT) and facet (FACET). Everything else (DC, GS/GSG/RF metal
        /// lines) is electrical.
        /// </summary>
        private static bool IsOptical(string name) =>
            name.Equals("ACT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("FACET", StringComparison.OrdinalIgnoreCase) ||
            (name.Length > 1 && (name[0] == 'E' || name[0] == 'e') && char.IsDigit(name[1]));

        private static Dictionary<object, object>? AsMap(object? value) => value as Dictionary<object, object>;

        private static object? Get(Dictionary<object, object>? map, string key) =>
            map != null && map.TryGetValue(key, out var v) ? v : null;

        private static string Str(object? value) => value?.ToString()?.Trim() ?? string.Empty;

        private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

        private static bool TryDouble(string s, out double value) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
