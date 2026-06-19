using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper
{
    /// <summary>
    /// Imports a <see cref="ProcessDefinition"/> from one source format. The process
    /// model is format-agnostic; concrete importers adapt a specific PDK layout
    /// (Nazca CSV tables, openEPDA uPDK YAML, …). Each importer fills the fields its
    /// format provides — the rest is completed by another importer or by hand.
    /// </summary>
    public interface IProcessImporter
    {
        /// <summary>Short human-readable name of the source format (for UI / errors).</summary>
        string FormatName { get; }

        /// <summary>True if this importer can read the given file path.</summary>
        bool CanImport(string path);

        /// <summary>Reads the file into a process definition.</summary>
        ProcessDefinition Import(string path);
    }
}
