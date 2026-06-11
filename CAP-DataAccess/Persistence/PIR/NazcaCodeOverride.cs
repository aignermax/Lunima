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
    /// Optical pins and the S-matrix are unaffected — this is geometry-only.
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
}
