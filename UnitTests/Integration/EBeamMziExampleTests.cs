using System.Collections.ObjectModel;
using System.Numerics;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Analysis.WavelengthSpectrum;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// The shipped <c>EBeam Mach-Zehnder Interferometer.lun</c> example (issue #1310,
/// openEBL gap #1): a Mach-Zehnder built only from the bundled SiEPIC EBeam PDK
/// (2× <c>ebeam_gc_te1550</c>, 2× <c>ebeam_y_1550</c>), laid out to openEBL's
/// design-for-test rules — grating couplers at 0° in a vertical 127 µm-pitch
/// array, everything inside the 605 × 410 µm die — routed with zero blocked
/// wires and a clean DRC-lite, and simulating with a wavelength-dependent
/// response across 1500–1600 nm.
/// </summary>
public class EBeamMziExampleTests
{
    private const string ExampleFileName = "EBeam Mach-Zehnder Interferometer.lun";
    private const string EBeamPdkName = "SiEPIC EBeam PDK";
    private const double OpenEblPitchMicrometers = 127.0;
    private const double PitchToleranceMicrometers = 0.01;
    private const double DieWidthMicrometers = 605.0;
    private const double DieHeightMicrometers = 410.0;
    private const double MinArmLengthDifferenceMicrometers = 40.0;
    private const double MinWavelengthVariationRatio = 2.0;

    [Fact]
    public async Task LoadsThroughRealLoadPath_WithOnlyEBeamPdkComponents()
    {
        var (canvas, _, errorConsole) = await LoadExample();

        canvas.Components.Count.ShouldBe(4);
        canvas.Connections.Count.ShouldBe(4);
        foreach (var compVm in canvas.Components)
        {
            compVm.TemplatePdkSource.ShouldBe(EBeamPdkName,
                $"'{compVm.Component.Identifier}' must come from the bundled EBeam PDK");
        }
        errorConsole.Entries.ShouldBeEmpty("the shipped example must load without errors");
    }

    [Fact]
    public async Task GratingCouplers_FormVerticalArrayAtOpeneblPitch()
    {
        var (canvas, _, _) = await LoadExample();
        var gcIn = FindComponent(canvas, "gc_in");
        var gcOut = FindComponent(canvas, "gc_out");

        gcIn.Rotation90CounterClock.ShouldBe(DiscreteRotation.R0, "openEBL DFT: GCs at 0° orientation");
        gcOut.Rotation90CounterClock.ShouldBe(DiscreteRotation.R0, "openEBL DFT: GCs at 0° orientation");

        gcIn.PhysicalX.ShouldBe(gcOut.PhysicalX, PitchToleranceMicrometers);
        (gcOut.PhysicalY - gcIn.PhysicalY).ShouldBe(OpenEblPitchMicrometers, PitchToleranceMicrometers);

        var (inX, inY) = FindPin(gcIn, "port 2").GetAbsolutePosition();
        var (outX, outY) = FindPin(gcOut, "port 2").GetAbsolutePosition();
        inX.ShouldBe(outX, PitchToleranceMicrometers);
        (outY - inY).ShouldBe(OpenEblPitchMicrometers, PitchToleranceMicrometers);
    }

    [Fact]
    public async Task ComponentsAndRoutes_FitInsideOpenEblDie()
    {
        var (canvas, _, _) = await LoadExample();

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var compVm in canvas.Components)
        {
            minX = Math.Min(minX, compVm.X);
            minY = Math.Min(minY, compVm.Y);
            maxX = Math.Max(maxX, compVm.X + compVm.Width);
            maxY = Math.Max(maxY, compVm.Y + compVm.Height);
        }
        foreach (var segment in canvas.Connections.SelectMany(c => c.Connection.RoutedPath!.Segments))
        {
            minX = Math.Min(minX, Math.Min(segment.StartPoint.X, segment.EndPoint.X));
            minY = Math.Min(minY, Math.Min(segment.StartPoint.Y, segment.EndPoint.Y));
            maxX = Math.Max(maxX, Math.Max(segment.StartPoint.X, segment.EndPoint.X));
            maxY = Math.Max(maxY, Math.Max(segment.StartPoint.Y, segment.EndPoint.Y));
        }

        (maxX - minX).ShouldBeLessThanOrEqualTo(DieWidthMicrometers);
        (maxY - minY).ShouldBeLessThanOrEqualTo(DieHeightMicrometers);
    }

    [Fact]
    public async Task EveryConnectionRouted_ZeroBlockedWires()
    {
        var (canvas, fileOps, _) = await LoadExample();
        await fileOps.PostLoadRouting;

        foreach (var connVm in canvas.Connections)
        {
            var route = connVm.Connection.RoutedPath;
            route.ShouldNotBeNull("every wire must carry a route after load");
            connVm.Connection.IsBlockedFallback.ShouldBeFalse("no blocked wires");
            route!.IsValid.ShouldBeTrue("route must be valid");
            route.IsPlaceholderGeometry.ShouldBeFalse("route must be real geometry");
        }
    }

    [Fact]
    public async Task Arms_HaveVisibleLengthDifference()
    {
        var (canvas, _, _) = await LoadExample();
        var upperArm = FindConnection(canvas, "mzi_splitter", "port 2");
        var lowerArm = FindConnection(canvas, "mzi_splitter", "port 3");

        (lowerArm.PathLengthMicrometers - upperArm.PathLengthMicrometers)
            .ShouldBeGreaterThanOrEqualTo(MinArmLengthDifferenceMicrometers);
        lowerArm.TargetLengthMicrometers.ShouldNotBeNull(
            "the stretched arm carries its meander length intent");
    }

    [Fact]
    public async Task DrcLite_ZeroIssues()
    {
        var (canvas, _, _) = await LoadExample();
        var connections = canvas.ConnectionManager.Connections;
        var components = canvas.Components.Select(c => c.Component).ToList();
        var externalPortPins = new[]
        {
            FindPin(FindComponent(canvas, "gc_in"), "port 1"),
            FindPin(FindComponent(canvas, "gc_out"), "port 1"),
        };

        var validator = new DesignValidator();
        validator.Validate(connections, components, externalPortPins)
            .ShouldBeEmpty("DRC-lite must report zero issues");
        validator.ValidateComponentBounds(components, DieWidthMicrometers, DieHeightMicrometers)
            .ShouldBeEmpty("the die fits openEBL's 605 x 410 µm");
    }

    [Fact]
    public async Task Sweep_OutputCouplerReceivesWavelengthDependentPower()
    {
        var (canvas, _, _) = await LoadExample();
        canvas.ConnectionManager.RecalculateAllTransmissions(null, CancellationToken.None);

        var inputPin = FindPin(FindComponent(canvas, "gc_in"), "port 1");
        var receivedPin = FindPin(FindComponent(canvas, "gc_out"), "port 2");
        var fiberOutPin = FindPin(FindComponent(canvas, "gc_out"), "port 1");

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("laser", LaserType.Red, 0, new Complex(1.0, 0)),
            inputPin.LogicalPin!.IDInFlow);

        var tileManager = new ComponentListTileManager();
        foreach (var compVm in canvas.Components)
            tileManager.AddComponent(compVm.Component);
        var grid = GridManager.CreateForSimulation(tileManager, canvas.ConnectionManager, portManager);
        var sweeper = new WavelengthSweeper(new SystemMatrixBuilder(grid), portManager);
        var sweep = await sweeper.RunSweepAsync(new WavelengthSweepConfiguration(1500, 1600, 11), grid);

        var received = sweep.GetInsertionLossSeriesForPin(receivedPin.LogicalPin!.IDInFlow)
            .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray();
        var fiberOut = sweep.GetInsertionLossSeriesForPin(fiberOutPin.LogicalPin!.IDOutFlow)
            .Select(TransmissionSpectrumBuilder.DbToLinear).ToArray();

        foreach (var power in received)
            power.ShouldBeGreaterThan(1e-6, "the output coupler receives power at every wavelength");
        foreach (var power in fiberOut)
            power.ShouldBeGreaterThan(1e-6, "the fiber-side port carries the coupled-out wave");

        (received.Max() / received.Min()).ShouldBeGreaterThan(MinWavelengthVariationRatio,
            "the response is wavelength-dependent across 1500–1600 nm");
        (fiberOut.Max() / fiberOut.Min()).ShouldBeGreaterThan(MinWavelengthVariationRatio);
    }

    private static Component FindComponent(DesignCanvasViewModel canvas, string identifier) =>
        canvas.Components.Single(c => c.Component.Identifier == identifier).Component;

    private static PhysicalPin FindPin(Component component, string pinName) =>
        component.PhysicalPins.Single(p => p.Name == pinName);

    private static CAP_Core.Components.Connections.WaveguideConnection FindConnection(
        DesignCanvasViewModel canvas, string startComponentId, string startPinName) =>
        canvas.Connections.Single(c =>
            c.Connection.StartPin?.ParentComponent.Identifier == startComponentId
            && c.Connection.StartPin?.Name == startPinName).Connection;

    private static async Task<(DesignCanvasViewModel Canvas, FileOperationsViewModel FileOps, ErrorConsoleService ErrorConsole)>
        LoadExample()
    {
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var fileOps = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: errorConsole);
        fileOps.FileDialogService = new Mock<IFileDialogService>().Object;
        fileOps.ApplyChipSizeAfterLoad = (widthUm, heightUm) =>
        {
            canvas.ChipMinX = 0;
            canvas.ChipMinY = 0;
            canvas.ChipMaxX = widthUm;
            canvas.ChipMaxY = heightUm;
            canvas.InitializeAStarRouting(0, 0, widthUm, heightUm);
        };

        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
            $"'{ExampleFileName}' must load through the real load path");
        return (canvas, fileOps, errorConsole);
    }
}
