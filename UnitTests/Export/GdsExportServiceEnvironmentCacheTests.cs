using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// An export reuses the last READY environment probe of the same interpreter instead of
/// spawning python again; anything that could have changed the answer probes afresh.
/// </summary>
public class GdsExportServiceEnvironmentCacheTests : IDisposable
{
    private readonly string _script = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_script);
        File.Delete(Path.ChangeExtension(_script, ".gds"));
    }

    [Fact]
    public async Task ReadyProbe_ThenExport_DoesNotProbeAgain()
    {
        var service = new InstantEnvironmentGdsExportService(ready: true);
        await service.CheckPythonEnvironmentAsync();

        await service.ExportToGdsAsync(_script, generateGds: true);

        service.ProbeCount.ShouldBe(1);
    }

    [Fact]
    public async Task NotReadyProbe_ThenExport_ProbesAgain()
    {
        var service = new InstantEnvironmentGdsExportService(ready: false);
        await service.CheckPythonEnvironmentAsync();

        var result = await service.ExportToGdsAsync(_script, generateGds: true);

        service.ProbeCount.ShouldBe(2, "a missing Nazca may have been installed since");
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangedInterpreter_DropsTheReadyResult()
    {
        var service = new InstantEnvironmentGdsExportService(ready: true);
        await service.CheckPythonEnvironmentAsync();
        service.SetCustomPythonPath(Path.Combine(Path.GetTempPath(), "other-python"));
        service.Ready = false;

        var result = await service.ExportToGdsAsync(_script, generateGds: true);

        service.ProbeCount.ShouldBe(2);
        result.Success.ShouldBeFalse("the new interpreter has no Nazca");
    }

    [Fact]
    public async Task ExplicitCheck_AlwaysProbesFresh()
    {
        var service = new InstantEnvironmentGdsExportService(ready: true);
        await service.CheckPythonEnvironmentAsync();
        service.Ready = false;

        (await service.CheckPythonEnvironmentAsync()).IsReady.ShouldBeFalse();
        service.ProbeCount.ShouldBe(2);
    }
}
