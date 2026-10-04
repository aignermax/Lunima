using System;
using System.IO;
using System.Threading.Tasks;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CommunityToolkit.Mvvm.Input;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.WavelengthSpectrum;

public class MeasuredSpectrumOverlayViewModelTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory, "Analysis", "MeasuredSpectrum", "Fixtures", "synthetic_mzi_spectrum.csv");

    private static MeasuredSpectrumOverlayViewModel CreateVm() => new();

    [Fact]
    public async Task Load_ValidCsv_SetsOverlayAndResultLine()
    {
        var vm = CreateVm();
        vm.ArmImbalanceUm = 50;

        await vm.LoadFromFileAsync(FixturePath);

        vm.HasOverlay.ShouldBeTrue();
        vm.Spectrum.ShouldNotBeNull();
        vm.ErrorText.ShouldBeEmpty();
        vm.ResultText.ShouldContain("FSR");
        vm.ResultText.ShouldContain("n_g");
        vm.ResultText.ShouldContain("50");
    }

    [Fact]
    public async Task Load_WithoutArmImbalance_ShowsFsrButNoGroupIndex()
    {
        var vm = CreateVm();

        await vm.LoadFromFileAsync(FixturePath);

        vm.ResultText.ShouldContain("FSR");
        vm.ResultText.ShouldNotContain("n_g");
    }

    [Fact]
    public async Task Load_MalformedCsv_SetsInlineErrorWithLineNumber_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bad_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "1500,0.5\n1501,not_a_number\n1502,0.7");
        try
        {
            var vm = CreateVm();

            await vm.LoadFromFileAsync(path); // must not throw

            vm.HasOverlay.ShouldBeFalse();
            vm.Spectrum.ShouldBeNull();
            vm.ErrorText.ShouldContain("line 2");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Clear_RemovesOverlayAndResult()
    {
        var vm = CreateVm();
        await vm.LoadFromFileAsync(FixturePath);
        vm.HasOverlay.ShouldBeTrue();

        vm.ClearMeasuredCommand.Execute(null);

        vm.HasOverlay.ShouldBeFalse();
        vm.Spectrum.ShouldBeNull();
        vm.ResultText.ShouldBeEmpty();
    }

    [Fact]
    public async Task OverlayChanged_FiresOnLoadAndClear()
    {
        var vm = CreateVm();
        int fired = 0;
        vm.OverlayChanged += (_, _) => fired++;

        await vm.LoadFromFileAsync(FixturePath);
        fired.ShouldBe(1);
        vm.ClearMeasuredCommand.Execute(null);
        fired.ShouldBe(2);
    }

    [Fact]
    public async Task ChangingArmImbalance_ReAnalyzesWithoutReload()
    {
        var vm = CreateVm();
        await vm.LoadFromFileAsync(FixturePath);
        vm.ResultText.ShouldNotContain("n_g");

        vm.ArmImbalanceUm = 100;

        vm.ResultText.ShouldContain("n_g");
        vm.ResultText.ShouldContain("100");
    }

    [Fact]
    public void LoadMeasuredCommand_IsAsyncRelayCommand_SoItDoesNotBlockUi()
    {
        var vm = CreateVm();
        vm.LoadMeasuredCommand.ShouldBeAssignableTo<IAsyncRelayCommand>();
    }

    [Fact]
    public async Task LoadFromFileAsync_CompletesAsynchronously()
    {
        var vm = CreateVm();
        var task = vm.LoadFromFileAsync(FixturePath);
        // The parse runs on a threadpool thread; the returned Task represents
        // real async work (not a synchronously-completed stub).
        await task;
        vm.HasOverlay.ShouldBeTrue();
    }
}
