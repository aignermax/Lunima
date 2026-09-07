using CAP.Avalonia.Services;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// The Mach-Zehnder "Hello World" example must light up the moment the simulation starts:
/// its grating coupler is the design's light source, so the simulation has to classify it
/// as one — under the example's own identifier and the demo PDK's <c>demo.io</c> function.
/// </summary>
public class MachZehnderExampleTests
{
    private const string ExampleFileName = "Mach-Zehnder Interferometer.lun";
    private const string InputCouplerIdentifier = "mzi_input_coupler";
    private const int ExpectedComponentCount = 7;

    [Fact]
    public async Task Example_HasAnEnabledGratingCouplerTheSimulationTreatsAsLightSource()
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);

        canvas.Components.Count.ShouldBe(ExpectedComponentCount, "splitter, two arms, combiner, two detectors and the input coupler");
        var coupler = canvas.Components.Single(c => c.Component.Identifier == InputCouplerIdentifier);
        coupler.IsLightSource.ShouldBeTrue("the properties panel must offer the laser editor on the coupler");
        SimulationService.IsLightSource(coupler.Component).ShouldBeTrue(
            "the simulation must inject light at the coupler — otherwise pressing L reports no laser switched on");
        coupler.Component.LaserEnabled.ShouldBeTrue("the example ships with the laser on");
        canvas.Components.Count(c => SimulationService.IsLightSource(c.Component)).ShouldBe(1,
            "only the input coupler is a source; the detectors and MMIs are not");
    }
}
