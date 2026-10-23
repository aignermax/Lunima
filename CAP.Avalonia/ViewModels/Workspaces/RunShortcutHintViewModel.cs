using CAP.Avalonia.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Workspaces;

/// <summary>
/// The brief, non-modal hint under the Run button that names what the L key just did
/// ("L — Design checks", "L — Logic step"). The same key means a different action per
/// workspace, so the first few presses of each action say it out loud; after that the
/// user knows and the hint stays quiet. Hides itself after a moment and never takes focus.
/// </summary>
public partial class RunShortcutHintViewModel : ObservableObject
{
    /// <summary>How often each action is announced per session.</summary>
    public const int MaxShowsPerAction = 3;

    /// <summary>How long the hint stays visible.</summary>
    public static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(2.5);

    private readonly Dictionary<string, int> _showCounts = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _hideCts;
    private readonly Services.Lifetime.ShellWorkScope _work = new();

    /// <summary>Retires hint admission and drains its scheduled hide.</summary>
    public Task StopAsync() => _work.StopAsync();

    /// <summary>The hint text, e.g. "L — Design checks".</summary>
    [ObservableProperty]
    private string _text = "";

    /// <summary>True while the hint is on screen.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Creates the hint; tests inject the hide delay.</summary>
    public RunShortcutHintViewModel(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? Task.Delay;
    }

    /// <summary>The pending auto-hide (test seam).</summary>
    internal Task PendingHide { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Announces the action behind <paramref name="actionKey"/> unless it was announced
    /// <see cref="MaxShowsPerAction"/> times already.
    /// </summary>
    /// <param name="actionKey">String-table key of the action name.</param>
    /// <returns>True when the hint was shown.</returns>
    public bool TryShow(string actionKey)
    {
        if (_work.IsStopping) return false;
        ArgumentException.ThrowIfNullOrEmpty(actionKey);
        var count = _showCounts.GetValueOrDefault(actionKey);
        if (count >= MaxShowsPerAction)
            return false;
        _showCounts[actionKey] = count + 1;
        var loc = LocalizationService.Instance;
        Text = string.Format(loc.Translate("Workspace.ShortcutHint"), loc.Translate(actionKey));
        IsVisible = true;
        _hideCts?.Cancel();
        PendingHide = _work.RunAsync(async stop =>
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _hideCts = source;
            try { await HideLaterAsync(source.Token); }
            finally { if (ReferenceEquals(_hideCts, source)) _hideCts = null; }
        });
        return true;
    }

    private async Task HideLaterAsync(CancellationToken token)
    {
        try
        {
            await _delay(VisibleDuration, token);
            _work.ThrowIfStopping();
            token.ThrowIfCancellationRequested();
            IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // A newer hint replaced this one and owns the hide.
        }
    }
}
