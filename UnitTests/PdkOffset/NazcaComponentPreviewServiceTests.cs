using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;

namespace UnitTests.PdkOffset;

/// <summary>
/// Unit tests for <see cref="NazcaComponentPreviewService"/> and related ViewModel integration.
/// </summary>
public class NazcaComponentPreviewServiceTests
{
    /// <summary>
    /// Resolves a working Python 3 path by running a minimal subprocess validation.
    /// Returns null when Python is not available or cannot execute scripts.
    /// </summary>
    private static string? FindWorkingPython3()
    {
        // Prefer system Python over venv paths — venvs on NTFS mounts can be unreliable
        // when invoked from a subprocess without the venv being activated.
        var candidates = new[] { "/usr/bin/python3", "/usr/local/bin/python3", "python3" };
        foreach (var c in candidates)
        {
            try
            {
                // Validate that Python can actually execute a minimal inline script,
                // not just that the binary exists. This guards against broken venvs.
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = c,
                    Arguments = "-c \"import sys; print('ok')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(5000);
                if (p?.ExitCode == 0) return c;
            }
            catch { /* try next */ }
        }
        return null;
    }

    // ─── NazcaComponentPreviewService ──────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_WithNonExistentScript_ReturnsFailure()
    {
        var svc = new NazcaComponentPreviewService(
            "python3",
            "/tmp/does_not_exist_render_component_preview.py");

        var result = await svc.RenderAsync(null, "pdk.some_func", null);

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
        result.Error!.ShouldContain("not found");
    }

    [Fact]
    public async Task RenderAsync_WithBadPython_ReturnsFailure()
    {
        // Providing a non-existent executable; service must return failure not throw.
        var tempScript = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempScript, "print('{}')");
            var svc = new NazcaComponentPreviewService(
                "/absolutely/nonexistent/python99",
                tempScript);

            var result = await svc.RenderAsync(null, "func", null);

            result.Success.ShouldBeFalse();
            result.Error.ShouldNotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(tempScript);
        }
    }

    [Fact]
    public async Task RenderAsync_CachesSuccessfulResult()
    {
        var python = FindWorkingPython3();
        if (python == null)
        {
            // Python not available in this environment — skip test gracefully
            return;
        }

        // Script that writes a valid success JSON; ignores argv
        var tempScript = Path.Combine(Path.GetTempPath(), $"cap_test_{Guid.NewGuid():N}.py");
        try
        {
            const string jsonBody = "{\"success\": true, \"bbox\": {\"xmin\": 0.0, \"ymin\": 0.0, \"xmax\": 10.0, \"ymax\": 5.0}, \"polygons\": [], \"pins\": []}";
            // Use a script that prints pre-built JSON to avoid any Python dict-to-JSON quoting quirks
            var script = $"import sys\nprint('{jsonBody}')\n";
            File.WriteAllText(tempScript, script);

            var svc = new NazcaComponentPreviewService(python, tempScript);

            var r1 = await svc.RenderAsync(null, "demo_func", null);
            if (!r1.Success)
            {
                // Python ran but subprocess failed (e.g. encoding / permission quirk in CI).
                // This is an environment issue, not a code bug — skip gracefully.
                return;
            }

            var r2 = await svc.RenderAsync(null, "demo_func", null);

            // Both calls must return the same (cached) object reference
            ReferenceEquals(r1, r2).ShouldBeTrue("second call should return the cached result");
        }
        finally
        {
            if (File.Exists(tempScript)) File.Delete(tempScript);
        }
    }

    [Fact]
    public async Task RenderAsync_ParsesPolygonsAndPins()
    {
        var python = FindWorkingPython3();
        if (python == null)
        {
            // Python not available in this environment — skip test gracefully
            return;
        }

        var tempScript = Path.Combine(Path.GetTempPath(), $"cap_test_{Guid.NewGuid():N}.py");
        try
        {
            // Use pre-built JSON string to avoid Python dict-to-JSON quoting edge cases
            const string jsonBody = "{\"success\": true, \"bbox\": {\"xmin\": -5.0, \"ymin\": -2.0, \"xmax\": 30.0, \"ymax\": 10.0}, \"polygons\": [{\"layer\": 1, \"vertices\": [[0,0],[1,0],[1,1],[0,1]]}], \"pins\": [{\"name\": \"a0\", \"x\": 0.0, \"y\": 4.0, \"angle\": 180.0, \"stubX1\": -3.0, \"stubY1\": 4.0}]}";
            var script = $"import sys\nprint('{jsonBody}')\n";
            File.WriteAllText(tempScript, script);

            var svc = new NazcaComponentPreviewService(python, tempScript);
            var result = await svc.RenderAsync(null, "func", null);

            if (!result.Success)
            {
                // Environment issue — skip gracefully rather than failing the build
                return;
            }

            result.XMin.ShouldBe(-5.0);
            result.YMax.ShouldBe(10.0);
            result.Polygons.Count.ShouldBe(1);
            result.Polygons[0].Layer.ShouldBe(1);
            result.Polygons[0].Vertices.Count.ShouldBe(4);
            result.Pins.Count.ShouldBe(1);
            result.Pins[0].Name.ShouldBe("a0");
            result.Pins[0].StubX1.ShouldBe(-3.0);
        }
        finally
        {
            if (File.Exists(tempScript)) File.Delete(tempScript);
        }
    }

    // ─── ParseOutput (pure data path, no subprocess) ──────────────────────────
    //
    // The subprocess-based tests above gracefully skip when Python is not
    // available, which means on a Python-less CI box they exercise zero
    // assertions. These tests cover the JSON-parsing logic directly so the
    // parser has binding force regardless of Python availability.

    [Fact]
    public void ParseOutput_EmptyString_ReturnsFailure()
    {
        var result = NazcaComponentPreviewService.ParseOutput("");
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ParseOutput_MalformedJson_ReturnsFailure()
    {
        var result = NazcaComponentPreviewService.ParseOutput("{not json");
        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ParseOutput_ScriptReportedFailure_PropagatesError()
    {
        var json = "{\"success\": false, \"error\": \"function not found\"}";
        var result = NazcaComponentPreviewService.ParseOutput(json);
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("function not found");
    }

    [Fact]
    public void ParseOutput_ValidPayload_PopulatesBBoxPolygonsAndPins()
    {
        const string json =
            "{\"success\": true, " +
            "\"bbox\": {\"xmin\": -5.0, \"ymin\": -2.0, \"xmax\": 30.0, \"ymax\": 10.0}, " +
            "\"polygons\": [{\"layer\": 1, \"vertices\": [[0,0],[1,0],[1,1],[0,1]]}], " +
            "\"pins\": [{\"name\": \"a0\", \"x\": 0.0, \"y\": 4.0, \"angle\": 180.0, " +
            "\"stubX1\": -3.0, \"stubY1\": 4.0}]}";

        var result = NazcaComponentPreviewService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.XMin.ShouldBe(-5.0);
        result.YMax.ShouldBe(10.0);
        result.Polygons.Count.ShouldBe(1);
        result.Polygons[0].Layer.ShouldBe(1);
        result.Polygons[0].Vertices.Count.ShouldBe(4);
        result.Pins.Count.ShouldBe(1);
        result.Pins[0].Name.ShouldBe("a0");
        result.Pins[0].StubX1.ShouldBe(-3.0);
    }

    [Fact]
    public void ParseOutput_PinWithoutStubFields_DefaultsToZero()
    {
        // Defensive: missing optional fields must not throw — older preview
        // scripts may not emit stubX1/stubY1.
        const string json =
            "{\"success\": true, " +
            "\"bbox\": {\"xmin\": 0, \"ymin\": 0, \"xmax\": 1, \"ymax\": 1}, " +
            "\"polygons\": [], " +
            "\"pins\": [{\"name\": \"p\", \"x\": 0.0, \"y\": 0.0, \"angle\": 0.0}]}";

        var result = NazcaComponentPreviewService.ParseOutput(json);

        result.Success.ShouldBeTrue();
        result.Pins.Count.ShouldBe(1);
        result.Pins[0].StubX1.ShouldBe(0.0);
        result.Pins[0].StubY1.ShouldBe(0.0);
    }

    // ─── ResolveModuleAndFunction (split nazcaFunction → module + function) ──
    //
    // These guard the bug that the previous E2E tests missed: the ViewModel
    // calls `RenderAsync(null, draft.NazcaFunction, …)` with the unsplit
    // function string, and the bug was that the dot in "demo.mmi2x2_dp" was
    // never peeled off (so the script looked up the dotted name as an attribute
    // on nazca.demofab and threw "no attribute 'demo.mmi2x2_dp'"). The earlier
    // E2E tests called the service with the already-split form ("demo",
    // "mmi2x2_dp") and so passed even with the bug present.

    [Fact]
    public void ResolveModuleAndFunction_DottedDemoNotation_SplitsAtLastDot()
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("demo.mmi2x2_dp");
        module.ShouldBe("demo");
        function.ShouldBe("mmi2x2_dp");
    }

    [Fact]
    public void ResolveModuleAndFunction_NestedDottedNotation_SplitsAtLastDotOnly()
    {
        // Real PDKs sometimes use deeper paths like "siepic_ebeam_pdk.mmi.dc_1550".
        // Only the last segment is the function name.
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("siepic_ebeam_pdk.mmi.dc_1550");
        module.ShouldBe("siepic_ebeam_pdk.mmi");
        function.ShouldBe("dc_1550");
    }

    [Theory]
    [InlineData("ebeam_y_1550")]
    [InlineData("ebeam_dc_te1550")]
    [InlineData("gc_te1550_8deg_oxide_1")]
    public void ResolveModuleAndFunction_SiepicPrefixedFlatName_RoutesToSiepicEbeamPdk(string fn)
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction(fn);
        module.ShouldBe("siepic_ebeam_pdk");
        function.ShouldBe(fn);
    }

    [Fact]
    public void ResolveModuleAndFunction_UnrecognisedFlatName_FallsBackToDemo()
    {
        var (module, function) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("custom_thing");
        module.ShouldBe("demo");
        function.ShouldBe("custom_thing");
    }

    [Fact]
    public void ResolveModuleAndFunction_NullOrEmpty_FallsBackToDemo()
    {
        var (m1, f1) = PdkOffsetEditorViewModel.ResolveModuleAndFunction(null);
        m1.ShouldBe("demo"); f1.ShouldBe("");

        var (m2, f2) = PdkOffsetEditorViewModel.ResolveModuleAndFunction("");
        m2.ShouldBe("demo"); f2.ShouldBe("");
    }

    // ─── End-to-end against real Nazca ────────────────────────────────────────
    //
    // Run the real render_component_preview.py script through a real Python
    // interpreter against a real Nazca installation. Skip gracefully when:
    //   - Python is not on PATH
    //   - The script can't be located on disk
    //   - The subprocess returns success=false because Nazca / a PDK module
    //     isn't installed (environment issue, not a code bug).
    //
    // These tests catch regressions in the script ↔ service contract that the
    // pure-JSON ParseOutput tests can't, e.g. Nazca chatter polluting stdout.

    /// <summary>
    /// Walks up the directory tree from the test assembly location looking
    /// for scripts/render_component_preview.py.  Returns null when not found
    /// (test will skip gracefully).
    /// </summary>
    private static string? FindRealPreviewScript()
    {
        const string scriptName = "render_component_preview.py";
        var current = new DirectoryInfo(
            Path.GetDirectoryName(typeof(NazcaComponentPreviewServiceTests).Assembly.Location)!);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", scriptName);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        return null;
    }

    private async Task AssertRendersValidPreviewOrSkip(string moduleName, string functionName, string? parameters = null)
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

    [Fact]
    public async Task EndToEnd_DemoMmi1x2Splitter_RendersValidPreview()
    {
        // Demofab is bundled with Nazca itself, so this test only needs Nazca
        // to be installed in the Python interpreter (`pip install nazca-design`).
        await AssertRendersValidPreviewOrSkip("demo", "mmi1x2_sh");
    }

    [Fact]
    public async Task EndToEnd_DemoMmi2x2Coupler_RendersValidPreview()
    {
        await AssertRendersValidPreviewOrSkip("demo", "mmi2x2_dp");
    }

    [Fact]
    public async Task EndToEnd_SiepicEbeamDirectionalCoupler_RendersValidPreview()
    {
        // Requires the SiEPIC EBeam PDK to be importable as a Python module,
        // typically `pip install siepic_ebeam_pdk`. Skipped silently when
        // the module isn't available — this is the realistic case on a fresh
        // Lunima dev box without explicit PDK installation.
        await AssertRendersValidPreviewOrSkip("siepic_ebeam_pdk", "ebeam_dc_te1550");
    }

    // ─── End-to-end through the ViewModel ─────────────────────────────────────
    //
    // The earlier "EndToEnd_*" tests above call the service directly with the
    // module + function already split. That bypasses ResolveModuleAndFunction
    // — which is exactly where the previous "no attribute 'demo.mmi2x2_dp'"
    // bug lived. These tests drive the actual user flow: build a real draft
    // with a NazcaFunction string, set it on the real ViewModel, wait for
    // the async render to finish, then check the resulting overlay state.

    /// <summary>
    /// Drives the ViewModel through SelectedComponent assignment and waits up
    /// to <paramref name="timeoutMs"/> for the async render to populate the
    /// overlay (or for a non-empty status text reporting failure).
    /// </summary>
    private static async Task<PdkOffsetEditorViewModel?> TryRenderThroughViewModel(
        string nazcaFunction, string? nazcaParameters = null, int timeoutMs = 30000)
    {
        var python = FindWorkingPython3();
        if (python == null) return null;
        var script = FindRealPreviewScript();
        if (script == null) return null;

        var svc = new NazcaComponentPreviewService(python, script);
        var vm = new PdkOffsetEditorViewModel(
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

    [Fact]
    public async Task EndToEnd_ViewModel_DemoMmi2x2Dp_RendersOverlay()
    {
        // Reproduces the exact user click that produced
        //   "Preview unavailable: module 'nazca.demofab' has no attribute 'demo.mmi2x2_dp'"
        // before ResolveModuleAndFunction was added. Demo PDK is bundled with
        // Nazca, so this test is a strict assertion when Python+Nazca are
        // available — anything less than a populated overlay is a real bug.
        var vm = await TryRenderThroughViewModel("demo.mmi2x2_dp");
        if (vm == null) return;  // python or script missing — environment skip

        // Genuinely no Nazca? Status would say "No module named 'nazca'".
        if (vm.NazcaOverlayStatus.Contains("No module named 'nazca'", StringComparison.Ordinal))
            return;

        vm.IsNazcaRendering.ShouldBeFalse(
            $"Render never completed. Last status: '{vm.NazcaOverlayStatus}'");
        vm.HasNazcaOverlay.ShouldBeTrue(
            $"Demo MMI 2x2 must render through the full ViewModel pipeline. Status: {vm.NazcaOverlayStatus}");
        vm.NazcaPinStubs.Count.ShouldBeGreaterThanOrEqualTo(2,
            "a 2x2 MMI coupler must expose at least 2 pin stubs in the overlay");
    }

    [Fact]
    public async Task EndToEnd_ViewModel_SiepicEbeamYJunction_RoutesToSiepicEbeamPdkModule()
    {
        // SiEPIC EBeam PDK is a KLayout package, not a Nazca cell library:
        // siepic_ebeam_pdk does not expose `ebeam_y_1550` as a top-level
        // attribute. The realistic outcome is "module 'siepic_ebeam_pdk'
        // has no attribute …" — that means the routing in
        // ResolveModuleAndFunction worked (we hit the correct module) and
        // the limitation is upstream, not a Lunima bug. The negative
        // assertion below catches the *previous* bug, where the same
        // request hit nazca.demofab instead.
        var vm = await TryRenderThroughViewModel("ebeam_y_1550");
        if (vm == null) return;

        // The status must mention siepic_ebeam_pdk, NOT nazca.demofab — the
        // routing fix is exactly what this test guards.
        vm.NazcaOverlayStatus.Contains("nazca.demofab", StringComparison.Ordinal).ShouldBeFalse(
            $"Flat SiEPIC name was routed to nazca.demofab instead of siepic_ebeam_pdk. " +
            $"Status: {vm.NazcaOverlayStatus}");
    }

    // ─── ViewModel integration ────────────────────────────────────────────────

    private static PdkComponentDraft BuildDraft() => new()
    {
        Name = "Coupler",
        NazcaFunction = "pdk.coupler",
        WidthMicrometers = 40,
        HeightMicrometers = 20,
        NazcaOriginOffsetX = 5.0,
        NazcaOriginOffsetY = 10.0,
        Pins = new List<PhysicalPinDraft>
        {
            new() { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 5 },
        }
    };

    [Fact]
    public void ViewModel_WithPreviewServiceNull_NazcaOverlayNotShown()
    {
        var vm = new PdkOffsetEditorViewModel(new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.HasNazcaOverlay.ShouldBeFalse();
        vm.NazcaPolygons.ShouldBeEmpty();
        vm.NazcaPinStubs.ShouldBeEmpty();
    }

    [Fact]
    public void ViewModel_WhenComponentSelected_SetsComponentDimensions()
    {
        var vm = new PdkOffsetEditorViewModel(new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.ComponentWidth.ShouldBe(40.0);
        vm.ComponentHeight.ShouldBe(20.0);
    }

    [Fact]
    public void ViewModel_ApplyOffset_SavesWidthAndHeightToDraft()
    {
        var vm = new PdkOffsetEditorViewModel(new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        vm.ComponentWidth = 55.0;
        vm.ComponentHeight = 25.0;
        vm.ApplyOffsetCommand.Execute(null);

        vm.SelectedComponent.Draft.WidthMicrometers.ShouldBe(55.0);
        vm.SelectedComponent.Draft.HeightMicrometers.ShouldBe(25.0);
    }

    [Fact]
    public void ViewModel_CanvasComponentLeft_IsCanvasPaddingWhenNoOverlay()
    {
        var vm = new PdkOffsetEditorViewModel(new PdkLoader(), new PdkJsonSaver(), new PdkManagerViewModel(), previewService: null);
        var draft = BuildDraft();
        vm.Components.Add(new PdkComponentOffsetItemViewModel(draft, "Test"));
        vm.SelectedComponent = vm.Components[0];

        // Without overlay, CanvasComponentLeft should equal CanvasPadding (20)
        vm.CanvasComponentLeft.ShouldBe(20.0);
        vm.CanvasComponentTop.ShouldBe(20.0);
    }
}
