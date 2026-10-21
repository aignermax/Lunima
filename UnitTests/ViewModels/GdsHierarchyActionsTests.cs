using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;
using Avalonia.Headless.XUnit;

namespace UnitTests.ViewModels;

[Collection("LocalizationSingleton")]
public class GdsHierarchyActionsTests
{
    [AvaloniaFact]
    public async Task ImportedSelection_ExposesHierarchyActionOnCanvas()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original) = await Setup(host);
        var main = UnitTests.Helpers.MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        main.FileOperations.DesignScopedGdsComponents!.AddAndRegister(host.Scope.Sets.Single());
        main.CanvasInteraction.SelectedComponent = original;
        var panel = new CAP.Avalonia.Views.Panels.ImportedSelectionActions { DataContext = main };
        var window = new Avalonia.Controls.Window { Content = panel, Width = 420, Height = 220 };
        window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var button = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(panel)
                .OfType<Avalonia.Controls.Button>().Single(b => ReferenceEquals(b.Command, main.ImportedHierarchy.ExpandCommand));
            button.IsEffectivelyVisible.ShouldBeTrue();
            button.IsEffectivelyEnabled.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(420, 220));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, "imported-hierarchy-canvas-action.png"));
            }
            main.CanvasInteraction.SelectedComponent = null;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            button.IsEffectivelyVisible.ShouldBeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Expansion_YieldsAndCancelsWhenSelectionChanges()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original) = await Setup(host);
        var commands = new CommandManager();
        var vm = new GdsHierarchyActionsViewModel(canvas, commands, new(host.Scope));
        vm.IsVisible.ShouldBeFalse();
        vm.ConfigureForSelection(original);
        vm.IsVisible.ShouldBeTrue();
        var task = vm.ExpandCommand.ExecuteAsync(null);
        task.IsCompleted.ShouldBeFalse("reading the embedded hierarchy must yield to the UI");
        vm.IsBusy.ShouldBeTrue();
        vm.ConfigureForSelection(null);
        await task;
        vm.IsBusy.ShouldBeFalse();
        vm.IsVisible.ShouldBeFalse();
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
        commands.CanUndo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Expansion_IsUndoableAndReportsSuccess()
    {
        using var host = new GdsDesignScopeTestHost();
        var (canvas, original) = await Setup(host);
        var commands = new CommandManager();
        var vm = new GdsHierarchyActionsViewModel(canvas, commands, new(host.Scope));
        string? status = null;
        vm.ReportStatus = text => status = text;
        vm.ConfigureForSelection(original);
        await vm.ExpandCommand.ExecuteAsync(null);
        vm.IsBusy.ShouldBeFalse();
        vm.IsVisible.ShouldBeFalse();
        status.ShouldNotBeNullOrEmpty();
        canvas.Components.ShouldNotContain(original);
        commands.Undo().ShouldBeTrue();
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(original);
    }

    private static async Task<(DesignCanvasViewModel, ComponentViewModel)> Setup(GdsDesignScopeTestHost host)
    {
        var bytes = GdsTestWriter.Create().StandardPrologue().BeginCell("child")
            .Boundary(20, 0, (0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0)).EndCell()
            .BeginCell("parent").SRef("child", 20000, 20000).EndCell().EndLibrary().ToArray();
        using var stream = new MemoryStream(bytes);
        var library = await new GdsReader().ReadAsync(stream);
        var imported = await GdsHierarchyImporter.ImportAsync(library, "parent",
            new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });
        var draft = GdsCellDraftMapper.Map(imported.ImportedCellDrafts.Single(), GdsHierarchyImporter.GdsFileNameToken);
        host.Scope.AddAndRegister(new DesignScopedGdsSet
        {
            PdkName = "GDS Import - actions", GdsFileName = "actions.gds", GdsBytes = bytes, Drafts = [draft]
        });
        var canvas = new DesignCanvasViewModel();
        var placement = PlaceComponentCommand.CreateExact(canvas, host.Templates.Single(), 100, 100);
        placement.Execute();
        return (canvas, placement.CreatedViewModel!);
    }
}
