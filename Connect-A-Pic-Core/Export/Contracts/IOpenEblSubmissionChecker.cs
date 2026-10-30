namespace CAP_Core.Export.Contracts;

/// <summary>Checks an exported design against the openEBL submission and verification rules.</summary>
public interface IOpenEblSubmissionChecker
{
    /// <summary>Completes only after both checks and their owned process cleanup have finished.</summary>
    Task<OpenEblCheckReport> CheckAsync(string gdsPath, CancellationToken cancellationToken = default);
}
