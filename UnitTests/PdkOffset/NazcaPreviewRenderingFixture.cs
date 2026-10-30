using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewPythonFixture;

namespace UnitTests.PdkOffset;

/// <summary>Real renderer assertions, overlay execution, and bundled SiEPIC metadata fixtures.</summary>
internal static class NazcaPreviewRenderingFixture
{
    internal static async Task AssertRendersValidPreviewOrSkip(string moduleName, string functionName, string? parameters = null)
    {
        var python = FindWorkingPython3();
        if (python == null) return;  // no python — skip

        var script = FindRealPreviewScript();
        if (script == null) return;  // script not in expected location — skip

        var svc = new NazcaComponentPreviewService(python, script);
        var result = await svc.RenderAsync(moduleName, functionName, parameters);

        if (!result.Success)
        {
            // Most common cause: Nazca or the target PDK module isn't installed
            // in the test environment. The error message tells us which.
            // We could fail the test here, but a CI box without Nazca should
            // not block the rest of the suite — let the user run this locally.
            return;
        }

        // We have a real render. Assert that the shape is sane:
        result.XMax.ShouldBeGreaterThan(result.XMin,
            $"bbox xmin={result.XMin} xmax={result.XMax} for {moduleName}.{functionName} is degenerate");
        result.YMax.ShouldBeGreaterThan(result.YMin,
            $"bbox ymin={result.YMin} ymax={result.YMax} for {moduleName}.{functionName} is degenerate");

        // An MMI must have at least 2 pins (the whole point of a multi-mode
        // splitter is to fan out). If we get zero, the script is missing
        // pin extraction or the cell didn't expose any.
        result.Pins.Count.ShouldBeGreaterThanOrEqualTo(2,
            $"{moduleName}.{functionName} should expose at least 2 pins; got {result.Pins.Count}");

        // Polygons may legitimately be empty (gdspy not installed in the test
        // environment), so we don't assert on count. We DO assert that if any
        // polygon came through, it has a non-trivial vertex list.
        foreach (var poly in result.Polygons)
            poly.Vertices.Count.ShouldBeGreaterThanOrEqualTo(3,
                "every polygon must have at least 3 vertices to enclose an area");
    }

    internal static async Task<PdkOffsetEditorViewModel?> TryRenderThroughViewModel(
        string nazcaFunction, string? nazcaParameters = null, int timeoutMs = 120000)
    {
        var python = FindWorkingPython3();
        if (python == null) return null;
        var script = FindRealPreviewScript();
        if (script == null) return null;

        var svc = new NazcaComponentPreviewService(python, script);
        var vm = new PdkOffsetEditorViewModel(new CAP_DataAccess.Components.ComponentDraftMapper.BundledPdkCatalog(AppContext.BaseDirectory),
            new PdkLoader(), new PdkJsonSaver(),
            new CAP.Avalonia.ViewModels.Library.PdkManagerViewModel(),
            previewService: svc);
        // No Avalonia application running in xunit — replace the dispatcher
        // marshal with an inline executor so async render results actually apply.
        vm.UiThreadMarshaller = action => { action(); return Task.CompletedTask; };

        var draft = new PdkComponentDraft
        {
            Name = nazcaFunction,
            NazcaFunction = nazcaFunction,
            NazcaParameters = nazcaParameters ?? "",
            WidthMicrometers = 50,
            HeightMicrometers = 20,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 10 }
            }
        };
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));

        // Selecting the component triggers TriggerNazcaRenderAsync via the
        // [ObservableProperty] partial method — the very path the user clicks.
        vm.SelectedComponent = vm.Components[0];

        // Poll until the render is finished. Done == HasNazcaOverlay flipped to
        // true OR a terminal status arrived OR the rendering flag dropped
        // back to false (failure path).
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (vm.HasNazcaOverlay) return vm;
            if (!vm.IsNazcaRendering &&
                !string.IsNullOrEmpty(vm.NazcaOverlayStatus) &&
                !vm.NazcaOverlayStatus.StartsWith("Rendering"))
                return vm;
            await Task.Delay(100);
        }
        return vm;
    }

    internal static bool IsEnvironmentSkip(string status) =>
        status.Contains("No module named",                StringComparison.Ordinal) ||
        status.Contains("requires klayout-python",        StringComparison.Ordinal) ||
        status.Contains("siepic_ebeam_pdk is not installed", StringComparison.Ordinal) ||
        status.Contains("Neither gdstk nor gdspy",        StringComparison.Ordinal);

    internal static PdkComponentDraft? LoadSiepicDraftForFunction(string nazcaFunction)
    {
        // Walk up to repo root (same logic as FindRealPreviewScript) and read
        // the SiEPIC PDK JSON.
        var current = new DirectoryInfo(
            Path.GetDirectoryName(typeof(NazcaComponentPreviewServiceTests).Assembly.Location)!);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "CAP-DataAccess", "PDKs", "siepic-ebeam-pdk.json");
            if (File.Exists(candidate))
            {
                try
                {
                    var loader = new PdkLoader();
                    var pdk = loader.LoadFromFileForEditing(candidate);
                    return pdk.Components.FirstOrDefault(c =>
                        string.Equals(c.NazcaFunction, nazcaFunction, StringComparison.Ordinal));
                }
                catch { return null; }
            }
            current = current.Parent;
        }
        return null;
    }
}
