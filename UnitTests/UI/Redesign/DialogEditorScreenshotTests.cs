using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.LayerPicker;
using CAP.Avalonia.ViewModels.PdkImport;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Dialogs;
using CAP.Avalonia.Views.PdkImport;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Screenshots of the form-style dialogs (custom PDK, new component, PDK import wizard, layer picker)
/// for the design-system migration. Does nothing without <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class DialogEditorScreenshotTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-edshot-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ProcessDefinition SampleProcess() => new()
    {
        Name = "SiN 300nm",
        Layers = { new ProcessLayer { Name = "WG", Layer = 1, Datatype = 0, Field = "core", Description = "Waveguide core" } },
        Xsections = { new ProcessXsection { Name = "strip", Kind = XsectionKind.Optical, WidthUm = 1.0, MinRadiusUm = 50, RecommendedRadiusUm = 100 } },
        Materials = { new ProcessMaterial { Name = "SiN", Role = "core" } },
    };

    private UserPdkStore Store() => new(_root, new PdkJsonSaver(), new PdkLoader());

    [AvaloniaFact]
    public void CreateCustomPdkWindow_DefineNew()
    {
        if (!DialogShot.Enabled) return;
        Directory.CreateDirectory(_root);
        var vm = new CreateCustomPdkViewModel(
            Store(), new[] { SampleProcess() }, new ProcessManagementViewModel(Mock.Of<IFileDialogService>()));
        vm.ProcessSource = PdkProcessSource.DefineNew;
        vm.PdkName = "My PDK";
        DialogShot.Capture(new CreateCustomPdkWindow { DataContext = vm }, "create-custom-pdk");
    }

    [AvaloniaFact]
    public void NewComponentWindow_WithCode()
    {
        if (!DialogShot.Enabled) return;
        Directory.CreateDirectory(_root);
        var extractor = new ComponentGeometryExtractor(
            Mock.Of<IPdkGeometryRenderer>(), Mock.Of<IPdkGeometryRenderer>());
        var vm = new NewComponentViewModel(
            extractor, Mock.Of<IFdtdSMatrixService>(), Store(), new List<ProcessDefinition> { SampleProcess() })
        {
            ComponentName = "My coupler",
            Code = "import gdsfactory as gf\ncomponent = gf.components.coupler()",
        };
        DialogShot.Capture(new NewComponentWindow { DataContext = vm }, "new-component");
    }

    [AvaloniaFact]
    public void PdkImportWizard_ReviewAndSave()
    {
        if (!DialogShot.Enabled) return;
        var import = new PdkImportService(new UserPreferencesService(Path.Combine(Path.GetTempPath(), "dlgshot-prefs.json")));
        var vm = new PdkImportWizardViewModel("/home/user/pdk/components.py", import);
        void ReviewState()
        {
            // The window starts a real parse when it opens; let it fail, then overwrite its state.
            for (var i = 0; i < 20; i++) { DialogShot.Pump(); Thread.Sleep(25); }
            vm.ErrorText = "";
            vm.HasError = false;
            vm.ParsedComponents.Clear();
            foreach (var (name, pins) in new[] { ("ebeam_y_1550", 3), ("ebeam_gc_te1550", 1), ("ebeam_dummy", 0) })
                vm.ParsedComponents.Add(new ComponentParseResultViewModel(Geometry(name, pins)));
            vm.StatusText = "Parsed 3 component(s), 1 with warnings.";
            vm.CurrentStep = WizardStep.Review;
        }

        DialogShot.Capture(new PdkImportWizardWindow { DataContext = vm }, "pdk-import-review", ReviewState);
        DialogShot.Capture(new PdkImportWizardWindow { DataContext = vm }, "pdk-import-save", () =>
        {
            ReviewState();
            vm.CurrentStep = WizardStep.Save;
        });
    }

    [AvaloniaFact]
    public void GdsLayerPickerWindow_Default()
    {
        if (!DialogShot.Enabled) return;
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(Rect(1, 0, 0, 100, 100));
        top.Elements.Add(Rect(11, 10, 10, 14, 14));
        library.Cells["TOP"] = top;
        var vm = new GdsLayerPickerViewModel(
            GdsLayerGeometryScene.Build(library, "TOP"),
            new Dictionary<GdsLayerPair, GdsLayerFieldTarget>(), (_, _) => { });
        DialogShot.Capture(new GdsLayerPickerWindow { DataContext = vm }, "gds-layer-picker");
    }

    private static ParsedComponentGeometry Geometry(string name, int pinCount)
    {
        var geometry = new ParsedComponentGeometry
        {
            Name = name, Category = "Couplers", NazcaFunction = name,
            WidthMicrometers = 10.5, HeightMicrometers = 25.3,
        };
        for (var i = 0; i < pinCount; i++)
            geometry.Pins.Add(new ParsedPinGeometry { Name = $"pin{i}", OffsetXMicrometers = i * 2.0 });
        return geometry;
    }

    private static GdsPolygon Rect(int layer, double x0, double y0, double x1, double y1) => new()
    {
        Layer = layer,
        DataType = 0,
        Points = new[]
        {
            new GdsPoint(x0, y0), new GdsPoint(x1, y0), new GdsPoint(x1, y1), new GdsPoint(x0, y1), new GdsPoint(x0, y0),
        },
    };
}
