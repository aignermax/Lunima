using Avalonia.Headless.XUnit;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentEditorDialogRetirementTests
{
    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("matrix")]
    [InlineData("save")]
    public async Task Retirement_AwaitsPickerOrConfirmationBeforeReleasingEditor(string stage)
    {
        var fixture = new ComponentJobFixture("unused");
        var model = fixture.NewEditor();
        var barrier = new RetirementBarrier();
        model.PickPyFile = async () => { await barrier.WaitAsync(CancellationToken.None); return "late code"; };
        model.PickSMatrixFile = async () => { await barrier.WaitAsync(CancellationToken.None); return "late.s2p"; };
        fixture.Library.Setup(store => store.ComponentExistsInFile(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        model.ConfirmOverwrite = async (_, _) => { await barrier.WaitAsync(CancellationToken.None); return true; };
        var originalCode = model.Code;
        var saved = 0;
        model.Saved += (_, _) => saved++;
        var running = stage switch
        {
            "code" => model.LoadCodeFromFileCommand.ExecuteAsync(null),
            "matrix" => model.LoadSMatrixFromFileCommand.ExecuteAsync(null),
            _ => model.SaveCommand.ExecuteAsync(null),
        };
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            stopping = model.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the accepted dialog callback still owns editor work");
        }
        finally { await barrier.FinishAsync(running, stopping); }
        model.Code.ShouldBe(originalCode);
        model.SavedDraft.ShouldBeNull();
        model.HasSMatrix.ShouldBeFalse();
        saved.ShouldBe(0);
    }
}
