using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.PIR;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// ViewModel for the per-instance Nazca parameter override section of the
/// Component Settings dialog. Allows editing the Nazca function name and
/// parameters for one canvas component without affecting the PDK template
/// or any other instance.
/// Template values are stored inside the persisted <see cref="NazcaCodeOverride"/>
/// record so "Reset to template" works correctly after a project reload, even when
/// the live component's properties have already been replaced by the override.
/// </summary>
public partial class InstanceNazcaOverrideViewModel : ObservableObject
{
    private readonly Dictionary<string, NazcaCodeOverride> _storedOverrides;
    private readonly Component? _liveComponent;
    private readonly string _componentKey;
    private readonly string _templateFunctionName;
    private readonly string _templateFunctionParameters;
    private readonly string? _templateModuleName;
    private readonly Action? _onChanged;

    /// <summary>The editable Nazca function name for this instance.</summary>
    [ObservableProperty]
    private string _functionName = string.Empty;

    /// <summary>The editable Nazca function parameters string for this instance.</summary>
    [ObservableProperty]
    private string _functionParameters = string.Empty;

    /// <summary>The editable Nazca module name for this instance (optional).</summary>
    [ObservableProperty]
    private string _moduleName = string.Empty;

    /// <summary>True when an override is currently stored for this component.</summary>
    [ObservableProperty]
    private bool _hasOverride;

    /// <summary>Status message shown after save or reset actions.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Initializes a new instance of <see cref="InstanceNazcaOverrideViewModel"/>.
    /// </summary>
    /// <param name="componentKey">The component's Identifier string, used as the store key.</param>
    /// <param name="storedOverrides">The shared per-instance Nazca override dictionary.</param>
    /// <param name="liveComponent">
    /// The live canvas component. When non-null, saving applies the override to the
    /// component immediately so the next Nazca export picks it up.
    /// </param>
    /// <param name="templateFunctionName">
    /// The PDK template's function name, captured before any override has been applied.
    /// Used as the fallback value when no stored override exists, and as the restore target
    /// for the "Reset to template" command.
    /// </param>
    /// <param name="templateFunctionParameters">
    /// The PDK template's function parameters, captured before any override has been applied.
    /// </param>
    /// <param name="templateModuleName">The PDK template's module name, or null.</param>
    /// <param name="onChanged">
    /// Optional callback invoked after every successful save or reset so observers
    /// (e.g. the hierarchy panel) can refresh derived state such as override badges.
    /// </param>
    public InstanceNazcaOverrideViewModel(
        string componentKey,
        Dictionary<string, NazcaCodeOverride> storedOverrides,
        Component? liveComponent,
        string templateFunctionName,
        string templateFunctionParameters,
        string? templateModuleName = null,
        Action? onChanged = null)
    {
        _componentKey = componentKey;
        _storedOverrides = storedOverrides;
        _liveComponent = liveComponent;
        _templateFunctionName = templateFunctionName;
        _templateFunctionParameters = templateFunctionParameters;
        _templateModuleName = templateModuleName;
        _onChanged = onChanged;

        RefreshFromStore();
    }

    /// <summary>
    /// Saves the current editable values as the per-instance Nazca override and
    /// applies them immediately to the live component so the next export picks them up.
    /// Also stores the original template values inside the override record for future
    /// "Reset to template" calls.
    /// </summary>
    [RelayCommand]
    private void SaveOverride()
    {
        var trimmedName = FunctionName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            StatusText = "Function name cannot be empty.";
            return;
        }

        var overrideData = new NazcaCodeOverride
        {
            FunctionName = trimmedName,
            FunctionParameters = FunctionParameters.Trim(),
            ModuleName = string.IsNullOrWhiteSpace(ModuleName) ? null : ModuleName.Trim(),
            TemplateFunctionName = _templateFunctionName,
            TemplateFunctionParameters = _templateFunctionParameters,
            TemplateModuleName = _templateModuleName
        };

        _storedOverrides[_componentKey] = overrideData;
        ApplyToLiveComponent(overrideData);

        HasOverride = true;
        StatusText = "Nazca override saved for this instance.";
        _onChanged?.Invoke();
    }

    /// <summary>
    /// Resets this instance to PDK template values by removing the stored override
    /// and restoring the live component's Nazca properties to the template defaults.
    /// </summary>
    [RelayCommand]
    private void ResetToTemplate()
    {
        _storedOverrides.Remove(_componentKey);

        if (_liveComponent != null)
        {
            _liveComponent.NazcaFunctionName = _templateFunctionName;
            _liveComponent.NazcaFunctionParameters = _templateFunctionParameters;
            _liveComponent.NazcaModuleName = _templateModuleName;
        }

        FunctionName = _templateFunctionName;
        FunctionParameters = _templateFunctionParameters;
        ModuleName = _templateModuleName ?? string.Empty;
        HasOverride = false;
        StatusText = "Reset to PDK template values.";
        _onChanged?.Invoke();
    }

    private void RefreshFromStore()
    {
        if (_storedOverrides.TryGetValue(_componentKey, out var stored))
        {
            FunctionName = stored.FunctionName ?? _templateFunctionName;
            FunctionParameters = stored.FunctionParameters ?? _templateFunctionParameters;
            ModuleName = stored.ModuleName ?? _templateModuleName ?? string.Empty;
            HasOverride = true;
        }
        else
        {
            FunctionName = _templateFunctionName;
            FunctionParameters = _templateFunctionParameters;
            ModuleName = _templateModuleName ?? string.Empty;
            HasOverride = false;
        }
    }

    private void ApplyToLiveComponent(NazcaCodeOverride overrideData)
    {
        if (_liveComponent == null)
            return;

        _liveComponent.NazcaFunctionName = overrideData.FunctionName ?? _templateFunctionName;
        _liveComponent.NazcaFunctionParameters = overrideData.FunctionParameters ?? _templateFunctionParameters;
        _liveComponent.NazcaModuleName = overrideData.ModuleName;
    }
}
