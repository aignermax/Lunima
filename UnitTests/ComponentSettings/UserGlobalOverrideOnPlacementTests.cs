using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.ComponentSettings;

/// <summary>
/// Pins the placement-time application of user-global PDK template overrides.
///
/// Bug history: <see cref="MainViewModel"/> instantiated <see cref="FileOperationsViewModel"/>
/// without forwarding the <see cref="UserSMatrixOverrideStore"/> dependency, so
/// the optional parameter defaulted to <c>null</c> and
/// <see cref="FileOperationsViewModel.ApplyUserGlobalOverrides"/> early-returned
/// on every component placement. The user saw their template override take
/// effect on existing instances (via dialog-triggered re-apply) but NOT on
/// newly placed instances — exactly the silent failure
/// <see cref="UserSMatrixOverrideStore"/> was meant to prevent.
///
/// These tests exercise the placement path with the store actually wired so
/// we'd catch a regression of that wiring at unit-test time instead of at
/// "drag a component onto the canvas and run a sim" time.
/// </summary>
public class UserGlobalOverrideOnPlacementTests : IDisposable
{
    private readonly string _tempStorePath;

    public UserGlobalOverrideOnPlacementTests()
    {
        _tempStorePath = Path.Combine(Path.GetTempPath(), $"sparam-overrides-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempStorePath)) File.Delete(_tempStorePath);
    }

    private static FileOperationsViewModel BuildFileOps(
        DesignCanvasViewModel canvas,
        ObservableCollection<ComponentTemplate> library,
        UserSMatrixOverrideStore? userStore)
    {
        return new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            library,
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!, // verilogAExport — not exercised here
            errorConsole: null,
            userSMatrixOverrideStore: userStore);
    }

    /// <summary>
    /// Builds a minimal 2-port template that pretends to be a PDK template:
    /// matching NazcaFunctionName so ResolveTemplateKey can find it, identity
    /// PDK default S-matrix so the override path is the only thing that can
    /// produce the test's expected 0.7 transmission.
    /// </summary>
    private static ComponentTemplate BuildTwoPortTemplate()
    {
        return new ComponentTemplate
        {
            Name = "TestCoupler",
            Category = "Test",
            PdkSource = "test-pdk",
            NazcaFunctionName = "nazca_testcoupler",
            WidthMicrometers = 10,
            HeightMicrometers = 1,
            PinDefinitions = new[]
            {
                new PinDefinition("in", 0, 0.5, 180),
                new PinDefinition("out", 10, 0.5, 0)
            },
            CreateSMatrix = pins =>
            {
                // Identity default — picked so any non-identity transmission
                // in the assertion comes from the override, not the PDK.
                var allIds = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
                var sm = new CAP_Core.LightCalculation.SMatrix(allIds, new List<(Guid, double)>());
                return sm;
            }
        };
    }

    [Fact]
    public void Placement_WithUserGlobalOverrideForTemplate_AppliesOverrideToNewComponent()
    {
        // Set up a template the user-global store has an override for. The
        // template-key shape is "{PdkSource}::{Name}", same shape the dialog
        // writes — see FileOperationsViewModel.ResolveTemplateKey.
        var template = BuildTwoPortTemplate();
        var library = new ObservableCollection<ComponentTemplate> { template };

        var userStore = new UserSMatrixOverrideStore(_tempStorePath);
        var overrideKey = $"{template.PdkSource}::{template.Name}";
        userStore.Apply(overrideKey, BuildOverrideData());

        var canvas = new DesignCanvasViewModel();
        _ = BuildFileOps(canvas, library, userStore);

        // Place a fresh instance: the placement handler should apply the
        // user-global override via the template-key resolver.
        var instance = ComponentTemplates.CreateFromTemplate(template, x: 0, y: 0);
        var instanceVm = new ComponentViewModel(instance);
        canvas.Components.Add(instanceVm);

        // After the CollectionChanged handler runs, the live component's
        // wavelength map for 1550 nm must have been replaced with the
        // override matrix — same behaviour as the project-load path.
        instance.WaveLengthToSMatrixMap.ShouldContainKey(1550);
        var sMatrix = instance.WaveLengthToSMatrixMap[1550];
        sMatrix.ShouldNotBeNull();

        // Sanity-check the actual values came from our override (transmission
        // 0.7 between port 1 and port 2). Diagonal entries can stay 0.
        bool foundOverrideTransmission = false;
        foreach (var (key, value) in EnumerateNonZero(sMatrix))
        {
            if (Math.Abs(value.Magnitude - 0.7) < 1e-3)
            {
                foundOverrideTransmission = true;
                break;
            }
        }
        foundOverrideTransmission.ShouldBeTrue(
            "the placement handler must have copied the user-store override " +
            "into Component.WaveLengthToSMatrixMap; if not, ApplyUserGlobalOverrides " +
            "is being skipped (likely the store wasn't injected into FileOperationsViewModel)");
    }

    [Fact]
    public void Placement_WithoutUserGlobalOverride_KeepsPdkDefault()
    {
        // Negative case: with no override in the user store, placement leaves
        // the PDK-default matrix untouched. Locks in that the override path
        // doesn't bleed into projects that haven't opted in.
        var template = BuildTwoPortTemplate();
        var library = new ObservableCollection<ComponentTemplate> { template };

        var userStore = new UserSMatrixOverrideStore(_tempStorePath);
        var canvas = new DesignCanvasViewModel();
        _ = BuildFileOps(canvas, library, userStore);

        var instance = ComponentTemplates.CreateFromTemplate(template, x: 0, y: 0);
        var defaultMatrix = instance.WaveLengthToSMatrixMap[1550];
        canvas.Components.Add(new ComponentViewModel(instance));

        // Same instance reference, same matrix object — placement was a no-op
        // for the override path.
        instance.WaveLengthToSMatrixMap[1550].ShouldBeSameAs(defaultMatrix);
    }

    private static ComponentSMatrixData BuildOverrideData()
    {
        // 2-port override with strong cross-coupling — the value 0.7 is what
        // the test asserts came through to the live component.
        var data = new ComponentSMatrixData { SourceNote = "Test override" };
        data.Wavelengths["1550"] = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            // S[r=out, c=in] row-major. Cross-coupling at S[0,1] and S[1,0].
            Real = new List<double> { 0.0, 0.7, 0.7, 0.0 },
            Imag = new List<double> { 0, 0, 0, 0 },
            PortNames = new List<string> { "in", "out" }
        };
        return data;
    }

    private static IEnumerable<(string Key, System.Numerics.Complex Value)> EnumerateNonZero(
        CAP_Core.LightCalculation.SMatrix sMatrix)
    {
        for (int r = 0; r < sMatrix.SMat.RowCount; r++)
        {
            for (int c = 0; c < sMatrix.SMat.ColumnCount; c++)
            {
                var v = sMatrix.SMat[r, c];
                if (v.Magnitude > 1e-6)
                    yield return ($"[{r},{c}]", v);
            }
        }
    }
}
