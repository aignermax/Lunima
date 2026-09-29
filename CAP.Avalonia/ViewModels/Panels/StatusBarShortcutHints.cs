using System;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// Maps the canvas <see cref="InteractionMode"/> to the localization key of the short,
/// mode-relevant shortcut hint shown in the status bar (#1162): the bar no longer lists
/// every shortcut at once (that overflowed and slid under the process badge) — the full
/// reference lives in the (?) flyout next to the hints.
/// </summary>
public static class StatusBarShortcutHints
{
    /// <summary>Localization key of the status-bar hint for <paramref name="mode"/>.</summary>
    public static string KeyForMode(InteractionMode mode) => mode switch
    {
        InteractionMode.Select => "StatusBar.Hints.Select",
        InteractionMode.PlaceComponent or InteractionMode.PlaceGroupTemplate => "StatusBar.Hints.Place",
        InteractionMode.Connect => "StatusBar.Hints.Connect",
        InteractionMode.Delete => "StatusBar.Hints.Delete",
        InteractionMode.Probe => "StatusBar.Hints.Probe",
        InteractionMode.PickAnalysisOutput => "StatusBar.Hints.PickOutput",
        InteractionMode.Cut => "StatusBar.Hints.Cut",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
