using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Export;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// The Demo PDK carries its own 4-port crossing, so a demofab design (all bundled logic
/// examples) can cross wires without leaving its process; the crossing factory prefers the
/// crossing of the PDK the design is built from.
/// </summary>
public class DemoCrossingTemplateTests
{
    private const string DemoPdk = "Demo PDK";
    private const string SiepicPdk = "SiEPIC EBeam";
    private const double ExpectedThroughLossDb = 0.19;
    private const double LossToleranceDb = 0.01;

    private static readonly List<ComponentTemplate> Templates = TestPdkLoader.LoadAllTemplates();

    private static ComponentTemplate DemoCrossing() =>
        Templates.Single(t => t.NazcaFunctionName == CrossingComponentInstance.DemoCrossingNazcaFunctionName);

    [Fact]
    public void DemoPdk_ProvidesACrossingWithFourWiredPorts()
    {
        var template = DemoCrossing();
        template.PdkSource.ShouldBe(DemoPdk);
        CrossingComponentInstance.IsCrossingTemplate(template).ShouldBeTrue();

        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        var inserter = new CrossingInserter();
        inserter.HasAllFourWiredPorts(component).ShouldBeTrue("one port per direction: W, E, N, S");
        inserter.GetCrossingThroughLossDb(component).ShouldNotBeNull()
            .ShouldBe(ExpectedThroughLossDb, LossToleranceDb, "same through loss as the SiEPIC 4-port crossing");
    }

    [Fact]
    public void FindCrossingTemplate_PrefersThePdkTheDesignIsBuiltFrom()
    {
        CrossingComponentInstance.FindCrossingTemplate(Templates, new[] { DemoPdk })
            .ShouldNotBeNull().PdkSource.ShouldBe(DemoPdk);
        CrossingComponentInstance.FindCrossingTemplate(Templates, new[] { SiepicPdk })
            .ShouldNotBeNull().NazcaFunctionName.ShouldBe(CrossingComponentInstance.CrossingNazcaFunctionName);
    }

    [Fact]
    public void FindCrossingTemplate_WithoutPreference_KeepsTheSiepicDefault()
    {
        CrossingComponentInstance.FindCrossingTemplate(Templates)
            .ShouldNotBeNull().NazcaFunctionName.ShouldBe(CrossingComponentInstance.CrossingNazcaFunctionName);
    }

    [Fact]
    public void PreferredPdksOf_ReadsThePdksInsideGroups()
    {
        var canvas = new DesignCanvasViewModel();
        var mmi = ComponentTemplates.CreateFromTemplate(
            Templates.First(t => t.Name == "2x2 MMI Coupler" && t.PdkSource == DemoPdk), 0, 0);
        canvas.AddComponent(mmi, "2x2 MMI Coupler", DemoPdk);

        CrossingComponentInstance.PreferredPdksOf(canvas.Components.Select(c => c.Component), Templates)
            .ShouldBe(new[] { DemoPdk });
    }

    [Fact]
    public void Export_InlinesTheCrossingGeometry()
    {
        var canvas = new DesignCanvasViewModel();
        var crossing = CrossingComponentInstance.CreateFromTemplates(Templates, new[] { DemoPdk }).ShouldNotBeNull();
        canvas.AddComponent(crossing.Component, crossing.TemplateName!, crossing.TemplatePdkSource!);

        var script = new SimpleNazcaExporter().Export(canvas, library: Templates);

        script.ShouldContain("demo_crossing_body", customMessage: "the raw crossing cell is inlined, not a placeholder box");
        script.ShouldContain("demo.shallow.strt(length=20)");
    }

    [SkippableFact]
    [Trait("Category", "Slow")]
    public async Task Export_WithRealNazca_WritesTheCrossing()
    {
        var python = await GdsUserDesignFixture.FindNazcaPythonAsync();
        Skip.If(python == null, "No Python with nazca available.");
        var canvas = new DesignCanvasViewModel();
        var crossing = CrossingComponentInstance.CreateFromTemplates(Templates, new[] { DemoPdk }).ShouldNotBeNull();
        canvas.AddComponent(crossing.Component, crossing.TemplateName!, crossing.TemplatePdkSource!);
        var dir = Path.Combine(Path.GetTempPath(), "lunima-demo-crossing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "crossing.py");
            await File.WriteAllTextAsync(scriptPath, new SimpleNazcaExporter().Export(canvas, library: Templates));

            var run = await SiepicRealGeometryExportTests.RunPythonAsync(python!, dir, scriptPath);

            run.ExitCode.ShouldBe(0, $"{run.StdOut}\n{run.StdErr}");
            File.Exists(Path.ChangeExtension(scriptPath, ".gds")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
