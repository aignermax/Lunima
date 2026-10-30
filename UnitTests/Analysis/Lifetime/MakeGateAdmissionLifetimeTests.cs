using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class MakeGateAdmissionLifetimeTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("stop")]
    public async Task ConfiguredOldEditor_CannotStartNewWork(string change)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        if (change == "stop") await fixture.Panel.StopAsync();
        else fixture.ChangeContext(change);
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        fixture.Extractor.Calls.ShouldBe(0);
        fixture.Panel.HasResult.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task CompletedResult_CannotSaveOrExportThroughAnObsoleteEditor(string change)
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        fixture.Panel.HasResult.ShouldBeTrue(fixture.Panel.StatusText);
        var libraryWrites = 0;
        fixture.Panel.SaveToLibraryRequested = (_, _) => { libraryWrites++; return true; };
        fixture.Panel.GateName = "changed";
        fixture.ChangeContext(change);

        fixture.Panel.SaveCommand.Execute(null);
        fixture.Panel.SaveToLibraryCommand.Execute(null);

        fixture.Group.GroupName.ShouldBe("original");
        libraryWrites.ShouldBe(0);
        fixture.Panel.IsSaved.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task MainRetirement_RetiresItsActualMakeGateEditor()
    {
        await using var shell = MainViewModelTestHelper.CreateMainViewModel();
        var component = new ComponentViewModel(LogicSnapshotFixture.Gate("gate"));
        shell.Canvas.Components.Add(component);
        shell.Canvas.Selection.SelectSingle(component);
        var panel = shell.RightPanel.MakeGate;
        panel.ConfigureForSelection(component, shell.Canvas);
        panel.OpenCommand.Execute(null);
        panel.IsActive.ShouldBeTrue();

        await shell.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        panel.IsActive.ShouldBeFalse("the real shell must retire its owned editor");
        await panel.SimulateCommand.ExecuteAsync(null);
        panel.HasResult.ShouldBeFalse();
        panel.CanMakeGate.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Save_NotifiesOnlyAfterTheAdmittedDocumentMutationCompletes()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        await fixture.Panel.SimulateCommand.ExecuteAsync(null);
        fixture.Panel.GateName = "saved";
        var notifications = 0;
        fixture.Panel.GateDefinitionChanged += (_, _) =>
        {
            using var observer = fixture.Canvas.Document.CaptureOperation();
            observer.IsCurrent.ShouldBeTrue("observers may begin a fresh graph rebuild after the complete save");
            fixture.Group.GroupName.ShouldBe("saved");
            notifications++;
        };
        fixture.Panel.SaveCommand.Execute(null);
        fixture.Panel.IsSaved.ShouldBeTrue(fixture.Panel.StatusText);
        notifications.ShouldBe(1);
    }
}
