using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Creation;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using System.Numerics;
using Xunit;

namespace UnitTests.Simulation;

/// <summary>
/// Regression tests for issue #405: Prefab components lose S-Matrix after app restart.
///
/// Root cause: GroupTemplateSerializer must correctly round-trip S-Matrix data so that
/// child components of a prefab remain simulation-ready after save → disk → reload.
///
/// All tests simulate the "app restart" by going through the full disk round-trip:
/// serialize → file → deserialize → instantiate → simulate.
/// </summary>
public class PrefabSMatrixPreservationTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Grating-Coupler-like component with only the 1550 nm S-Matrix.
    /// Models the real "Grating Coupler TE 1550" PDK component that triggered #405.
    /// </summary>
    private static Component CreateGratingCoupler1550()
    {
        var logicalPin = new Pin("port_1", 0, MatterType.Light, RectSide.Left);
        var allPinIds = new List<Guid> { logicalPin.IDInFlow, logicalPin.IDOutFlow };

        var sMatrix1550 = new SMatrix(allPinIds, new());
        sMatrix1550.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (logicalPin.IDInFlow, logicalPin.IDOutFlow), new Complex(0.9, 0) }
        });

        var sMatrixMap = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, sMatrix1550 }   // 1550 nm only
        };

        return new Component(
            sMatrixMap,
            new List<Slider>(),
            "ebeam_gc_te1550",
            "",
            new Part[1, 1] { { new Part() } },
            100,
            $"gc_{Guid.NewGuid():N}",
            DiscreteRotation.R0,
            new List<PhysicalPin>
            {
                new PhysicalPin
                {
                    Name = "port_1",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 0,
                    AngleDegrees = 90,
                    LogicalPin = logicalPin
                }
            })
        {
            PhysicalX = 0,
            PhysicalY = 0,
            WidthMicrometers = 30,
            HeightMicrometers = 30,
            HumanReadableName = "Grating Coupler TE 1550"
        };
    }

    /// <summary>
    /// Creates an MMI-like component with S-Matrices for 1550 nm and 1310 nm.
    /// </summary>
    private static Component CreateMmi()
    {
        var pinA = new Pin("a0", 0, MatterType.Light, RectSide.Left);
        var pinB0 = new Pin("b0", 1, MatterType.Light, RectSide.Right);
        var pinB1 = new Pin("b1", 2, MatterType.Light, RectSide.Right);

        var allPinIds = new List<Guid>
        {
            pinA.IDInFlow, pinA.IDOutFlow,
            pinB0.IDInFlow, pinB0.IDOutFlow,
            pinB1.IDInFlow, pinB1.IDOutFlow
        };

        var sMatrix1550 = new SMatrix(allPinIds, new());
        sMatrix1550.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (pinA.IDInFlow, pinB0.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinA.IDInFlow, pinB1.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinB0.IDInFlow, pinA.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinB1.IDInFlow, pinA.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) }
        });

        // Second wavelength – same values for simplicity
        var sMatrix1310 = new SMatrix(allPinIds, new());
        sMatrix1310.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (pinA.IDInFlow, pinB0.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinA.IDInFlow, pinB1.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinB0.IDInFlow, pinA.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) },
            { (pinB1.IDInFlow, pinA.IDOutFlow), new Complex(Math.Sqrt(0.5), 0) }
        });

        var sMatrixMap = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM,   sMatrix1550 },
            { StandardWaveLengths.GreenNM, sMatrix1310 }
        };

        return new Component(
            sMatrixMap,
            new List<Slider>(),
            "mmi_1x2",
            "",
            new Part[1, 1] { { new Part() } },
            200,
            $"mmi_{Guid.NewGuid():N}",
            DiscreteRotation.R0,
            new List<PhysicalPin>
            {
                new PhysicalPin { Name = "a0", OffsetXMicrometers = 0,  OffsetYMicrometers = 0,  AngleDegrees = 180, LogicalPin = pinA  },
                new PhysicalPin { Name = "b0", OffsetXMicrometers = 50, OffsetYMicrometers = 5,  AngleDegrees = 0,   LogicalPin = pinB0 },
                new PhysicalPin { Name = "b1", OffsetXMicrometers = 50, OffsetYMicrometers = -5, AngleDegrees = 0,   LogicalPin = pinB1 }
            })
        {
            PhysicalX = 50,
            PhysicalY = 0,
            WidthMicrometers = 50,
            HeightMicrometers = 20,
            HumanReadableName = "MMI 1x2"
        };
    }

    /// <summary>
    /// Builds a prefab group with a Grating Coupler and an MMI joined by a frozen path.
    /// Mirrors the exact topology described in issue #405.
    /// </summary>
    private static ComponentGroup BuildGcMmiPrefab()
    {
        var gc  = CreateGratingCoupler1550();
        var mmi = CreateMmi();

        var group = new ComponentGroup("GC_MMI_Prefab");
        group.AddChild(gc);
        group.AddChild(mmi);

        // Frozen internal path: GC port_1 → MMI a0
        var frozenPath = new FrozenWaveguidePath
        {
            PathId = Guid.NewGuid(),
            StartPin = gc.PhysicalPins[0],   // port_1
            EndPin   = mmi.PhysicalPins[0],  // a0
            Path     = new RoutedPath()
        };
        frozenPath.Path.Segments.Add(new StraightSegment(30, 0, 50, 0, 0));
        group.AddInternalPath(frozenPath);

        // Expose MMI output pins as external group pins
        group.AddExternalPin(new GroupPin
        {
            PinId        = Guid.NewGuid(),
            Name         = "out0",
            InternalPin  = mmi.PhysicalPins[1],  // b0
            RelativeX    = 100,
            RelativeY    = 5,
            AngleDegrees = 0
        });
        group.AddExternalPin(new GroupPin
        {
            PinId        = Guid.NewGuid(),
            Name         = "out1",
            InternalPin  = mmi.PhysicalPins[2],  // b1
            RelativeX    = 100,
            RelativeY    = -5,
            AngleDegrees = 0
        });

        group.ComputeSMatrix();
        group.IsPrefab = true;

        return group;
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issue #405 core scenario: GC+MMI prefab saves to library, app restarts, user loads
    /// and instantiates the prefab, then runs simulation → must not crash.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_AfterRestartAndInstantiation_SimulatesWithout1550Crash()
    {
        // Arrange – create and save prefab (session A)
        var prefab = BuildGcMmiPrefab();
        var tempDir = Path.Combine(Path.GetTempPath(), $"lunima_405_{Guid.NewGuid():N}");
        var library = new GroupLibraryManager(tempDir);
        library.SaveTemplate(prefab, "GC_MMI");

        // "App restart" – create a fresh library and reload from disk
        var freshLibrary = new GroupLibraryManager(tempDir);
        freshLibrary.LoadTemplates();

        var loadedTemplate = freshLibrary.Templates.FirstOrDefault(t => t.Name == "GC_MMI");
        loadedTemplate.ShouldNotBeNull("Template must survive disk round-trip");

        // Instantiate from the reloaded template
        var instance = freshLibrary.InstantiateTemplate(loadedTemplate!, 100, 0);

        // Set up simulation context
        var tileManager       = new ComponentListTileManager();
        var connectionManager = new WaveguideConnectionManager(new WaveguideRouter());
        var portManager       = new PhysicalExternalPortManager();

        tileManager.AddComponent(instance);

        var gridManager = GridManager.CreateForSimulation(tileManager, connectionManager, portManager);
        var builder     = new SystemMatrixBuilder(gridManager);

        // Act – simulate at 1550 nm (the wavelength in the error message)
        var act = () => builder.GetSystemSMatrix(StandardWaveLengths.RedNM);

        // Assert
        act.ShouldNotThrow(
            "Simulation must not throw 'Matrix was not defined for waveLength 1550'");
    }

    /// <summary>
    /// Child components of a deserialized prefab must have non-empty WaveLengthToSMatrixMap
    /// so that SystemMatrixBuilder.CollectChildSMatrices does not throw.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_AfterDeserialize_ChildrenHaveSMatrices()
    {
        // Arrange
        var prefab     = BuildGcMmiPrefab();
        var serialized = GroupTemplateSerializer.Serialize(prefab);

        // Act – simulate "app restart" deserialization
        var loaded = GroupTemplateSerializer.Deserialize(serialized);

        // Assert
        loaded.ShouldNotBeNull();
        foreach (var child in loaded!.ChildComponents)
        {
            child.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0,
                $"Child '{child.HumanReadableName ?? child.Identifier}' must have S-Matrices after deserialization");
        }
    }

    /// <summary>
    /// S-Matrix VALUES (not just presence) must survive the round-trip.
    /// Regression guard: ensures the serializer does not silently zero out transfers.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_AfterDeserialize_SMatrixValuesAreCorrect()
    {
        // Arrange
        var gc         = CreateGratingCoupler1550();
        var group      = new ComponentGroup("TestGC");
        group.AddChild(gc);
        group.AddExternalPin(new GroupPin { Name = "p1", InternalPin = gc.PhysicalPins[0] });

        var serialized = GroupTemplateSerializer.Serialize(group);
        var loaded     = GroupTemplateSerializer.Deserialize(serialized);

        // Act
        var child = loaded!.ChildComponents[0];
        var sMatrix = child.WaveLengthToSMatrixMap[StandardWaveLengths.RedNM];
        var transfers = sMatrix.GetNonNullValues();

        // Assert – the 0.9 self-transfer from the original GC must survive
        transfers.Count.ShouldBeGreaterThan(0,
            "Non-zero S-Matrix transfers must survive serialization");

        var value = transfers.Values.FirstOrDefault();
        value.Real.ShouldBeGreaterThan(0,
            "Serialized transfer coefficient Real part must be non-zero");
    }

    /// <summary>
    /// LogicalPin GUIDs in deserialized physical pins must match the pin IDs
    /// stored in the corresponding S-Matrix. Mismatch causes silent simulation failure
    /// (light appears to disappear without error).
    /// </summary>
    [Fact]
    public void GcMmiPrefab_AfterDeserialize_LogicalPinGuidsMatchSMatrixPins()
    {
        // Arrange
        var prefab     = BuildGcMmiPrefab();
        var serialized = GroupTemplateSerializer.Serialize(prefab);
        var loaded     = GroupTemplateSerializer.Deserialize(serialized);

        // Assert – for every child component
        foreach (var child in loaded!.ChildComponents)
        {
            foreach (var physPin in child.PhysicalPins)
            {
                physPin.LogicalPin.ShouldNotBeNull(
                    $"Physical pin '{physPin.Name}' must have a LogicalPin after deserialization");

                var inFlow  = physPin.LogicalPin!.IDInFlow;
                var outFlow = physPin.LogicalPin.IDOutFlow;

                foreach (var sMatrix in child.WaveLengthToSMatrixMap.Values)
                {
                    sMatrix.PinReference.ContainsKey(inFlow).ShouldBeTrue(
                        $"SMatrix PinReference must contain IDInFlow {inFlow} of pin '{physPin.Name}'");
                    sMatrix.PinReference.ContainsKey(outFlow).ShouldBeTrue(
                        $"SMatrix PinReference must contain IDOutFlow {outFlow} of pin '{physPin.Name}'");
                }
            }
        }
    }

    /// <summary>
    /// The same template can be instantiated multiple times after restart;
    /// each instance must simulate independently without interference.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_MultipleInstantiationsAfterRestart_AllSimulateCorrectly()
    {
        // Arrange – save prefab to library and reload (simulate restart)
        var prefab  = BuildGcMmiPrefab();
        var tempDir = Path.Combine(Path.GetTempPath(), $"lunima_405_multi_{Guid.NewGuid():N}");

        var library = new GroupLibraryManager(tempDir);
        library.SaveTemplate(prefab, "GC_MMI_Multi");

        var freshLibrary = new GroupLibraryManager(tempDir);
        freshLibrary.LoadTemplates();

        var template = freshLibrary.Templates.First(t => t.Name == "GC_MMI_Multi");

        // Instantiate THREE times from the same reloaded template
        var instance1 = freshLibrary.InstantiateTemplate(template, 0,   0);
        var instance2 = freshLibrary.InstantiateTemplate(template, 200, 0);
        var instance3 = freshLibrary.InstantiateTemplate(template, 400, 0);

        foreach (var (instance, idx) in new[] { instance1, instance2, instance3 }
                                        .Select((g, i) => (g, i)))
        {
            var tileManager = new ComponentListTileManager();
            tileManager.AddComponent(instance);

            var gridManager = GridManager.CreateForSimulation(
                tileManager,
                new WaveguideConnectionManager(new WaveguideRouter()),
                new PhysicalExternalPortManager());

            var builder = new SystemMatrixBuilder(gridManager);

            var act = () => builder.GetSystemSMatrix(StandardWaveLengths.RedNM);
            act.ShouldNotThrow($"Instance {idx + 1} must simulate without crash");
        }

        try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// After loading a prefab and instantiating it, the children extracted by ungrouping
    /// must have non-null LogicalPins. "Pins turn red" in the UI = null LogicalPin.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_UngroupAfterRestart_ChildrenHaveNonNullLogicalPins()
    {
        // Arrange – full restart round-trip
        var prefab  = BuildGcMmiPrefab();
        var tempDir = Path.Combine(Path.GetTempPath(), $"lunima_405_ungroup_{Guid.NewGuid():N}");

        var library = new GroupLibraryManager(tempDir);
        library.SaveTemplate(prefab, "GC_MMI_Ungroup");

        var freshLibrary = new GroupLibraryManager(tempDir);
        freshLibrary.LoadTemplates();

        var template = freshLibrary.Templates.First(t => t.Name == "GC_MMI_Ungroup");
        var instance = freshLibrary.InstantiateTemplate(template, 0, 0);

        // Act – simulate "ungrouping" by extracting children
        var children = instance.ChildComponents;

        // Assert – every child physical pin that existed in the original must have a LogicalPin
        foreach (var child in children)
        {
            foreach (var pin in child.PhysicalPins)
            {
                pin.LogicalPin.ShouldNotBeNull(
                    $"Physical pin '{pin.Name}' on '{child.HumanReadableName ?? child.Identifier}' " +
                    $"must have a LogicalPin after prefab instantiation (pins must not turn red)");
            }
        }

        try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>
    /// A component with a single-wavelength S-Matrix (1550 nm only) must survive the
    /// round-trip and be used by the simulation correctly, with no "nearest-wavelength"
    /// fallback failure. This models a real Grating Coupler TE 1550 PDK component.
    /// </summary>
    [Fact]
    public void SingleWavelengthComponent_1550nm_SurvivesRoundTripAndSimulates()
    {
        // Arrange – single-wavelength GC inside a minimal prefab
        var gc    = CreateGratingCoupler1550();
        var group = new ComponentGroup("SingleWL");

        group.AddChild(gc);
        group.AddExternalPin(new GroupPin
        {
            PinId        = Guid.NewGuid(),
            Name         = "port_1",
            InternalPin  = gc.PhysicalPins[0],
            RelativeX    = 0,
            RelativeY    = 0,
            AngleDegrees = 90
        });

        group.ComputeSMatrix();

        // Round-trip through serializer
        var serialized = GroupTemplateSerializer.Serialize(group);
        var loaded     = GroupTemplateSerializer.Deserialize(serialized);

        loaded.ShouldNotBeNull();
        loaded!.EnsureSMatrixComputed();

        // Child must have exactly one wavelength: 1550 nm
        var child = loaded.ChildComponents[0];
        child.WaveLengthToSMatrixMap.Count.ShouldBe(1,
            "Single-wavelength GC must have exactly 1 S-Matrix after deserialization");
        child.WaveLengthToSMatrixMap.ContainsKey(StandardWaveLengths.RedNM).ShouldBeTrue(
            "The 1550 nm S-Matrix must be present after deserialization");

        // Simulate
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(loaded);

        var gridManager = GridManager.CreateForSimulation(
            tileManager,
            new WaveguideConnectionManager(new WaveguideRouter()),
            new PhysicalExternalPortManager());

        var act = () => new SystemMatrixBuilder(gridManager).GetSystemSMatrix(StandardWaveLengths.RedNM);
        act.ShouldNotThrow("Single-wavelength GC must simulate at 1550 nm after round-trip");
    }

    /// <summary>
    /// A multi-wavelength component (1550 nm + 1310 nm) must preserve ALL wavelengths
    /// after the prefab round-trip, not just the primary one.
    /// </summary>
    [Fact]
    public void MultiWavelengthMmi_PreservesAllWavelengthsAfterRoundTrip()
    {
        // Arrange
        var mmi    = CreateMmi();
        var group  = new ComponentGroup("MultiWL");
        group.AddChild(mmi);
        group.AddExternalPin(new GroupPin
        {
            Name        = "a0",
            InternalPin = mmi.PhysicalPins[0]   // a0
        });

        // Round-trip
        var serialized = GroupTemplateSerializer.Serialize(group);
        var loaded     = GroupTemplateSerializer.Deserialize(serialized);

        // Assert
        var child = loaded!.ChildComponents[0];
        child.WaveLengthToSMatrixMap.Count.ShouldBe(2,
            "Both 1550 nm and 1310 nm S-Matrices must survive deserialization");
        child.WaveLengthToSMatrixMap.ContainsKey(StandardWaveLengths.RedNM).ShouldBeTrue();
        child.WaveLengthToSMatrixMap.ContainsKey(StandardWaveLengths.GreenNM).ShouldBeTrue();
    }

    /// <summary>
    /// Frozen internal path transmission coefficients must survive the round-trip.
    /// If they are lost, the group S-Matrix computed after loading will be wrong.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_InternalPathsPreservedAfterRoundTrip()
    {
        // Arrange
        var prefab     = BuildGcMmiPrefab();
        var serialized = GroupTemplateSerializer.Serialize(prefab);
        var loaded     = GroupTemplateSerializer.Deserialize(serialized);

        // Assert – one internal path (GC → MMI)
        loaded.ShouldNotBeNull();
        loaded!.InternalPaths.Count.ShouldBe(1,
            "The GC→MMI frozen path must survive serialization");

        var path = loaded.InternalPaths[0];
        path.StartPin.ShouldNotBeNull();
        path.EndPin.ShouldNotBeNull();
        path.StartPin.LogicalPin.ShouldNotBeNull("Start pin of internal path must have LogicalPin");
        path.EndPin.LogicalPin.ShouldNotBeNull("End pin of internal path must have LogicalPin");
    }

    /// <summary>
    /// After the restart round-trip, the group-level S-Matrix computed from the
    /// deserialized children must be non-empty, indicating that the children's
    /// S-Matrices were restored correctly and the builder could combine them.
    /// </summary>
    [Fact]
    public void GcMmiPrefab_GroupSMatrixNonEmptyAfterRestartAndInstantiation()
    {
        // Arrange – full restart cycle via GroupLibraryManager
        var prefab  = BuildGcMmiPrefab();
        var tempDir = Path.Combine(Path.GetTempPath(), $"lunima_405_group_{Guid.NewGuid():N}");

        var library = new GroupLibraryManager(tempDir);
        library.SaveTemplate(prefab, "GC_MMI_Group");

        var freshLibrary = new GroupLibraryManager(tempDir);
        freshLibrary.LoadTemplates();

        var template = freshLibrary.Templates.First(t => t.Name == "GC_MMI_Group");
        var instance = freshLibrary.InstantiateTemplate(template, 0, 0);

        // Assert – group-level S-Matrix must be non-empty after EnsureSMatrixComputed
        instance.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0,
            "Group-level S-Matrix must be computed from the deserialized children");

        try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
    }
}
