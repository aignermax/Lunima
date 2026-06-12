namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// Per-instance Nazca function parameter override for a single canvas component.
/// All override fields are optional; a null value means "use the PDK template value".
/// Persisted in the .lun file under <c>NazcaOverrides[componentIdentifier]</c>.
/// Template fields capture the original PDK values at override-creation time so
/// "Reset to template" works correctly even after the component's live values have
/// already been replaced by the override.
/// </summary>
public class NazcaCodeOverride
{
    /// <summary>
    /// Override for the Nazca function name (e.g., "ebeam_mmi1x2_te1550").
    /// Null means use the PDK template's function name.
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// Override for the Nazca function parameters string (e.g., "length=3.5,width=2.0").
    /// Null means use the PDK template's parameter string.
    /// </summary>
    public string? FunctionParameters { get; set; }

    /// <summary>
    /// Override for the Nazca module name.
    /// Null means use the PDK template's module name.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// PDK template's original function name, captured at override-creation time.
    /// Used by the "Reset to template" command to restore the correct value even
    /// after the live component's <c>NazcaFunctionName</c> has already been overwritten.
    /// </summary>
    public string? TemplateFunctionName { get; set; }

    /// <summary>
    /// PDK template's original function parameters, captured at override-creation time.
    /// Used by the "Reset to template" command.
    /// </summary>
    public string? TemplateFunctionParameters { get; set; }

    /// <summary>
    /// PDK template's original module name, captured at override-creation time.
    /// Used by the "Reset to template" command.
    /// </summary>
    public string? TemplateModuleName { get; set; }

    /// <summary>
    /// Complete editable Nazca cell code for this instance (issue #556). When set,
    /// the geometry preview / size recompute is driven by executing this code rather
    /// than by calling the PDK template's <see cref="FunctionName"/>.
    /// Null means no raw-code override is active (parameter-only or PDK template).
    /// When applied, the override's ports replace the template pins
    /// (see <see cref="OverridePins"/> and <see cref="HasNoSimulationModel"/>).
    /// </summary>
    public string? RawCode { get; set; }

    /// <summary>
    /// Component width (µm) recomputed from the rendered raw-code geometry's bounding box.
    /// Null when no raw-code override has recomputed the size.
    /// </summary>
    public double? OverrideWidthMicrometers { get; set; }

    /// <summary>
    /// Component height (µm) recomputed from the rendered raw-code geometry's bounding box.
    /// Null when no raw-code override has recomputed the size.
    /// </summary>
    public double? OverrideHeightMicrometers { get; set; }

    /// <summary>
    /// Physical pins derived from the override's rendered geometry (issue #561).
    /// When non-null and non-empty, the component's <c>PhysicalPins</c> list is replaced
    /// with these values on Apply and on project load.
    /// Null means no pin override is active (template pins remain).
    /// </summary>
    public List<OverridePinData>? OverridePins { get; set; }

    /// <summary>
    /// Snapshot of the PDK-template physical pins captured on the first Apply,
    /// before the override pins replaced them. Used by "Reset to template" to
    /// restore the original port layout without a PDK re-query.
    /// Null when no Apply has been performed yet.
    /// </summary>
    public List<OverridePinData>? TemplatePins { get; set; }

    /// <summary>
    /// True when the override's pin layout differs from the PDK template's, meaning
    /// the template S-matrix no longer maps to the new ports.
    /// When true the component has no valid simulation model — the S-matrix shown
    /// is the old template's and must not be trusted.
    /// The user should supply a matching S-matrix via the per-instance import (#541)
    /// or accept geometry/export-only mode.
    /// </summary>
    public bool HasNoSimulationModel { get; set; }
}
