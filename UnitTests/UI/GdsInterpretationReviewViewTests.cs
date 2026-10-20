using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.GdsImport;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.UI;

/// <summary>Exercises the production interpretation view, commands, and guided stage targets.</summary>
[Collection("LocalizationSingleton")]
public class GdsInterpretationReviewViewTests
{
    [AvaloniaFact]
    public async Task LayerGuideStartsAtListAndRightClickTogglesContext()
    {
        var vm = await CreateReview();
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            vm.StageIndex = 1;
            window.Show();
            Layout(window);
            vm.GuideEnabled.ShouldBeTrue();
            vm.GuideTargetName.ShouldBe("GdsReviewLayerList");
            var preview = view.FindControl<GdsReviewPreview>("ReviewPreview")!;
            preview.IsolateSelectedLayer.ShouldBeFalse();
            var center = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Right);
            window.MouseUp(center, MouseButton.Right);
            Layout(window);
            vm.IsolateSelectedLayer.ShouldBeTrue();
            window.MouseDown(center, MouseButton.Right);
            window.MouseUp(center, MouseButton.Right);
            Layout(window);
            vm.IsolateSelectedLayer.ShouldBeFalse();
            var list = view.FindControl<ListBox>("GdsReviewLayerList")!;
            var row = list.TranslatePoint(new Point(30, 15), window)!.Value;
            window.MouseDown(row, MouseButton.Left);
            window.MouseUp(row, MouseButton.Left);
            Layout(window);
            vm.GuideTargetName.ShouldBe("GdsReviewRole");
            Capture(view, "layer-context-and-guide.png");
            vm.RestartGuideCommand.Execute(null);
            vm.GuideTargetName.ShouldBe("GdsReviewLayerList");
            list.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None);
            Layout(window);
            vm.GuideTargetName.ShouldBe("GdsReviewRole");
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task RouteSuggestionCanBeConfirmedByPointerAndUndone()
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "TOP" };
        cell.Elements.Add(new GdsPolygon { Layer = 42, Points = [new(0, 0), new(100, 0), new(100, 1), new(0, 1)] });
        library.Cells.Add(cell.Name, cell);
        var vm = await GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "route-hint",
            [], [], new GdsLayerProfileStore(Path.Combine(ScreenshotDirectory, $"profiles-{Guid.NewGuid():N}.json")));
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            vm.StageIndex = 1;
            window.Show();
            Layout(window);
            var button = view.FindControl<Button>("GdsReviewUseOptical")!;
            button.IsEffectivelyVisible.ShouldBeTrue();
            AssertActionableTarget(view, "GdsReviewUseOptical");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout(window);
            vm.SelectedLayer!.Role.ShouldBe(GdsLayerRole.OpticalCore);
            Capture(view, "route-layer-suggestion.png");
            vm.UndoCommand.Execute(null);
            vm.SelectedLayer.Role.ShouldBe(GdsLayerRole.Unknown);
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task GuidedAssemblyButtonOpensChildWithRealPointerClick()
    {
        var bytes = GdsTestWriter.Create().StandardPrologue()
            .BeginCell("TOP").SRef("assembly", 0, 0).SRef("assembly", 100000, 0).EndCell()
            .BeginCell("assembly").SRef("MMI", 0, 0).EndCell()
            .BeginCell("MMI").Boundary(1, 0, (0, 0), (60000, 0), (60000, 12000), (0, 12000), (0, 0))
            .EndCell().EndLibrary().ToArray();
        using var stream = new MemoryStream(bytes);
        var library = await new GdsReader().ReadAsync(stream);
        var vm = await GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "assembly-fixture",
            [new GdsLayerAssignment(1, 0, GdsLayerRole.OpticalCore)], [],
            new GdsLayerProfileStore(Path.Combine(ScreenshotDirectory, $"profiles-{Guid.NewGuid():N}.json")));
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            vm.StageIndex = 2;
            window.Show();
            Layout(window);
            vm.GuideTargetName.ShouldBe("GdsReviewOpenAssembly");
            var button = view.FindControl<Button>("GdsReviewOpenAssembly")!;
            Capture(view, "assembly-before-open.png");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout(window);
            vm.SelectedCell!.CellName.ShouldBe("MMI");
            vm.ActiveCells.ShouldHaveSingleItem().CellName.ShouldBe("MMI");
            var imported = CAP_DataAccess.Import.Gds.Interpretation.GdsInterpretationExpander
                .Expand(library, vm.BuildInterpretation());
            imported.Cells["TOP"].Elements.OfType<GdsReference>().Count().ShouldBe(2);
            imported.Cells["TOP"].Elements.OfType<GdsReference>().ShouldAllBe(r => r.CellName == "MMI");
            Capture(view, "assembly-after-open.png");
            vm.UndoCommand.Execute(null);
            vm.ActiveCells.ShouldHaveSingleItem().CellName.ShouldBe("assembly");
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task ComponentOverviewClickSelectsPartAndModelsMatchItsSignalDomains()
    {
        var optical = new GdsModelCandidate("pdk", "two optical ports", ["in", "out"]);
        var electrical = new GdsModelCandidate("pdk", "two electrical ports", ["p", "n"])
        { PinDomains = new Dictionary<string, string> { ["p"] = "electrical", ["n"] = "electrical" } };
        var vm = await CreateReview([optical, electrical, new("pdk", "three ports", ["a", "b", "c"])]);
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            await vm.NextCommand.ExecuteAsync(null);
            await vm.NextCommand.ExecuteAsync(null);
            vm.SelectedCell = null;
            Layout(window);
            var preview = view.FindControl<GdsReviewPreview>("ReviewPreview")!;
            preview.Scene!.CellName.ShouldBe("TOP");
            var point = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            vm.SelectedCell.ShouldNotBeNull().CellName.ShouldBe("coupler");
            vm.HighlightCellName.ShouldBe("coupler");
            vm.CompatibleModels.ShouldBe([optical]);
            vm.SelectedCell.Ports[0].Kind = "vertical_te";
            vm.CompatibleModels.ShouldBe([optical]);
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task PointerCanReturnToLayoutWhileGuideIsVisible()
    {
        var vm = await CreateReview();
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            await vm.NextCommand.ExecuteAsync(null);
            Layout(window);
            var layout = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.DataContext is GdsReviewStep { Index: 0 });
            var point = layout.TranslatePoint(new Point(layout.Bounds.Width / 2, layout.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            vm.StageIndex.ShouldBe(0);
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task LayerRolesAreVisibleChoicesAndEmptyPortsHideCoordinateEditor()
    {
        var vm = await CreateReview();
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            await vm.NextCommand.ExecuteAsync(null);
            Layout(window);
            var choices = view.FindControl<ListBox>("GdsReviewRole").ShouldNotBeNull();
            choices.Items.Count.ShouldBe(10);
            choices.SelectedItem.ShouldBeSameAs(vm.SelectedLayer!.SelectedRole);
            vm.StageIndex = 3;
            Layout(window);
            view.FindControl<Control>("GdsReviewPortEditor").ShouldNotBeNull()
                .IsEffectivelyVisible.ShouldBeFalse();
            vm.AddPortCommand.Execute(null);
            Layout(window);
            view.FindControl<Control>("GdsReviewPortEditor")!.IsEffectivelyVisible.ShouldBeTrue();
        }
        finally { vm.Close(); window.Close(); }
    }

    private static readonly string ScreenshotDirectory =
        Path.Combine(Path.GetTempPath(), "lunima-gds-review-qa");

    [AvaloniaFact]
    public async Task RealMziLayoutRendersInNativeReview()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Tools", "gds-test-data")))
            directory = directory.Parent;
        var source = Path.Combine(directory.ShouldNotBeNull().FullName, "Tools", "gds-test-data", "gdsfactory_mzi_like.gds");
        var analysis = await GdsImportService.AnalyzeAsync(source);
        var vm = await GdsInterpretationReviewViewModel.CreateAsync(analysis.Library, analysis.TopCellCandidates[0],
            analysis.SourceSha256, [new(1, 0, GdsLayerRole.OpticalCore), new(1, 10, GdsLayerRole.PortLabel)], [],
            new GdsLayerProfileStore(Path.Combine(ScreenshotDirectory, $"mzi-{Guid.NewGuid():N}.json")));
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            await vm.NextCommand.ExecuteAsync(null);
            Layout(window);
            vm.Scene.Layers.Sum(x => x.Polygons.Count).ShouldBeGreaterThan(0);
            Capture(view, "native-mzi-layout.png");
        }
        finally { vm.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task SixStagesRenderWithBoundCommandsAndActionableGuideTargets()
    {
        var vm = await CreateReview();
        var view = new GdsInterpretationReviewView { DataContext = vm };
        var window = new Window { Width = 1180, Height = 780, Content = view };
        try
        {
            window.Show();
            Layout(window);
            var next = view.FindControl<Button>("GdsReviewNext").ShouldNotBeNull();
            next.Command.ShouldBeSameAs(vm.NextCommand);
            var preview = view.FindControl<GdsReviewPreview>("ReviewPreview").ShouldNotBeNull();
            var overlay = view.GetVisualDescendants().OfType<TourAnchorOverlay>().Single();
            var stepButtons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.DataContext is GdsReviewStep).ToArray();
            stepButtons.Length.ShouldBe(6);
            stepButtons.ShouldAllBe(b => ReferenceEquals(b.Command, vm.ChooseStepCommand));

            for (var stage = 0; stage < 6; stage++)
            {
                vm.StageIndex.ShouldBe(stage);
                Layout(window);
                preview.Bounds.Width.ShouldBeGreaterThan(400);
                preview.Bounds.Height.ShouldBeGreaterThanOrEqualTo(300);
                preview.Scene.ShouldBeSameAs(vm.Scene);
                preview.ShowPorts.ShouldBe(stage == 3);
                overlay.TargetName.ShouldBe(vm.GuideTargetName);
                overlay.IsEffectivelyVisible.ShouldBeTrue();
                AssertActionableTarget(view, vm.GuideTargetName);
                AssertStageBindings(view, vm, stage);
                Capture(view, $"stage-{stage + 1}.png");
                if (stage < 5)
                    await vm.NextCommand.ExecuteAsync(null);
            }

            vm.CanApply.ShouldBeTrue();
            var applied = false;
            vm.ApplyRequested = () => applied = true;
            await vm.NextCommand.ExecuteAsync(null);
            applied.ShouldBeTrue();
            vm.PauseGuideCommand.Execute(null);
            Layout(window);
            overlay.IsEffectivelyVisible.ShouldBeFalse();
            Capture(view, "summary-without-guide.png");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertActionableTarget(GdsInterpretationReviewView view, string name)
    {
        var target = view.FindControl<Control>(name).ShouldNotBeNull();
        target.IsEffectivelyVisible.ShouldBeTrue($"stage target {name} must be visible");
        target.IsEffectivelyEnabled.ShouldBeTrue($"stage target {name} must be enabled");
        target.Bounds.Width.ShouldBeGreaterThan(20);
        target.Bounds.Height.ShouldBeGreaterThan(20);
        var position = target.TranslatePoint(default, view).ShouldNotBeNull();
        position.X.ShouldBeGreaterThanOrEqualTo(0);
        position.Y.ShouldBeGreaterThanOrEqualTo(0);
        (position.X + target.Bounds.Width).ShouldBeLessThanOrEqualTo(view.Bounds.Width + 1);
        (position.Y + target.Bounds.Height).ShouldBeLessThanOrEqualTo(view.Bounds.Height + 1);
    }

    private static void AssertStageBindings(GdsInterpretationReviewView view,
        GdsInterpretationReviewViewModel vm, int stage)
    {
        var selector = view.FindControl<ComboBox>("GdsReviewCellSelector").ShouldNotBeNull();
        selector.IsVisible.ShouldBe(stage is 2 or 3 or 4);
        if (stage is 2 or 3 or 4) selector.SelectedItem.ShouldBeSameAs(vm.SelectedCell);
        if (stage == 1)
        {
            var preview = view.FindControl<GdsReviewPreview>("ReviewPreview")!;
            preview.IsolateSelectedLayer.ShouldBeFalse();
            vm.IsolateSelectedLayer = true;
            Dispatcher.UIThread.RunJobs();
            preview.IsolateSelectedLayer.ShouldBeTrue();
            vm.IsolateSelectedLayer = false;
            var detail = view.FindControl<ComboBox>("GdsReviewDetail")!;
            detail.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            preview.DetailLevel.ShouldBe(1);
            detail.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            preview.DetailLevel.ShouldBe(2);
            detail.SelectedIndex = 0;
            var role = view.FindControl<ListBox>("GdsReviewRole").ShouldNotBeNull();
            role.SelectedItem.ShouldBeSameAs(vm.SelectedLayer!.SelectedRole);
            role.Items.Count.ShouldBeGreaterThan(5);
        }
        if (stage == 3)
        {
            vm.SelectedCell!.Ports.ShouldNotBeEmpty();
            view.FindControl<Button>("GdsReviewAddPort")!.Command.ShouldBeSameAs(vm.AddPortCommand);
            var kind = view.FindControl<ComboBox>("GdsReviewPortKind").ShouldNotBeNull();
            kind.SelectedItem.ShouldBe(vm.SelectedPort!.Kind);
            view.FindControl<GdsReviewPreview>("ReviewPreview")!.Ports!.Count
                .ShouldBe(vm.SelectedCell.Ports.Count);
            var preview = view.FindControl<GdsReviewPreview>("ReviewPreview")!;
            preview.AllowPortEditing.ShouldBeFalse();
            vm.IsPortEditing = true;
            Dispatcher.UIThread.RunJobs();
            preview.AllowPortEditing.ShouldBeTrue();
            vm.IsPortEditing = false;
        }
        if (stage == 4)
            view.FindControl<Button>("GdsReviewGeometryOnly")!.Command.ShouldBeSameAs(vm.GeometryOnlyCommand);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Control view, string name)
    {
        Directory.CreateDirectory(ScreenshotDirectory);
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(view.Bounds.Width), (int)Math.Ceiling(view.Bounds.Height)));
        bitmap.Render(view);
        bitmap.Save(Path.Combine(ScreenshotDirectory, name));
        new FileInfo(Path.Combine(ScreenshotDirectory, name)).Length.ShouldBeGreaterThan(1000);
    }

    private static async Task<GdsInterpretationReviewViewModel> CreateReview(IReadOnlyList<GdsModelCandidate>? models = null)
    {
        var bytes = GdsTestWriter.Create().StandardPrologue()
            .BeginCell("TOP").SRef("coupler", 0, 0).EndCell()
            .BeginCell("coupler")
                .Boundary(1, 0, (0, 0), (60000, 0), (60000, 12000), (0, 12000), (0, 0))
                .Boundary(8, 0, (10000, 2000), (50000, 2000), (50000, 10000), (10000, 10000), (10000, 2000))
                .Text(56, 0, "o1", 0, 6000).Text(56, 0, "o2", 60000, 6000)
            .EndCell().EndLibrary().ToArray();
        using var stream = new MemoryStream(bytes);
        var library = await new GdsReader().ReadAsync(stream);
        return await GdsInterpretationReviewViewModel.CreateAsync(library, "TOP", "review-view-fixture",
            [
                new GdsLayerAssignment(1, 0, GdsLayerRole.OpticalCore),
                new GdsLayerAssignment(8, 0, GdsLayerRole.Slab),
                new GdsLayerAssignment(56, 0, GdsLayerRole.PortLabel)
            ], models ?? [], new GdsLayerProfileStore(Path.Combine(ScreenshotDirectory, $"profiles-{Guid.NewGuid():N}.json")));
    }
}
