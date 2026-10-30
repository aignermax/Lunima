using System.Collections.Immutable;
using Lunima.ArchitectureAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

public sealed class ArchitectureMemberOwnershipTests
{
    private const string ConstructorId = "M:CAP.Avalonia.Views.MainWindow.#ctor";
    private const string Providers = """
        namespace Fixture.Composition
        {
            public static class DefaultMainWindowComposition { public static object Create() => new object(); }
        }
        namespace Fixture.Infrastructure { public class Value { } }
        namespace Fixture.Contracts
        {
            public class Operand
            {
                public static Fixture.Infrastructure.Value operator +(Operand left, Operand right) => new();
            }
        }
        """;

    [Theory]
    [InlineData("public MainWindow() : this(Fixture.Composition.DefaultMainWindowComposition.Create()) { } public MainWindow(object ports) { }")]
    [InlineData("public MainWindow() { var ports = Fixture.Composition.DefaultMainWindowComposition.Create(); }")]
    [InlineData("public MainWindow() { var left = new Fixture.Contracts.Operand(); object result = left + left; }")]
    public async Task ExactParameterlessConstructor_AllowsReviewedComposition(string members)
    {
        var (diagnostics, compilation) = await AnalyzeAsync(members);
        compilation.GetTypeByMetadataName("CAP.Avalonia.Views.MainWindow")!.InstanceConstructors
            .Single(ctor => ctor.Parameters.Length == 0).GetDocumentationCommentId().ShouldBe(ConstructorId);
        diagnostics.ShouldBeEmpty("both syntax references and inferred operation types must use the exact member owner");
    }

    [Theory]
    [InlineData("public MainWindow(int ignored) { _ = Fixture.Composition.DefaultMainWindowComposition.Create(); }")]
    [InlineData("public void Open() { _ = Fixture.Composition.DefaultMainWindowComposition.Create(); }")]
    [InlineData("private static object Ports = Fixture.Composition.DefaultMainWindowComposition.Create();")]
    [InlineData("private object Ports = Fixture.Composition.DefaultMainWindowComposition.Create();")]
    [InlineData("static MainWindow() { _ = Fixture.Composition.DefaultMainWindowComposition.Create(); }")]
    [InlineData("public class Nested { public Nested() { _ = Fixture.Composition.DefaultMainWindowComposition.Create(); } }")]
    public async Task OtherMembersAndInitializers_KeepPresentationOwnership(string members)
    {
        var (diagnostics, _) = await AnalyzeAsync("public MainWindow() { } " + members);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId
            && d.GetMessage().Contains("'presentation'") && d.GetMessage().Contains("'composition'"));
    }

    [Theory]
    [InlineData("public MainWindow(int ignored) { var left = new Fixture.Contracts.Operand(); object result = left + left; }")]
    [InlineData("public void Open() { var left = new Fixture.Contracts.Operand(); object result = left + left; }")]
    [InlineData("private static object Value = new Fixture.Contracts.Operand() + new Fixture.Contracts.Operand();")]
    public async Task InferredOperationOutsideConstructor_CannotUseMemberException(string members)
    {
        var (diagnostics, _) = await AnalyzeAsync("public MainWindow() { } " + members);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId
            && d.GetMessage().Contains("Fixture.Infrastructure.Value")
            && d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).Contains(" + "),
            "the operator result has no written infrastructure type name; the operation collector must reject it");
    }

    [Theory]
    [InlineData("M:CAP.Avalonia.Views.MainWindow.#ctor(System.Int32)")]
    [InlineData("M:CAP.Avalonia.Views.MainWindow.#ctor*")]
    public async Task OtherOverloadOrPrefixEntry_DoesNotAuthorizeParameterlessConstructor(string configuredId)
    {
        var (diagnostics, _) = await AnalyzeAsync(
            "public MainWindow() { _ = Fixture.Composition.DefaultMainWindowComposition.Create(); }", configuredId);
        diagnostics.ShouldContain(d => d.Id == ArchitectureBoundaryAnalyzer.ForbiddenDependencyId);
    }

    [Fact]
    public async Task CallingReviewedConstructor_DoesNotReclassifyItsViewType()
    {
        var (diagnostics, _) = await AnalyzeAsync("public MainWindow() { }", extraType:
            "public class Consumer { public object Open() => new MainWindow(); }");
        diagnostics.ShouldBeEmpty("a constructor's source ownership must not change its target type ownership");
    }

    [Fact]
    public void RepositoryException_NamesOnlyTheReviewedConstructor()
    {
        ArchitecturePolicy.Repository.MemberModules.ShouldHaveSingleItem()
            .ShouldBe(new KeyValuePair<string, string>(ConstructorId, "composition"));
        ArchitecturePolicy.Repository.NamespaceModules["CAP.Avalonia.Views"].ShouldBe("presentation");
        ArchitecturePolicy.Repository.TypeModules.ContainsKey("CAP.Avalonia.Views.MainWindow").ShouldBeFalse();
    }

    private static async Task<(ImmutableArray<Diagnostic> Diagnostics, CSharpCompilation Compilation)> AnalyzeAsync(
        string members, string configuredId = ConstructorId, string extraType = "")
    {
        var policy = new ArchitecturePolicy(["MemberOwnershipFixture"], new Dictionary<string, string>
        {
            ["CAP.Avalonia.Views"] = "presentation", ["Fixture.Composition"] = "composition",
            ["Fixture.Contracts"] = "contracts", ["Fixture.Infrastructure"] = "infrastructure"
        }, new Dictionary<string, string>(),
        [ ("presentation", "contracts"), ("presentation", "runtime"), ("composition", "runtime"),
          ("composition", "contracts"), ("composition", "infrastructure"), ("composition", "presentation"),
          ("infrastructure", "runtime"),
          ("contracts", "runtime"), ("contracts", "infrastructure") ],
        new Dictionary<string, string> { ["System.Private.CoreLib"] = "runtime", ["System.Runtime"] = "runtime" },
        memberModules: new Dictionary<string, string> { [configuredId] = "composition" });
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("MemberOwnershipFixture",
            [ CSharpSyntaxTree.ParseText(Providers, path: "Providers.cs"),
              CSharpSyntaxTree.ParseText("namespace CAP.Avalonia.Views { public class MainWindow { " + members + " } " + extraType + " }",
                  path: "MainWindow.cs") ], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty(
            "fixtures must compile independently of architecture diagnostics");
        var diagnostics = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new ArchitectureBoundaryAnalyzer(policy)))
            .GetAnalyzerDiagnosticsAsync();
        return (diagnostics, compilation);
    }
}
