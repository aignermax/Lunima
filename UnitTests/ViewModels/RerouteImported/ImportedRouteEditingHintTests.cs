using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.RerouteImported;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.RerouteImported;

public partial class RerouteImportedRoutesViewModelTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GroupOnlyImport_OffersVisibleEnabledRerouteAllAction()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        ConnectFrozenImported(canvas, start, end);
        var commands = new CommandManager();
        var main = UnitTests.Helpers.MainViewModelTestHelper.CreateMainViewModel(
            commandManager: commands, canvas: canvas);
        commands.ExecuteCommand(new CreateGroupCommand(canvas, canvas.Components.ToList(), "Imported"));
        var panel = new CAP.Avalonia.Views.Panels.RerouteImportedPanel { DataContext = main };
        var window = new Avalonia.Controls.Window { Content = panel, Width = 460, Height = 500 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            canvas.Connections.ShouldBeEmpty();
            main.BottomPanel.RerouteImported.RerouteAllCommand.CanExecute(null).ShouldBeTrue();
            var button = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel)
                .OfType<Avalonia.Controls.Button>()
                .Single(b => ReferenceEquals(b.Command, main.BottomPanel.RerouteImported.RerouteAllCommand));
            button.IsEffectivelyVisible.ShouldBeTrue("group-only imports must expose the available reroute action");
            button.IsEffectivelyEnabled.ShouldBeTrue();
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task SelectedDrawnRoute_UpdatesEditingHint_OnSelectionConversionAndUndo()
    {
        var canvas = new DesignCanvasViewModel();
        var (start, end) = AddComponentPair(canvas, 0);
        var route = ConnectFrozenImported(canvas, start, end);
        var drawn = new AsDrawnGeometry(new[]
        {
            new OutlinePolygon { Layer = 401, Points = new[]
            {
                new OutlinePoint(0, 0), new OutlinePoint(100, 0),
                new OutlinePoint(100, 2), new OutlinePoint(0, 2)
            } }
        });
        route.Connection.AttachAsDrawnGeometry(drawn);
        var commands = new CommandManager();
        var vm = new RerouteImportedRoutesViewModel(canvas, commands);
        vm.HasSelectedDrawnRoute.ShouldBeFalse();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        vm.SelectedConnection = route;
        vm.HasSelectedDrawnRoute.ShouldBeTrue();
        changes.ShouldContain("HasSelectedDrawnRoute",
            "selecting a preserved import must reveal why handles are hidden and how to enable editing");
        vm.RerouteSelectedCommand.CanExecute(null).ShouldBeTrue();

        changes.Clear();
        await vm.RerouteSelectedCommand.ExecuteAsync(null);
        route.Connection.AsDrawnGeometry.ShouldBeNull();
        vm.HasSelectedDrawnRoute.ShouldBeFalse();
        changes.ShouldContain("HasSelectedDrawnRoute", "conversion must dismiss the preservation hint");

        changes.Clear();
        commands.Undo().ShouldBeTrue();
        route.Connection.AsDrawnGeometry.ShouldBeSameAs(drawn);
        vm.HasSelectedDrawnRoute.ShouldBeTrue();
        changes.ShouldContain("HasSelectedDrawnRoute", "undo must restore the editing hint with the source polygons");
    }
}
