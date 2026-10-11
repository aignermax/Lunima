using System.Diagnostics;

namespace CAP.Avalonia.Controls.Rendering;

/// <summary>
/// Clock for the canvas' few one-shot animations: the selection glow fading in and the pulse a
/// blocked-connection marker gives the first time it appears. Renderers ask for a progress
/// value per item; while any animation is running <see cref="NeedsNextFrame"/> is set and the
/// canvas schedules one more frame. Once everything has settled no frame is requested, so an
/// idle canvas never repaints on its own.
/// </summary>
public sealed class CanvasMotion
{
    /// <summary>Selection glow fade-in (Motion.Fast-ish, Tokens.axaml).</summary>
    public const double SelectionFadeMs = 150;

    /// <summary>Length of one blocked-marker pulse.</summary>
    public const double PulseMs = 900;

    /// <summary>Number of pulses a newly shown blocked marker gives.</summary>
    public const int PulseCount = 2;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<Guid, double> _selectedSince = new();
    private readonly HashSet<Guid> _selectedThisFrame = new();
    private readonly Dictionary<Guid, double> _blockedFirstSeen = new();
    private double _nowMs;

    /// <summary>When false every animation is reported as finished (tests, screenshots).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>True when the last frame drew an animation that is still running.</summary>
    public bool NeedsNextFrame { get; private set; }

    /// <summary>Starts a frame: samples the clock once so every item of the frame agrees.</summary>
    public void BeginFrame()
    {
        _nowMs = _clock.Elapsed.TotalMilliseconds;
        NeedsNextFrame = false;
        _selectedThisFrame.Clear();
    }

    /// <summary>Ends a frame: forgets items no longer selected so a re-selection fades in again.</summary>
    public void EndFrame()
    {
        if (_selectedSince.Count == _selectedThisFrame.Count)
            return;
        foreach (var id in _selectedSince.Keys.Where(id => !_selectedThisFrame.Contains(id)).ToList())
            _selectedSince.Remove(id);
    }

    /// <summary>Glow strength 0..1 for a selected item (ease-out over <see cref="SelectionFadeMs"/>).</summary>
    public double SelectionGlow(Guid id)
    {
        if (!IsEnabled)
            return 1.0;
        _selectedThisFrame.Add(id);
        if (!_selectedSince.TryGetValue(id, out var since))
        {
            since = _nowMs;
            _selectedSince[id] = since;
        }
        double t = Math.Clamp((_nowMs - since) / SelectionFadeMs, 0, 1);
        if (t < 1)
            NeedsNextFrame = true;
        return 1 - (1 - t) * (1 - t);
    }

    /// <summary>
    /// Pulse phase 0..1 of a blocked marker within its current pulse, or <c>null</c> once its
    /// pulses are over (or motion is disabled). The first call for an id starts its pulses.
    /// </summary>
    public double? BlockedPulse(Guid id)
    {
        if (!IsEnabled)
            return null;
        if (!_blockedFirstSeen.TryGetValue(id, out var firstSeen))
        {
            firstSeen = _nowMs;
            _blockedFirstSeen[id] = firstSeen;
        }
        double elapsed = _nowMs - firstSeen;
        if (elapsed >= PulseMs * PulseCount)
            return null;
        NeedsNextFrame = true;
        return elapsed % PulseMs / PulseMs;
    }
}
