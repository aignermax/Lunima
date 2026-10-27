using Avalonia.Headless.XUnit;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class ShellDesignAnalysisReplacementTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AcceptedReplacement_AdmitsPersistentDesignAnalysis(bool optimize, bool load)
    {
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(Path.GetTempPath(), $"shell-design-analysis-{Guid.NewGuid():N}.lun");
        await File.WriteAllTextAsync(path, "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}");
        try
        {
            var accepted = load
                ? await main.FileOperations.LoadDesignFromPathAsync(path)
                : await main.FileOperations.TryNewProjectAsync();
            accepted.ShouldBeTrue();
            if (optimize)
            {
                var target = CircuitOptimizationLifetimeFixture.Populate(main.Canvas, 0.2);
                var panel = main.BottomPanel.Analysis.Optimization;
                panel.EvaluationBudget = 2;
                panel.SelectedTarget = new("output", [target.Component.PhysicalPins[1].LogicalPin.IDOutFlow]);
                await panel.RunOptimizationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(15));
                panel.BaselineText.ShouldNotBeEmpty("the new document must reach the actual optimization runner");
                panel.StatusText.ShouldNotBeEmpty();
            }
            else
            {
                // No routes are needed for admission; keep this control bounded
                // independently of crossing/route search performance.
                main.Canvas.Components.Add(new CAP.Avalonia.ViewModels.Canvas.ComponentViewModel(
                    TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins()));
                var panel = main.RightPanel.CompressLayout;
                await panel.CompressLayoutCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(15));
                panel.ResultText.ShouldNotBeEmpty("the new document must reach the actual compression runner");
                panel.CurrentIteration.ShouldBe(panel.MaxIterations);
            }
        }
        finally { File.Delete(path); }
    }
}
