using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class MainShellCoordinatorTests
{
    [Fact]
    public void SelectedConnection_IsProjectedIntoEveryConnectionPanel()
    {
        using var main = MainViewModelTestHelper.CreateMainViewModel();
        var connection = new WaveguideConnectionViewModel(new WaveguideConnection
        {
            StartPin = new PhysicalPin(), EndPin = new PhysicalPin()
        });

        main.CanvasInteraction.SelectedWaveguideConnection = connection;

        main.BottomPanel.ConnectionRouting.SelectedConnection.ShouldBeSameAs(connection);
        main.BottomPanel.RerouteImported.SelectedConnection.ShouldBeSameAs(connection);
        main.BottomPanel.LengthMatching.SelectedConnection.ShouldBeSameAs(connection);
        main.BottomPanel.ConnectionLoss.SelectedConnection.ShouldBeSameAs(connection);
        main.CanvasInteraction.SelectedWaveguideConnection = null;
        main.BottomPanel.ConnectionRouting.SelectedConnection.ShouldBeNull();
        main.BottomPanel.RerouteImported.SelectedConnection.ShouldBeNull();
        main.BottomPanel.LengthMatching.SelectedConnection.ShouldBeNull();
        main.BottomPanel.ConnectionLoss.SelectedConnection.ShouldBeNull();
    }

    [Fact]
    public void MissingSavedGroup_PreservesPlacementAndReportsCorruption()
    {
        using var main = MainViewModelTestHelper.CreateMainViewModel();
        var previous = new GroupTemplate { Name = "Loaded", TemplateGroup = new ComponentGroup("Loaded") };
        main.LeftPanel.SelectedGroupTemplate = previous;
        var missing = new GroupTemplate { Name = "Missing", FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json") };

        main.LeftPanel.SelectedGroupTemplate = missing;

        main.CanvasInteraction.SelectedGroupTemplate.ShouldBeSameAs(previous);
        main.StatusText.ShouldBe(string.Format(LocalizationService.Instance.Translate("Status.TemplateCorrupted"), missing.Name));
    }

    [Fact]
    public void MalformedSavedGroup_PreservesPlacementAndReportsLoadError()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ invalid");
            using var main = MainViewModelTestHelper.CreateMainViewModel();
            var previous = new GroupTemplate { Name = "Loaded", TemplateGroup = new ComponentGroup("Loaded") };
            main.LeftPanel.SelectedGroupTemplate = previous;

            main.LeftPanel.SelectedGroupTemplate = new GroupTemplate { Name = "Malformed", FilePath = file };

            main.CanvasInteraction.SelectedGroupTemplate.ShouldBeSameAs(previous);
            main.StatusText.ShouldContain("Malformed");
            main.StatusText.ShouldNotBe(string.Format(LocalizationService.Instance.Translate("Status.TemplateCorrupted"), "Malformed"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Dispose_DetachesOwnedCallbacksAndChapterProgress()
    {
        var main = MainViewModelTestHelper.CreateMainViewModel();
        main.CanvasInteraction.OnSelectionChanged.ShouldNotBeNull();
        main.Home.WatchComputeTourRequested.ShouldNotBeNull();
        var chapter = main.LearningPath.CurrentNumber;

        main.Dispose();
        main.Dispose();

        main.CanvasInteraction.OnSelectionChanged.ShouldBeNull();
        main.CanvasInteraction.ClearLeftPanelGroupSelection.ShouldBeNull();
        main.LeftPanel.OnGroupTemplateSelected.ShouldBeNull();
        main.RightPanel.DesignValidation.HighlightConnection.ShouldBeNull();
        main.RightPanel.DesignValidation.AlignChipletHandler.ShouldBeNull();
        main.Home.WatchComputeTourRequested.ShouldBeNull();
        main.WatchTour.Start();
        try
        {
            main.WatchTour.IsActive.ShouldBeTrue();
            main.LearningPath.CurrentNumber.ShouldBe(chapter);
            main.LearningPath.IsVisible.ShouldBeFalse();
        }
        finally { main.WatchTour.Skip(); }
    }

    [Fact]
    public void WritableBusyAndSuccessFacades_RelayPropertyNotifications()
    {
        using var main = MainViewModelTestHelper.CreateMainViewModel();
        var changed = new List<string?>();
        main.PropertyChanged += (_, change) => changed.Add(change.PropertyName);

        main.IsRunningDesignChecks = true;
        main.ShowLearningSuccess = true;

        main.IsRunningDesignChecks.ShouldBeTrue();
        main.ShowLearningSuccess.ShouldBeTrue();
        changed.ShouldContain(nameof(MainViewModel.IsRunningDesignChecks));
        changed.ShouldContain(nameof(MainViewModel.ShowLearningSuccess));
    }
}
