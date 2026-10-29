using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;

namespace UnitTests.Routing.LengthEqualization;

/// <summary>
/// Builds the shipped example "Length Matching - Fiber Array Fan-In" through the commands the UI
/// runs: a column of grating couplers at the 127 µm fibre-array pitch feeds a denser, offset
/// column of on-chip waveguides. The router's routes differ in length by several hundred µm —
/// the starting point for "select all, Equalize lengths".
/// </summary>
internal static class FiberArrayExampleBuilder
{
    public const string ExampleFileName = "Length Matching - Fiber Array Fan-In.lun";
    public const int ChannelCount = 16;
    private const string DemoPdk = "Demo PDK";
    private const double ChipWidth = 2600;
    private const double ChipHeight = 2600;
    private const double FiberPitch = 127;
    private const double ChipPitch = 50;
    private const double CouplerX = 150;
    private const double CouplerTop = 300;
    private const double WaveguideX = 1700;
    private const double WaveguideTop = 1150;

    /// <summary>Builds the fan-in in <paramref name="vm"/> and returns its connections.</summary>
    public static async Task<List<WaveguideConnectionViewModel>> BuildAsync(MainViewModel vm)
    {
        var canvas = vm.Canvas;
        vm.FileOperations.SetActiveProcess(ProcessOf(vm), markDirty: false);
        vm.ChipSize.ApplyFromMicrometers(ChipWidth, ChipHeight);

        var connections = new List<WaveguideConnectionViewModel>();
        for (int i = 0; i < ChannelCount; i++)
        {
            var coupler = Place(vm, "Grating Coupler", CouplerX, CouplerTop + i * FiberPitch);
            var waveguide = Place(vm, "Straight Waveguide 100µm", WaveguideX, WaveguideTop + i * ChipPitch);
            connections.Add(canvas.ConnectPins(Pin(coupler, "waveguide"), Pin(waveguide, "a0"))!);
        }
        await WaitForRoutingAsync(canvas);
        return connections;
    }

    /// <summary>Waits until the background router has settled.</summary>
    public static async Task WaitForRoutingAsync(DesignCanvasViewModel canvas)
    {
        const int MaxPolls = 400;
        const int PollMilliseconds = 25;
        await canvas.RecalculateRoutesAsync();
        for (int i = 0; i < MaxPolls && canvas.IsRouting; i++)
            await Task.Delay(PollMilliseconds);
    }

    private static ActiveProcessSelection ProcessOf(MainViewModel vm) =>
        ActiveProcessSelection.ForGroup(ProcessCatalog.BuildGroups(vm.LeftPanel.GetLoadedPdkProcessEntries())
            .Single(g => g.MemberPdkNames.Contains(DemoPdk)));

    private static Component Place(MainViewModel vm, string name, double x, double y)
    {
        var template = vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == DemoPdk && t.Name == name);
        var command = PlaceComponentCommand.CreateExact(vm.Canvas, template, x, y);
        vm.CommandManager.ExecuteCommand(command);
        return command.PlacedComponent!;
    }

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);
}
