using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;

/// <summary>
/// Assembly half of <see cref="LogicPanelViewModel"/>: runs the
/// <see cref="CAP_Core.Analysis.LogicAnalysis.LogicNetworkAssembler"/> asynchronously
/// over the current design with cancellation and maps the result (or any failure)
/// onto the panel's display state.
/// </summary>
public partial class LogicPanelViewModel
{
    /// <summary>Assembles the logic network of the current design at the active wavelength.</summary>
    [RelayCommand]
    private Task BuildNetwork() => _work.RunAsync(BuildNetworkCoreAsync);

    private async Task BuildNetworkCoreAsync(CancellationToken stop)
    {
        if (IsProcessing || _canvas == null)
            return;

        var canvas = _canvas;
        var configuration = _configurationVersion;
        AnalysisSweepOperation? operation = null;
        long? publicationRevision = null;
        IsProcessing = true;
        StatusText = Translate("Analysis.LogicPanel.Running");
        try
        {
            operation = new AnalysisSweepOperation(_work, canvas.Document,
                () => configuration == _configurationVersion && ReferenceEquals(canvas, _canvas), stop, stop);
            _activeBuild = operation;
            publicationRevision = _builtNetworkProvider?.PublicationRevision;
            var components = canvas.Components.Select(c => c.Component).ToList();
            var connections = canvas.Connections.Select(c => c.Connection).ToList();
            var wavelengthNm = ResolveWavelengthNm();
            WavelengthText = string.Format(Translate("LogicPanel.Wavelength"), wavelengthNm);

            var network = await _assembler.AssembleAsync(
                components, connections, wavelengthNm, operation.Token);
            operation.ThrowIfObsolete();
            operation.TryPublish(() =>
            {
                ShowNetwork(network);
                StatusText = string.Format(
                    Translate("Analysis.LogicPanel.Complete"), Inputs.Count, Outputs.Count);
            });
        }
        catch (OperationCanceledException)
        {
            if (operation?.IsContextCurrent != true) return;
            ClearNetwork(publicationRevision);
            StatusText = Translate("Analysis.LogicPanel.Cancelled");
        }
        catch (Exception ex)
        {
            if (operation?.CanPublish != true) return;
            // No gate in the design (InvalidOperationException), wiring violations
            // naming the pins (ArgumentException), or an unexpected simulation
            // failure: a readable message, never a crash or a stale "running" state.
            ClearNetwork(publicationRevision);
            StatusText = string.Format(Translate("Analysis.LogicPanel.Failed"), ex.Message);
        }
        finally
        {
            IsProcessing = false;
            if (ReferenceEquals(_activeBuild, operation)) _activeBuild = null;
            if (operation != null) await operation.DisposeAsync();
        }
    }

    /// <summary>Cancels the running assembly.</summary>
    [RelayCommand]
    private void Cancel() => _activeBuild?.Cancel();
}
