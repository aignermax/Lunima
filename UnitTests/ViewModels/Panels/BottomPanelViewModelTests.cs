using CAP.Avalonia.ViewModels.Panels;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Panels;

/// <summary>
/// Unit tests for BottomPanelViewModel (status text).
/// </summary>
public class BottomPanelViewModelTests
{
    [Fact]
    public void Constructor_InitializesStatusText()
    {
        // Arrange & Act
        var vm = new BottomPanelViewModel();

        // Assert
        vm.StatusText.ShouldBe("Ready");
    }

    [Fact]
    public void StatusText_CanBeChanged()
    {
        // Arrange
        var vm = new BottomPanelViewModel();

        // Act
        vm.StatusText = "New status message";

        // Assert
        vm.StatusText.ShouldBe("New status message");
    }
}
