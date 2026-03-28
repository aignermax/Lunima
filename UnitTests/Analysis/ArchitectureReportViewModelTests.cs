using CAP.Avalonia.ViewModels.Analysis;
using CAP_Core.Analysis;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for <see cref="ArchitectureReportViewModel"/> and <see cref="ArchitectureMetrics"/>.
/// </summary>
public class ArchitectureReportViewModelTests
{
    [Fact]
    public void InitialState_HasCorrectDefaultStatusText()
    {
        var vm = new ArchitectureReportViewModel();
        vm.StatusText.ShouldNotBeNullOrEmpty();
        vm.HasMetrics.ShouldBeFalse();
    }

    [Fact]
    public void LoadMetrics_PopulatesViewModelCount()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.ViewModelCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void LoadMetrics_PopulatesTestFileCount()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.TestFileCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void LoadMetrics_MaturityScoreIsInValidRange()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.MaturityScore.ShouldBeInRange(1, 5);
    }

    [Fact]
    public void LoadMetrics_SetsHasMetricsTrue()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.HasMetrics.ShouldBeTrue();
    }

    [Fact]
    public void LoadMetrics_PopulatesRecommendations()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.Recommendations.ShouldNotBeEmpty();
    }

    [Fact]
    public void LoadMetrics_SetsPrismRecommendationText()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.PrismRecommendationText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void LoadMetrics_UpdatesStatusText()
    {
        var vm = new ArchitectureReportViewModel();
        var initialText = vm.StatusText;
        vm.LoadMetricsCommand.Execute(null);
        vm.StatusText.ShouldNotBe(initialText);
    }

    [Fact]
    public void LoadMetrics_DiRegistrationCountIsPositive()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        vm.DiRegistrationCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void LoadMetrics_MainViewModelLinesIsReasonable()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        // MainViewModel.cs must be at least 100 lines (it has significant logic)
        vm.MainViewModelLines.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void ArchitectureMetrics_Current_ReturnsValidInstance()
    {
        var metrics = ArchitectureMetrics.Current();
        metrics.ShouldNotBeNull();
        metrics.ViewModelCount.ShouldBeGreaterThan(0);
        metrics.TestFileCount.ShouldBeGreaterThan(0);
        metrics.MaturityScore.ShouldBeInRange(1, 5);
        metrics.Recommendations.ShouldNotBeEmpty();
    }

    [Fact]
    public void ArchitectureMetrics_Current_PrismNotRecommended()
    {
        // For a project with <30 features, PRISM should not be recommended
        var metrics = ArchitectureMetrics.Current();
        metrics.PrismMigrationRecommended.ShouldBeFalse();
    }

    [Fact]
    public void LoadMetrics_CanBeCalledMultipleTimes()
    {
        var vm = new ArchitectureReportViewModel();
        vm.LoadMetricsCommand.Execute(null);
        var firstCount = vm.Recommendations.Count;
        vm.LoadMetricsCommand.Execute(null);
        // Recommendations should be replaced, not duplicated
        vm.Recommendations.Count.ShouldBe(firstCount);
    }
}
