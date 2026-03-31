using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Tests for issue #395: Light sources (Grating Couplers) not recognized after prefab ungroup.
///
/// Reproduction:
/// 1. Create a group with Grating Couplers + other components
/// 2. Save as prefab (serialize)
/// 3. Simulate app restart (deserialize)
/// 4. Instantiate prefab on canvas
/// 5. Ungroup (Ctrl+Shift+G)
/// 6. Run simulation → "No light sources found" (BUG)
/// </summary>
public class LightSourceAfterPrefabUngroupTests
{
    private static readonly int[] Wavelengths = { 1550 };

    /// <summary>
    /// Core bug: After serialize → deserialize → DeepCopy → ungroup,
    /// SimulationService.IsLightSource() should still detect Grating Couplers.
    /// </summary>
    [Fact]
    public void UngroupedPrefabComponents_AreDetectedAsLightSources()
    {
        // 1. Create grating coupler with proper name (as real app does)
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler(
            "Grating Coupler TE 1550_1", 0, 0, Wavelengths);
        var mmi = IntegrationCircuitBuilder.CreateSplitter(
            "MMI_1x2_1", 50, 0, Wavelengths);

        // 2. Group them
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc.Component);
        group.AddChild(mmi.Component);

        group.AddInternalPath(new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = new RoutedPath(),
            StartPin = gc.Pins["waveguide"],
            EndPin = mmi.Pins["in1"]
        });

        // 3. Save as prefab (serialize)
        var json = GroupTemplateSerializer.Serialize(group);

        // 4. Simulate app restart (deserialize)
        var loadedTemplate = GroupTemplateSerializer.Deserialize(json);
        loadedTemplate.ShouldNotBeNull();

        // 5. Instantiate from template (as library does)
        var libraryManager = new GroupLibraryManager();
        var template = new GroupTemplate
        {
            Name = "My Prefab",
            TemplateGroup = loadedTemplate!,
            Source = "User"
        };
        var instance = libraryManager.InstantiateTemplate(template, 100, 100);

        // 6. Ungroup — extract children (simulates UngroupCommand)
        var ungroupedChildren = instance.ChildComponents.ToList();

        // 7. Add ungrouped children to canvas and run light source detection
        var canvas = new DesignCanvasViewModel();
        foreach (var child in ungroupedChildren)
        {
            child.ParentGroup = null;
            canvas.AddComponent(child);
        }

        // 8. Run light source detection (as SimulationService does)
        var simService = new SimulationService();
        var portManager = new CAP_Core.Grid.PhysicalExternalPortManager();
        var sources = simService.ConfigureLightSources(canvas, portManager);

        // ASSERT: Should find the Grating Coupler as a light source
        sources.Count.ShouldBeGreaterThan(0,
            "BUG #395: No light sources found after prefab ungroup. " +
            "Grating Coupler should be detected as a light source.");
    }

    /// <summary>
    /// Verifies that the Identifier property survives the full pipeline
    /// and still contains "grating" for light source detection.
    /// </summary>
    [Fact]
    public void GratingCouplerIdentifier_SurvivesSerializeDeserializeDeepCopy()
    {
        // Create with proper PDK-like identifier
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler(
            "Grating Coupler TE 1550_1", 0, 0, Wavelengths);

        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc.Component);

        // Serialize → Deserialize (app restart)
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json)!;

        // After deserialize, identifier should be preserved
        loaded.ChildComponents[0].Identifier
            .ShouldContain("Grating Coupler", Case.Insensitive,
                "Identifier lost during serialization round-trip");

        // DeepCopy (template instantiation)
        var copy = loaded.DeepCopy();

        // After DeepCopy, identifier should still contain "Grating"
        copy.ChildComponents[0].Identifier
            .ShouldContain("grating", Case.Insensitive,
                "Identifier lost 'grating' during DeepCopy");
    }

    /// <summary>
    /// Verifies that LogicalPins with MatterType.Light survive the full pipeline.
    /// Even if IsLightSource() returns true, missing LogicalPins would prevent
    /// light source configuration.
    /// </summary>
    [Fact]
    public void LogicalPins_SurviveSerializeDeserializeDeepCopyPipeline()
    {
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler(
            "Grating Coupler TE 1550_1", 0, 0, Wavelengths);

        // Verify original has LogicalPin with MatterType.Light
        gc.Component.PhysicalPins[0].LogicalPin.ShouldNotBeNull();
        gc.Component.PhysicalPins[0].LogicalPin!.MatterType.ShouldBe(MatterType.Light);

        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc.Component);

        // Full pipeline
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json)!;
        var instance = loaded.DeepCopy();

        // After full pipeline, LogicalPins should still be intact
        var ungroupedGC = instance.ChildComponents[0];
        ungroupedGC.PhysicalPins.Count.ShouldBeGreaterThan(0,
            "Physical pins lost during pipeline");

        var pin = ungroupedGC.PhysicalPins[0];
        pin.LogicalPin.ShouldNotBeNull(
            "LogicalPin lost during serialize→deserialize→DeepCopy pipeline");
        pin.LogicalPin!.MatterType.ShouldBe(MatterType.Light,
            "MatterType.Light lost during pipeline");
        pin.LogicalPin.IDInFlow.ShouldNotBe(Guid.Empty,
            "LogicalPin IDInFlow is empty after pipeline");
    }

    /// <summary>
    /// Tests with GUID-based identifiers (as created by some code paths).
    /// This is the scenario where IsLightSource() fails because the Identifier
    /// doesn't contain "grating" — only HumanReadableName does.
    /// </summary>
    [Fact]
    public void GuidBasedIdentifier_WithGratingHumanReadableName_ShouldBeDetected()
    {
        // Simulate component created with GUID identifier but "Grating Coupler" HumanReadableName
        var gc = IntegrationCircuitBuilder.CreateGratingCoupler(
            $"comp_{Guid.NewGuid():N}", 0, 0, Wavelengths);
        gc.Component.HumanReadableName = "Grating Coupler TE 1550";

        // Add to canvas
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(gc.Component);

        // Run light source detection
        var simService = new SimulationService();
        var portManager = new CAP_Core.Grid.PhysicalExternalPortManager();
        var sources = simService.ConfigureLightSources(canvas, portManager);

        // This reveals whether IsLightSource checks Identifier vs HumanReadableName
        sources.Count.ShouldBeGreaterThan(0,
            "Light source not detected when Identifier is GUID-based. " +
            "IsLightSource() should also check HumanReadableName.");
    }

    /// <summary>
    /// Full end-to-end: create → group → save → load → instantiate → ungroup → simulate.
    /// Uses two Grating Couplers as specified in the issue.
    /// </summary>
    [Fact]
    public void FullPipeline_TwoGratingCouplers_DetectedAfterUngroup()
    {
        // Create 2 GCs + a phase shifter
        var gc1 = IntegrationCircuitBuilder.CreateGratingCoupler(
            "Grating Coupler TE 1550_1", 0, 0, Wavelengths);
        var gc2 = IntegrationCircuitBuilder.CreateGratingCoupler(
            "Grating Coupler_2", 100, 0, Wavelengths);
        var splitter = IntegrationCircuitBuilder.CreateSplitter(
            "Splitter_1", 50, 0, Wavelengths);

        // Group
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gc1.Component);
        group.AddChild(gc2.Component);
        group.AddChild(splitter.Component);

        group.AddInternalPath(new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            Path = new RoutedPath(),
            StartPin = gc1.Pins["waveguide"],
            EndPin = splitter.Pins["in1"]
        });

        // Save → Load (app restart)
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json)!;

        // Instantiate
        var libraryManager = new GroupLibraryManager();
        var template = new GroupTemplate
        {
            Name = "Two GC Prefab",
            TemplateGroup = loaded,
            Source = "User"
        };
        var instance = libraryManager.InstantiateTemplate(template, 200, 200);

        // Ungroup + add to canvas
        var canvas = new DesignCanvasViewModel();
        foreach (var child in instance.ChildComponents.ToList())
        {
            child.ParentGroup = null;
            canvas.AddComponent(child);
        }

        // Detect light sources
        var simService = new SimulationService();
        var portManager = new CAP_Core.Grid.PhysicalExternalPortManager();
        var sources = simService.ConfigureLightSources(canvas, portManager);

        // Should find BOTH Grating Couplers
        sources.Count.ShouldBe(2,
            $"Expected 2 light sources (2 Grating Couplers), found {sources.Count}. " +
            "Identifiers: " + string.Join(", ",
                instance.ChildComponents.Select(c =>
                    $"[Id={c.Identifier}, HR={c.HumanReadableName}]")));
    }
}
