using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Gathers validation inputs, runs checks and commits their findings to the canvas.</summary>
internal sealed class DesignCheckRunCoordinator : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly LeftPanelViewModel _library;
    private readonly ProcessRoutingContext _process;
    private readonly DesignValidationViewModel _validation;
    private readonly ChipSizeViewModel _chipSize;
    private readonly Action<string> _status;
    private bool _isRunning;

    public DesignCheckRunCoordinator(DesignCanvasViewModel canvas, LeftPanelViewModel library,
        ProcessRoutingContext process, DesignValidationViewModel validation, ChipSizeViewModel chipSize,
        Action<string> status) =>
        (_canvas, _library, _process, _validation, _chipSize, _status) = (canvas, library, process, validation, chipSize, status);

    public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }
    public async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        // Status-bar busy indicator for the backgrounded run (issue #1150): the user
        // sees the validation is in flight instead of a frozen status bar.
        _status(LocalizationService.Instance.Translate("Status.RunningDesignChecks"));
        try
        {
            var connections = _canvas.Connections
                .Select(c => c.Connection)
                .ToList();

            var groups = _canvas.Components
                .Select(c => c.Component)
                .OfType<CAP_Core.Components.Core.ComponentGroup>()
                .ToList();

            var allComponents = _canvas.Components
                .Select(c => c.Component)
                .ToList();

            // PDK-process compatibility (issue #570 follow-up, LC-T4): resolve each placed
            // component's PDK source the same way the placement/paste guards do — the snapshot
            // TemplatePdkSource captured when it was placed, falling back to a live library match
            // (see ComponentClipboard/FileOperationsViewModel for the same fallback) — so a process
            // edit that diverges a PDK from the design's active process is flagged for review even
            // though the already-placed components themselves are never touched or deleted.
            var pdkSourceByComponent = _canvas.Components.ToDictionary(
                c => c.Component,
                c => c.TemplatePdkSource ?? _process.Placement.ResolveComponentPdkSource(c.Component));
            // Group children carry no template snapshot of their own; the PDK check walks
            // them at every nesting depth, so resolve them through the live library too.
            foreach (var nested in groups.SelectMany(g => g.GetAllComponentsRecursive()))
                pdkSourceByComponent.TryAdd(nested, _process.Placement.ResolveComponentPdkSource(nested));

            // Under a real process lock the allowed set is the lock-derived membership; without one
            // (Playground/no selection) nothing is locked, so GetProcessCompatiblePdkNames() equals
            // all loaded PDK names and only a component whose PDK isn't loaded at all (e.g.
            // trash-deleted while its placed instances were kept, as PdkDelete_Click promises) gets
            // flagged — with a "not loaded" wording instead of a process-mismatch message that would
            // reference a process that doesn't exist (PR #739 review, both directions).
            // The design-level process governs single-die designs only; inside a die its own
            // process does, and at an assembly's root each die checks its parts itself.
            var scopeProcess = _process.CanvasScopeProcess;
            var processLockActive = scopeProcess is { IsPlayground: false };
            var compatiblePdkNames = _library.PdkManager.GetProcessCompatiblePdkNames();

            // DRC-lite per connection (issue #936): each connection's width and spacing
            // limits come from its OWN endpoint components' PDK processes — the stricter
            // chiplet governs a cross-chiplet route, same rule as the router's bend floor
            // (#937) — so a two-process canvas (only possible in Playground, #935) is
            // checked per chiplet instead of silently skipping every PDK-dependent rule,
            // and a locked multi-member process no longer checks the other members against
            // the first member PDK's limits. PDKs that declare no minimum stay silent
            // (#926). Library and drafts are snapshotted here on the UI thread (same
            // pattern as the #937 floor provider); connections whose endpoints don't
            // resolve to a PDK (built-ins) yield null and fall back to the lock-derived
            // canvas-wide values below, exactly like the #937 floor. Frozen group paths
            // carry no pins to resolve an owning process from, so they also keep the
            // canvas-wide spacing (0 in Playground = off, as before).
            var drcTemplates = _library.AllTemplates.ToList();
            var drcDrafts = _library.GetLoadedPdkDrafts().ToList();
            string? DrcPdkSourceOf(PhysicalPin? pin) =>
                pin?.ParentComponent is { } component
                    ? ComponentPdkSourceResolver.Resolve(component, drcTemplates)
                    : null;
            Func<CAP_Core.Components.Connections.WaveguideConnection, CAP_Core.Analysis.ConnectionDrcRules?> connectionDrcRuleProvider =
                connection => ConnectionDrcRuleResolver.ResolveForEndpointPdkNames(
                    DrcPdkSourceOf(connection.StartPin), DrcPdkSourceOf(connection.EndPin), drcDrafts);

            double minWaveguideSpacingMicrometers = 0;
            IReadOnlyList<CAP_Core.Analysis.WaveguideMinWidthRule>? minWaveguideWidthRules = null;
            if (processLockActive)
            {
                var memberProcess = _library.ResolveLiveMemberPdkNames(scopeProcess!)
                    .Select(name => drcDrafts.FirstOrDefault(
                        d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))?.Process)
                    .FirstOrDefault(p => p is not null);
                minWaveguideSpacingMicrometers = memberProcess.GetMinWaveguideSpacingMicrometersOrDefault();
                minWaveguideWidthRules = memberProcess.GetMinWaveguideWidthRules();
            }

            bool usesDieBounds = _canvas.Dies.IsAssemblyRoot || _canvas.Dies.ActiveDie != null;
            await _validation.RunValidationAsync(
                connections,
                groups,
                allComponents,
                // A die assembly (and the inside of one of its dies) is bounded per die by the
                // die checks; the single-chip bounds check only applies to single-die designs.
                usesDieBounds ? 0 : _chipSize.CurrentWidthMicrometers,
                usesDieBounds ? 0 : _chipSize.CurrentHeightMicrometers,
                pdkSourceByComponent,
                _library.GetProcessAgnosticPdkNames(),
                compatiblePdkNames,
                processLockActive,
                minWaveguideSpacingMicrometers: minWaveguideSpacingMicrometers,
                minWaveguideWidthRules: minWaveguideWidthRules,
                connectionDrcRuleProvider: connectionDrcRuleProvider,
                wavelengthNm: _canvas.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm);

            _status(_validation.StatusText);
            _canvas.ViewSettings.SetDesignCheckFindings(_validation.AllFindings);
        }
        finally
        {
            IsRunning = false;
        }
    }

}
