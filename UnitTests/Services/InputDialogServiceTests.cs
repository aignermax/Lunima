using CAP.Avalonia.Services;
using Shouldly;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// Unit tests for IInputDialogService interface contract.
/// Actual UI testing would require integration tests with Avalonia test framework.
/// </summary>
public class InputDialogServiceTests
{
    [Fact]
    public void InputDialogService_ImplementsInterface()
    {
        // Arrange & Act
        var service = new InputDialogService();

        // Assert
        service.ShouldBeAssignableTo<IInputDialogService>();
    }

    [Fact]
    public void IInputDialogService_HasRequiredMethods()
    {
        // Verify interface contract
        var interfaceType = typeof(IInputDialogService);

        // Check ShowInputDialogAsync method
        var showInputMethod = interfaceType.GetMethod("ShowInputDialogAsync");
        showInputMethod.ShouldNotBeNull();
        showInputMethod!.ReturnType.ShouldBe(typeof(Task<string?>));

        // Check ShowMultiInputDialogAsync method
        var showMultiMethod = interfaceType.GetMethod("ShowMultiInputDialogAsync");
        showMultiMethod.ShouldNotBeNull();
        showMultiMethod!.ReturnType.ShouldBe(typeof(Task<Dictionary<string, string>?>));
    }

    // Note: Full UI testing of InputDialogService would require:
    // - Avalonia Headless testing setup
    // - Mocking ApplicationLifetime
    // - UI automation framework
    // These are better suited for integration tests in a UI test project
}
