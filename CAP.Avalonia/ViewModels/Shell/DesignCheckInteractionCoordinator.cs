using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Connects finding navigation and undoable chiplet fixes to the active canvas.</summary>
internal sealed class DesignCheckInteractionCoordinator : IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly DesignValidationViewModel _validation;
    private readonly Action<double, double> _navigate;
    private readonly Func<Task> _runChecks;
    private readonly ChipletAlignmentService _alignment;

    public DesignCheckInteractionCoordinator(DesignCanvasViewModel canvas, CommandManager commands,
        DesignValidationViewModel validation, Action<double, double> navigate, Func<Task> runChecks)
    {
        (_canvas, _validation, _navigate, _runChecks) = (canvas, validation, navigate, runChecks);
        _alignment = new ChipletAlignmentService(canvas, commands);
        validation.NavigateToPosition = navigate;
        validation.HighlightConnection = Highlight;
        validation.AlignChipletHandler = AlignAsync;
    }

    private void Highlight(WaveguideConnection? connection)
    {
        foreach (var item in _canvas.Connections) item.IsSelected = item.Connection == connection;
    }

    private async Task<string?> AlignAsync(WaveguideConnection connection)
    {
        double wavelength = _canvas.Components.FirstOrDefault(component => component.IsLightSource)
            ?.LaserConfig?.WavelengthNm ?? CAP_Core.Components.ComponentHelpers.StandardWaveLengths.RedNM;
        var refusal = _alignment.TryAlign(connection, wavelength);
        if (refusal != null) return LocalizationService.Instance.Translate($"DesignChecks.Align.Refused.{refusal}");
        await _runChecks();
        return null;
    }

    public void Dispose()
    {
        if (_validation.NavigateToPosition == _navigate) _validation.NavigateToPosition = null;
        if (_validation.HighlightConnection == Highlight) _validation.HighlightConnection = null;
        if (_validation.AlignChipletHandler == AlignAsync) _validation.AlignChipletHandler = null;
    }
}
