using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Documents;
using System.Runtime.ExceptionServices;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// Rebinds shell-owned analyses after a completed document replacement. Component
/// dialogs retain their original document identity and cannot follow navigation.
/// </summary>
internal sealed class PersistentAnalysisDocumentBinding
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly AnalysisDockViewModel _analysis;
    private readonly CompressLayoutViewModel _compression;
    private DocumentIdentity _identity;
    private BindingAttempt? _inProgress;

    internal PersistentAnalysisDocumentBinding(DesignCanvasViewModel canvas,
        AnalysisDockViewModel analysis, CompressLayoutViewModel compression)
    {
        _canvas = canvas;
        _analysis = analysis;
        _compression = compression;
        _identity = canvas.Document.CaptureIdentity();
    }

    internal void ProjectOpened()
    {
        if (_canvas.Document.IsCurrentIdentity(_identity)) return;
        if (_inProgress is { } current && _canvas.Document.IsCurrentIdentity(current.Identity)) return;
        DocumentIdentity identity;
        try { identity = _canvas.Document.CaptureIdentity(); }
        catch (OperationCanceledException) { return; }
        // Prevent recursive same-document binding without marking an incomplete
        // attempt as successful. A failed observer must allow a later retry.
        var attempt = new BindingAttempt(identity);
        _inProgress = attempt;
        List<Exception> errors = [];
        try
        {
            Configure(() => _analysis.Transient.Configure(_canvas));
            Configure(() => _analysis.Eye.Configure(_canvas));
            Configure(() => _analysis.MonteCarlo.Configure(_canvas));
            Configure(() => _analysis.Optimization.ConfigureForCanvas(_canvas));
            Configure(() => _compression.Configure(_canvas));
            if (errors.Count == 0 && _canvas.Document.IsCurrentIdentity(identity))
                _identity = identity;
        }
        finally
        {
            // A nested replacement owns its own attempt/completion state.
            if (ReferenceEquals(_inProgress, attempt)) _inProgress = null;
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(errors);

        void Configure(Action configure)
        {
            if (!_canvas.Document.IsCurrentIdentity(identity)) return;
            try { configure(); }
            catch (Exception error) { errors.Add(error); }
        }
    }

    private sealed record BindingAttempt(DocumentIdentity Identity);
}
