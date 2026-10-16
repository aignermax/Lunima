using Avalonia.Threading;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    // Loading fits before the chapter opens its analysis dock. Fit once more after
    // layout so the chosen example is framed in the viewport the learner will use.
    private void FitStartedLearningJourney(Func<bool> isActive)
    {
        var components = Canvas.Components.ToArray();
        var workspace = WorkspaceSwitch.Current;
        Dispatcher.UIThread.Post(() =>
        {
            if (!isActive() || WorkspaceSwitch.Current != workspace ||
                !Canvas.Components.SequenceEqual(components))
                return;
            var (width, height) = ViewportControl.GetViewportSize?.Invoke() ?? (900, 800);
            ViewportControl.ZoomToFit(width, height);
        }, DispatcherPriority.Loaded);
    }
}
