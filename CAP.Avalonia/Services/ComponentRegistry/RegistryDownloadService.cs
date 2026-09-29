using CAP_Core.ComponentRegistry.RegistryClient;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

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

/// <summary>
/// "Download" of the registry browser (issue #773): fetches the selected
/// S-parameter artifact (cache-first via <see cref="RegistryClient"/>, so an
/// already-cached artifact adopts offline), maps it to a
/// <see cref="PdkComponentDraft"/> carrying the artifact's provenance, and
/// persists it into the process-bound user PDK <c>Registry &lt;process&gt;</c>
/// via <see cref="UserPdkStore"/> — placement then follows the existing
/// single-process lock on that process id.
/// </summary>
public sealed class RegistryDownloadService
{
    private readonly RegistryClient _client;
    private readonly UserPdkStore _userPdkStore;
    private readonly Action<string>? _onPdkSaved;

    /// <summary>
    /// Creates the service. <paramref name="onPdkSaved"/> receives the saved
    /// PDK file path after each successful download (the app wires it to the
    /// component library's same-session registration — lazily, because the
    /// library ViewModel itself depends on the registry browser).
    /// </summary>
    public RegistryDownloadService(
        RegistryClient client, UserPdkStore userPdkStore, Action<string>? onPdkSaved = null)
    {
        _client = client;
        _userPdkStore = userPdkStore;
        _onPdkSaved = onPdkSaved;
    }

    /// <summary>The user-PDK name registry components of <paramref name="processId"/> are adopted into.</summary>
    public static string PdkNameForProcess(string processId) => $"Registry {processId}";

    /// <summary>
    /// Downloads <paramref name="choice"/>'s artifact of <paramref name="manifest"/>
    /// and adopts it into the local Registry PDK. Never throws: fetch and
    /// validation failures are reported in the result.
    /// </summary>
    public async Task<RegistryDownloadResult> DownloadAsync(
        string manifestPath, ComponentManifest manifest, RegistryArtifactChoice choice,
        CancellationToken cancellationToken = default)
    {
        var spectrumResult = await _client.GetSpectrumAsync(manifestPath, choice.Artifact, cancellationToken: cancellationToken);
        if (!spectrumResult.IsSuccess || spectrumResult.Value is null)
        {
            return RegistryDownloadResult.Failure(
                spectrumResult.ErrorMessage ?? "The artifact could not be loaded.");
        }

        PdkComponentDraft draft;
        try
        {
            draft = RegistryComponentDraftMapper.ToDraft(
                manifest, choice.Artifact, choice.Tier, spectrumResult.Value);
        }
        catch (InvalidDataException ex)
        {
            return RegistryDownloadResult.Failure(ex.Message);
        }

        var pdkName = PdkNameForProcess(manifest.Process);
        var process = new ProcessDefinition
        {
            Name = manifest.Process,
            Foundry = string.IsNullOrEmpty(choice.Artifact.Provenance.Fab)
                ? null
                : choice.Artifact.Provenance.Fab,
        };
        // Backend like a black-box GDS import: no exportable geometry backend —
        // the component is a data-only S-matrix block. Keyed on the registry id
        // (carried in the provenance note), never the display name: a re-download
        // replaces its own earlier copy with a .trash backup, while a same-named
        // DIFFERENT registry entry is a clash that must not overwrite it.
        var save = _userPdkStore.SaveComponentKeyed(
            pdkName, process, draft, IsSameRegistryComponent(manifest.Id), backend: "nazca", routingCrossSection: null);
        if (save.Outcome == KeyedComponentSaveOutcome.NameClash)
        {
            return RegistryDownloadResult.Failure(
                $"A different component named '{manifest.Name}' already exists in '{pdkName}' — " +
                "refusing to overwrite it. Rename the existing component first.");
        }

        _onPdkSaved?.Invoke(save.FilePath);
        return RegistryDownloadResult.Success(
            pdkName, save.FilePath, save.Outcome == KeyedComponentSaveOutcome.Replaced);
    }

    private static Predicate<PdkComponentDraft> IsSameRegistryComponent(string registryId)
    {
        var prefix = RegistryComponentDraftMapper.RegistryIdPrefix(registryId);
        return c => c.SMatrix?.SourceNote?.StartsWith(prefix, StringComparison.Ordinal) == true;
    }
}
