using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.LightCalculation;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.Integration;

/// <summary>
/// Master roundtrip test that validates ALL component types from BOTH PDKs preserve their
/// position, size, rotation, slider values, and pin positions after a complete save/load cycle.
/// Systematically catches any persistence regression across the entire PDK.
/// Covers issue #357.
/// </summary>
public class AllComponentsRoundtripTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;
    private readonly List<ComponentTemplate> _builtInTemplates;
    private readonly List<(ComponentTemplate Template, PdkComponentDraft Draft)> _demoPdkComponents;

    /// <summary>Initializes the test suite with built-in and Demo PDK templates.</summary>
    public AllComponentsRoundtripTests()
    {
        _builtInTemplates = ComponentTemplates.GetAllTemplates();
        _demoPdkComponents = LoadDemoPdkComponents();

        _library = new ObservableCollection<ComponentTemplate>(_builtInTemplates);
        foreach (var (template, _) in _demoPdkComponents)
            _library.Add(template);
    }

    /// <summary>
    /// Verifies that every component from BOTH PDKs preserves X, Y, Width, Height,
    /// Rotation, SliderValue, and all Pin positions after a complete save/load roundtrip.
    /// </summary>
    [Fact]
    public async Task AllPdkComponents_SaveLoadRoundtrip_PreserveAllProperties()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_all_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            var originalStates = new List<ComponentState>();
            double x = 0;

            foreach (var template in _library)
            {
                var component = ComponentTemplates.CreateFromTemplate(template, x, y: 100);
                var identifier = $"test_{template.PdkSource}_{template.Name.Replace(" ", "_")}";
                component.Identifier = identifier;

                var vm = saveCanvas.AddComponent(component, template.Name);

                if (template.HasSlider)
                    vm.SliderValue = (template.SliderMin + template.SliderMax) / 2.0;

                originalStates.Add(new ComponentState
                {
                    Identifier = identifier,
                    TemplateName = $"{template.PdkSource}/{template.Name}",
                    X = component.PhysicalX,
                    Y = component.PhysicalY,
                    Width = component.WidthMicrometers,
                    Height = component.HeightMicrometers,
                    Rotation = (int)component.Rotation90CounterClock,
                    SliderValue = template.HasSlider ? vm.SliderValue : (double?)null,
                    Pins = component.PhysicalPins.Select(p => new PinState
                    {
                        Name = p.Name,
                        OffsetX = p.OffsetXMicrometers,
                        OffsetY = p.OffsetYMicrometers,
                        Angle = p.AngleDegrees
                    }).ToList()
                });

                x += 600;
            }

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Components.Count.ShouldBe(
                originalStates.Count,
                "All components must survive save/load roundtrip");

            foreach (var original in originalStates)
            {
                var loadedVm = loadCanvas.Components
                    .FirstOrDefault(c => c.Component.Identifier == original.Identifier);

                loadedVm.ShouldNotBeNull($"{original.TemplateName}: not found after load");

                var comp = loadedVm!.Component;

                comp.PhysicalX.ShouldBe(original.X, 0.01,
                    $"{original.TemplateName}: X changed after roundtrip");
                comp.PhysicalY.ShouldBe(original.Y, 0.01,
                    $"{original.TemplateName}: Y changed after roundtrip");
                comp.WidthMicrometers.ShouldBe(original.Width, 0.01,
                    $"{original.TemplateName}: Width changed after roundtrip");
                comp.HeightMicrometers.ShouldBe(original.Height, 0.01,
                    $"{original.TemplateName}: Height changed after roundtrip");
                ((int)comp.Rotation90CounterClock).ShouldBe(original.Rotation,
                    $"{original.TemplateName}: Rotation changed after roundtrip");

                if (original.SliderValue.HasValue)
                    loadedVm.SliderValue.ShouldBe(original.SliderValue.Value, 0.01,
                        $"{original.TemplateName}: SliderValue changed after roundtrip");

                comp.PhysicalPins.Count.ShouldBe(original.Pins.Count,
                    $"{original.TemplateName}: pin count changed after roundtrip");

                for (int i = 0; i < original.Pins.Count; i++)
                {
                    var loadedPin = comp.PhysicalPins[i];
                    var savedPin = original.Pins[i];
                    loadedPin.Name.ShouldBe(savedPin.Name,
                        $"{original.TemplateName} pin[{i}]: Name changed");
                    loadedPin.OffsetXMicrometers.ShouldBe(savedPin.OffsetX, 0.01,
                        $"{original.TemplateName} pin[{i}] '{savedPin.Name}': OffsetX changed");
                    loadedPin.OffsetYMicrometers.ShouldBe(savedPin.OffsetY, 0.01,
                        $"{original.TemplateName} pin[{i}] '{savedPin.Name}': OffsetY changed");
                    loadedPin.AngleDegrees.ShouldBe(savedPin.Angle, 0.01,
                        $"{original.TemplateName} pin[{i}] '{savedPin.Name}': Angle changed");
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Validates that components created from built-in templates have pin positions
    /// that exactly match the template's PinDefinitions.
    /// </summary>
    [Fact]
    public void BuiltInTemplates_CreatedComponents_PinsMatchTemplateDefinitions()
    {
        foreach (var template in _builtInTemplates)
        {
            var component = ComponentTemplates.CreateFromTemplate(template, x: 0, y: 0);
            var label = $"Built-in/{template.Name}";

            component.PhysicalPins.Count.ShouldBe(template.PinDefinitions.Length,
                $"{label}: pin count mismatch");
            component.WidthMicrometers.ShouldBe(template.WidthMicrometers, 0.01,
                $"{label}: Width mismatch");
            component.HeightMicrometers.ShouldBe(template.HeightMicrometers, 0.01,
                $"{label}: Height mismatch");

            for (int i = 0; i < template.PinDefinitions.Length; i++)
            {
                var def = template.PinDefinitions[i];
                var pin = component.PhysicalPins[i];
                var pinLabel = $"{label} pin[{i}] '{def.Name}'";

                pin.Name.ShouldBe(def.Name, $"{pinLabel}: Name mismatch");
                pin.OffsetXMicrometers.ShouldBe(def.OffsetX, 0.01, $"{pinLabel}: OffsetX mismatch");
                pin.OffsetYMicrometers.ShouldBe(def.OffsetY, 0.01, $"{pinLabel}: OffsetY mismatch");
                pin.AngleDegrees.ShouldBe(def.AngleDegrees, 0.01, $"{pinLabel}: Angle mismatch");
            }
        }
    }

    /// <summary>
    /// Validates that components created from Demo PDK templates have pin positions
    /// that match the JSON PDK definition exactly.
    /// </summary>
    [Fact]
    public void DemoPdkTemplates_CreatedComponents_PinsMatchPdkDefinitions()
    {
        if (_demoPdkComponents.Count == 0)
            return; // Skip if demo PDK not found (CI environment)

        foreach (var (template, draft) in _demoPdkComponents)
        {
            var component = ComponentTemplates.CreateFromTemplate(template, x: 0, y: 0);
            var label = $"Demo PDK/{template.Name}";

            component.PhysicalPins.Count.ShouldBe(draft.Pins.Count,
                $"{label}: pin count mismatch vs PDK JSON");
            component.WidthMicrometers.ShouldBe(draft.WidthMicrometers, 0.01,
                $"{label}: Width mismatch vs PDK JSON");
            component.HeightMicrometers.ShouldBe(draft.HeightMicrometers, 0.01,
                $"{label}: Height mismatch vs PDK JSON");

            for (int i = 0; i < draft.Pins.Count; i++)
            {
                var jsonPin = draft.Pins[i];
                var pin = component.PhysicalPins[i];
                var pinLabel = $"{label} pin[{i}] '{jsonPin.Name}'";

                pin.Name.ShouldBe(jsonPin.Name, $"{pinLabel}: Name mismatch");
                pin.OffsetXMicrometers.ShouldBe(jsonPin.OffsetXMicrometers, 0.01,
                    $"{pinLabel}: OffsetX mismatch vs PDK JSON");
                pin.OffsetYMicrometers.ShouldBe(jsonPin.OffsetYMicrometers, 0.01,
                    $"{pinLabel}: OffsetY mismatch vs PDK JSON");
                pin.AngleDegrees.ShouldBe(jsonPin.AngleDegrees, 0.01,
                    $"{pinLabel}: Angle mismatch vs PDK JSON");
            }
        }
    }

    /// <summary>
    /// Verifies that rotated components preserve pin positions after roundtrip.
    /// </summary>
    [Theory]
    [InlineData("1x2 MMI Splitter", 1)]
    [InlineData("Phase Shifter", 1)]
    [InlineData("Directional Coupler", 2)]
    [InlineData("Grating Coupler", 3)]
    public async Task RotatedComponent_PinsAndSize_PreservedAfterRoundtrip(
        string templateName, int rotationSteps)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_rot_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();

            var template = _library.First(t => t.Name == templateName);
            var component = ComponentTemplates.CreateFromTemplate(template, x: 0, y: 0);
            var identifier = $"test_rotated_{templateName.Replace(" ", "_")}";
            component.Identifier = identifier;

            var vm = saveCanvas.AddComponent(component, template.Name);

            for (int i = 0; i < rotationSteps; i++)
                ApplyRotationToComponentViaViewModel(vm);

            var originalPins = component.PhysicalPins
                .Select(p => new PinState { Name = p.Name, OffsetX = p.OffsetXMicrometers, OffsetY = p.OffsetYMicrometers, Angle = p.AngleDegrees })
                .ToList();
            var originalWidth = component.WidthMicrometers;
            var originalHeight = component.HeightMicrometers;
            var originalRotation = (int)component.Rotation90CounterClock;

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            var loaded = loadCanvas.Components
                .FirstOrDefault(c => c.Component.Identifier == identifier);

            loaded.ShouldNotBeNull($"{templateName}: not found after load");

            var loadedComp = loaded!.Component;
            loadedComp.WidthMicrometers.ShouldBe(originalWidth, 0.01,
                $"{templateName} at {rotationSteps}×90°: Width changed");
            loadedComp.HeightMicrometers.ShouldBe(originalHeight, 0.01,
                $"{templateName} at {rotationSteps}×90°: Height changed");
            ((int)loadedComp.Rotation90CounterClock).ShouldBe(originalRotation,
                $"{templateName} at {rotationSteps}×90°: Rotation changed");

            loadedComp.PhysicalPins.Count.ShouldBe(originalPins.Count,
                $"{templateName} at {rotationSteps}×90°: pin count changed");

            for (int i = 0; i < originalPins.Count; i++)
            {
                var pin = loadedComp.PhysicalPins[i];
                var saved = originalPins[i];
                pin.OffsetXMicrometers.ShouldBe(saved.OffsetX, 0.01,
                    $"{templateName} pin[{i}] '{saved.Name}': OffsetX changed after rotation+roundtrip");
                pin.OffsetYMicrometers.ShouldBe(saved.OffsetY, 0.01,
                    $"{templateName} pin[{i}] '{saved.Name}': OffsetY changed after rotation+roundtrip");
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            _library,
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()));
        return (vm, canvas);
    }

    private async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("Design file must be created during save");
    }

    private async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }

    private static void ApplyRotationToComponentViaViewModel(ComponentViewModel compVm)
    {
        var comp = compVm.Component;
        var width = comp.WidthMicrometers;
        var height = comp.HeightMicrometers;

        foreach (var pin in comp.PhysicalPins)
        {
            var cx = width / 2;
            var cy = height / 2;
            var px = pin.OffsetXMicrometers - cx;
            var py = pin.OffsetYMicrometers - cy;
            pin.OffsetXMicrometers = -py + cy;
            pin.OffsetYMicrometers = px + cx;
        }

        comp.WidthMicrometers = height;
        comp.HeightMicrometers = width;
        comp.RotateBy90CounterClockwise();
        compVm.NotifyDimensionsChanged();
    }

    /// <summary>
    /// Loads Demo PDK components and returns them paired with their original JSON draft.
    /// Returns empty list if demo-pdk.json is not found (CI environments without bundled PDKs).
    /// </summary>
    private static List<(ComponentTemplate Template, PdkComponentDraft Draft)> LoadDemoPdkComponents()
    {
        var pdkPath = FindDemoPdkPath();
        if (pdkPath == null)
            return new List<(ComponentTemplate, PdkComponentDraft)>();

        var loader = new PdkLoader();
        var pdk = loader.LoadFromFile(pdkPath);

        return pdk.Components.Select(pdkComp =>
        {
            var pinDefs = pdkComp.Pins.Select(p =>
                new PinDefinition(p.Name, p.OffsetXMicrometers, p.OffsetYMicrometers, p.AngleDegrees))
                .ToArray();

            var firstPin = pdkComp.Pins.FirstOrDefault();
            var template = new ComponentTemplate
            {
                Name = pdkComp.Name,
                Category = pdkComp.Category,
                WidthMicrometers = pdkComp.WidthMicrometers,
                HeightMicrometers = pdkComp.HeightMicrometers,
                PinDefinitions = pinDefs,
                NazcaFunctionName = pdkComp.NazcaFunction,
                NazcaParameters = pdkComp.NazcaParameters,
                HasSlider = pdkComp.Sliders?.Any() ?? false,
                SliderMin = pdkComp.Sliders?.FirstOrDefault()?.MinVal ?? 0,
                SliderMax = pdkComp.Sliders?.FirstOrDefault()?.MaxVal ?? 100,
                PdkSource = pdk.Name,
                NazcaOriginOffsetX = firstPin?.OffsetXMicrometers ?? 0,
                NazcaOriginOffsetY = firstPin?.OffsetYMicrometers ?? 0,
                CreateSMatrix = pins =>
                {
                    var ids = pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();
                    return new SMatrix(ids, new List<(Guid, double)>());
                }
            };

            return (template, pdkComp);
        }).ToList();
    }

    private static string? FindDemoPdkPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", "demo-pdk.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "CAP-DataAccess", "PDKs", "demo-pdk.json"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class ComponentState
    {
        public string Identifier { get; set; } = "";
        public string TemplateName { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int Rotation { get; set; }
        public double? SliderValue { get; set; }
        public List<PinState> Pins { get; set; } = new();
    }

    private sealed class PinState
    {
        public string Name { get; set; } = "";
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double Angle { get; set; }
    }
}
