using System.Xml.Linq;
using Lunima.ArchitectureAnalyzers;
using Microsoft.CodeAnalysis;
using Shouldly;

namespace UnitTests.Architecture;

public sealed class DefaultBuildArchitectureGateTests
{
    [Fact]
    public void EveryDefaultShippingBuild_EnforcesResolvedArchitectureDependencies()
    {
        var root = SourceSizePolicy.RepositoryRoot();
        var targets = XDocument.Load(Path.Combine(root, "Directory.Build.targets"));
        var reference = targets.Descendants("ProjectReference").Single(element =>
            ((string?)element.Attribute("Include"))?.Contains("Lunima.ArchitectureAnalyzers.csproj") == true);

        // Only tests and the analyzer itself are tooling. New shipping projects must also be checked.
        ((string?)reference.Parent!.Attribute("Condition")).ShouldBe(
            "'$(MSBuildProjectName)' != 'UnitTests' and '$(MSBuildProjectName)' != 'Lunima.ArchitectureAnalyzers'");
        ((string?)reference.Attribute("OutputItemType")).ShouldBe("Analyzer");
        ((string?)reference.Attribute("ReferenceOutputAssembly")).ShouldBe("false");
        targets.Descendants("EditorConfigFiles").ShouldNotContain(element =>
            ((string?)element.Attribute("Include") ?? "").Contains("ArchitectureAudit.globalconfig"));
        ArchitectureBoundaryAnalyzer.ForbiddenDependency.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        ArchitectureBoundaryAnalyzer.UnknownOwnership.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        ArchitectureBoundaryAnalyzer.UncoveredProject.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
    }
}
