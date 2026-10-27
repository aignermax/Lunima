using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using Moq;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class ShellTransientDocumentReplacementTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AcceptedReplacement_RejectsHeldOldResultAndAdmitsFreshShellRun(bool eye, bool load)
    {
        await using var fixture = new TransientPanelFixture(eye);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(
            canvas: fixture.Canvas, transientViewModel: fixture.Transient, eyeViewModel: fixture.Eye);
        var path = Path.Combine(Path.GetTempPath(), $"shell-transient-{Guid.NewGuid():N}.lun");
        await File.WriteAllTextAsync(path, "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}");
        var oldIdentity = fixture.Canvas.Document.CaptureIdentity();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            var accepted = load
                ? await main.FileOperations.LoadDesignFromPathAsync(path)
                : await main.FileOperations.TryNewProjectAsync();
            accepted.ShouldBeTrue();
            fixture.Canvas.Document.CaptureIdentity().ShouldNotBe(oldIdentity);
            fixture.Canvas.Components.ShouldBeEmpty();

            // Populate through the real canonical canvas after completion, without
            // manually reconfiguring the persistent shell panel under test.
            var replacement = new SweepSnapshotFixture();
            replacement.First.IsUserMarkedLightSource = true;
            replacement.First.LaserEnabled = true;
            replacement.Second.IsUserMarkedLightSource = true;
            replacement.Second.LaserEnabled = false;
            fixture.Canvas.Components.Add(new ComponentViewModel(replacement.First));
            fixture.Canvas.Components.Add(new ComponentViewModel(replacement.Second));
            fixture.Canvas.RestoreConnectionAndViewModel(replacement.Wire);

            await fixture.Runner.Barrier.FinishAsync(running);
            fixture.HasResult.ShouldBeFalse("the held result belongs to the replaced document");
            fixture.Text.ShouldBeEmpty();
            await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(15));

            fixture.Runner.Calls.ShouldBe(2, "persistent shell panels must bind to the accepted replacement");
            fixture.HasResult.ShouldBeTrue();
            fixture.Text.ShouldNotBeEmpty();
            fixture.Errors.Entries.ShouldBeEmpty();
            if (!eye)
                fixture.Runner.TransientResult!.Result.PinTraces.ShouldContainKey(replacement.Output);
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectedReplacement_KeepsTheAcceptedShellRun(bool eye, bool load)
    {
        await using var fixture = new TransientPanelFixture(eye);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(
            canvas: fixture.Canvas, transientViewModel: fixture.Transient, eyeViewModel: fixture.Eye);
        var path = Path.Combine(Path.GetTempPath(), $"shell-transient-rejected-{Guid.NewGuid():N}.lun");
        await File.WriteAllTextAsync(path, "{\"FormatVersion\":\"2.0\",\"Components\":[],\"Connections\":[]}");
        var messages = new Mock<IMessageBoxService>();
        messages.Setup(service => service.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(SavePromptResult.Cancel);
        main.FileOperations.MessageBoxService = messages.Object;
        main.FileOperations.HasUnsavedChanges = true;
        var identity = fixture.Canvas.Document.CaptureIdentity();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            var accepted = load
                ? await main.FileOperations.LoadDesignFromPathAsync(path)
                : await main.FileOperations.TryNewProjectAsync();
            accepted.ShouldBeFalse();
            fixture.Canvas.Document.CaptureIdentity().ShouldBe(identity);
            await fixture.Runner.Barrier.FinishAsync(running);
            fixture.Runner.Calls.ShouldBe(1);
            fixture.HasResult.ShouldBeTrue("a rejected navigation must not revoke the current analysis");
            fixture.Errors.Entries.ShouldBeEmpty();
        }
        finally
        {
            await fixture.Runner.Barrier.FinishAsync(running);
            File.Delete(path);
        }
    }
}
