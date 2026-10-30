using System.Collections.Immutable;
using Lunima.ArchitectureAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>Compiles real symbols: import spelling must never hide a forbidden dependency.</summary>
public sealed class ArchitectureDependencyAnalyzerTests
{
    private const string Targets = """
        namespace Fixture.Forbidden
        {
            public class Target { }
            public interface IForbidden { }
            public sealed class MarkerAttribute : System.Attribute { }
            public static class Factory { public static Target Make() => new Target(); }
        }
        namespace Fixture.Allowed { public class Value { } }
        """;

    public static IEnumerable<object[]> ForbiddenUses()
    {
        yield return Case("normal import", "using Fixture.Forbidden;", "public Target Value = new Target();");
        yield return Case("namespace alias", "using F = Fixture.Forbidden;", "public F.Target Value = new F.Target();");
        yield return Case("type alias", "using T = Fixture.Forbidden.Target;", "public T Value = new T();");
        yield return Case("static import", "using static Fixture.Forbidden.Factory;", "public object Run() => Make();");
        yield return Case("global import", "global using Fixture.Forbidden;", "public Target Value = new Target();");
        yield return Case("global alias", "global using T = Fixture.Forbidden.Target;", "public T Value = new T();");
        yield return Case("fully qualified", "", "public global::Fixture.Forbidden.Target Value = new();");
        yield return Case("generic argument", "", "public System.Collections.Generic.List<Fixture.Forbidden.Target> Values = new();");
        yield return Case("inferred expression type", "", "public object Run() { var value = Fixture.Forbidden.Factory.Make(); return value; }");
        yield return Case("typeof", "", "public System.Type Type => typeof(Fixture.Forbidden.Target);");
        yield return new object[] { "base type", "namespace Fixture.Source { public class Client : Fixture.Forbidden.Target { } }" };
        yield return new object[] { "interface", "namespace Fixture.Source { public class Client : Fixture.Forbidden.IForbidden { } }" };
        yield return new object[] { "attribute", "namespace Fixture.Source { [Fixture.Forbidden.Marker] public class Client { } }" };
    }

    private static object[] Case(string description, string imports, string member) =>
        new object[] { description, imports + "\nnamespace Fixture.Source { public class Client { " + member + " } }" };

    [Theory]
    [MemberData(nameof(ForbiddenUses))]
    public async Task ResolvedForbiddenDependency_IsRejectedRegardlessOfSpelling(string description, string source)
    {
        var diagnostics = await AnalyzeAsync(source);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId,
            description + ": resolved forbidden dependency must produce LUN001");
        diagnostics.Where(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId)
            .ShouldAllBe(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ExplicitAllowedEdge_IsAccepted()
    {
        var diagnostics = await AnalyzeAsync("namespace Fixture.Source { public class Client { public Fixture.Allowed.Value Value = new(); } }");
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnusedImport_DoesNotInventASemanticDependency()
    {
        var diagnostics = await AnalyzeAsync("using Fixture.Forbidden; namespace Fixture.Source { public class Client { public int Value = 1; } }");
        diagnostics.ShouldBeEmpty("a namespace import alone does not reference a foreign type or member");
    }

    [Fact]
    public async Task AnonymousProjection_WithAllowedProperties_HasNoInventedModuleOwner()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Fixture.Source
            {
                public class Client
                {
                    public object Run() => new { Count = 1, Nested = new { Value = new Fixture.Allowed.Value() } };
                }
            }
            """);
        diagnostics.ShouldBeEmpty("anonymous shapes belong to their containing operation; their property types carry dependencies");
    }

    [Fact]
    public async Task AnonymousProjection_WithForbiddenProperty_RejectsDependencyWithoutInventedOwner()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Fixture.Source
            {
                public class Client { public object Run() => new { Value = new Fixture.Forbidden.Target() }; }
            }
            """);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId);
        diagnostics.ShouldNotContain(d => d.Id == ArchitectureBoundaryAnalyzer.UnknownOwnershipId);
    }

    [Fact]
    public async Task LocalFunctionsAndCapturedLambdas_KeepTheContainingTypeOwner()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Fixture.Source
            {
                public class Client
                {
                    public object Run()
                    {
                        var value = new Fixture.Allowed.Value();
                        object Local() => value;
                        System.Func<object> callback = () => Local();
                        return callback();
                    }
                }
            }
            """);
        diagnostics.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Fixture.Source.NewFeature")]
    [InlineData("Fixture.SourceLookalike")]
    public async Task UnlistedNamespace_IsRejectedInsteadOfInheritingPrefixPermission(string unlisted)
    {
        var diagnostics = await AnalyzeAsync("namespace " + unlisted + " { public class Client { } }");
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.UnknownOwnershipId);
    }

    [Fact]
    public async Task UnlistedProject_IsRejected()
    {
        var diagnostics = await AnalyzeAsync("namespace Fixture.Source { public class Client { } }", "UnlistedProject");
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.UncoveredProjectId);
    }

    [Fact]
    public async Task GeneratedPartialMember_StillExposesForbiddenTypeToAuthoredConsumer()
    {
        var generated = CSharpSyntaxTree.ParseText("""
            // <auto-generated/>
            namespace Fixture.Allowed
            {
                public partial class Value { public Fixture.Forbidden.Target Target => new(); }
            }
            """, path: "Value.g.cs");
        var diagnostics = await AnalyzeAsync("""
            namespace Fixture.Source
            {
                public class Client { public object Run() { var target = new Fixture.Allowed.Value().Target; return target; } }
            }
            """, extraTree: generated);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId &&
            d.Location.SourceTree != generated,
            "authored consumers must see the real generated member type");
    }

    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or
            "System.Collections.dll" or "netstandard.dll")
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source,
        string assemblyName = "ArchitectureFixture", SyntaxTree? extraTree = null)
    {
        // Allowed→Forbidden exists only so the generated-provider fixture isolates the consumer edge.
        var policy = new ArchitecturePolicy(new[] { "ArchitectureFixture" },
            new Dictionary<string, string>
            {
                ["Fixture.Source"] = "source", ["Fixture.Forbidden"] = "forbidden", ["Fixture.Allowed"] = "allowed"
            }, new Dictionary<string, string>(), new[]
            {
                ("source", "allowed"), ("allowed", "forbidden"),
                ("source", "runtime"), ("allowed", "runtime"), ("forbidden", "runtime")
            }, new Dictionary<string, string>
            {
                ["System.Private.CoreLib"] = "runtime", ["System.Runtime"] = "runtime",
                ["System.Collections"] = "runtime", ["netstandard"] = "runtime"
            });
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(Targets, path: "Targets.cs"),
            CSharpSyntaxTree.ParseText(source, path: "Client.cs")
        };
        if (extraTree != null)
        {
            trees[0] = CSharpSyntaxTree.ParseText(Targets.Replace("public class Value", "public partial class Value"), path: "Targets.cs");
            trees.Add(extraTree);
        }
        var compilation = CSharpCompilation.Create(assemblyName, trees, References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("negative fixtures must compile; only the architecture analyzer should reject them");
        return await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureBoundaryAnalyzer(policy)))
            .GetAnalyzerDiagnosticsAsync();
    }
}
