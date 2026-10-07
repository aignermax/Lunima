using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.Views;
using Shouldly;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// The ISA playground shows the program once: the editor carries the line numbers and
/// highlights the current step in place (no separate trace listing). Renders PR media
/// when <c>UI_SHOT_DIR</c> is set.
/// </summary>
[Collection("LocalizationSingleton")]
public sealed class IsaProgramEditorTests
{
    private const int StepsToMidProgram = 3;
    private const int RenderTicks = 5;

    [AvaloniaFact]
    public void Editor_SyncsProgramTextBothWays()
    {
        var vm = new IsaPlaygroundViewModel();
        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = window.FindControl<IsaProgramEditor>("ProgramEditor").ShouldNotBeNull();
            editor.Text.ShouldBe(vm.ProgramText, "the sample program arrives in the editor");
            editor.ShowLineNumbers.ShouldBeTrue();

            editor.Text = "LOAD 7\nHALT";
            Dispatcher.UIThread.RunJobs();

            vm.ProgramText.ShouldBe("LOAD 7\nHALT", "typing in the editor reaches the program");
            vm.IsAssembled.ShouldBeFalse("an edit makes the assembled program stale");
            editor.HighlightedLine.ShouldBeNull("a stale program has no current step");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void Stepping_MovesTheHighlightInTheEditor()
    {
        var vm = new IsaPlaygroundViewModel();
        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = window.FindControl<IsaProgramEditor>("ProgramEditor").ShouldNotBeNull();
            var first = editor.HighlightedLine.ShouldNotBeNull("the first instruction is highlighted after assembling");

            for (int i = 0; i < StepsToMidProgram; i++)
                vm.StepCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            editor.HighlightedLine.ShouldBe(vm.CurrentSourceLine);
            editor.HighlightedLine.ShouldNotBe(first, "the highlight follows the program counter");
            SaveShotIfRequested(window);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void SaveShotIfRequested(Window window)
    {
        var root = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(root))
            return;
        var dir = Path.Combine(root, "isa-program-editor");
        Directory.CreateDirectory(dir);
        // Headless frames render only on a render-timer tick: without pumping, the capture
        // shows the state before the last step.
        for (int i = 0; i < RenderTicks; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        ScreenshotArtifacts.SavePng(frame!, Path.Combine(dir, "isa-playground-editor-current-step.png"));
    }
}
