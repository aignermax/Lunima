namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>
/// Thrown when a PDK JSON file fails validation.
/// Contains all collected validation errors so they can be displayed together.
/// </summary>
public class PdkValidationException : Exception
{
    /// <summary>Name of the PDK that failed validation.</summary>
    public string PdkName { get; }

    /// <summary>All validation errors found in the PDK.</summary>
    public IReadOnlyList<string> Errors { get; }

    public PdkValidationException(string pdkName, IReadOnlyList<string> errors)
        : base($"PDK '{pdkName}' has {errors.Count} validation error(s)")
    {
        PdkName = pdkName;
        Errors = errors;
    }
}
