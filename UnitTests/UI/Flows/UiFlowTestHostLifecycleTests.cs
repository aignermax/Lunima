using Avalonia.Headless.XUnit;
using Xunit;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class UiFlowTestHostLifecycleTests
{
    [AvaloniaFact]
    public void Dispose_WithUnsavedFixtureChanges_ClosesTheRealWindow()
    {
        var host = new UiFlowTestHost();
        var closed = false;
        host.Window.Closed += (_, _) => closed = true;
        host.Vm.FileOperations.HasUnsavedChanges = true;
        try
        {
            host.Dispose();
            Assert.True(closed, "Fixture cleanup must close a dirty window instead of cancelling close.");
            Assert.False(host.Window.IsVisible);
        }
        finally
        {
            // Also clean up after the RED assertion, without leaking into other UI tests.
            host.Vm.FileOperations.HasUnsavedChanges = false;
            host.Window.Close();
            UiInput.RunJobs();
        }
    }
}
