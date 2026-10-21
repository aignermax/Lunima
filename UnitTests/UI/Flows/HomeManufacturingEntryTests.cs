using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Views;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class HomeManufacturingEntryTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HomeManufacturingRow_PointerClickStartsGuidanceOrExplainsMissingExample(bool missingExample)
    {
        using var host = new UiFlowTestHost();
        host.Window.Width = 1280;
        host.Window.Height = 900;
        host.Vm.Home.Show();
        if (missingExample) host.Vm.Home.Examples.Clear();
        await Task.Delay(180);
        UiInput.RunJobs();
        var home = UiInput.Descendants<HomeView>(host.Window).Single();
        var chapters = UiInput.Descendants<Expander>(home).Single();
        var toggle = UiInput.Descendants<global::Avalonia.Controls.Primitives.ToggleButton>(chapters).First();
        UiInput.Click(host.Window, toggle);
        await Task.Delay(200);
        UiInput.RunJobs();
        Assert.True(chapters.IsExpanded);
        var entry = UiInput.Descendants<Button>(home).Single(button =>
            ReferenceEquals(button.Command, host.Vm.Home.ManufacturingTourCommand));
        Assert.True(entry.IsEffectivelyEnabled);
        UiInput.Click(host.Window, entry);
        for (var attempt = 0; attempt < 50 && host.Vm.Home.ManufacturingTourCommand.IsRunning; attempt++)
        {
            await Task.Delay(20);
            UiInput.RunJobs();
        }
        if (missingExample)
        {
            Assert.False(host.Vm.ManufacturingTour.IsActive);
            Assert.True(host.Vm.Home.IsHomeVisible);
            Assert.Contains(UiInput.Descendants<TextBlock>(home), text =>
                text.IsEffectivelyVisible && text.Text?.Contains("bundled example", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Equal(0, host.Vm.LearningPath.CompletedCount);
        }
        else
        {
            Assert.True(host.Vm.ManufacturingTour.IsActive);
            Assert.False(host.Vm.Home.IsHomeVisible);
            Assert.Equal(7, host.Vm.LearningPath.CurrentNumber);
        }
    }
}
