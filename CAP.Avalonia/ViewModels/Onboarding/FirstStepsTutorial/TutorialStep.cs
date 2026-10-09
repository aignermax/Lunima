namespace CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;

/// <summary>
/// A single guided-tour step: localized title/body keys plus a completion
/// predicate evaluated against live canvas state. The engine re-checks the
/// predicate whenever the canvas signals a relevant change; when it turns
/// true the tour advances to the next step.
/// </summary>
public sealed class TutorialStep
{
    /// <summary>Creates a step with its localization keys and completion condition.</summary>
    /// <param name="targetName">
    /// <c>x:Name</c> of the control the step anchors to (spotlight + arrow, #1167);
    /// null means the card floats bottom-centre without a spotlight.
    /// </param>
    public TutorialStep(string titleKey, string bodyKey, Func<bool> isCompleted, string? targetName = null)
    {
        TitleKey = titleKey;
        BodyKey = bodyKey;
        IsCompleted = isCompleted;
        TargetName = targetName;
    }

    /// <summary>Localization key for the step title.</summary>
    public string TitleKey { get; }

    /// <summary>Localization key for the task-focused body text.</summary>
    public string BodyKey { get; }

    /// <summary><c>x:Name</c> of the anchor target control, or null for a floating card.</summary>
    public string? TargetName { get; }

    /// <summary>Returns true when the step's task has been performed on the canvas.</summary>
    public Func<bool> IsCompleted { get; }
}
