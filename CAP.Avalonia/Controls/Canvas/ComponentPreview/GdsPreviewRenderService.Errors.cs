using Avalonia.Threading;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

public sealed partial class GdsPreviewRenderService
{
    private string? _previewFailureDetail;

    /// <summary>The most recent preview failure, available in the shared library hint.</summary>
    public string? PreviewFailureDetail => _previewFailureDetail;
    /// <summary>Whether previews failed in the currently selected Python environment.</summary>
    public bool HasUnavailablePreviews => _previewFailureDetail != null;
    /// <summary>Optional diagnostic sink; failures are reported once per attempted geometry.</summary>
    public Action<string>? ReportPreviewFailure { get; set; }

    private void PublishPreviewFailure(string? detail, int generation)
    {
        void Publish()
        {
            if (generation != Volatile.Read(ref _environmentGeneration)) return;
            if (SetProperty(ref _previewFailureDetail, detail, nameof(PreviewFailureDetail)))
                OnPropertyChanged(nameof(HasUnavailablePreviews));
            if (detail != null) ReportPreviewFailure?.Invoke(detail);
        }
        if (Dispatcher.UIThread.CheckAccess()) Publish();
        else Dispatcher.UIThread.Post(Publish);
    }
}
