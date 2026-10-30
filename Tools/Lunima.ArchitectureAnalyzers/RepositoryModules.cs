using System.Collections.Generic;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Architectural responsibilities, independent of project or folder names.</summary>
internal static class RepositoryModules
{
    internal const string Contracts = "contracts";
    internal const string Domain = "domain";
    internal const string Application = "application";
    internal const string Infrastructure = "infrastructure";
    internal const string Presentation = "presentation";
    internal const string Composition = "composition";
    internal const string Runtime = "runtime";
    internal const string Numerics = "numerics";
    internal const string Expressions = "expressions";
    internal const string Serialization = "serialization";
    internal const string UiFramework = "ui-framework";
    internal const string Mvvm = "mvvm";
    internal const string DependencyInjection = "dependency-injection";
    internal const string Logging = "logging";

    internal static IEnumerable<(string Source, string Target)> AllowedEdges()
    {
        yield return (Contracts, Runtime);
        yield return (Contracts, Serialization);
        // Serialized contracts reuse domain geometry/enums; both form the UI-independent foundation.
        yield return (Contracts, Domain);
        yield return (Domain, Contracts);
        yield return (Domain, Runtime);
        yield return (Domain, Numerics);
        yield return (Domain, Expressions);
        yield return (Domain, Serialization);
        yield return (Application, Contracts);
        yield return (Application, Domain);
        yield return (Application, Runtime);
        yield return (Application, Numerics);
        yield return (Application, Expressions);
        yield return (Application, Serialization);
        yield return (Infrastructure, Contracts);
        yield return (Infrastructure, Domain);
        yield return (Infrastructure, Application);
        yield return (Infrastructure, Runtime);
        yield return (Infrastructure, Numerics);
        yield return (Infrastructure, Expressions);
        yield return (Infrastructure, Serialization);
        yield return (Infrastructure, Logging);
        yield return (Presentation, Contracts);
        yield return (Presentation, Domain);
        yield return (Presentation, Application);
        yield return (Presentation, Runtime);
        yield return (Presentation, Numerics);
        yield return (Presentation, Expressions);
        yield return (Presentation, Serialization);
        yield return (Presentation, UiFramework);
        yield return (Presentation, Mvvm);
        yield return (Presentation, Logging);
        yield return (Composition, Contracts);
        yield return (Composition, Domain);
        yield return (Composition, Application);
        yield return (Composition, Infrastructure);
        yield return (Composition, Presentation);
        yield return (Composition, Runtime);
        yield return (Composition, Numerics);
        yield return (Composition, Expressions);
        yield return (Composition, Serialization);
        yield return (Composition, UiFramework);
        yield return (Composition, Mvvm);
        yield return (Composition, DependencyInjection);
        yield return (Composition, Logging);
    }
}
