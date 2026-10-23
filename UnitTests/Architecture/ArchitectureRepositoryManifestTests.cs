using System.Xml.Linq;
using Lunima.ArchitectureAnalyzers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

public sealed class ArchitectureRepositoryManifestTests
{
    private static readonly string[] ShippingProjects =
    {
        "CAP_Contracts/CAP_Contracts.csproj", "Connect-A-Pic-Core/CAP-Core.csproj",
        "CAP-DataAccess/CAP-DataAccess.csproj", "CAP.Avalonia/CAP.Avalonia.csproj",
        "CAP.Desktop/CAP.Desktop.csproj", "CAP.Browser/CAP.Browser.csproj"
    };

    [Fact]
    public void EveryRepositoryProject_HasAnExplicitRole()
    {
        var root = SourceSizePolicy.RepositoryRoot();
        var known = ShippingProjects.Concat(new[]
        {
            "UnitTests/UnitTests.csproj", "Tools/Lunima.ArchitectureAnalyzers/Lunima.ArchitectureAnalyzers.csproj"
        }).ToHashSet(StringComparer.Ordinal);
        var actual = AuthoredFiles(root, "*.csproj").Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
        actual.Order().ShouldBe(known.Order(), "a new repository project needs explicit shipping/tooling ownership");
        var assemblies = ShippingProjects.Select(relative =>
        {
            var project = XDocument.Load(Path.Combine(root, relative));
            return project.Descendants("AssemblyName").Select(element => element.Value).SingleOrDefault()
                ?? Path.GetFileNameWithoutExtension(relative);
        });
        assemblies.Order().ShouldBe(ArchitecturePolicy.Repository.Assemblies.Order());
    }

    [Fact]
    public void EveryShippingNamespace_HasExplicitOwnership_WithoutStaleEntries()
    {
        var namespaces = ShippingDeclarations().Select(type => Namespace(type)).ToHashSet(StringComparer.Ordinal);
        namespaces.Order().ShouldBe(ArchitecturePolicy.Repository.NamespaceModules.Keys.Order(),
            "new namespace descendants must be reviewed rather than inheriting a parent module");
    }

    [Fact]
    public void MixedNamespaceTypeOverrides_ReferToActualDeclaredTypes()
    {
        var declared = ShippingDeclarations().Select(TypeName).ToHashSet(StringComparer.Ordinal);
        ArchitecturePolicy.Repository.TypeModules.Keys.Where(type => !declared.Contains(type)).ShouldBeEmpty(
            "stale or misspelled type exceptions would silently classify a real responsibility incorrectly");
    }

    [Theory]
    [InlineData("Avalonia.Base", "ui-framework")]
    [InlineData("CommunityToolkit.Mvvm", "mvvm")]
    [InlineData("MathNet.Numerics", "numerics")]
    public void ExternalFrameworks_HaveDistinctReviewedOwners(string assembly, string module) =>
        ArchitecturePolicy.Repository.ExternalAssemblyModules[assembly].ShouldBe(module);

    [Theory]
    [InlineData("domain", "infrastructure")]
    [InlineData("domain", "ui-framework")]
    [InlineData("application", "infrastructure")]
    [InlineData("infrastructure", "presentation")]
    [InlineData("presentation", "infrastructure")]
    public void TargetDirection_HasNoTransitionalEscape(string source, string target) =>
        ArchitecturePolicy.Repository.AllowedEdges.ShouldNotContain((source, target));

    [Theory]
    [InlineData("CAP_DataAccess.Import.Gds", "domain")]
    [InlineData("CAP_DataAccess.Import.Gds.LayerPicker", "domain")]
    [InlineData("CAP_DataAccess.Import.Gds.LayerCensus", "domain")]
    [InlineData("CAP_DataAccess.Import.Gds.Interpretation", "domain")]
    public void ParsedGeometryAndInterpretation_HaveDomainOwnership(string ns, string module) =>
        ArchitecturePolicy.Repository.NamespaceModules[ns].ShouldBe(module);

    [Theory]
    [InlineData("CAP_DataAccess.Import.Gds.GdsReader", "infrastructure")]
    [InlineData("CAP_Core.Analysis.SweepCsvExporter", "infrastructure")]
    [InlineData("CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumCsvReader", "infrastructure")]
    [InlineData("CAP.Avalonia.Services.ExamplesManifest", "infrastructure")]
    public void StreamAndFileOwners_AreNotDeclaredPureDomainOrContracts(string type, string module) =>
        ArchitecturePolicy.Repository.TypeModules.GetValueOrDefault(type).ShouldBe(module);

    [Fact]
    public void FileFormatContracts_MayCarryDomainValues() =>
        ArchitecturePolicy.Repository.AllowedEdges.ShouldContain(("contracts", "domain"));

    private static IEnumerable<BaseTypeDeclarationSyntax> ShippingDeclarations()
    {
        var root = SourceSizePolicy.RepositoryRoot();
        foreach (var project in ShippingProjects)
        foreach (var file in AuthoredFiles(Path.Combine(root, Path.GetDirectoryName(project)!), "*.cs"))
        foreach (var type in CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()
                     .DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            yield return type;
    }

    private static IEnumerable<string> AuthoredFiles(string directory, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(directory, pattern)) yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj" or ".git" or "node_modules") continue;
            foreach (var file in AuthoredFiles(child, pattern)) yield return file;
        }
    }

    private static string Namespace(BaseTypeDeclarationSyntax type) => string.Join(".",
        type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString()));

    private static string TypeName(BaseTypeDeclarationSyntax type)
    {
        var types = type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(declaration =>
            declaration.Identifier.ValueText + (declaration is TypeDeclarationSyntax { TypeParameterList: { } parameters }
                ? "`" + parameters.Parameters.Count : ""));
        var ns = Namespace(type);
        return (ns.Length == 0 ? "" : ns + ".") + string.Join(".", types);
    }
}
