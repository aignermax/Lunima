using System;
using System.Threading.Tasks;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.WavelengthSpectrum;

public class WavelengthSpectrumViewModelTests
{
    private static WavelengthSpectrumViewModel CreateVm()
    {
        var vm = new WavelengthSpectrumViewModel { AutoRefreshDelay = TimeSpan.Zero };
        vm.Configure(new DesignCanvasViewModel());
        return vm;
    }

    [Fact]
    public void ParameterChange_BeforeFirstSweep_DoesNotAutoRefresh()
    {
        var vm = CreateVm();

        vm.StartNm = 1400;

        vm.PendingAutoRefresh.ShouldBeNull();
    }

    [Fact]
    public async Task ParameterChange_AfterFirstSweep_ReRunsAutomatically()
    {
        var vm = CreateVm();
        vm.HasResult = true; // simulate a completed first sweep

        vm.EndNm = 1650;

        vm.PendingAutoRefresh.ShouldNotBeNull();
        await vm.PendingAutoRefresh!;
        // The empty canvas makes the auto-triggered sweep report "no circuit" —
        // proof that the sweep pipeline actually re-ran on the parameter change.
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task EachParameter_TriggersAutoRefresh()
    {
        var vm = CreateVm();
        vm.HasResult = true;

        vm.StartNm = 1400;
        var first = vm.PendingAutoRefresh;
        first.ShouldNotBeNull();
        await first!;

        vm.StepCount = 50;
        var second = vm.PendingAutoRefresh;
        second.ShouldNotBeNull();
        await second!;
    }

    [Fact]
    public async Task RunSweep_InvalidRange_ReportsValidationError()
    {
        var vm = CreateVm();
        vm.StartNm = 1600;
        vm.EndNm = 1500;

        await vm.RunSweepCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldContain("wavelength");
    }

    [Fact]
    public async Task RunSweep_EmptyCanvas_ReportsNoCircuit()
    {
        var vm = CreateVm();

        await vm.RunSweepCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldNotBeNullOrEmpty();
        vm.IsSweeping.ShouldBeFalse();
    }

    [Fact]
    public async Task CoherentToggle_On_SetsCanvasFlag_AndReRunsSweepAsynchronously()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new WavelengthSpectrumViewModel { AutoRefreshDelay = TimeSpan.Zero };
        vm.Configure(canvas);
        vm.HasResult = true; // simulate a completed first sweep

        vm.IsCoherentInterference = true;

        canvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue(
            "the toggle drives the canvas connection manager (#1333)");
        vm.PendingAutoRefresh.ShouldNotBeNull("flipping the toggle re-runs the sweep");
        await vm.PendingAutoRefresh!;
        // The empty canvas makes the auto-triggered sweep report "no circuit" —
        // proof the sweep pipeline actually re-ran — and IsSweeping reset, so the
        // UI thread was never blocked waiting for the result.
        vm.StatusText.ShouldNotBeNullOrEmpty();
        vm.IsSweeping.ShouldBeFalse();
    }

    [Fact]
    public void CoherentToggle_BeforeFirstSweep_SetsFlagWithoutSweeping()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new WavelengthSpectrumViewModel { AutoRefreshDelay = TimeSpan.Zero };
        vm.Configure(canvas);

        vm.IsCoherentInterference = true;

        canvas.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeTrue();
        vm.PendingAutoRefresh.ShouldBeNull("no surprise sweep before the first manual run");
    }

    [Fact]
    public void Configure_SyncsToggleFromCanvas_WithoutSweeping()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
        var vm = new WavelengthSpectrumViewModel();

        vm.Configure(canvas);

        vm.IsCoherentInterference.ShouldBeTrue(
            "a design loaded with the flag on shows the toggle on");
        vm.PendingAutoRefresh.ShouldBeNull();
    }

    [Fact]
    public void SyncCoherentToggleFromCanvas_UpdatesToggle_WithoutSweeping()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new WavelengthSpectrumViewModel { AutoRefreshDelay = TimeSpan.Zero };
        vm.Configure(canvas);
        vm.HasResult = true; // a sweep result exists from before the load

        // Simulate a .lun load restoring the flag (canvas instance survives loads).
        canvas.ConnectionManager.EnableCoherentPropagationPhase = true;
        vm.SyncCoherentToggleFromCanvas();

        vm.IsCoherentInterference.ShouldBeTrue("the toggle follows the restored flag");
        vm.PendingAutoRefresh.ShouldBeNull(
            "loading a design must not kick off a surprise simulation");
    }
}
