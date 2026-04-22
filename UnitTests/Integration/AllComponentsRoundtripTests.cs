using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.Integration;

/// <summary>
/// Master roundtrip test that validates ALL component types preserve their
/// position, size, rotation, and slider values after a complete save/load cycle.
/// Systematically catches any persistence regression across the entire PDK.
/// Covers issue #357.
/// </summary>
public class AllComponentsRoundtripTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    /// <summary>Initializes the test suite with the full component library.</summary>
    public AllComponentsRoundtripTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Verifies that every component template preserves X, Y, Width, Height,
    /// Rotation, and SliderValue (where applicable) after a complete save/load roundtrip.
    /// This test will fail for any component that has a persistence size/position bug.
    /// </summary>
    [Fact]
    public async Task AllComponentTypes_SaveLoadRoundtrip_PreservePositionAndSize()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_all_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            var originalStates = new List<ComponentState>();
            double x = 0;

            // Create one instance of each template and record the original state
            foreach (var template in _library)
            {
                var component = ComponentTemplates.CreateFromTemplate(template, x, y: 100);
                var identifier = $"test_{template.Name.Replace(" ", "_")}";
                component.Identifier = identifier;

                var vm = saveCanvas.AddComponent(component, template.Name);

                // Set slider to midpoint to test slider persistence
                if (template.HasSlider)
                {
                    var midpoint = (template.SliderMin + template.SliderMax) / 2.0;
                    vm.SliderValue = midpoint;
                }

                originalStates.Add(new ComponentState
                {
                    Identifier = identifier,
                    TemplateName = template.Name,
                    X = component.PhysicalX,
                    Y = component.PhysicalY,
                    Width = component.WidthMicrometers,
                    Height = component.HeightMicrometers,
                    Rotation = (int)component.Rotation90CounterClock,
                    SliderValue = template.HasSlider ? vm.SliderValue : (double?)null
                });

                x += 600; // Space components apart so they don't overlap
            }

            await SaveToFile(saveVm, tempFile);

            // Load design into a fresh canvas
            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Components.Count.ShouldBe(
                originalStates.Count,
                "All components must survive save/load roundtrip");

            // Verify each component's properties are preserved
            foreach (var original in originalStates)
            {
                var loadedVm = loadCanvas.Components
                    .FirstOrDefault(c => c.Component.Identifier == original.Identifier);

                loadedVm.ShouldNotBeNull(
                    $"{original.TemplateName}: component not found after load");

                loadedVm!.Component.PhysicalX.ShouldBe(original.X, 0.01,
                    $"{original.TemplateName}: X position changed after roundtrip");

                loadedVm.Component.PhysicalY.ShouldBe(original.Y, 0.01,
                    $"{original.TemplateName}: Y position changed after roundtrip");

                loadedVm.Component.WidthMicrometers.ShouldBe(original.Width, 0.01,
                    $"{original.TemplateName}: WidthMicrometers changed after roundtrip");

                loadedVm.Component.HeightMicrometers.ShouldBe(original.Height, 0.01,
                    $"{original.TemplateName}: HeightMicrometers changed after roundtrip");

                ((int)loadedVm.Component.Rotation90CounterClock).ShouldBe(original.Rotation,
                    $"{original.TemplateName}: Rotation changed after roundtrip");

                if (original.SliderValue.HasValue)
                {
                    loadedVm.SliderValue.ShouldBe(original.SliderValue.Value, 0.01,
                        $"{original.TemplateName}: SliderValue changed after roundtrip");
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that position/size is preserved even when non-zero rotations are applied.
    /// Tests each rotated component separately to give clear diagnostics per component.
    /// </summary>
    [Theory]
    [InlineData("1x2 MMI Splitter", 1)]
    [InlineData("Phase Shifter", 1)]
    [InlineData("Directional Coupler", 2)]
    [InlineData("Grating Coupler", 3)]
    public async Task RotatedComponent_SaveLoadRoundtrip_PreservesSize(
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

            // Apply rotation steps
            for (int i = 0; i < rotationSteps; i++)
            {
                ApplyRotationToComponentViaViewModel(vm);
            }

            var originalWidth = component.WidthMicrometers;
            var originalHeight = component.HeightMicrometers;
            var originalRotation = (int)component.Rotation90CounterClock;

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            var loadedVm = loadCanvas.Components
                .FirstOrDefault(c => c.Component.Identifier == identifier);

            loadedVm.ShouldNotBeNull($"{templateName}: component not found after load");

            loadedVm!.Component.WidthMicrometers.ShouldBe(originalWidth, 0.01,
                $"{templateName} at rotation {rotationSteps}×90°: Width changed");

            loadedVm.Component.HeightMicrometers.ShouldBe(originalHeight, 0.01,
                $"{templateName} at rotation {rotationSteps}×90°: Height changed");

            ((int)loadedVm.Component.Rotation90CounterClock).ShouldBe(originalRotation,
                $"{templateName} at rotation {rotationSteps}×90°: Rotation changed");
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
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas));
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

    /// <summary>
    /// Applies a single 90° CCW rotation using the same logic as FileOperationsViewModel.ApplyRotationToComponent.
    /// Mirrors the load-time rotation path to produce a valid pre-save state.
    /// </summary>
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

    /// <summary>Snapshot of a component's key properties before save.</summary>
    private sealed class ComponentState
    {
        /// <summary>Unique identifier used to find the component after load.</summary>
        public string Identifier { get; set; } = "";

        /// <summary>Template name for human-readable failure messages.</summary>
        public string TemplateName { get; set; } = "";

        /// <summary>X position in micrometers.</summary>
        public double X { get; set; }

        /// <summary>Y position in micrometers.</summary>
        public double Y { get; set; }

        /// <summary>Width in micrometers.</summary>
        public double Width { get; set; }

        /// <summary>Height in micrometers.</summary>
        public double Height { get; set; }

        /// <summary>Discrete rotation as integer (0–3).</summary>
        public int Rotation { get; set; }

        /// <summary>Slider value if component has a slider; null otherwise.</summary>
        public double? SliderValue { get; set; }
    }
}
