using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Contracts.Logger;
using CAP_Core;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.OnaAnalysis;

/// <summary>
/// An imported GDS design brings pin-less cells along (logos, pads — geometry only, no light
/// path). The wavelength sweeps behind the spectrum, Monte-Carlo and eye analyses interpolate
/// every component's S-matrix between wavelength stops; a pin-less cell's empty matrix must not
/// break them. Built from synthetic cells: an imported one-pin edge coupler marked as the light
/// source, a waveguide, and a pin-less geometry cell.
/// </summary>
public class GeometryOnlyComponentSweepTests
{
    private const string ImportPdk = "GDS Import";

    [Fact]
    public async Task Sweeps_WithAPinlessGeometryCellOnTheCanvas_RunWithoutErrors()
    {
        var canvas = BuildCanvas();
        var console = new ErrorConsoleService();

        var spectrum = new WavelengthSpectrumViewModel(console);
        spectrum.Configure(canvas);
        await spectrum.RunSweepCommand.ExecuteAsync(null);
        var monteCarlo = new MonteCarloViewModel(console);
        monteCarlo.Configure(canvas);
        await monteCarlo.RunAnalysisCommand.ExecuteAsync(null);
        var eye = new EyeDiagramViewModel(console);
        eye.Configure(canvas);
        await eye.RunEyeAnalysisCommand.ExecuteAsync(null);

        console.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.ToString()).ShouldBeEmpty();
        spectrum.HasResult.ShouldBeTrue(spectrum.StatusText);
    }

    private static DesignCanvasViewModel BuildCanvas()
    {
        var canvas = new DesignCanvasViewModel();
        var edgeCoupler = ImportedCell("edge_coupler", new DetectedPin
            { Name = "o1", XUm = 50, YUm = 10, AngleDegrees = 0, Source = DetectedPinSource.Label });
        var edgeVm = canvas.AddComponent(ComponentTemplates.CreateFromTemplate(edgeCoupler, 0, 0), edgeCoupler.Name, ImportPdk);
        edgeVm.IsUserMarkedLightSource = true;

        var straight = TestPdkLoader.LoadAllTemplates().First(t => t.PdkSource == "Demo PDK" && t.Name.Contains("Straight"));
        var waveguide = ComponentTemplates.CreateFromTemplate(straight, 200, 0);
        canvas.AddComponent(waveguide, straight.Name, "Demo PDK");
        canvas.ConnectPins(edgeVm.Component.PhysicalPins[0], waveguide.PhysicalPins[0]);

        var logo = ImportedCell("logo");
        canvas.AddComponent(ComponentTemplates.CreateFromTemplate(logo, 0, 300), logo.Name, ImportPdk);
        return canvas;
    }

    private static ComponentTemplate ImportedCell(string name, params DetectedPin[] pins)
    {
        var cell = new GdsCellDraft { CellName = name, WidthUm = 50, HeightUm = 30, Pins = pins, RawCode = "def component(): pass" };
        return PdkTemplateConverter.ConvertToTemplate(GdsCellDraftMapper.Map(cell, "synthetic.gds"), ImportPdk, null);
    }
}
