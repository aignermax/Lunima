using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Regression tests for issue #394: Prefab pins turn red after ungroup on app restart.
///
/// Root cause: GroupTemplateSerializer.DeserializeComponent() created PhysicalPins without
/// restoring LogicalPin, causing pin.LogicalPin == null after deserialization.
/// Rendering code treats LogicalPin == null as an invalid (red) pin.
///
/// Fix: PinDto now stores LogicalPin GUIDs; DeserializeComponent() restores them.
/// </summary>
public class PrefabPinStateAfterUngroupTests
{
    /// <summary>
    /// This test demonstrates the bug: pins turn red after serialize/deserialize + ungroup.
    /// It passes only after the fix in GroupTemplateSerializer.DeserializeComponent().
    /// </summary>
    [Fact]
    public void UngroupAfterAppRestart_AllPinsHaveLogicalPin()
    {
        // Step 1: Create components with LogicalPins (simulating PDK-loaded components)
        var gc1 = CreateGratingCouplerWithLogicalPins("gc1");
        var gc2 = CreateGratingCouplerWithLogicalPins("gc2");

        // Step 2: Create group (simulating user grouping components)
        var originalGroup = new ComponentGroup("TestPrefab");
        originalGroup.AddChild(gc1);
        originalGroup.AddChild(gc2);

        // Step 3: Save as template and immediately deserialize (simulates save to disk)
        var json = GroupTemplateSerializer.Serialize(originalGroup);

        // Step 4: Deserialize — simulates app restart + loading template from disk
        var loadedTemplate = GroupTemplateSerializer.Deserialize(json);
        loadedTemplate.ShouldNotBeNull();

        // Step 5: Instantiate the loaded template (simulates dragging from library)
        var libraryManager = new GroupLibraryManager();
        var template = new GroupTemplate
        {
            Name = "TestPrefab",
            TemplateGroup = loadedTemplate!,
            Source = "User"
        };
        var instance = libraryManager.InstantiateTemplate(template, 100, 100);

        // Step 6: Ungroup — extract child components
        var ungroupedChildren = instance.ChildComponents.ToList();

        // Assert: all pins on ungrouped children must have a valid LogicalPin (not null)
        // Before fix: LogicalPin was null → pins rendered red
        // After fix: LogicalPin is restored → pins rendered green
        foreach (var child in ungroupedChildren)
        {
            foreach (var pin in child.PhysicalPins)
            {
                pin.LogicalPin.ShouldNotBeNull(
                    $"Pin '{pin.Name}' on component '{child.Name}' should have a " +
                    $"LogicalPin after ungroup. LogicalPin == null causes pins to render red.");
            }
        }
    }

    [Fact]
    public void SerializeDeserialize_PreservesLogicalPinGUIDs()
    {
        // Arrange: component with LogicalPins that have specific GUIDs
        var comp = CreateGratingCouplerWithLogicalPins("gc1");
        var group = new ComponentGroup("GuidTest");
        group.AddChild(comp);

        var originalIDInFlow = comp.PhysicalPins[0].LogicalPin!.IDInFlow;
        var originalIDOutFlow = comp.PhysicalPins[0].LogicalPin!.IDOutFlow;

        // Act: roundtrip through serialization
        var json = GroupTemplateSerializer.Serialize(group);
        var loaded = GroupTemplateSerializer.Deserialize(json);

        // Assert: GUIDs are preserved
        var loadedPin = loaded!.ChildComponents[0].PhysicalPins[0];
        loadedPin.LogicalPin.ShouldNotBeNull();
        loadedPin.LogicalPin!.IDInFlow.ShouldBe(originalIDInFlow,
            "IDInFlow GUID must be preserved to maintain S-Matrix simulation integrity");
        loadedPin.LogicalPin!.IDOutFlow.ShouldBe(originalIDOutFlow,
            "IDOutFlow GUID must be preserved to maintain S-Matrix simulation integrity");
    }

    [Fact]
    public void DeserializeOldTemplate_WithoutLogicalPinGUIDs_StillCreatesValidLogicalPin()
    {
        // Arrange: JSON without LogicalPinIDInFlow/LogicalPinIDOutFlow fields (old format)
        const string oldFormatJson = """
            {
              "GroupName": "OldGroup",
              "Description": "",
              "Identifier": "old_group",
              "PhysicalX": 0,
              "PhysicalY": 0,
              "WidthMicrometers": 50,
              "HeightMicrometers": 30,
              "Rotation": 0,
              "Children": [
                {
                  "IsGroup": false,
                  "Identifier": "comp_old",
                  "HumanReadableName": "Old Component",
                  "NazcaFunctionName": "test_func",
                  "NazcaFunctionParameters": "",
                  "NazcaModuleName": null,
                  "TypeNumber": 1,
                  "PhysicalX": 0,
                  "PhysicalY": 0,
                  "WidthMicrometers": 50,
                  "HeightMicrometers": 30,
                  "Rotation": 0,
                  "Pins": [
                    { "Name": "a0", "OffsetX": 0, "OffsetY": 0, "AngleDegrees": 180 },
                    { "Name": "b0", "OffsetX": 50, "OffsetY": 0, "AngleDegrees": 0 }
                  ]
                }
              ],
              "InternalPaths": [],
              "ExternalPins": []
            }
            """;

        // Act: deserialize old-format template
        var group = GroupTemplateSerializer.Deserialize(oldFormatJson);

        // Assert: pins still get LogicalPins (fresh GUIDs) for backward compat
        group.ShouldNotBeNull();
        var pins = group!.ChildComponents[0].PhysicalPins;
        pins[0].LogicalPin.ShouldNotBeNull("Old-format pins should still get a LogicalPin");
        pins[1].LogicalPin.ShouldNotBeNull("Old-format pins should still get a LogicalPin");
    }

    [Fact]
    public void UngroupAfterAppRestart_ConnectionsHaveValidPins()
    {
        // Verifies that frozen paths → connections can be built after ungroup
        // because StartPin/EndPin have LogicalPins required by WaveguideConnectionManager
        var gc1 = CreateGratingCouplerWithLogicalPins("gc1");
        var gc2 = CreateGratingCouplerWithLogicalPins("gc2");

        var originalGroup = new ComponentGroup("ConnTest");
        originalGroup.AddChild(gc1);
        originalGroup.AddChild(gc2);

        // Add internal path connecting gc1's b0 to gc2's a0
        var frozenPath = new CAP_Core.Components.Core.FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            StartPin = gc1.PhysicalPins[1],
            EndPin = gc2.PhysicalPins[0],
            Path = new CAP_Core.Routing.RoutedPath()
        };
        originalGroup.AddInternalPath(frozenPath);

        // Simulate app restart
        var json = GroupTemplateSerializer.Serialize(originalGroup);
        var loaded = GroupTemplateSerializer.Deserialize(json)!;

        // After ungroup, internal paths expose StartPin and EndPin
        foreach (var path in loaded.InternalPaths)
        {
            path.StartPin.LogicalPin.ShouldNotBeNull(
                "StartPin.LogicalPin must not be null — needed by WaveguideConnectionManager");
            path.EndPin.LogicalPin.ShouldNotBeNull(
                "EndPin.LogicalPin must not be null — needed by WaveguideConnectionManager");
        }
    }

    /// <summary>
    /// Creates a Grating Coupler component with proper LogicalPins,
    /// simulating a component loaded from a PDK template.
    /// </summary>
    private static Component CreateGratingCouplerWithLogicalPins(string identifier)
    {
        var logicalPinA = new Pin("a0", 0, MatterType.Light, RectSide.Left);
        var logicalPinB = new Pin("b0", 1, MatterType.Light, RectSide.Right);

        var physicalPins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = "a0",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = 15,
                AngleDegrees = 180,
                LogicalPin = logicalPinA
            },
            new PhysicalPin
            {
                Name = "b0",
                OffsetXMicrometers = 30,
                OffsetYMicrometers = 15,
                AngleDegrees = 0,
                LogicalPin = logicalPinB
            }
        };

        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "ebeam_gc_te1550",
            "",
            new Part[1, 1] { { new Part() } },
            123,
            identifier,
            DiscreteRotation.R0,
            physicalPins)
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 30,
            HeightMicrometers = 30,
            HumanReadableName = "Grating Coupler TE 1550",
            NazcaModuleName = "siepic_ebeam_pdk"
        };
    }
}
