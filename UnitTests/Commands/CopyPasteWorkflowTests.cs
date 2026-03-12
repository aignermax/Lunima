using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>
/// Tests for copy/paste workflow (Ctrl+C / Ctrl+V).
/// Verifies that components can be copied, pasted with smart placement,
/// and automatically selected after paste.
/// </summary>
public class CopyPasteWorkflowTests
{
    /// <summary>
    /// Verifies that copying a single component stores it in the clipboard.
    /// </summary>
    [Fact]
    public void Copy_SingleComponent_StoresInClipboard()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateComponent(100, 50, 100, 100);
        var vm = canvas.AddComponent(comp);

        // Select the component
        canvas.Selection.SelectSingle(vm);

        // Copy to clipboard
        canvas.Clipboard.Copy(
            canvas.Selection.SelectedComponents.ToList(),
            canvas.Connections);

        canvas.Clipboard.HasContent.ShouldBeTrue("Clipboard should have content after copy");
    }

    /// <summary>
    /// Verifies that pasting creates a new component with an offset.
    /// </summary>
    [Fact]
    public void Paste_SingleComponent_CreatesNewComponentWithOffset()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateComponent(100, 50, 100, 100);
        var vm = canvas.AddComponent(comp);

        // Copy to clipboard
        canvas.Clipboard.Copy(
            new[] { vm },
            canvas.Connections);

        int initialCount = canvas.Components.Count;

        // Paste
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        canvas.Components.Count.ShouldBe(initialCount + 1, "One new component should be created");
        cmd.Result.ShouldNotBeNull("Paste result should contain pasted components");
        cmd.Result.Components.Count.ShouldBe(1, "Should paste exactly one component");

        var pastedVm = cmd.Result.Components[0];
        pastedVm.X.ShouldNotBe(vm.X, "Pasted component should have different X position");
        pastedVm.Y.ShouldNotBe(vm.Y, "Pasted component should have different Y position");
    }

    /// <summary>
    /// Verifies that pasting multiple components preserves their relative positions.
    /// </summary>
    [Fact]
    public void Paste_MultipleComponents_PreservesRelativePositions()
    {
        var canvas = new DesignCanvasViewModel();
        var comp1 = CreateComponent(100, 50, 100, 100);
        var comp2 = CreateComponent(100, 50, 300, 200);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        double deltaXOriginal = vm2.X - vm1.X;
        double deltaYOriginal = vm2.Y - vm1.Y;

        // Copy both components
        canvas.Clipboard.Copy(
            new[] { vm1, vm2 },
            canvas.Connections);

        // Paste
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        cmd.Result.ShouldNotBeNull();
        cmd.Result.Components.Count.ShouldBe(2, "Should paste both components");

        var pasted1 = cmd.Result.Components[0];
        var pasted2 = cmd.Result.Components[1];

        double deltaXPasted = pasted2.X - pasted1.X;
        double deltaYPasted = pasted2.Y - pasted1.Y;

        Math.Abs(deltaXPasted - deltaXOriginal).ShouldBeLessThan(1.0,
            "Relative X distance should be preserved");
        Math.Abs(deltaYPasted - deltaYOriginal).ShouldBeLessThan(1.0,
            "Relative Y distance should be preserved");
    }

    /// <summary>
    /// Verifies that pasting components with internal connections preserves those connections.
    /// </summary>
    [Fact]
    public void Paste_ConnectedComponents_PreservesConnections()
    {
        var canvas = new DesignCanvasViewModel();

        var comp1 = CreateComponentWithPins(100, 50, 100, 100);
        var comp2 = CreateComponentWithPins(100, 50, 300, 100);

        var vm1 = canvas.AddComponent(comp1);
        var vm2 = canvas.AddComponent(comp2);

        // Connect the components
        var pin1 = comp1.PhysicalPins[1]; // east0
        var pin2 = comp2.PhysicalPins[0]; // west0

        canvas.ConnectPins(pin1, pin2);

        int initialConnectionCount = canvas.Connections.Count;

        // Copy both components
        canvas.Clipboard.Copy(
            new[] { vm1, vm2 },
            canvas.Connections);

        // Paste
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        cmd.Result.ShouldNotBeNull();
        cmd.Result.Components.Count.ShouldBe(2, "Should paste both components");
        cmd.Result.Connections.Count.ShouldBe(1, "Should paste the internal connection");

        canvas.Connections.Count.ShouldBe(initialConnectionCount + 1,
            "Total connections should increase by 1");
    }

    /// <summary>
    /// Verifies that undo removes pasted components and connections.
    /// </summary>
    [Fact]
    public void Paste_Undo_RemovesPastedComponents()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateComponent(100, 50, 100, 100);
        var vm = canvas.AddComponent(comp);

        canvas.Clipboard.Copy(
            new[] { vm },
            canvas.Connections);

        int initialCount = canvas.Components.Count;

        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        canvas.Components.Count.ShouldBe(initialCount + 1);

        cmd.Undo();

        canvas.Components.Count.ShouldBe(initialCount,
            "Undo should remove pasted component");
    }

    /// <summary>
    /// Verifies that pasting when clipboard is empty returns null.
    /// </summary>
    [Fact]
    public void Paste_EmptyClipboard_ReturnsNull()
    {
        var canvas = new DesignCanvasViewModel();

        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        cmd.Result.ShouldBeNull("Paste should return null when clipboard is empty");
    }

    /// <summary>
    /// Verifies that pasting at a specific cursor position places the component at that location.
    /// </summary>
    [Fact]
    public void Paste_AtCursorPosition_PlacesAtSpecifiedLocation()
    {
        var canvas = new DesignCanvasViewModel();

        // Place original component at (100, 100)
        var comp1 = CreateComponent(100, 50, 100, 100);
        var vm1 = canvas.AddComponent(comp1);

        // Copy it
        canvas.Clipboard.Copy(
            new[] { vm1 },
            canvas.Connections);

        // Paste at cursor position (300, 200)
        var targetX = 300.0;
        var targetY = 200.0;
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard, targetX, targetY);
        cmd.Execute();

        cmd.Result.ShouldNotBeNull();
        var pastedVm = cmd.Result.Components[0];

        // The pasted component should be at the cursor position
        pastedVm.X.ShouldBe(targetX, "Pasted component should be at cursor X");
        pastedVm.Y.ShouldBe(targetY, "Pasted component should be at cursor Y");
    }

    /// <summary>
    /// Verifies that multiple paste operations create multiple copies.
    /// </summary>
    [Fact]
    public void Paste_MultipleTimes_CreatesMultipleCopies()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateComponent(100, 50, 100, 100);
        var vm = canvas.AddComponent(comp);

        canvas.Clipboard.Copy(
            new[] { vm },
            canvas.Connections);

        int initialCount = canvas.Components.Count;

        // Paste twice
        var cmd1 = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd1.Execute();

        var cmd2 = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd2.Execute();

        canvas.Components.Count.ShouldBe(initialCount + 2,
            "Should create 2 new components from 2 paste operations");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Component CreateComponent(
        double widthMicrometers,
        double heightMicrometers,
        double x = 0,
        double y = 0)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            parts,
            0,
            "TestComp",
            DiscreteRotation.R0);

        component.WidthMicrometers = widthMicrometers;
        component.HeightMicrometers = heightMicrometers;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }

    private static Component CreateComponentWithPins(
        double widthMicrometers,
        double heightMicrometers,
        double x,
        double y)
    {
        var physicalPins = new List<PhysicalPin>
        {
            new() { Name = "west0", OffsetXMicrometers = 0, OffsetYMicrometers = heightMicrometers / 2 },
            new() { Name = "east0", OffsetXMicrometers = widthMicrometers, OffsetYMicrometers = heightMicrometers / 2 },
        };

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right),
        });

        var component = new Component(
            new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            parts,
            0,
            "TestComp",
            DiscreteRotation.R0,
            physicalPins);

        component.WidthMicrometers = widthMicrometers;
        component.HeightMicrometers = heightMicrometers;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}
