namespace CAP.Avalonia.Services.ComponentRegistry;

/// <summary>
/// Outcome of adopting a registry component into the local library
/// (issue #773). On success <see cref="PdkName"/> is the "Registry &lt;process&gt;"
/// user PDK the component was added to and <see cref="FilePath"/> its file;
/// <see cref="ReplacedExisting"/> tells the UI whether a previous download of
/// the same registry entry was replaced (its state backed up to <c>.trash</c>)
/// or the component was newly added. On failure <see cref="ErrorMessage"/>
/// explains why (never throws).
/// </summary>
public sealed record RegistryDownloadResult(
    bool IsSuccess, string? PdkName, string? FilePath, string? ErrorMessage, bool ReplacedExisting)
{
    /// <summary>Successful adoption into <paramref name="pdkName"/>.</summary>
    public static RegistryDownloadResult Success(string pdkName, string filePath, bool replacedExisting) =>
        new(true, pdkName, filePath, null, replacedExisting);

    /// <summary>Failed adoption; nothing was written.</summary>
    public static RegistryDownloadResult Failure(string errorMessage) =>
        new(false, null, null, errorMessage, false);
}

