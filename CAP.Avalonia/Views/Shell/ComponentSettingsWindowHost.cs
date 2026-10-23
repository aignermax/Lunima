using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Views.Dialogs;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns per-entity settings windows and their template/instance override lifetime.</summary>
internal sealed class ComponentSettingsWindowHost(
    Window owner, CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel library,
    CAP.Avalonia.ViewModels.Panels.FileOperationsViewModel files, UserSMatrixOverrideStore? userStore,
    Func<ComponentSettingsDialogFactory> createDialogFactory, Action openSolverSettings)
{
    private readonly Window _owner = owner;
    private readonly CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel _library = library;
    private readonly CAP.Avalonia.ViewModels.Panels.FileOperationsViewModel _files = files;
    private readonly UserSMatrixOverrideStore? _userStore = userStore;
    private readonly Func<ComponentSettingsDialogFactory> _createDialogFactory = createDialogFactory;
    private readonly Action _openSolverSettings = openSolverSettings;
    private readonly Dictionary<string, ComponentSettingsDialog> _openComponentSettingsDialogs = new();

    /// <summary>
    /// Refreshes the 📊 user-global-override badges on every PDK template in the
    /// library list. Called on initial wire-up and after every dialog mutation in
    /// template mode so the badge tracks the on-disk store without manual reloads.
    /// </summary>
    public void RefreshTemplateOverrideBadges()
    {
        var userStore = _userStore;
        if (userStore == null) return;

        _library.RefreshUserGlobalOverrideBadges(userStore.Overrides.ContainsKey);
    }

    /// <summary>
    /// Creates and shows the Component Settings dialog for the given entity.
    ///
    /// Per-Instance mode (<paramref name="liveComponent"/> non-null): the dialog
    /// reads/writes <c>FileOperations.StoredSMatrices</c>, so the override is
    /// scoped to this canvas instance and persisted in the .lun file.
    ///
    /// Per-Template mode (<paramref name="liveComponent"/> null): the dialog
    /// reads/writes the user-global <see cref="UserSMatrixOverrideStore"/>, so
    /// the override applies to every instance of that template across every
    /// project the user opens. After a successful import/delete the store is
    /// flushed to disk and live components matching the template are
    /// re-applied so the change takes effect immediately without reloading.
    /// </summary>
    public void Show(
        string entityKey,
        string displayName,
        CAP_Core.Components.Core.Component? liveComponent,
        ComponentTemplate? templateForDefaults = null)
    {
        if (_openComponentSettingsDialogs.TryGetValue(entityKey, out var existingDialog))
        {
            existingDialog.Activate();
            return;
        }

        // Reset affordance for user-sourced draft matrices (e.g. an FDTD-computed
        // fork component): reverts to the bundled foundry definition via the same
        // mechanism as the library's per-component restore. Only an ACTUAL revert
        // yields the fresh template — a failed rewrite must not show "restored".
        Func<Task<ComponentTemplate?>>? resetToPdkOriginal = null;
        if (templateForDefaults != null && _library.IsComponentRevertToBundled(templateForDefaults))
        {
            resetToPdkOriginal = () => Task.FromResult(
                _library.RestoreTemplateToBundledOriginal(templateForDefaults)
                    == ViewModels.Panels.BundledRevertResult.Reverted
                    ? _library.AllTemplates.FirstOrDefault(t =>
                        t.Name == templateForDefaults.Name && t.PdkSource == templateForDefaults.PdkSource)
                    : null);
        }

        var (dialogVm, backendSelection) = _createDialogFactory().Create(resetToPdkOriginal, _openSolverSettings);

        bool isTemplateMode = liveComponent == null && _userStore != null;
        var store = isTemplateMode
            ? _userStore!.Overrides
            : _files.StoredSMatrices;

        Action onChanged = isTemplateMode
            ? () =>
              {
                  _userStore!.Save();
                  _files.ReapplyTemplateOverrides();
                  _library.HierarchyPanel.RefreshOverrideMarkers();
                  RefreshTemplateOverrideBadges();
              }
            : () => _library.HierarchyPanel.RefreshOverrideMarkers();

        // Effective S-matrix data feeds the read-only "Currently effective" section.
        // Per-Instance: read straight off the live component (its WaveLengthToSMatrixMap
        // is exactly what the simulator will use, including any override already applied).
        // Per-Template: build a throwaway component from the template so we can show
        // the PDK default without requiring a canvas instance.
        Dictionary<int, CAP_Core.LightCalculation.SMatrix>? effectiveSMatrices = null;
        IReadOnlyList<CAP_Core.Components.Core.Pin>? effectivePins = null;
        IReadOnlyList<string>? availablePinNames = null;
        if (liveComponent != null)
        {
            effectiveSMatrices = liveComponent.WaveLengthToSMatrixMap;
            effectivePins = liveComponent.PhysicalPins
                .Where(pp => pp.LogicalPin != null)
                .Select(pp => pp.LogicalPin!)
                .ToList();
            // Pin-name list drives the port-mapping dialog. Use PhysicalPin
            // names (what the user sees in the UI), not the LogicalPin's
            // internal id, so the dialog matches the rest of the dialog.
            availablePinNames = liveComponent.PhysicalPins
                .Where(pp => pp.LogicalPin != null)
                .Select(pp => pp.Name)
                .ToList();
        }
        else if (templateForDefaults != null)
        {
            var tempInstance = ComponentTemplates.CreateFromTemplate(templateForDefaults, 0, 0);
            effectiveSMatrices = tempInstance.WaveLengthToSMatrixMap;
            effectivePins = tempInstance.PhysicalPins
                .Where(pp => pp.LogicalPin != null)
                .Select(pp => pp.LogicalPin!)
                .ToList();
            availablePinNames = templateForDefaults.PinDefinitions
                .Select(pd => pd.Name)
                .ToList();
        }

        string? templateFunctionName = liveComponent?.NazcaFunctionName;
        string? templateFunctionParameters = liveComponent?.NazcaFunctionParameters;
        string? templateModuleName = liveComponent?.NazcaModuleName;

        // S-matrix overrides (FDTD recompute / file import) are stored under the component's
        // geometry identity so a copy inherits them; the library/template path keys on entityKey.
        Func<string> smatrixKeyResolver = liveComponent != null
            ? () => CAP.Avalonia.Services.ComponentGeometryKey.For(liveComponent)
            : () => entityKey;
        string smatrixKey = smatrixKeyResolver();

        // Issue #580 E: after a per-instance FDTD recompute, promote the result to the
        // user-global template override — but only while the instance geometry still matches
        // the template draft.
        Func<CAP_DataAccess.Persistence.PIR.ComponentSMatrixData, bool>? propagateToTemplate = null;
        if (liveComponent != null && _userStore != null)
        {
            propagateToTemplate = data =>
            {
                var templateKey = _files.ResolveTemplateKey(liveComponent);
                if (templateKey == null)
                    return false;

                if (!CAP.Avalonia.Services.TemplateGeometryMatch.Matches(
                        liveComponent, templateModuleName, templateFunctionName, templateFunctionParameters))
                    return false;

                _userStore.Overrides[templateKey] = data;
                _userStore.Save();
                _files.ReapplyTemplateOverrides();
                _library.HierarchyPanel.RefreshOverrideMarkers();
                RefreshTemplateOverrideBadges();
                return true;
            };
        }

        dialogVm.Configure(
            entityKey,
            smatrixKey,
            displayName,
            store,
            liveComponent,
            onChanged: onChanged,
            isUserGlobalScope: isTemplateMode,
            effectiveSMatrices: effectiveSMatrices,
            effectivePins: effectivePins,
            availablePinNames: availablePinNames,
            smatrixKeyResolver: smatrixKeyResolver,
            propagateToTemplate: propagateToTemplate,
            template: templateForDefaults);

        var dialog = new ComponentSettingsDialog { DataContext = dialogVm };
        _openComponentSettingsDialogs[entityKey] = dialog;
        dialog.Closed += (_, _) =>
        {
            _openComponentSettingsDialogs.Remove(entityKey);
            // Release the picker's subscription to the singleton backend registry.
            backendSelection?.Dispose();
        };
        // Probe the selected backend upfront so a known-bad state (Docker down, no
        // API key) disables the run button and shows the hint before the first click.
        _ = dialogVm.RefreshBackendAvailabilityAsync();
        dialog.Show(_owner);
    }
}
