using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.LayerPicker;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Tests for <see cref="GdsLayerPickerViewModel"/>: picks select the clicked
/// layer's row, assignments fire the apply callback and update the legend, and
/// the assignment commands stay disabled without a selection.
/// </summary>
[Collection("LocalizationSingleton")]
public class GdsLayerPickerViewModelTests
{
    /// <summary>AssignmentText is localized; pin English so assertions stay
    /// culture-independent regardless of the CI/dev OS language.</summary>
    public GdsLayerPickerViewModelTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    private static GdsPolygon Rect(int layer, double x0, double y0, double x1, double y1) =>
        new()
        {
            Layer = layer,
            DataType = 0,
            Points = new[]
            {
                new GdsPoint(x0, y0), new GdsPoint(x1, y0),
                new GdsPoint(x1, y1), new GdsPoint(x0, y1), new GdsPoint(x0, y0),
            },
        };

    private static GdsLayerGeometryScene BuildScene()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "TOP" };
        top.Elements.Add(Rect(1, 0, 0, 100, 100));
        top.Elements.Add(Rect(11, 10, 10, 14, 14));
        top.Elements.Add(new GdsText { Layer = 56, TextType = 0, Text = "opt_in", Position = new GdsPoint(50, 50) });
        library.Cells["TOP"] = top;
        return GdsLayerGeometryScene.Build(library, "TOP");
    }

    private static GdsLayerPickerViewModel CreatePicker(
        List<(GdsLayerPair Pair, GdsLayerFieldTarget? Target)> applied,
        Dictionary<GdsLayerPair, GdsLayerFieldTarget>? initial = null)
    {
        return new GdsLayerPickerViewModel(
            BuildScene(),
            initial ?? new Dictionary<GdsLayerPair, GdsLayerFieldTarget>(),
            (pair, target) => applied.Add((pair, target)));
    }

    [Fact]
    public void Rows_OnePerLayer_WithDistinctColors()
    {
        var vm = CreatePicker(new());

        vm.Rows.Count.ShouldBe(3);
        vm.Rows.Select(r => r.Color).Distinct().Count().ShouldBe(3);
        vm.Rows.Select(r => r.PairText).ShouldBe(new[] { "(1,0)", "(11,0)", "(56,0)" });
    }

    [Fact]
    public void InitialAssignments_ShowInLegend()
    {
        var initial = new Dictionary<GdsLayerPair, GdsLayerFieldTarget>
        {
            [new GdsLayerPair(1, 0)] = GdsLayerFieldTarget.Waveguide,
        };
        var vm = CreatePicker(new(), initial);

        vm.Rows.Single(r => r.Pair == new GdsLayerPair(1, 0)).AssignmentText.ShouldBe("waveguide");
        vm.Rows.Single(r => r.Pair == new GdsLayerPair(11, 0)).AssignmentText.ShouldBe("—");
    }

    [Fact]
    public void PickAt_OnGeometry_SelectsTheClickedLayer()
    {
        var vm = CreatePicker(new());

        vm.PickAt(12, 12, 0.1);

        vm.HasSelection.ShouldBeTrue();
        vm.SelectedRow!.Pair.ShouldBe(new GdsLayerPair(11, 0));
        vm.SelectedRow.IsSelected.ShouldBeTrue();
        vm.SelectionText.ShouldContain("(11,0)");
    }

    [Fact]
    public void PickAt_OnEmptySpace_ClearsSelection()
    {
        var vm = CreatePicker(new());
        vm.PickAt(12, 12, 0.1);

        vm.PickAt(500, 500, 0.1);

        vm.HasSelection.ShouldBeFalse();
        vm.Rows.ShouldAllBe(r => !r.IsSelected);
    }

    [Fact]
    public void AssignCommands_DisabledWithoutSelection()
    {
        var vm = CreatePicker(new());

        vm.AssignWaveguideCommand.CanExecute(null).ShouldBeFalse();
        vm.AssignMetalCommand.CanExecute(null).ShouldBeFalse();
        vm.AssignPortLabelsCommand.CanExecute(null).ShouldBeFalse();
        vm.AssignIgnoreCommand.CanExecute(null).ShouldBeFalse();

        vm.PickAt(12, 12, 0.1);
        vm.AssignMetalCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void AssignMetal_FiresCallback_AndUpdatesLegend()
    {
        var applied = new List<(GdsLayerPair, GdsLayerFieldTarget?)>();
        var vm = CreatePicker(applied);
        vm.PickAt(12, 12, 0.1);

        vm.AssignMetalCommand.Execute(null);

        applied.ShouldBe(new[] { (new GdsLayerPair(11, 0), (GdsLayerFieldTarget?)GdsLayerFieldTarget.Metal) });
        vm.SelectedRow!.AssignmentText.ShouldBe("metal");
    }

    [Fact]
    public void AssignIgnore_FiresCallbackWithNullTarget_AndClearsLegendEntry()
    {
        var applied = new List<(GdsLayerPair, GdsLayerFieldTarget?)>();
        var initial = new Dictionary<GdsLayerPair, GdsLayerFieldTarget>
        {
            [new GdsLayerPair(11, 0)] = GdsLayerFieldTarget.Metal,
        };
        var vm = CreatePicker(applied, initial);
        vm.PickAt(12, 12, 0.1);

        vm.AssignIgnoreCommand.Execute(null);

        applied.ShouldBe(new[] { (new GdsLayerPair(11, 0), (GdsLayerFieldTarget?)null) });
        vm.SelectedRow!.AssignmentText.ShouldBe("—");
    }

    [Fact]
    public void SelectRowCommand_SelectsFromLegend()
    {
        var vm = CreatePicker(new());
        var row = vm.Rows.Single(r => r.Pair == new GdsLayerPair(56, 0));

        vm.SelectRowCommand.Execute(row);

        vm.SelectedRow.ShouldBe(row);
        row.IsSelected.ShouldBeTrue();
    }
}
