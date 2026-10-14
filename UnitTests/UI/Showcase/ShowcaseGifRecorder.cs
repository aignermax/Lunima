using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;

namespace UnitTests.UI.Showcase;

/// <summary>
/// Records the frames of one feature-showcase GIF from the real headless window: every
/// <see cref="Capture"/> grabs the window, crops it to a fixed window-space rectangle and
/// stores a PNG plus its display duration. <see cref="EncodeAsync"/> writes
/// <c>frames.json</c> and runs <c>scripts/assemble_showcase_gif.py</c> (Pillow: one global
/// palettes, no dithering, delta frames) to produce <c>&lt;name&gt;.gif</c> in the showcase
/// output directory. Frames live under <c>artifacts/showcase-frames/&lt;name&gt;/</c>
/// (git-ignored) so a re-encode never needs a re-render.
/// </summary>
internal sealed class ShowcaseGifRecorder
{
    private const int EncodeTimeoutMs = 180_000;
    private readonly string _name;
    private readonly Window _window;
    private readonly PixelRect _crop;
    private readonly string _framesDir;
    private readonly List<(string File, int Ms)> _frames = new();

    /// <summary>Starts a recording; stale frames of a previous run are deleted.</summary>
    /// <param name="name">GIF base name, e.g. <c>hero-loop</c>.</param>
    /// <param name="window">The shown window to capture.</param>
    /// <param name="crop">Window-space pixel rectangle kept in every frame.</param>
    public ShowcaseGifRecorder(string name, Window window, PixelRect crop)
    {
        _name = name;
        _window = window;
        _crop = crop;
        _framesDir = Path.Combine(ShowcaseCapture.FramesRoot(), name);
        Directory.CreateDirectory(_framesDir);
        foreach (var stale in Directory.GetFiles(_framesDir))
            File.Delete(stale);
    }

    /// <summary>Number of frames recorded so far.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>Directory holding the recorded PNG frames.</summary>
    public string FramesDirectory => _framesDir;

    /// <summary>Captures the current window state as a frame shown for <paramref name="ms"/>.</summary>
    public void Capture(int ms)
    {
        var file = $"frame_{_frames.Count:D3}.png";
        ScreenshotArtifacts.SettleAnimations();
        using var frame = ShowcaseCapture.CaptureFrame(_window, $"{_name}/{file}");
        var crop = _crop.Intersect(new PixelRect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height));
        using var plate = new RenderTargetBitmap(new PixelSize(crop.Width, crop.Height));
        using (var ctx = plate.CreateDrawingContext())
        {
            ctx.DrawImage(frame,
                new Rect(crop.X, crop.Y, crop.Width, crop.Height),
                new Rect(0, 0, crop.Width, crop.Height));
        }
        ScreenshotArtifacts.SavePng(plate, Path.Combine(_framesDir, file));
        _frames.Add((file, ms));
    }

    /// <summary>Extends the display time of the last frame (a pause without a new capture).</summary>
    public void Hold(int ms)
    {
        if (_frames.Count == 0) return;
        var (file, current) = _frames[^1];
        _frames[^1] = (file, current + ms);
    }

    /// <summary>Total loop duration in milliseconds.</summary>
    public int TotalMs => _frames.Sum(f => f.Ms);

    /// <summary>
    /// Writes <c>frames.json</c> and encodes the GIF next to the showcase PNGs. Returns the
    /// GIF path, or null when no Python with Pillow is available (frames stay on disk).
    /// </summary>
    /// <param name="width">Optional output width in px (frames are downscaled to it).</param>
    public async Task<string?> EncodeAsync(int? width = null)
    {
        var spec = new
        {
            width,
            frames = _frames.Select(f => new { file = f.File, ms = f.Ms }).ToArray(),
        };
        File.WriteAllText(Path.Combine(_framesDir, "frames.json"), JsonSerializer.Serialize(spec));

        var python = await ShowcasePython.FindWithPillowAsync();
        if (python == null) return null;
        var script = Path.Combine(ShowcaseCapture.RepoRoot(), "scripts", "assemble_showcase_gif.py");
        var gif = Path.Combine(ShowcaseCapture.OutputDirectory(), $"{_name}.gif");
        var (exit, _, err) = await UvBootstrapper.RunProcessAsync(
            ProcessLaunchFactory.CreateDefault(), python, new[] { script, _framesDir, gif },
            CancellationToken.None, EncodeTimeoutMs);
        if (exit != 0)
            throw new InvalidOperationException($"GIF encode failed for {_name}: {err}");
        return gif;
    }
}
