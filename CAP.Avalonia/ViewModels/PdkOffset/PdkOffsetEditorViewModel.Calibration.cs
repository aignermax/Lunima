using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.PdkOffset;

/// <summary>
/// PdkOffsetEditorViewModel partial — pin-alignment, single-component
/// Auto-Calibrate, and the Check-All / Try-Fix-All batch commands.
/// All state (properties, fields) is declared in the main partial file.
/// </summary>
public partial class PdkOffsetEditorViewModel
{
    /// <summary>
    /// Compares Lunima's PDK-JSON pin positions against the Nazca render's
    /// pin stubs (in Nazca-space micrometres) and populates
    /// <see cref="PinAlignmentResults"/> + <see cref="PinAlignmentSummary"/>.
    /// Each Lunima pin is matched to its nearest Nazca pin by Euclidean
    /// distance — name-matching is unreliable across PDKs (Lunima uses
    /// "in"/"out", SiEPIC uses "opt1"/"opt2").
    /// </summary>
    internal void ComputePinAlignment(NazcaPreviewResult result, PdkComponentDraft draft)
    {
        PinAlignmentResults.Clear();
        if (result.Pins.Count == 0 || draft.Pins.Count == 0)
        {
            PinAlignmentSummary = result.Pins.Count == 0
                ? "Nazca cell exposes no pins — Lunima pin positions cannot be cross-checked."
                : "Lunima component has no pins defined.";
            return;
        }

        int aligned = 0;
        foreach (var lp in draft.Pins)
        {
            // Lunima pin position in Nazca-space micrometres. Lunima offsets
            // are measured from the bbox top-left in y-down. The Nazca origin
            // sits at (NazcaOriginOffsetX, ComponentHeight - NazcaOriginOffsetY)
            // inside that bbox in y-down — i.e. the offset Y is measured from
            // the bottom edge upward, the Lunima pin Y from the top edge down.
            // The y-flip therefore needs the ComponentHeight term to subtract
            // the Lunima distance from the bottom, then push to Nazca origin.
            // Same formula as PinPositionViewModel.NazcaRelY.
            var lunimaNazcaX = lp.OffsetXMicrometers - (draft.NazcaOriginOffsetX ?? 0);
            var lunimaNazcaY = (draft.HeightMicrometers - lp.OffsetYMicrometers)
                               - (draft.NazcaOriginOffsetY ?? 0);

            var nearest = result.Pins
                .Select(np => (np, dist: Math.Sqrt(
                    (np.X - lunimaNazcaX) * (np.X - lunimaNazcaX) +
                    (np.Y - lunimaNazcaY) * (np.Y - lunimaNazcaY))))
                .OrderBy(t => t.dist)
                .First();

            var dx = nearest.np.X - lunimaNazcaX;
            var dy = nearest.np.Y - lunimaNazcaY;
            var isAligned = nearest.dist <= PinAlignmentToleranceMicrometers;
            if (isAligned) aligned++;

            PinAlignmentResults.Add(new PinAlignmentInfo(
                lp.Name, nearest.np.Name, dx, dy, nearest.dist, isAligned));
        }

        PinAlignmentSummary = aligned == draft.Pins.Count
            ? $"✓ All {aligned}/{draft.Pins.Count} Lunima pins align with Nazca pins (≤{PinAlignmentToleranceMicrometers:F1} µm)."
            : $"⚠ {aligned}/{draft.Pins.Count} pins aligned. Worst delta: " +
              $"{PinAlignmentResults.Max(p => p.DistanceMicrometers):F2} µm — adjust NazcaOriginOffset.";
    }

    /// <summary>
    /// Derives Width / Height / NazcaOriginOffset from the cached Nazca bbox
    /// and snaps every Lunima pin to its matched Nazca pin position. The user
    /// no longer has to reverse-engineer the bbox math — one click and the
    /// JSON aligns with the GDS down to the pin.
    ///
    /// Pin matching is greedy bipartite by Euclidean distance using the
    /// component's CURRENT calibration as the projection space, so a
    /// roughly-correct starting offset is enough. Pin counts must match —
    /// otherwise the command refuses with an explicit error so the user
    /// knows the mismatch is real (e.g. SiEPIC GC has 'io' + 'wg' but the
    /// Lunima JSON only declares one pin).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoCalibrate))]
    private void AutoCalibrate()
    {
        if (_lastNazcaResult is not { Success: true } r || SelectedComponent == null)
        {
            StatusText = "Auto-calibrate needs a successful Nazca preview.";
            return;
        }

        var draft = SelectedComponent.Draft;
        var outcome = PdkOffsetCalibration.ApplyAutoCalibrate(draft, r);
        if (outcome != AutoCalibrateOutcome.Success)
        {
            StatusText = outcome switch
            {
                AutoCalibrateOutcome.DegenerateBbox =>
                    $"Auto-calibrate aborted: Nazca bbox is degenerate " +
                    $"(XMin={r.XMin}, XMax={r.XMax}, YMin={r.YMin}, YMax={r.YMax}).",
                AutoCalibrateOutcome.PinCountMismatch =>
                    $"Auto-calibrate aborted: Lunima component '{SelectedComponent.ComponentName}' " +
                    $"declares {draft.Pins.Count} pins but the Nazca cell exposes {r.Pins.Count} — " +
                    "pin counts must match for unambiguous alignment.",
                _ => "Auto-calibrate failed for an unknown reason.",
            };
            return;
        }

        // Mirror back into the bound numeric controls so the editor reflects
        // the new calibration without requiring the user to re-select the row.
        OffsetX = draft.NazcaOriginOffsetX!.Value;
        OffsetY = draft.NazcaOriginOffsetY!.Value;
        ComponentWidth = draft.WidthMicrometers;
        ComponentHeight = draft.HeightMicrometers;

        SelectedComponent.RefreshStatus();
        RefreshPinPositions(draft);
        RefreshCanvasMarkers(draft);
        ComputePinAlignment(r, draft);
        HasUnsavedChanges = true;
        StatusText = $"Auto-calibrated '{SelectedComponent.ComponentName}' from GDS bbox " +
                     $"({draft.WidthMicrometers:F2} × {draft.HeightMicrometers:F2} µm, " +
                     $"origin {draft.NazcaOriginOffsetX:F2}/{draft.NazcaOriginOffsetY:F2}). " +
                     "Click Save to persist.";
    }

    private bool CanAutoCalibrate() =>
        _lastNazcaResult is { Success: true } && SelectedComponent != null;

    /// <summary>
    /// Test seam: lets unit tests place a synthetic <see cref="NazcaPreviewResult"/>
    /// into the cache slot the AutoCalibrate command reads from, without spinning
    /// up the Python preview pipeline.
    /// </summary>
    internal void SeedNazcaResultForTesting(NazcaPreviewResult result)
    {
        _lastNazcaResult = result;
        AutoCalibrateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cancels any in-flight Check-All / Try-Fix-All run.</summary>
    [RelayCommand]
    private void CancelBatch() => _batchCts?.Cancel();

    /// <summary>
    /// Renders every PDK component through the Nazca preview helper and
    /// builds a per-component report (aligned / misaligned / pin-count
    /// mismatch / render-failed). Pure read-only — no draft mutation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunBatch))]
    private async Task CheckAll() => await RunCheckAll();

    /// <summary>
    /// Runs Check-All, applies Auto-Calibrate to every fixable component,
    /// then re-runs Check-All. The remaining report rows are exactly the
    /// components whose JSON / GDS combination cannot be auto-fixed
    /// (pin-count mismatch, render error).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunBatch))]
    private async Task TryFixAll()
    {
        if (_previewService == null || Components.Count == 0) return;

        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        IsBatchRunning = true;
        try
        {
            await RunCheckAllInternal(token);
            if (token.IsCancellationRequested) return;

            var fixable = BatchCheckResults
                .Where(r => r.Status == ComponentCheckStatus.Misaligned)
                .Select(r => r.ComponentName)
                .ToHashSet();

            int fixedCount = 0;
            for (int i = 0; i < Components.Count; i++)
            {
                if (token.IsCancellationRequested) break;
                var item = Components[i];
                if (!fixable.Contains(item.Draft.Name ?? "")) continue;
                BatchProgress = $"Fixing [{++fixedCount}] {item.Draft.Name}…";
                var result = await RenderForBatch(item.Draft, token);
                if (result?.Success != true) continue;
                if (PdkOffsetCalibration.ApplyAutoCalibrate(item.Draft, result) ==
                    AutoCalibrateOutcome.Success)
                {
                    await UiThreadMarshaller(() =>
                    {
                        item.RefreshStatus();
                        HasUnsavedChanges = true;
                    });
                }
            }

            await RunCheckAllInternal(token);
            int remaining = BatchCheckResults.Count(r => r.Status != ComponentCheckStatus.Aligned);
            int total = BatchCheckResults.Count;
            BatchSummary = remaining == 0
                ? $"✓ Try-Fix-All: all {total} components aligned. Click Save PDK to persist."
                : $"⚠ Try-Fix-All: {total - remaining}/{total} aligned, " +
                  $"{remaining} need manual edits (see report below).";
            // Refresh the currently-selected component's overlay so the user
            // sees their fix without having to re-click the row.
            if (SelectedComponent != null)
                _ = TriggerNazcaRenderAsync(SelectedComponent.Draft);
        }
        finally
        {
            IsBatchRunning = false;
            BatchProgress = "";
        }
    }

    private async Task RunCheckAll()
    {
        if (_previewService == null || Components.Count == 0) return;
        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        IsBatchRunning = true;
        try
        {
            await RunCheckAllInternal(token);
            int aligned = BatchCheckResults.Count(r => r.Status == ComponentCheckStatus.Aligned);
            int total = BatchCheckResults.Count;
            BatchSummary = aligned == total
                ? $"✓ Check-All: all {total} components aligned."
                : $"⚠ Check-All: {aligned}/{total} aligned. " +
                  $"{BatchCheckResults.Count(r => r.IsAutoFixable && r.Status != ComponentCheckStatus.Aligned)} " +
                  "fixable via Try-Fix-All.";
        }
        finally
        {
            IsBatchRunning = false;
            BatchProgress = "";
        }
    }

    private async Task RunCheckAllInternal(CancellationToken token)
    {
        await UiThreadMarshaller(() => BatchCheckResults.Clear());
        for (int i = 0; i < Components.Count; i++)
        {
            if (token.IsCancellationRequested) return;
            var item = Components[i];
            BatchProgress = $"[{i + 1}/{Components.Count}] {item.Draft.Name}…";
            var result = await RenderForBatch(item.Draft, token);
            if (token.IsCancellationRequested) return;
            var check = PdkOffsetCalibration.Evaluate(
                item.Draft, result ?? NazcaPreviewResult.Fail("render returned null"),
                PinAlignmentToleranceMicrometers);
            await UiThreadMarshaller(() => BatchCheckResults.Add(check));
        }
    }

    private async Task<NazcaPreviewResult?> RenderForBatch(PdkComponentDraft draft, CancellationToken token)
    {
        try
        {
            var (module, function) = ResolveModuleAndFunction(draft.NazcaFunction);
            return await _previewService!.RenderAsync(module, function, draft.NazcaParameters, token);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            return NazcaPreviewResult.Fail(ex.Message);
        }
    }

    private bool CanRunBatch() =>
        _previewService != null && !IsBatchRunning && Components.Count > 0;
}
