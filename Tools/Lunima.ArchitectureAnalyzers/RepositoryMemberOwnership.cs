using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Reviewed composition entry points, identified by exact Roslyn documentation IDs.</summary>
internal static class RepositoryMemberOwnership
{
    internal static IEnumerable<KeyValuePair<string, string>> Entries()
    {
        // The public compatibility constructor assembles default ports; the view itself stays presentation.
        yield return new("M:CAP.Avalonia.Views.MainWindow.#ctor", RepositoryModules.Composition);
    }
}
