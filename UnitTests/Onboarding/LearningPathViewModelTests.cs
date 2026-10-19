using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Onboarding.LearningPath;
using Shouldly;

namespace UnitTests.Onboarding;

public class LearningPathViewModelTests
{
    [Fact]
    public void Pause_notifies_before_hiding_and_reveal_preserves_chapter_without_starting_or_replacing_design()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new LearningPathViewModel(Preferences(path));
            bool visibleAtPause = false;
            int starts = 0;
            model.Pausing += () => visibleAtPause = model.IsVisible;
            model.StartChapterRequested = _ => { starts++; return Task.CompletedTask; };
            model.Activate(4);
            model.CloseCommand.Execute(null);
            visibleAtPause.ShouldBeTrue();
            model.IsVisible.ShouldBeFalse();
            model.RevealCommand.Execute(null);
            model.IsVisible.ShouldBeTrue();
            model.IsExpanded.ShouldBeTrue();
            model.CurrentNumber.ShouldBe(4);
            starts.ShouldBe(0);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Closing_during_pending_start_prevents_late_sidebar_reactivation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new LearningPathViewModel(Preferences(path));
            var pending = new TaskCompletionSource();
            model.StartChapterRequested = async number => { await pending.Task; model.Activate(number); };
            var start = model.ResumeCommand.ExecuteAsync(null);
            model.CloseCommand.Execute(null);
            pending.SetResult();
            await start;
            model.IsVisible.ShouldBeFalse();
            model.CompletedCount.ShouldBe(0);
        }
        finally { File.Delete(path); }
    }
    private static UserPreferencesService Preferences(string path) => new(path);

    [Fact]
    public void Completion_is_persisted_once_and_skip_does_not_award()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new LearningPathViewModel(Preferences(path));
            model.Activate(1);
            model.Pause();
            model.CompletedCount.ShouldBe(0);
            model.Activate(1);
            model.Complete(1);
            model.Complete(1);
            model.CompletedCount.ShouldBe(1);
            model.CanContinue.ShouldBeTrue();
            var restored = new LearningPathViewModel(Preferences(path));
            restored.CompletedCount.ShouldBe(1);
            restored.Chapters[0].IsCompleted.ShouldBeTrue();
            restored.IsVisible.ShouldBeFalse();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Next_calls_next_starter_without_awarding_it_and_cancel_keeps_selection()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new LearningPathViewModel(Preferences(path));
            int requested = 0;
            model.StartChapterRequested = number => { requested = number; return Task.CompletedTask; };
            model.Activate(1);
            model.Complete(1);
            await model.ContinueCommand.ExecuteAsync(null);
            requested.ShouldBe(2);
            model.CurrentNumber.ShouldBe(1); // The starter did not activate: cancelled save prompt.
            model.CompletedCount.ShouldBe(1);
            model.Activate(2);
            model.CanContinue.ShouldBeFalse();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Invalid_or_inactive_completion_cannot_award_and_last_chapter_has_no_next()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new LearningPathViewModel(Preferences(path));
            model.Complete(4);
            model.CompletedCount.ShouldBe(0);
            model.Activate(7);
            model.Complete(6);
            model.CompletedCount.ShouldBe(0);
            model.Complete(7);
            model.CanContinue.ShouldBeFalse();
            model.CompletedCount.ShouldBe(1);
        }
        finally { File.Delete(path); }
    }
}
