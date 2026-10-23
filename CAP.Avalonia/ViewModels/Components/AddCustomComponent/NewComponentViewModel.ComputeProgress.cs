using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Solvers.Fdtd;

namespace CAP.Avalonia.ViewModels.Components.AddCustomComponent;

public partial class NewComponentViewModel
{
    private CancellationTokenSource? _computeCts;

    public void CancelCompute() => _computeCts?.Cancel();

    private async Task<FdtdSMatrixResult> RunSolveWithLiveStatusAsync(
        IFdtdSMatrixService service, FdtdSMatrixRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var active = true;
        var baseMessage = service is IFdtdCostEstimator
            ? string.Format(
                LocalizationService.Instance.Translate("CompSettings.FdtdRunBaseMessageCloud"), SolverLabel)
            : LocalizationService.Instance.Translate("NewComp.FdtdBaseMessage");

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (!active || _work.IsStopping || ct.IsCancellationRequested) return;
            StatusText = string.Format(LocalizationService.Instance.Translate("NewComp.FdtdElapsed"),
                baseMessage, stopwatch.Elapsed.ToString(@"m\:ss"));
        };
        StatusText = $"{baseMessage}…";
        timer.Start();

        var progress = new Progress<string>(line =>
        {
            if (!active || _work.IsStopping || ct.IsCancellationRequested) return;
            StatusText = string.Format(
                LocalizationService.Instance.Translate("NewComp.FdtdRunning"),
                stopwatch.Elapsed.ToString(@"m\:ss"), Shorten(line));
        });
        try
        {
            return await service.SolveAsync(request, progress, ct);
        }
        finally
        {
            active = false;
            timer.Stop();
        }
    }

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
