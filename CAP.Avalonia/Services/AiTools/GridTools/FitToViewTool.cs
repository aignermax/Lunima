using System.Text.Json;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.Services.AiTools.GridTools;

/// <summary>
/// AI tool that adjusts zoom and pan so all placed components are visible in the viewport.
/// Call this after placing components so the user can immediately see the design.
/// </summary>
public class FitToViewTool : IAiTool
{
    private readonly ViewportControlViewModel _viewport;

    private const double DefaultViewportWidth = 900.0;
    private const double DefaultViewportHeight = 800.0;

    /// <summary>Initializes the tool with the viewport ViewModel.</summary>
    public FitToViewTool(ViewportControlViewModel viewport) => _viewport = viewport;

    /// <inheritdoc/>
    public string Name => "fit_to_view";

    /// <inheritdoc/>
    public string Description =>
        "Adjusts zoom and pan to fit all placed components in the viewport. " +
        "Always call this after placing or connecting components so the user can see the result immediately.";

    /// <inheritdoc/>
    public object InputSchema => new { type = "object", properties = new { } };

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement input)
    {
        var (width, height) = _viewport.GetViewportSize?.Invoke() ?? (DefaultViewportWidth, DefaultViewportHeight);
        _viewport.ZoomToFit(width, height);
        return Task.FromResult(JsonSerializer.Serialize(new { success = true, message = "Viewport fitted to show all components." }));
    }
}
