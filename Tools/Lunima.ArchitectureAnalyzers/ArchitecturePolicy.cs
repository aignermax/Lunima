using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Explicit ownership and permitted dependency edges; namespaces match exactly.</summary>
public sealed class ArchitecturePolicy
{
    public ArchitecturePolicy(IEnumerable<string> assemblies,
        IEnumerable<KeyValuePair<string, string>> namespaces,
        IEnumerable<KeyValuePair<string, string>> types,
        IEnumerable<(string Source, string Target)> allowedEdges,
        IEnumerable<KeyValuePair<string, string>>? externalAssemblyModules = null)
    {
        Assemblies = assemblies.ToImmutableHashSet(StringComparer.Ordinal);
        NamespaceModules = namespaces.ToImmutableDictionary(StringComparer.Ordinal);
        TypeModules = types.ToImmutableDictionary(StringComparer.Ordinal);
        AllowedEdges = allowedEdges.ToImmutableHashSet();
        ExternalAssemblyModules = (externalAssemblyModules ?? Array.Empty<KeyValuePair<string, string>>())
            .ToImmutableDictionary(StringComparer.Ordinal);
    }

    public ImmutableHashSet<string> Assemblies { get; }
    public ImmutableDictionary<string, string> NamespaceModules { get; }
    public ImmutableDictionary<string, string> TypeModules { get; }
    public ImmutableHashSet<(string Source, string Target)> AllowedEdges { get; }
    public ImmutableDictionary<string, string> ExternalAssemblyModules { get; }

    // Production attachment follows explicit repository ownership review, never an allow-all default.
    public static ArchitecturePolicy Repository { get; } = new(
        new[] { "CAP_Contracts", "CAP-Core", "CAP-DataAccess", "CAP.Avalonia", "CAP.Desktop", "CAP.Browser" },
        RepositoryModelNamespaces.Entries().Concat(RepositoryPresentationNamespaces.Entries()),
        RepositoryTypeOwnership.Entries().Concat(RepositoryBoundaryOwnership.Entries()), RepositoryModules.AllowedEdges(),
        RepositoryRuntimeAssemblies.Entries().Concat(RepositoryExternalAssemblies.Entries()));
}
