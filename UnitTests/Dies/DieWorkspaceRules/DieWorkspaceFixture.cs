using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using UnitTests.Dies.WorkSurface;
using UnitTests.Helpers;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies.DieWorkspaceRules;

/// <summary>
/// A full main view model holding a two-die assembly built through the real New-die command:
/// "Die 1" (the former 2 × 2 mm chip) carries parts a and b joined by a routed wire, "Die 2"
/// (1 × 1 mm) sits to its right, empty. Starts in the Layout workspace.
/// </summary>
internal sealed class DieWorkspaceFixture
{
    /// <summary>Single-die chip size before New die.</summary>
    public const double ChipSize = 2000;

    public DieWorkspaceFixture()
    {
        Vm = MainViewModelTestHelper.CreateMainViewModel();
        Vm.WorkspaceSwitch.Current = Workspace.Layout;
        Canvas.SetRootChipSize(ChipSize, ChipSize);
        A = Canvas.AddComponent(Part("a", 100, 100)).Component;
        B = Canvas.AddComponent(Part("b", 400, 100)).Component;
        Canvas.ConnectPinsWithCachedRoute(A.PhysicalPins[1], B.PhysicalPins[0],
            WorkSurfaceCanvas.StraightBetween(A.PhysicalPins[1], B.PhysicalPins[0]));

        var newDie = new CreateDieCommand(Canvas, new DieSpec("Die 2", null, 1000, 1000), new DieSpec("Die 1", null, ChipSize, ChipSize));
        Vm.CommandManager.ExecuteCommand(newDie);
        Die1 = newDie.ConvertedChipDie!;
        Die2 = newDie.CreatedDie!;
    }

    public MainViewModel Vm { get; }

    public DesignCanvasViewModel Canvas => Vm.Canvas;

    public ComponentGroup Die1 { get; }

    public ComponentGroup Die2 { get; }

    /// <summary>Part a on Die 1.</summary>
    public Component A { get; }

    /// <summary>Part b on Die 1.</summary>
    public Component B { get; }

    /// <summary>The top-level view model of a die.</summary>
    public ComponentViewModel DieVm(ComponentGroup die) => Canvas.Components.Single(c => ReferenceEquals(c.Component, die));

    /// <summary>The die-surface view model of a part.</summary>
    public ComponentViewModel PartVm(Component part) => Canvas.Surface.ViewModelFor(part);

    /// <summary>Selects <paramref name="item"/> alone, as a click would.</summary>
    public void Select(ComponentViewModel item)
    {
        Canvas.Selection.SelectSingle(item);
        Canvas.SelectedComponent = item;
    }

    /// <summary>The outline of a die.</summary>
    public static DieOutline Outline(ComponentGroup die) => DieAssembly.OutlineOf(die);
}
