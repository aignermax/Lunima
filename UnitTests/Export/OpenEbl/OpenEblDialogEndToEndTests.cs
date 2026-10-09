using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Issue #1376 — end-to-end proof of the path a student actually clicks: the
/// <em>DI-wired</em> <see cref="OpenEblCheckViewModel"/> (resolved from the real production
/// container, <c>App.ConfigureServices</c> — no hand-constructed dialog services) over a
/// shipped example loaded through the real load path, running the real
/// <c>SimpleNazcaExporter</c> + <c>GdsExportService</c> (temp dir, nazca) and the real
/// <c>OpenEblSubmissionChecker</c> resolving <c>scripts/openebl</c> next to the binary —
/// for both the EBeam Mach-Zehnder Interferometer and the EBeam Add-Drop Ring.
/// <para>
/// <c>FileOperationsViewModel</c> itself is owned by <c>MainViewModel</c> and not registered
/// in the container, so it is composed here exactly the way <c>MainViewModel</c>'s
/// constructor composes it — but every dependency is the container's singleton, and the
/// canvas it loads into IS the instance the resolved dialog ViewModel exports from.
/// </para>
/// <para>
/// Gating: needs a Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (installed
/// on the CI runner by the "Install Nazca + KLayout + SiEPIC PDK" step); skips cleanly
/// elsewhere. Same gate as <see cref="OpenEblEBeamMziVerificationTests"/>. The negative
/// empty-canvas case needs no toolchain and always runs.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class OpenEblDialogEndToEndTests
{
    private const double MaxDieWidthMicrometers = 605.0;
    private const double MaxDieHeightMicrometers = 410.0;
    private const string Username = "jsmith";
    private const string DesignName = "MZI v2";
    private const string ExpectedFileName = "EBeam_jsmith_MZI_v2.gds";

    /// <summary>Mirror of the dialog's export target directory (private const in the VM).</summary>
    private static readonly string DialogTempDirectory =
        Path.Combine(Path.GetTempPath(), "Lunima", "openEBL");

    public OpenEblDialogEndToEndTests()
    {
        // Status/headline assertions depend on the process-wide locale.
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    [SkippableFact]
    public Task EBeamMziExample_ExportAndCheckThroughDiWiredDialog_Passes() =>
        RunGatedJourneyAsync("EBeam Mach-Zehnder Interferometer.lun");

    [SkippableFact]
    public Task EBeamAddDropRingExample_ExportAndCheckThroughDiWiredDialog_Passes() =>
        RunGatedJourneyAsync("EBeam Add-Drop Ring.lun");

    // One gated test per example: a single test running both would exceed the 120 s
    // wall-clock budget (a full journey is ~80 s here).
    private static async Task RunGatedJourneyAsync(string exampleFileName)
    {
        var python = await OpenEblMziReadinessTests.FindOpenEblVerificationPythonAsync();
        Skip.If(python == null,
            "No Python with nazca + klayout + siepic_ebeam_pdk + SiEPIC (expected on CI).");

        await RunDialogJourneyAsync(exampleFileName);
    }

    [Fact]
    public async Task EmptyCanvas_ExportAndCheck_ReportsNothingToExportAndNeverTouchesTheToolchain()
    {
        var preferencesPath = NewTempPreferencesPath();
        using var provider = ProductionContainerTestHelper.BuildWithTempPreferences(preferencesPath);
        try
        {
            var vm = provider.GetRequiredService<OpenEblCheckViewModel>();
            vm.FileName = "EBeam_negative_probe.gds";

            await vm.ExportAndCheckCommand.ExecuteAsync(null);

            vm.StatusText.ShouldBe("Nothing to check — add components to the design first.");
            vm.HasResult.ShouldBeFalse();
            vm.IsChecking.ShouldBeFalse();
            // The real export service and checker were wired in — neither may have run:
            // no export script and no GDS for the probe stem may exist in the temp dir.
            File.Exists(Path.Combine(DialogTempDirectory, "EBeam_negative_probe.py")).ShouldBeFalse();
            File.Exists(Path.Combine(DialogTempDirectory, "EBeam_negative_probe.gds")).ShouldBeFalse();
        }
        finally
        {
            try { File.Delete(preferencesPath); } catch (IOException) { /* best effort */ }
        }
    }

    private static async Task RunDialogJourneyAsync(string exampleFileName)
    {
        var preferencesPath = NewTempPreferencesPath();
        using var provider = ProductionContainerTestHelper.BuildWithTempPreferences(preferencesPath);
        try
        {
            var canvas = provider.GetRequiredService<DesignCanvasViewModel>();
            var leftPanel = provider.GetRequiredService<LeftPanelViewModel>();
            // Production startup step (MainViewModel's constructor): loads the bundled PDKs.
            leftPanel.Initialize();

            var fileOperations = ComposeFileOperations(provider, canvas, leftPanel);
            var examplePath = Path.Combine(
                ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
            (await fileOperations.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue(
                $"'{exampleFileName}' must load through the real load path");
            await fileOperations.PostLoadRouting;
            canvas.Components.Count.ShouldBeGreaterThan(0);

            var vm = provider.GetRequiredService<OpenEblCheckViewModel>();
            vm.PrepareForOpen(Path.GetFileNameWithoutExtension(fileOperations.CurrentFilePath));
            vm.Username = Username;
            vm.DesignName = DesignName;
            vm.FileName.ShouldBe(ExpectedFileName);

            await vm.ExportAndCheckCommand.ExecuteAsync(null);

            vm.IsChecking.ShouldBeFalse();
            vm.HasResult.ShouldBeTrue();
            vm.IsToolchainMissing.ShouldBeFalse(
                "the gate probed a full toolchain, so the DI checker must find one too");
            vm.Headline.ShouldBe("All checks passed — ready for openEBL submission",
                $"detail: {vm.DetailMessage}\nstatus: {vm.StatusText}\n" +
                $"errors: {string.Join(" | ", vm.Errors.Select(e => e.Message))}");
            vm.SubmissionPassed.ShouldBeTrue();
            vm.VerificationPassed.ShouldBeTrue();
            vm.Errors.ShouldBeEmpty();

            var (width, height) = ParseDieSize(vm.DieSizeText);
            width.ShouldBeLessThanOrEqualTo(MaxDieWidthMicrometers);
            height.ShouldBeLessThanOrEqualTo(MaxDieHeightMicrometers);

            var gdsPath = Path.Combine(DialogTempDirectory, ExpectedFileName);
            vm.GdsPathText.ShouldContain(gdsPath);
            File.Exists(gdsPath).ShouldBeTrue("the checked GDS must exist at the displayed path");
        }
        finally
        {
            try { File.Delete(preferencesPath); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>
    /// Composes <see cref="FileOperationsViewModel"/> from the container's singletons exactly
    /// as <c>MainViewModel</c>'s constructor does (it owns the instance; the type is not
    /// DI-registered itself).
    /// </summary>
    private static FileOperationsViewModel ComposeFileOperations(
        ServiceProvider provider, DesignCanvasViewModel canvas, LeftPanelViewModel leftPanel) =>
        new(
            canvas,
            provider.GetRequiredService<CommandManager>(),
            provider.GetRequiredService<SimpleNazcaExporter>(),
            provider.GetRequiredService<CAP_Core.Export.SaxExporter>(),
            leftPanel.AllTemplates,
            provider.GetRequiredService<GdsExportViewModel>(),
            provider.GetRequiredService<PhotonTorchExportViewModel>(),
            provider.GetRequiredService<VerilogAExportViewModel>(),
            provider.GetRequiredService<ErrorConsoleService>(),
            provider.GetRequiredService<UserSMatrixOverrideStore>());

    /// <summary>Parses the VM's invariant "{w} × {h} µm" die-size text.</summary>
    private static (double Width, double Height) ParseDieSize(string dieSizeText)
    {
        var parts = dieSizeText.Replace(" µm", string.Empty, StringComparison.Ordinal).Split('×');
        parts.Length.ShouldBe(2, $"die-size text has an unexpected format: '{dieSizeText}'");
        return (double.Parse(parts[0].Trim(), CultureInfo.InvariantCulture),
                double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
    }

    private static string NewTempPreferencesPath() =>
        Path.Combine(Path.GetTempPath(), $"lunima-openebl-e2e-{Guid.NewGuid():N}.json");
}
