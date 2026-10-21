using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport;

/// <summary>Contextual, cancellable access to the embedded source hierarchy.</summary>
public partial class GdsHierarchyActionsViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commands;
    private readonly GdsHierarchyExpansionService? _service;
    private ComponentViewModel? _selected;
    private CancellationTokenSource? _preparation;

    /// <summary>Whether the selection has an embedded source GDS.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExpandCommand))]
    private bool _isVisible;

    /// <summary>Whether source children are being prepared off the UI thread.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExpandCommand))]
    private bool _isBusy;

    /// <summary>A short result or reason the original geometry was kept intact.</summary>
    [ObservableProperty]
    private string _resultText = "";

    /// <summary>Mirrors outcomes into the application's status bar.</summary>
    public Action<string>? ReportStatus { get; set; }

    /// <summary>Creates actions on the current design; absent source storage disables expansion.</summary>
    public GdsHierarchyActionsViewModel(DesignCanvasViewModel canvas, CommandManager commands,
        GdsHierarchyExpansionService? service)
    {
        _canvas = canvas;
        _commands = commands;
        _service = service;
    }

    /// <summary>Updates the action for the current selection and cancels obsolete preparation.</summary>
    public void ConfigureForSelection(ComponentViewModel? selected)
    {
        if (!ReferenceEquals(_selected, selected))
        {
            _preparation?.Cancel();
            ResultText = "";
        }
        _selected = selected;
        IsVisible = selected is not null && _service?.IsImportedComponent(selected) == true;
    }

    private bool CanExpand() => IsVisible && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExpand))]
    private async Task ExpandAsync()
    {
        if (_selected is not { } selected || _service is null) return;
        var scope = _canvas.CurrentEditGroup;
        using var cancellation = new CancellationTokenSource();
        _preparation = cancellation;
        IsBusy = true;
        ResultText = LocalizationService.Instance.Translate("GdsSelection.Preparing");
        try
        {
            await Task.Yield();
            cancellation.Token.ThrowIfCancellationRequested();
            var command = await _service.PrepareAsync(_canvas, selected, cancellation.Token);
            bool applied = await _canvas.Routing.ExecuteHistoryChangeAsync(() =>
            {
                _commands.ExecuteCommand(command);
                return true;
            }, () => !cancellation.IsCancellationRequested && ReferenceEquals(_selected, selected) &&
                     ReferenceEquals(_canvas.CurrentEditGroup, scope) && _canvas.Components.Contains(selected));
            if (applied)
            {
                ConfigureForSelection(null);
                ReportStatus?.Invoke(LocalizationService.Instance.Translate("GdsSelection.Expanded"));
            }
        }
        catch (OperationCanceledException) { ResultText = ""; }
        catch (Exception ex)
        {
            ResultText = ex.Message;
            ReportStatus?.Invoke(ResultText);
        }
        finally
        {
            _preparation = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _preparation?.Cancel();
}
