using System.Collections.Immutable;
using Lunima.ArchitectureAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>A referenced project cannot escape ownership by being treated as a third-party assembly.</summary>
public sealed class ArchitectureExternalAssemblyTests
{
    [Fact]
    public async Task UnlistedReferencedAssembly_IsRejected()
    {
        var diagnostics = await AnalyzeDependencyAsync("NewInRepoProject", declaredModule: null, allowEdge: false);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.UncoveredProjectId);
    }

    [Fact]
    public async Task DeclaredUiFramework_IsStillForbiddenToDomain()
    {
        var diagnostics = await AnalyzeDependencyAsync("Example.UiFramework", "presentation-framework", allowEdge: false);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId,
            "an external inventory entry declares ownership, not permission for every module");
    }

    [Fact]
    public async Task DeclaredNumericalLibrary_WithExplicitEdge_IsAccepted()
    {
        var diagnostics = await AnalyzeDependencyAsync("Example.Numerics", "numerics", allowEdge: true);
        diagnostics.ShouldBeEmpty();
    }

    private static readonly ImmutableArray<MetadataReference> RuntimeReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeDependencyAsync(string dependencyAssembly,
        string? declaredModule, bool allowEdge)
    {
        var dependency = CSharpCompilation.Create(dependencyAssembly,
            new[] { CSharpSyntaxTree.ParseText("namespace Dependency { public class Value { } }") }, RuntimeReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emit = dependency.Emit(bytes);
        emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var consumer = CSharpCompilation.Create("CoveredProject",
            new[] { CSharpSyntaxTree.ParseText("namespace Domain { public class Client { public Dependency.Value Value = new(); } }") },
            RuntimeReferences.Add(MetadataReference.CreateFromImage(bytes.ToArray())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        consumer.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var externalModules = new Dictionary<string, string>
        {
            ["System.Private.CoreLib"] = "runtime", ["System.Runtime"] = "runtime"
        };
        if (declaredModule != null) externalModules.Add(dependencyAssembly, declaredModule);
        var edges = new List<(string, string)> { ("domain", "runtime") };
        if (allowEdge) edges.Add(("domain", declaredModule!));
        var policy = new ArchitecturePolicy(new[] { "CoveredProject" },
            new Dictionary<string, string> { ["Domain"] = "domain" }, new Dictionary<string, string>(), edges, externalModules);
        return await consumer.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureBoundaryAnalyzer(policy)))
            .GetAnalyzerDiagnosticsAsync();
    }
}
