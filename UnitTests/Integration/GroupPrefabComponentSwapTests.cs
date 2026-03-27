using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests for bug where components swap names after saving/loading group prefabs.
/// Reproduction: Create group with "Grating Coupler TE 1550" → Save as prefab →
/// Restart app → Load prefab → Component appears as "ebeam_gc_te1550"
/// </summary>
public class GroupPrefabComponentSwapTests
{
    [Fact]
    public void SaveAndLoadGroupPrefab_GratingCouplerComponent_PreservesDisplayName()
    {
        // Arrange: Create a component simulating "Grating Coupler TE 1550"
        var gratingCoupler = CreateGratingCouplerComponent();

        // Verify initial state (after recent changes, Identifier is GUID-based)
        gratingCoupler.NazcaFunctionName.ShouldBe("ebeam_gc_te1550");
        gratingCoupler.HumanReadableName.ShouldBe("Grating Coupler TE 1550_1");
        gratingCoupler.Identifier.ShouldNotBeNullOrWhiteSpace();

        // Create a group with this component
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gratingCoupler);

        // Act: Serialize and deserialize (simulating save/load)
        var json = GroupTemplateSerializer.Serialize(group);
        var loadedGroup = GroupTemplateSerializer.Deserialize(json);

        // Assert: Loaded component should preserve HumanReadableName
        loadedGroup.ShouldNotBeNull();
        loadedGroup!.ChildComponents.Count.ShouldBe(1);

        var loadedComponent = loadedGroup.ChildComponents[0];

        // The bug: HumanReadableName should be preserved, not showing NazcaFunctionName
        loadedComponent.HumanReadableName.ShouldBe("Grating Coupler TE 1550_1");
        loadedComponent.NazcaFunctionName.ShouldBe("ebeam_gc_te1550");
        loadedComponent.Identifier.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SaveAndLoadGroupPrefab_NestedGroupWithGratingCoupler_PreservesDisplayName()
    {
        // Arrange: Create nested structure
        var gratingCoupler = CreateGratingCouplerComponent();
        var mmi = CreateMMIComponent();

        var innerGroup = new ComponentGroup("InnerGroup");
        innerGroup.AddChild(gratingCoupler);
        innerGroup.AddChild(mmi);

        var outerGroup = new ComponentGroup("OuterGroup");
        outerGroup.AddChild(innerGroup);

        // Act: Serialize and deserialize
        var json = GroupTemplateSerializer.Serialize(outerGroup);
        var loadedGroup = GroupTemplateSerializer.Deserialize(json);

        // Assert: Navigate to nested component
        loadedGroup.ShouldNotBeNull();
        var loadedInnerGroup = loadedGroup!.ChildComponents[0].ShouldBeOfType<ComponentGroup>();
        var loadedGC = loadedInnerGroup.ChildComponents[0];
        var loadedMMI = loadedInnerGroup.ChildComponents[1];

        // Verify display names are preserved
        loadedGC.HumanReadableName.ShouldBe("Grating Coupler TE 1550_1");
        loadedGC.NazcaFunctionName.ShouldBe("ebeam_gc_te1550");

        loadedMMI.HumanReadableName.ShouldBe("MMI 1x2_1");
        loadedMMI.NazcaFunctionName.ShouldBe("mmi_1x2");
    }

    [Fact]
    public void InstantiateTemplate_GroupWithGratingCoupler_PreservesDisplayNameNotNazcaName()
    {
        // Arrange: Create a group with GC, serialize, deserialize (simulating save/restart)
        var gratingCoupler = CreateGratingCouplerComponent();
        var group = new ComponentGroup("TestGroup");
        group.AddChild(gratingCoupler);

        var json = GroupTemplateSerializer.Serialize(group);
        var loadedGroup = GroupTemplateSerializer.Deserialize(json);

        // Simulate GroupLibraryManager workflow
        var template = new GroupTemplate
        {
            Name = "My Template",
            TemplateGroup = loadedGroup!,
            Source = "User"
        };

        var libraryManager = new GroupLibraryManager();

        // Verify the template has the correct HumanReadableName before instantiation
        loadedGroup.ChildComponents[0].HumanReadableName.ShouldBe("Grating Coupler TE 1550_1");

        // Act: Instantiate the template (this is where the bug occurs)
        var instance = libraryManager.InstantiateTemplate(template, 100, 100);

        // Assert: Child component should have PDK name, NOT Nazca function name
        instance.ChildComponents.Count.ShouldBe(1);
        var child = instance.ChildComponents[0];

        // BUG: Was setting HumanReadableName to "ebeam_gc_te1550_1" instead of "Grating Coupler TE 1550_1"
        child.HumanReadableName.ShouldBe("Grating Coupler TE 1550_1");
        child.NazcaFunctionName.ShouldBe("ebeam_gc_te1550");
    }

    /// <summary>
    /// Creates a component simulating "Grating Coupler TE 1550" from the PDK.
    /// </summary>
    private Component CreateGratingCouplerComponent()
    {
        var pins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "port 1",
                OffsetXMicrometers = 15,
                OffsetYMicrometers = 30,
                AngleDegrees = 90
            },
            new PhysicalPin
            {
                Name = "port 2",
                OffsetXMicrometers = 30,
                OffsetYMicrometers = 15,
                AngleDegrees = 0
            }
        };

        // Use GUID-based identifier like the real system (after HumanReadableName refactoring)
        var identifier = $"comp_{Guid.NewGuid():N}";

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "ebeam_gc_te1550",  // This is the NazcaFunctionName
            "",
            new Part[1, 1] { { new Part() } },
            123, // TypeNumber
            identifier,  // GUID-based unique identifier
            DiscreteRotation.R0,
            pins)
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 30,
            HeightMicrometers = 30,
            HumanReadableName = "Grating Coupler TE 1550_1",  // This should be preserved
            NazcaModuleName = "siepic_ebeam_pdk"
        };
    }

    /// <summary>
    /// Creates a component simulating "MMI 1x2" from the PDK.
    /// </summary>
    private Component CreateMMIComponent()
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
            "mmi_1x2",  // NazcaFunctionName
            "",
            new Part[1, 1] { { new Part() } },
            456,
            identifier,  // GUID-based unique identifier
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
