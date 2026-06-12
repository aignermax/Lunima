using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;

namespace UnitTests.Integration;

/// <summary>
/// Environment resolution and override setup shared by the alignment test matrix
/// (<see cref="GdsExportAlignmentTests"/>). Uses the same env-skip pattern and
/// editor-VM flow as <see cref="NazcaOverrideFullFlowIntegrationTests"/>.
/// </summary>
internal static class GdsAlignmentTestSetup
{
    /// <summary>Resolves (nazca-capable python, preview script path), or nulls to skip.</summary>
    public static async Task<(string? Python, string? Script)> ResolveEnvironmentAsync()
    {
        var python = await new PythonDiscoveryService().FindFirstNazcaPythonPathAsync();
        return (python, FindRealPreviewScript());
    }

    /// <summary>Pastes the showcase code, runs a REAL preview and applies the override.</summary>
    public static async Task<Dictionary<string, NazcaCodeOverride>> ApplyShowcaseOverrideAsync(
        DesignCanvasViewModel canvas, Component dut, string python, string previewScript)
    {
        var store = new Dictionary<string, NazcaCodeOverride>();
        var vm = new InstanceNazcaCodeEditorViewModel(
            componentKey: dut.Identifier,
            storedOverrides: store,
            liveComponent: dut,
            moduleName: dut.NazcaModuleName,
            nazcaFunction: dut.NazcaFunctionName ?? string.Empty,
            nazcaParameters: dut.NazcaFunctionParameters,
            templateCode: NazcaCodeTemplateBuilder.Build(
                dut.NazcaModuleName, dut.NazcaFunctionName, dut.NazcaFunctionParameters),
            previewService: new NazcaComponentPreviewService(python, previewScript),
            onPinsChanged: _ => canvas.OnComponentPinsChanged(dut));
        await vm.InitializeAsync();

        vm.Code = NazcaCodeExamples.Complex;
        await vm.RunPreviewCommand.ExecuteAsync(null);
        vm.IsValid.ShouldBeTrue($"showcase preview must render. Error: {vm.PreviewError}");
        vm.ApplyOverrideCommand.Execute(null);

        store[dut.Identifier].OverrideBboxXMinMicrometers.ShouldNotBeNull(
            "Apply must persist the bbox anchor — the mapper needs it for placement");
        return store;
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root and returns the path to
    /// <c>scripts/render_component_preview.py</c>, or null if not found. Shared by the
    /// nazca-preview tests so the lookup lives in one place (issue #565).
    /// </summary>
    internal static string? FindRealPreviewScript()
    {
        const string scriptName = "render_component_preview.py";
        var current = new DirectoryInfo(
            Path.GetDirectoryName(typeof(GdsAlignmentTestSetup).Assembly.Location)!);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", scriptName);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return null;
    }
}
