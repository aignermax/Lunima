using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for Phase Shifter and other parameterized component
/// size persistence across save/load roundtrips.
///
/// Root cause under test: when two templates share the same name (e.g. Demo PDK
/// "Phase Shifter" at 500×60 µm and Custom Test PDK "Phase Shifter" at 200×20 µm),
/// the old code always matched the *first* template in the library, silently
/// replacing the saved component with the wrong size.  The fix stores the
/// PdkSource alongside TemplateName so the correct template is found.
/// </summary>
public class ComponentSizePersistenceTests
{
    // ── Demo PDK template dimensions (from demo-pdk.json, Phase Shifter 500×60 µm) ──
    private const double BuiltInWidth  = 500;
    private const double BuiltInHeight = 60;
    private const string BuiltInPdkSource = "Demo PDK";

    // ── Custom PDK template dimensions (name collision with Demo PDK) ─────────
    private const double PdkWidth  = 200;
    private const double PdkHeight = 20;
    private const string DemoPdkSource = "Custom Test PDK";

    // ── Template / component names ────────────────────────────────────────────
    private const string PhaseShifterName = "Phase Shifter";

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: create a minimal test setup.
    // The returned template library always contains BOTH a built-in AND a PDK
    // template named "Phase Shifter" so that name-only lookup is ambiguous.
    // ─────────────────────────────────────────────────────────────────────────

    private static (FileOperationsViewModel fileOps,
                    DesignCanvasViewModel canvas,
                    ObservableCollection<ComponentTemplate> templates,
                    string tempFile)
        CreateTestSetup()
    {
        var canvas    = new DesignCanvasViewModel();
        var templates = new ObservableCollection<ComponentTemplate>();

        // 1. Add ALL JSON PDK templates (Demo PDK "Phase Shifter" 500×60 is among them)
        foreach (var t in TestPdkLoader.LoadAllTemplates())
            templates.Add(t);

        // 2. Add a Custom PDK "Phase Shifter" with DIFFERENT dimensions.
        //    This causes the name-collision that the fix addresses.
        var pdkTemplate = CreatePdkPhaseShifterTemplate();
        templates.Add(pdkTemplate);

        var commandManager = new CommandManager();
        var nazcaExporter  = new SimpleNazcaExporter();
        var gdsExportVm    = new GdsExportViewModel(new GdsExportService());
        var photonTorchVm = new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(
            new CAP_Core.Export.PhotonTorchExporter(), canvas);
        var fileOps = new FileOperationsViewModel(
            canvas, commandManager, nazcaExporter, templates, gdsExportVm, photonTorchVm);

        var tempFile = Path.Combine(Path.GetTempPath(), $"cap_test_{Guid.NewGuid():N}.lun");
        return (fileOps, canvas, templates, tempFile);
    }

    /// <summary>Creates the fake custom PDK Phase Shifter template (200×20 µm) used to test name-collision resolution.</summary>
    private static ComponentTemplate CreatePdkPhaseShifterTemplate()
    {
        return new ComponentTemplate
        {
            Name              = PhaseShifterName,
            Category          = "Modulators",
            WidthMicrometers  = PdkWidth,
            HeightMicrometers = PdkHeight,
            PdkSource         = DemoPdkSource,
            NazcaFunctionName = "custom_test_pdk.phase_shifter",
            HasSlider         = true,
            SliderMin         = 0,
            SliderMax         = 360,
            NazcaOriginOffsetX = 0,
            NazcaOriginOffsetY = PdkHeight / 2,
            PinDefinitions = new[]
            {
                new PinDefinition("in",  0,         PdkHeight / 2, 180),
                new PinDefinition("out", PdkWidth,  PdkHeight / 2, 0)
            },
            CreateSMatrixWithSliders = (pins, sliders) =>
            {
                var pinIds    = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
                var sliderIds = sliders.Select(s => (s.ID, s.Value)).ToList();
                return new CAP_Core.LightCalculation.SMatrix(pinIds, sliderIds);
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IFileDialogService> CreateDialogMock(string savePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog
            .Setup(d => d.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(savePath);
        dialog
            .Setup(d => d.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(savePath);
        return dialog;
    }

    private static ComponentTemplate GetBuiltInPhaseShifter(ObservableCollection<ComponentTemplate> templates)
        => templates.First(t => t.Name == PhaseShifterName && t.PdkSource == BuiltInPdkSource);

    private static ComponentTemplate GetPdkPhaseShifter(ObservableCollection<ComponentTemplate> templates)
        => templates.First(t => t.Name == PhaseShifterName && t.PdkSource == DemoPdkSource);

    // ─────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Basic roundtrip: built-in Phase Shifter (500×60 µm).
    /// Even when a same-named PDK template is in the library, the built-in
    /// component must load back at exactly 500×60.
    /// </summary>
    [Fact]
    public async Task BuiltInPhaseShifter_SaveLoadRoundtrip_PreservesSize()
    {
        var (fileOps, canvas, templates, tempFile) = CreateTestSetup();
        try
        {
            var template  = GetBuiltInPhaseShifter(templates);
            var component = ComponentTemplates.CreateFromTemplate(template, 100, 100);
            canvas.AddComponent(component, template.Name, template.PdkSource);

            var dialogMock = CreateDialogMock(tempFile);
            fileOps.FileDialogService = dialogMock.Object;

            await fileOps.SaveDesignCommand.ExecuteAsync(null);
            canvas.Components.Clear();
            canvas.Connections.Clear();
            canvas.AllPins.Clear();
            await fileOps.LoadDesignCommand.ExecuteAsync(null);

            canvas.Components.Count.ShouldBe(1);
            var loaded = canvas.Components[0].Component;
            loaded.WidthMicrometers.ShouldBe(BuiltInWidth,  tolerance: 0.01);
            loaded.HeightMicrometers.ShouldBe(BuiltInHeight, tolerance: 0.01);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Regression test for the PDK name-collision bug.
    /// A Phase Shifter placed from the Demo PDK (200×20 µm) must load back at
    /// 200×20, not at the built-in 500×60.
    /// </summary>
    [Fact]
    public async Task PdkPhaseShifter_SaveLoadRoundtrip_PreservesSize_NotBuiltInSize()
    {
        var (fileOps, canvas, templates, tempFile) = CreateTestSetup();
        try
        {
            var template  = GetPdkPhaseShifter(templates);
            var component = ComponentTemplates.CreateFromTemplate(template, 100, 100);
            canvas.AddComponent(component, template.Name, template.PdkSource);

            var dialogMock = CreateDialogMock(tempFile);
            fileOps.FileDialogService = dialogMock.Object;

            await fileOps.SaveDesignCommand.ExecuteAsync(null);
            canvas.Components.Clear();
            canvas.Connections.Clear();
            canvas.AllPins.Clear();
            await fileOps.LoadDesignCommand.ExecuteAsync(null);

            canvas.Components.Count.ShouldBe(1);
            var loaded = canvas.Components[0].Component;

            // PDK dimensions must be preserved (bug: was loading built-in 500×60 instead)
            loaded.WidthMicrometers.ShouldBe(PdkWidth,   tolerance: 0.01,
                $"Expected PDK Phase Shifter width {PdkWidth} but got {loaded.WidthMicrometers}. " +
                "This is the template name-collision bug: wrong template was selected during load.");
            loaded.HeightMicrometers.ShouldBe(PdkHeight, tolerance: 0.01,
                $"Expected PDK Phase Shifter height {PdkHeight} but got {loaded.HeightMicrometers}.");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Slider value is preserved during save/load for a Phase Shifter.
    /// </summary>
    [Fact]
    public async Task PhaseShifter_SaveLoadRoundtrip_PreservesSliderValue()
    {
        var (fileOps, canvas, templates, tempFile) = CreateTestSetup();
        try
        {
            const double savedSliderValue = 270.0;

            var template  = GetBuiltInPhaseShifter(templates);
            var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
            var vm        = canvas.AddComponent(component, template.Name, template.PdkSource);

            // Set a non-default slider value
            vm.SliderValue = savedSliderValue;

            var dialogMock = CreateDialogMock(tempFile);
            fileOps.FileDialogService = dialogMock.Object;

            await fileOps.SaveDesignCommand.ExecuteAsync(null);
            canvas.Components.Clear();
            canvas.Connections.Clear();
            canvas.AllPins.Clear();
            await fileOps.LoadDesignCommand.ExecuteAsync(null);

            canvas.Components.Count.ShouldBe(1);
            var loadedVm = canvas.Components[0];
            loadedVm.HasSliders.ShouldBeTrue();
            loadedVm.SliderValue.ShouldBe(savedSliderValue, tolerance: 0.01);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that the save file contains the PdkSource field.
    /// This confirms the fix is actually writing PdkSource to disk.
    /// </summary>
    [Fact]
    public async Task PdkPhaseShifter_SavedJson_ContainsPdkSource()
    {
        var (fileOps, canvas, templates, tempFile) = CreateTestSetup();
        try
        {
            var template  = GetPdkPhaseShifter(templates);
            var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
            canvas.AddComponent(component, template.Name, template.PdkSource);

            var dialogMock = CreateDialogMock(tempFile);
            fileOps.FileDialogService = dialogMock.Object;

            await fileOps.SaveDesignCommand.ExecuteAsync(null);

            var json = await File.ReadAllTextAsync(tempFile);
            json.ShouldContain("pdkSource", Case.Insensitive);
            json.ShouldContain(DemoPdkSource);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
