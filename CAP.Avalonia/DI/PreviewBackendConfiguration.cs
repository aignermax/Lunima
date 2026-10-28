using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Export;

namespace CAP.Avalonia.DI;

/// <summary>Connects effective interpreter activation and loaded PDK revisions to preview lifetime.</summary>
internal static class PreviewBackendConfiguration
{
    public static void Configure(NazcaComponentPreviewService backend, UserPreferencesService preferences,
        PdkManagerViewModel? manager)
    {
        preferences.PythonInterpreterActivated += backend.InvalidateCache;
        if (manager == null) return;

        string Snapshot() => PreviewEnvironmentIdentity.Hash(string.Join("\n", manager.LoadedPdks
            .Select(pdk => pdk.FilePath is { } path ? PreviewEnvironmentIdentity.FileRevision(path) : pdk.Name)
            .OrderBy(value => value, StringComparer.Ordinal)));

        var revision = Snapshot();
        backend.EnvironmentRevisionResolver = () => Volatile.Read(ref revision);
        void RefreshRevision()
        {
            Volatile.Write(ref revision, Snapshot());
            backend.InvalidateCache();
        }
        manager.LoadedPdks.CollectionChanged += (_, _) => RefreshRevision();
        manager.DefinitionsChanged += RefreshRevision;
    }
}
