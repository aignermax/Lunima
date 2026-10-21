using Avalonia.Controls;
using Avalonia.VisualTree;
using System.ComponentModel;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Views.Dialogs;
using CAP.Avalonia.Controls.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using Shouldly;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Renders the actual dialog after its single OnOpened analysis. This covers
/// native review templates and parent command bindings, not hidden legacy chips.
/// </summary>
[Collection("LocalizationSingleton")]
public class GdsImportDialogRenderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gdsdlg-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _host.Dispose();
    }

    /// <summary>Same shape as the census fixture: port-style texts on (56,0)
    /// plus a route-like path on the unknown (37,0), so both suggestion chips
    /// and census rows are non-empty.</summary>
    private static byte[] FoundryStyleLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wg", 0, 0)
            .Path(37, 0, 500, 0, (0, 0), (50000, 0))
        .EndCell()
        .BeginCell("wg")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Text(56, 0, "opt_in", 0, 2000)
            .Text(56, 0, "opt_out", 10000, 2000)
        .EndCell()
        .EndLibrary()
        .ToArray();

    [AvaloniaFact]
    public async Task Dialog_WithNativeReview_RendersWithoutThrowing()
    {
        Directory.CreateDirectory(_root);
        var gdsPath = Path.Combine(_root, "census.gds");
        File.WriteAllBytes(gdsPath, FoundryStyleLibrary());
        var service = _host.CreateService();
        var executor = new GdsPlacementExecutor(
            new DesignCanvasViewModel(), new CommandManager(), () => new List<ComponentTemplate>());
        var vm = new GdsImportDialogViewModel(gdsPath, service, executor)
        { ReviewProfileStore = new GdsLayerProfileStore(Path.Combine(_root, "profiles.json")) };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        void OnAnalysisChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(vm.IsBusy)) return;
            if (vm.IsBusy) starts++;
            else if (starts > 0) completed.TrySetResult();
        }
        vm.PropertyChanged += OnAnalysisChanged;
        var dialog = new GdsImportDialog { DataContext = vm };
        try
        {
            // OnOpened owns the analysis. Await its actual completion instead of
            // pre-analyzing and racing a second run started by Show().
            dialog.Show();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            starts.ShouldBe(1);
            vm.HasError.ShouldBeFalse(vm.ErrorText);
            vm.HasReview.ShouldBeTrue();
            var review = vm.Review.ShouldNotBeNull();
            review.Layers.ShouldNotBeEmpty();
            review.Scene.Layers.Sum(layer => layer.Polygons.Count).ShouldBeGreaterThan(0);
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var view = dialog.GetVisualDescendants().OfType<GdsInterpretationReviewView>().Single();
            view.IsEffectivelyVisible.ShouldBeTrue();
            view.FindControl<GdsReviewPreview>("ReviewPreview")!.Scene.ShouldBeSameAs(review.Scene);
            var steps = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.DataContext is GdsReviewStep).ToArray();
            steps.Length.ShouldBe(6);
            steps.ShouldAllBe(button => ReferenceEquals(button.Command, review.ChooseStepCommand));
            await review.NextCommand.ExecuteAsync(null);
            dialog.UpdateLayout();
            view.FindControl<ListBox>("GdsReviewRole")!.Items.Count.ShouldBe(10);
        }
        finally
        {
            vm.PropertyChanged -= OnAnalysisChanged;
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
