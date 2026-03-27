using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests for bug where ungrouping a loaded prefab instance loses HumanReadableName.
///
/// Scenario:
/// 1. Create group with GC 1550 + MMI 1x2
/// 2. Save as prefab
/// 3. Place prefab on canvas
/// 4. Save to disk (serialize)
/// 5. Load from disk (deserialize)
/// 6. Ungroup the loaded group
/// 7. Components should still have correct HumanReadableName
///
/// Bug: After step 6, components show "ebeam_gc_te1550_1" instead of "Grating Coupler TE 1550_1"
/// </summary>
public class UngroupLoadedPrefabTests
{
    [Fact]
    public void UngroupLoadedPrefab_ComponentsPreserveHumanReadableName()
    {
        // Step 1: Create group with GC + MMI
        var gc = CreateGratingCoupler();
        var mmi = CreateMMI();

        var originalGroup = new ComponentGroup("TestGroup");
        originalGroup.AddChild(gc);
        originalGroup.AddChild(mmi);

        // Step 2 & 3: Simulate saving as prefab and placing instance
        var libraryManager = new GroupLibraryManager();
        var json = GroupTemplateSerializer.Serialize(originalGroup);
        var loadedTemplate = GroupTemplateSerializer.Deserialize(json);

        var template = new GroupTemplate
        {
            Name = "My Prefab",
            TemplateGroup = loadedTemplate!,
            Source = "User"
        };

        var placedInstance = libraryManager.InstantiateTemplate(template, 100, 100);

        // Verify names before save/load cycle
        placedInstance.ChildComponents[0].HumanReadableName.ShouldStartWith("Grating Coupler TE 1550");
        placedInstance.ChildComponents[1].HumanReadableName.ShouldStartWith("MMI 1x2");

        // Step 4 & 5: Simulate save to disk and load from disk
        var serialized = GroupTemplateSerializer.Serialize(placedInstance);
        var loadedGroup = GroupTemplateSerializer.Deserialize(serialized);

        loadedGroup.ShouldNotBeNull();

        // Verify names are preserved after load
        loadedGroup!.ChildComponents[0].HumanReadableName.ShouldStartWith("Grating Coupler TE 1550");
        loadedGroup.ChildComponents[1].HumanReadableName.ShouldStartWith("MMI 1x2");

        // Step 6: Ungroup - extract children
        var extractedGC = loadedGroup.ChildComponents[0];
        var extractedMMI = loadedGroup.ChildComponents[1];

        // BUG: After ungrouping, components lose HumanReadableName
        // They should still show "Grating Coupler TE 1550" not "ebeam_gc_te1550"
        extractedGC.HumanReadableName.ShouldStartWith("Grating Coupler TE 1550");
        extractedGC.NazcaFunctionName.ShouldBe("ebeam_gc_te1550");

        extractedMMI.HumanReadableName.ShouldStartWith("MMI 1x2");
        extractedMMI.NazcaFunctionName.ShouldBe("mmi_1x2");
    }

    [Fact]
    public void UngroupLoadedPrefab_WithFullSaveLoadCycle_PreservesNames()
    {
        // This test uses the actual GridPersistenceWithGroupsManager to simulate
        // the full save/load cycle that the user experiences

        // Create prefab instance
        var gc = CreateGratingCoupler();
        var mmi = CreateMMI();

        var group = new ComponentGroup("MyPrefab_1");
        group.AddChild(gc);
        group.AddChild(mmi);

        // Mark as prefab instance (IsPrefab = true means it's an instance)
        group.IsPrefab = true;

        // Simulate the full persistence workflow
        // (We'd need GridPersistenceWithGroupsManager here, but it requires
        // a component factory which we don't have in unit tests)

        // For now, use GroupTemplateSerializer as a proxy
        var json = GroupTemplateSerializer.Serialize(group);
        var loadedGroup = GroupTemplateSerializer.Deserialize(json);

        loadedGroup.ShouldNotBeNull();

        // After loading, children should preserve HumanReadableName
        var loadedGC = loadedGroup!.ChildComponents[0];
        var loadedMMI = loadedGroup.ChildComponents[1];

        loadedGC.HumanReadableName.ShouldStartWith("Grating Coupler TE 1550");
        loadedMMI.HumanReadableName.ShouldStartWith("MMI 1x2");
    }

    private Component CreateGratingCoupler()
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "port 1",
                OffsetXMicrometers = 15,
                OffsetYMicrometers = 30,
                AngleDegrees = 90
            }
        };

        var identifier = $"comp_{Guid.NewGuid():N}";

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "ebeam_gc_te1550",
            "",
            new Part[1, 1] { { new Part() } },
            123,
            identifier,
            DiscreteRotation.R0,
            pins)
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 30,
            HeightMicrometers = 30,
            HumanReadableName = "Grating Coupler TE 1550_1",
            NazcaModuleName = "siepic_ebeam_pdk"
        };
    }

    private Component CreateMMI()
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 0, AngleDegrees = 180 },
            new PhysicalPin { Name = "b0", OffsetXMicrometers = 50, OffsetYMicrometers = 10, AngleDegrees = 0 },
            new PhysicalPin { Name = "b1", OffsetXMicrometers = 50, OffsetYMicrometers = -10, AngleDegrees = 0 }
        };

        var identifier = $"comp_{Guid.NewGuid():N}";

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "mmi_1x2",
            "",
            new Part[1, 1] { { new Part() } },
            456,
            identifier,
            DiscreteRotation.R0,
            pins)
        {
            PhysicalX = 50,
            PhysicalY = 0,
            WidthMicrometers = 50,
            HeightMicrometers = 20,
            HumanReadableName = "MMI 1x2_1",
            NazcaModuleName = "siepic_ebeam_pdk"
        };
    }
}
