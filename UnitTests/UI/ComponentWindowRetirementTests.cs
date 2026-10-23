using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Views;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.UI;

public sealed class ComponentWindowRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedClose_KeepsWindowAliveUntilItsWorkerCleanupFinishes(bool settings)
    {
        var fixture = new ComponentJobFixture(settings ? "solve" : "preview");
        var editor = settings ? null : fixture.NewEditor();
        var dialog = settings ? fixture.Settings() : null;
        Window window = settings ? new ComponentSettingsDialog { DataContext = dialog }
            : new NewComponentWindow { DataContext = editor };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var running = settings ? fixture.StartAsync(dialog!) : fixture.StartAsync(editor!);
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            window.Close();
            window.IsVisible.ShouldBeTrue("the worker still owns this window's model");
            window.IsEnabled.ShouldBeFalse();
            await fixture.Barrier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closed.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            await fixture.Barrier.FinishAsync(running);
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (settings) await dialog!.StopAsync(); else { await editor!.StopAsync(); editor.Dispose(); }
        }
        fixture.Store.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task CancelledSettingsClose_KeepsWorkerAndDialogActive()
    {
        var fixture = new ComponentJobFixture("solve");
        var model = fixture.Settings();
        var window = new ComponentSettingsDialog { DataContext = model };
        var refuse = true;
        window.Closing += (_, args) => args.Cancel = refuse;
        window.Show();
        var running = fixture.StartAsync(model);
        try
        {
            await fixture.Barrier.AwaitStartAsync(running);
            window.Close();
            window.IsVisible.ShouldBeTrue();
            window.IsEnabled.ShouldBeTrue();
            fixture.Barrier.Cancelled.Task.IsCompleted.ShouldBeFalse("an unapproved close must not retire the live feature");
        }
        finally
        {
            await fixture.Barrier.FinishAsync(running);
            refuse = false;
            window.Close();
            await model.StopAsync();
        }
    }
}
