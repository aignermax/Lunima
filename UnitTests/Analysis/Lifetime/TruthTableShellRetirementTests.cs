using Avalonia.Headless.XUnit;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class TruthTableShellRetirementTests
{
    [AvaloniaFact]
    public async Task MainStop_CancelsAndDrainsActualTruthTableExtractionBeforeReturning()
    {
        await using var fixture = new TruthTableLifetimeFixture();
        await using var shell = MainViewModelTestHelper.CreateMainViewModel(canvas: fixture.Canvas,
            truthTableViewModel: fixture.Panel);
        fixture.ConfigureOriginal();
        shell.RightPanel.TruthTable.ShouldBeSameAs(fixture.Panel);
        var originalAssignment = fixture.Group.TruthTablePinAssignment;
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        try
        {
            await fixture.Extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping = shell.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the actual extraction is held after its numerical solve");
            await fixture.Extractor.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await fixture.FinishAsync(running);
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        }

        fixture.Group.TruthTablePinAssignment.ShouldBeSameAs(originalAssignment);
        fixture.Panel.HasResult.ShouldBeFalse();
        fixture.Panel.Rows.ShouldBeEmpty();
        await fixture.RunAsync();
        fixture.Extractor.Calls.ShouldBe(1, "a retained command cannot restart extraction after shell shutdown");
    }
}
