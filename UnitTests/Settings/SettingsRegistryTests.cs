using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Settings;
using Shouldly;
using Xunit;

namespace UnitTests.Settings;

/// <summary>
/// Unit tests for the settings registry pattern.
/// Verifies that <see cref="SettingsWindowViewModel"/> correctly enumerates pages
/// and that individual page implementations meet the contract.
/// </summary>
public class SettingsRegistryTests
{
    // -----------------------------------------------------------------------
    // SettingsWindowViewModel — registry contract
    // -----------------------------------------------------------------------

    [Fact]
    public void SettingsWindowViewModel_SelectsFirstPageByDefault()
    {
        // Arrange
        var pages = new List<ISettingsPage>
        {
            new StubSettingsPage("Alpha"),
            new StubSettingsPage("Beta"),
        };

        // Act
        var vm = new SettingsWindowViewModel(pages);

        // Assert
        vm.SelectedPage.ShouldNotBeNull();
        vm.SelectedPage!.Title.ShouldBe("Alpha");
    }

    [Fact]
    public void SettingsWindowViewModel_ExposesAllRegisteredPages()
    {
        // Arrange
        var pages = new List<ISettingsPage>
        {
            new StubSettingsPage("A"),
            new StubSettingsPage("B"),
            new StubSettingsPage("C"),
        };

        // Act
        var vm = new SettingsWindowViewModel(pages);

        // Assert
        vm.Pages.Count.ShouldBe(3);
    }

    [Fact]
    public void SettingsWindowViewModel_WithNoPages_HasNullSelectedPage()
    {
        var vm = new SettingsWindowViewModel(Enumerable.Empty<ISettingsPage>());
        vm.SelectedPage.ShouldBeNull();
    }

    [Fact]
    public void SettingsWindowViewModel_ChangingSelectedPage_UpdatesProperty()
    {
        // Arrange
        var page1 = new StubSettingsPage("Page1");
        var page2 = new StubSettingsPage("Page2");
        var vm = new SettingsWindowViewModel(new[] { page1, page2 });

        // Act
        vm.SelectedPage = page2;

        // Assert
        vm.SelectedPage!.Title.ShouldBe("Page2");
    }

    // -----------------------------------------------------------------------
    // GridSnapSettingsPage
    // -----------------------------------------------------------------------

    [Fact]
    public void GridSnapSettingsPage_ContractIsSatisfied()
    {
        var canvas = new DesignCanvasViewModel();
        ISettingsPage page = new GridSnapSettingsPage(canvas);

        page.Title.ShouldNotBeNullOrEmpty();
        page.Icon.ShouldNotBeNullOrEmpty();
        page.ViewModel.ShouldNotBeNull();
        page.ViewModel.ShouldBeOfType<GridSnapSettingsViewModel>();
    }

    [Fact]
    public void GridSnapSettingsPage_ViewModelWrapsCanvasGridSnap()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.GridSnap.IsEnabled = true;
        canvas.GridSnap.GridSizeMicrometers = 25.0;

        var page = new GridSnapSettingsPage(canvas);
        var vm = (GridSnapSettingsViewModel)page.ViewModel;

        vm.GridSnap.IsEnabled.ShouldBeTrue();
        vm.GridSnap.GridSizeMicrometers.ShouldBe(25.0);
    }

    [Fact]
    public void GridSnapSettingsPage_ViewModelChange_AffectsCanvas()
    {
        var canvas = new DesignCanvasViewModel();
        var page = new GridSnapSettingsPage(canvas);
        var vm = (GridSnapSettingsViewModel)page.ViewModel;

        // Act: change via settings ViewModel
        vm.GridSnap.IsEnabled = true;
        vm.GridSnap.GridSizeMicrometers = 100.0;

        // Assert: same objects — canvas reflects the change immediately
        canvas.GridSnap.IsEnabled.ShouldBeTrue();
        canvas.GridSnap.GridSizeMicrometers.ShouldBe(100.0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class StubSettingsPage : ISettingsPage
    {
        public StubSettingsPage(string title) { Title = title; }
        public string Title { get; }
        public string Icon => "⚙";
        public string? Category => null;
        public object ViewModel => new object();
    }
}
