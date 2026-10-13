using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using Shouldly;

namespace UnitTests.Dies;

/// <summary>
/// Builds the shipped example "Two Dies - SOI and SiN Edge-Coupler Link" through the same
/// commands the UI runs: a Demo-SOI chip (grating coupler → waveguide → edge coupler on its
/// east edge), New die in CornerStone SiN 300 nm, a SiN edge coupler dropped on the new
/// die's west edge and wired to a SiN grating coupler inside the die, then the die dragged
/// toward the chip until the facing couplers snap at the 2 µm link gap and link.
/// </summary>
internal static class TwoDieExampleBuilder
{
    public const string ExampleFileName = "Two Dies - SOI and SiN Edge-Coupler Link.lun";
    public const string DemoPdk = "Demo PDK";
    public const string SinPdk = "CornerStone SiN 300nm";

    private const double ChipWidth = 1000;
    private const double ChipHeight = 600;
    private const double RowY = 290;

    /// <summary>Builds the two-die design in <paramref name="vm"/> and returns the created SiN die.</summary>
    public static async Task<ComponentGroup> BuildAsync(MainViewModel vm)
    {
        var canvas = vm.Canvas;
        vm.FileOperations.SetActiveProcess(ProcessOf(vm, DemoPdk), markDirty: false);
        vm.ChipSize.ApplyFromMicrometers(ChipWidth, ChipHeight);

        var gc = Place(vm, DemoPdk, "Grating Coupler", 60, RowY - 9.5);
        var wg = Place(vm, DemoPdk, "Straight Waveguide 100µm", 450, RowY - 5);
        var ec = Place(vm, DemoPdk, "Edge Coupler", ChipWidth - 140, RowY - 9.5);
        new SnapEdgeCouplerCommandHost(canvas).SnapRootCoupler(ec, new DieOutline(0, 0, ChipWidth, ChipHeight));
        canvas.ConnectPins(Pin(gc, "waveguide"), Pin(wg, "a0"));
        canvas.ConnectPins(Pin(wg, "b0"), Pin(ec, "waveguide"));
        await WaitForRoutingAsync(canvas);

        vm.NewDie.Prepare();
        vm.NewDie.SelectedProcess = vm.NewDie.ProcessOptions.Single(o => o.Selection.MemberPdkNames.Contains(SinPdk));
        vm.NewDie.SelectedSize = vm.NewDie.SizeOptions.Single(o => o.IsCustom);
        vm.NewDie.CustomWidthMillimeters = 1.0;
        vm.NewDie.CustomHeightMillimeters = 0.6;
        vm.NewDie.CreateCommand.Execute(null);
        var sinDie = canvas.Dies.RootDies.Single(d => d.ProcessBinding?.MemberPdkNames.Contains(SinPdk) == true);
        canvas.Dies.RootDies.Single(d => d != sinDie).GroupName = "SOI die";
        sinDie.GroupName = "SiN die";

        var sinOutline = DieAssembly.OutlineOf(sinDie);
        var place = PlaceComponentInDieCommand.TryCreate(canvas, sinDie, Template(vm, SinPdk, "SiN Edge Coupler"),
            sinOutline.X + 110, sinOutline.Y + RowY + 40)!;
        vm.CommandManager.ExecuteCommand(place);
        var sinGc = PlaceComponentInDieCommand.TryCreate(canvas, sinDie, Template(vm, SinPdk, "Grating Coupler Elliptical"),
            sinOutline.X + 650, sinOutline.Y + RowY + 40)!;
        vm.CommandManager.ExecuteCommand(sinGc);

        canvas.EnterGroupEditMode(sinDie);
        canvas.ConnectPins(Pin(place.PlacedComponent, "o2"), Pin(sinGc.PlacedComponent, "o1"));
        await WaitForRoutingAsync(canvas);
        canvas.ExitToRoot();
        await WaitForRoutingAsync(canvas);

        SnapAndLink(vm, sinDie);
        await WaitForRoutingAsync(canvas);
        return sinDie;
    }

    /// <summary>Drags <paramref name="die"/> toward its neighbour the way the canvas does: snap, move, link.</summary>
    public static void SnapAndLink(MainViewModel vm, ComponentGroup die)
    {
        var canvas = vm.Canvas;
        var dieVm = canvas.Components.Single(c => c.Component == die);
        var others = canvas.Dies.RootDies;
        var neighbour = others.Single(d => d != die);
        double toward = DieAssembly.OutlineOf(neighbour).Right + DieLinkSnapper.DefaultGapMicrometers + 30 - dieVm.X;
        var snap = DieLinkSnapper.Plan(die, toward, 0, others);
        snap.ShouldNotBeNull("the facing edge couplers must come within snap range");
        vm.CommandManager.ExecuteCommand(new MoveComponentCommand(canvas, dieVm,
            dieVm.X, dieVm.Y, dieVm.X + snap!.DeltaX, dieVm.Y + snap.DeltaY));
        canvas.Dies.ApplyScopeBounds();
        vm.CommandManager.ExecuteCommand(LinkDiesCommand.TryCreate(canvas, snap.Pairs)!);
    }

    /// <summary>Waits until the background router has settled.</summary>
    public static async Task WaitForRoutingAsync(DesignCanvasViewModel canvas)
    {
        const int MaxPolls = 200;
        const int PollMilliseconds = 25;
        await canvas.RecalculateRoutesAsync();
        for (int i = 0; i < MaxPolls && canvas.IsRouting; i++)
            await Task.Delay(PollMilliseconds);
    }

    /// <summary>The library template <paramref name="name"/> of <paramref name="pdk"/>.</summary>
    public static ComponentTemplate Template(MainViewModel vm, string pdk, string name) =>
        vm.LeftPanel.AllTemplates.Single(t => t.PdkSource == pdk && t.Name == name);

    private static ActiveProcessSelection ProcessOf(MainViewModel vm, string pdk) =>
        ActiveProcessSelection.ForGroup(ProcessCatalog.BuildGroups(vm.LeftPanel.GetLoadedPdkProcessEntries())
            .Single(g => g.MemberPdkNames.Contains(pdk)));

    private static Component Place(MainViewModel vm, string pdk, string name, double x, double y)
    {
        var command = PlaceComponentCommand.CreateExact(vm.Canvas, Template(vm, pdk, name), x, y);
        vm.CommandManager.ExecuteCommand(command);
        return command.PlacedComponent!;
    }

    private static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Snaps a top-level coupler onto the single chip's edge (the chip becomes Die 1 later).</summary>
    private sealed class SnapEdgeCouplerCommandHost
    {
        private readonly DesignCanvasViewModel _canvas;

        public SnapEdgeCouplerCommandHost(DesignCanvasViewModel canvas) => _canvas = canvas;

        public void SnapRootCoupler(Component coupler, DieOutline chip)
        {
            var snap = DieEdgeSnapper.Plan(coupler, chip, DieDefinition.DefaultEdgeZoneMicrometers)!;
            DieEdgeSnapper.Apply(coupler, snap);
            var vm = _canvas.Components.Single(c => c.Component == coupler);
            vm.X = coupler.PhysicalX;
            vm.Y = coupler.PhysicalY;
            vm.NotifyDimensionsChanged();
            _canvas.Router.UpdateComponentObstacle(coupler);
        }
    }
}
