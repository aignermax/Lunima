using CAP_Core.ComponentRegistry.RegistryClient;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP.Avalonia.Services.ComponentRegistry;

/// <summary>
/// "Download" of the registry browser (issue #773): fetches the selected
/// S-parameter artifact (cache-first via <see cref="RegistryClient"/>, so an
/// already-cached artifact adopts offline), maps it to a
/// <see cref="PdkComponentDraft"/> carrying the artifact's provenance, and
/// persists it into the process-bound user PDK <c>Registry &lt;process&gt;</c>
/// via <see cref="IUserPdkLibrary"/> — placement then follows the existing
/// single-process lock on that process id.
/// </summary>
public sealed class RegistryDownloadService : IRegistryAdoption
{
    private readonly IRegistryCatalog _client;
    private readonly IUserPdkLibrary _userPdkStore;
    private readonly Action<string>? _onPdkSaved;

    /// <summary>
    /// Creates the service. <paramref name="onPdkSaved"/> receives the saved
    /// PDK file path after each successful download (the app wires it to the
    /// component library's same-session registration — lazily, because the
    /// library ViewModel itself depends on the registry browser).
    /// </summary>
    public RegistryDownloadService(
        IRegistryCatalog client, IUserPdkLibrary userPdkStore, Action<string>? onPdkSaved = null)
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
        cancellationToken.ThrowIfCancellationRequested();
        var spectrumResult = await _client.GetSpectrumAsync(manifestPath, choice.Artifact, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
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
