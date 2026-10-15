using System.Text.Json;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// The tolerant (per-component) load of <see cref="PdkLoader"/>, split out
    /// to keep the main file under the project's line-count gate.
    /// </summary>
    public partial class PdkLoader
    {
        /// <summary>
        /// Loads a PDK file tolerantly — for the STARTUP reload only: each
        /// component is validated on its own; invalid ones are dropped with
        /// their errors collected (same "[pdk/component] reason" format the
        /// strict path reports) instead of failing the whole file, so one
        /// broken component cannot make its valid siblings disappear from the
        /// library after a restart. File-level problems (missing file,
        /// unreadable JSON, missing PDK name) still throw — the file itself is
        /// skipped then. Nazca origin offsets are not required (same rule as
        /// <see cref="LoadFromFileForEditing"/>, which this replaces at startup).
        /// The explicit user-facing load paths (<see cref="LoadFromFile"/>,
        /// <see cref="LoadFromFileForEditing"/>, <see cref="LoadFromJson"/>)
        /// stay strict and keep surfacing every validation error.
        /// </summary>
        public PdkTolerantLoadResult LoadFromFileTolerant(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"PDK file not found: {filePath}");
            }

            var pdk = JsonSerializer.Deserialize<PdkDraft>(File.ReadAllText(filePath), JsonOptions);
            if (pdk == null)
            {
                throw new InvalidOperationException("Failed to deserialize PDK JSON");
            }
            if (string.IsNullOrWhiteSpace(pdk.Name))
            {
                throw new InvalidOperationException("PDK must have a name");
            }
            pdk.FilePath = filePath;

            var componentErrors = new List<string>();
            var validComponents = new List<PdkComponentDraft>(pdk.Components.Count);
            foreach (var comp in pdk.Components)
            {
                var errors = new List<string>();
                try
                {
                    ValidateComponent(comp, pdk.Name, errors, requireNazcaOffset: false, pdk.IsGdsFactoryBackend);
                }
                catch (Exception ex)
                {
                    var label = string.IsNullOrWhiteSpace(comp.Name) ? "(unnamed)" : comp.Name;
                    errors.Add($"[{pdk.Name}/{label}] {ex.Message}");
                }

                if (errors.Count == 0)
                {
                    validComponents.Add(comp);
                }
                else
                {
                    componentErrors.AddRange(errors);
                }
            }

            pdk.Components = validComponents;
            return new PdkTolerantLoadResult(pdk, componentErrors);
        }
    }
}
