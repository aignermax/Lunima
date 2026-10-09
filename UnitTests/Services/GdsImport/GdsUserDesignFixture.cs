using System.Collections.ObjectModel;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using UnitTests.Export;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Shared fixture behind the GDS round-trip tests (<see cref="GdsUserDesignRoundTripTests"/>,
/// <see cref="GdsHighestLevelRoundTripTests"/>): rebuilds the REAL user design (the one
/// behind the "components are missing after re-import" report) on a fresh canvas — seven
/// components from two bundled PDKs (2× Demo PDK "2x2 MMI Coupler"; SiEPIC "Adiabatic
/// Coupler TE 1550", "Broadband DC TE 1550", 2× "Crossing 4-Port", "DC Halfring-Straight")
/// at his exact coordinates, wired with his ten waveguide connections, routed for real.
/// Also carries the shared harness: nazca-python discovery, the throwaway user-PDK store,
/// and the runtime-registration sink wired like <c>GdsImportServiceTests</c>.
/// <para>
/// Design-build mapping: the user's netlist pin names match the bundled templates
/// verbatim (<c>in1/in2/out1/out2</c> on the MMI, <c>port 1..4</c> on every ebeam
/// cell), so no substitution was needed. The halfring's settings
/// (<c>gap=100E-9,radius=3E-6</c>) are exactly the PDK defaults, so the plain
/// template instance already carries them — no slider fiddling. His 8 external
/// ports are NOT modeled: they are a simulation concept, and the Nazca export only
/// writes top-cell port labels for grating/edge couplers — this design has none, so
/// external ports leave no trace in the GDS either way.
/// </para>
/// </summary>
internal static class GdsUserDesignFixture
{
    /// <summary>A repair budget no routing pass of this fixture ever reaches.</summary>
    private static readonly TimeSpan UnboundedRepairBudget = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Rebuilds the user's design on a fresh canvas: the seven components at his
    /// exact coordinates, instantiated from the REAL bundled PDK templates, then
    /// his ten waveguide connections, routed for real (the A* grid is initialized
    /// around the design's negative-Y extent like the app does).
    /// </summary>
    public static DesignCanvasViewModel BuildUserDesignCanvas()
    {
        var templates = TestPdkLoader.LoadAllTemplates();
        var canvas = new DesignCanvasViewModel();
        // The contention repair stops at a wall-clock budget; on a loaded CI runner it would
        // stop earlier than on a desktop and leave different routes behind. The pinned routing
        // outcome must not depend on machine speed.
        canvas.ConnectionManager.ContentionRepairTimeBudget = UnboundedRepairBudget;

        Component Place(string templateName, string pdk, double x, double y)
        {
            var template = templates.First(t => t.Name == templateName && t.PdkSource == pdk);
            var component = ComponentTemplates.CreateFromTemplate(template, x, y);
            canvas.AddComponent(component, templateName, pdk);
            return component;
        }

        const string demo = "Demo PDK";
        const string siepic = "SiEPIC EBeam PDK";
        // His coordinates, verbatim from the exported netlist.
        var mmi1 = Place("2x2 MMI Coupler", demo, 259.699, -513.629);       // _2x2_MMI_Coupler_1
        var mmi2 = Place("2x2 MMI Coupler", demo, 253.899, -397.528);       // _2x2_MMI_Coupler_2
        var adiabatic = Place("Adiabatic Coupler TE 1550", siepic, 298.420, -451.179);
        var bdc = Place("Broadband DC TE 1550", siepic, 730.431, -452.029);
        var crossing872 = Place("Crossing 4-Port", siepic, 542.084, -449.679);
        var crossing1175 = Place("Crossing 4-Port", siepic, 519.699, -449.679);
        var halfring = Place("DC Halfring-Straight", siepic, 267.425, -452.679);

        // His ten connections, verbatim. Pin names match the bundled templates
        // one-to-one (MMI: in1/in2/out1/out2; ebeam: "port N").
        PhysicalPin Pin(Component c, string name) => c.PhysicalPins.First(p => p.Name == name);
        canvas.ConnectPins(Pin(mmi2, "in2"), Pin(mmi1, "in1"));
        canvas.ConnectPins(Pin(mmi1, "in2"), Pin(mmi2, "in1"));
        canvas.ConnectPins(Pin(mmi1, "out2"), Pin(crossing872, "port 4"));
        canvas.ConnectPins(Pin(crossing872, "port 3"), Pin(mmi2, "out1"));
        canvas.ConnectPins(Pin(mmi2, "out2"), Pin(crossing1175, "port 3"));
        canvas.ConnectPins(Pin(crossing1175, "port 4"), Pin(mmi1, "out1"));
        canvas.ConnectPins(Pin(crossing1175, "port 2"), Pin(crossing872, "port 1"));
        canvas.ConnectPins(Pin(crossing872, "port 2"), Pin(bdc, "port 1"));
        canvas.ConnectPins(Pin(adiabatic, "port 4"), Pin(crossing1175, "port 1"));
        canvas.ConnectPins(Pin(halfring, "port 3"), Pin(adiabatic, "port 2"));

        // The app always routes on an initialized grid; the default bounds
        // (-100..5100) would not cover this design's negative-Y extent.
        canvas.InitializeAStarRouting(150, -700, 950, -250);
        canvas.RecalculateRoutesAsync().GetAwaiter().GetResult();
        return canvas;
    }

    /// <summary>Counts the lines of <paramref name="script"/> containing <paramref name="marker"/>.</summary>
    public static int CountLines(string script, string marker) =>
        script.Split('\n').Count(l => l.Contains(marker, StringComparison.Ordinal));

    /// <summary>A throwaway user-PDK store rooted under <paramref name="root"/>.</summary>
    public static UserPdkStore CreateStore(string root, string name) => new(
        Path.Combine(root, name), new PdkJsonSaver(), new PdkLoader());

    // The tool environment cannot change mid-run, so the probe spawns its
    // subprocesses once per test process instead of once per calling test.
    private static readonly Lazy<Task<string?>> CachedNazcaPython = new(FindNazcaPythonUncachedAsync);
    private static readonly Lazy<Task<string?>> CachedSiepicRoundTripPython = new(FindSiepicRoundTripPythonUncachedAsync);

    /// <summary>
    /// Locates a Python with nazca importable: first a Lunima managed env
    /// (%LOCALAPPDATA%/Lunima/envs/*), then python/python3 on PATH. Shared and
    /// cached for all nazca-gated tests.
    /// </summary>
    public static Task<string?> FindNazcaPythonAsync() => CachedNazcaPython.Value;

    /// <summary>
    /// Locates a Python carrying the FULL stack the SiEPIC round-trip scenario
    /// pins: nazca plus klayout.db + siepic_ebeam_pdk, so the export's klayout
    /// post-pass swaps the ebeam stub boxes for the real foundry cells. A
    /// nazca-only interpreter is NEVER returned: with it the upgrade silently
    /// keeps the stub boxes (by design the export degrades instead of breaking)
    /// and the round trip then sees the stub topology — heuristic edge pins,
    /// entangled route chains — while its expectations are pinned to the
    /// upgraded scenario (#1353: a nazca-only managed env enumerated first on a
    /// Windows dev machine shadowed the full env and the CI PATH python).
    /// Same scan order as <see cref="FindNazcaPythonAsync"/>: Lunima managed
    /// envs first, then python/python3 on PATH.
    /// </summary>
    public static Task<string?> FindSiepicRoundTripPythonAsync() => CachedSiepicRoundTripPython.Value;

    /// <summary>Probed capabilities of one interpreter candidate — the pure selection input.</summary>
    internal sealed record PythonCandidateCapabilities(string Path, bool HasNazca, bool HasSiepicUpgradeStack);

    /// <summary>
    /// Picks the interpreter for the SiEPIC round-trip tests: the first candidate
    /// in scan order that carries the full stack. Nazca-only candidates are
    /// skipped, never selected — regardless of where they appear in the
    /// (OS/filesystem-dependent) enumeration order.
    /// </summary>
    internal static string? SelectRoundTripPython(IEnumerable<PythonCandidateCapabilities> candidates) =>
        candidates.FirstOrDefault(c => c.HasNazca && c.HasSiepicUpgradeStack)?.Path;

    private static async Task<string?> FindNazcaPythonUncachedAsync()
    {
        foreach (var candidate in EnumeratePythonCandidates())
        {
            if (await ProbeNazca(candidate))
                return candidate;
        }
        return null;
    }

    private static async Task<string?> FindSiepicRoundTripPythonUncachedAsync()
    {
        var candidates = new List<PythonCandidateCapabilities>();
        foreach (var python in EnumeratePythonCandidates())
        {
            var hasNazca = await ProbeNazca(python);
            var hasSiepicUpgradeStack = hasNazca && await ProbeSiepicUpgradeStack(python);
            candidates.Add(new PythonCandidateCapabilities(python, hasNazca, hasSiepicUpgradeStack));
        }
        return SelectRoundTripPython(candidates);
    }

    /// <summary>Managed-env interpreters that exist on disk, then the PATH command names.</summary>
    private static IEnumerable<string> EnumeratePythonCandidates()
    {
        var envs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "envs");
        if (Directory.Exists(envs))
        {
            foreach (var root in Directory.GetDirectories(envs))
            {
                foreach (var rel in new[] { Path.Combine("Scripts", "python.exe"), Path.Combine("bin", "python") })
                {
                    var py = Path.Combine(root, rel);
                    if (File.Exists(py))
                        yield return py;
                }
            }
        }

        yield return "python";
        yield return "python3";
    }

    /// <summary>True when <paramref name="python"/> starts and can import nazca.</summary>
    public static async Task<bool> ProbeNazca(string python)
    {
        try
        {
            var probe = await SiepicRealGeometryExportTests.RunPythonAsync(
                python, Path.GetTempPath(), "-c", "import nazca");
            return probe.ExitCode == 0;
        }
        catch
        {
            return false;   // not on PATH at all
        }
    }

    /// <summary>
    /// True when <paramref name="python"/> can import the klayout/SiEPIC stack the
    /// export's stub→real-cell upgrade post-pass needs (klayout.db + siepic_ebeam_pdk).
    /// </summary>
    private static async Task<bool> ProbeSiepicUpgradeStack(string python)
    {
        try
        {
            var probe = await SiepicRealGeometryExportTests.RunPythonAsync(
                python, Path.GetTempPath(), "-c", "import klayout.db, siepic_ebeam_pdk");
            return probe.ExitCode == 0;
        }
        catch
        {
            return false;   // not on PATH at all
        }
    }

    /// <summary>Wires the real registrar with throwaway library state (pattern from GdsImportServiceTests).</summary>
    internal sealed class LibrarySink
    {
        public readonly ObservableCollection<ComponentTemplate> Templates = new();
        public readonly ObservableCollection<string> Categories = new();
        public readonly PdkManagerViewModel PdkManager = new();
        public readonly List<PdkDraft> LoadedDrafts = new();
        public readonly UserPreferencesService Preferences;
        public readonly Action<PdkComponentDraft, string, string> Register;

        public LibrarySink(string prefsPath)
        {
            Preferences = new UserPreferencesService(prefsPath);
            var loader = new PdkLoader();
            Register = (draft, pdkName, filePath) =>
                CustomComponentLibraryRegistrar.Register(
                    draft, pdkName, filePath, Templates, Categories, PdkManager,
                    Preferences, loader, LoadedDrafts, () => { }, () => { });
        }
    }
}
