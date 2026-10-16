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
using Shouldly;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Render smoke test for the GDS import dialog's census/suggestion UI (#848):
/// with analysis results present, showing the dialog must build the chip and
/// census DataTemplates without throwing. Regression guard for the field crash
/// where the <c>$parent[Window].((vm:…)DataContext)</c> casts failed runtime
/// type resolution (<c>using:</c> xmlns form) the moment the suggestions
/// rendered — the ViewModel-level tests never exercised the view.
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
    public async Task Dialog_WithSuggestionsAndCensus_RendersWithoutThrowing()
    {
        Directory.CreateDirectory(_root);
        var gdsPath = Path.Combine(_root, "census.gds");
        File.WriteAllBytes(gdsPath, FoundryStyleLibrary());
        var service = _host.CreateService();
        var executor = new GdsPlacementExecutor(
            new DesignCanvasViewModel(), new CommandManager(), () => new List<ComponentTemplate>());
        var vm = new GdsImportDialogViewModel(gdsPath, service, executor);
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
            vm.HasSuggestions.ShouldBeTrue("the shown chips must have analyzed data");
            vm.HasCensus.ShouldBeTrue("the shown census must have analyzed data");

            var layers = dialog.FindControl<Expander>("LayerAssignmentExpander").ShouldNotBeNull();
            layers.IsExpanded = true;
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            // Census has its own nested expander; expand it too so both formerly
            // crashing parent-Window command bindings are actually materialized.
            layers.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            buttons.ShouldContain(b => b.DataContext is GdsLayerSuggestionChip
                && ReferenceEquals(b.Command, vm.AcceptSuggestionCommand));
            buttons.ShouldContain(b => b.DataContext is GdsLayerCensusRow
                && ReferenceEquals(b.Command, vm.AppendCensusRowCommand));
        }
        finally
        {
            vm.PropertyChanged -= OnAnalysisChanged;
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
