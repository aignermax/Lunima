using System.Globalization;
using System.Linq;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// S-matrix list display half of the dialog: rebuilds the stored-entry list and the
/// "currently effective S-matrix" list (with per-wavelength override tags) from the
/// override store and the live component's resolved matrices.
/// </summary>
public partial class ComponentSettingsDialogViewModel
{
    private void RefreshEntries(bool notifyChanged)
    {
        SMatrixEntries.Clear();

        if (_storedSMatrices == null || !_storedSMatrices.TryGetValue(_smatrixKey, out var data))
        {
            HasSMatrices = false;
            if (notifyChanged)
            {
                RefreshEffectiveEntries();
                _onChanged?.Invoke();
            }
            return;
        }

        foreach (var kvp in data.Wavelengths.OrderBy(k => k.Key))
            SMatrixEntries.Add(new SMatrixEntryViewModel(kvp.Key, kvp.Value, data.SourceNote));

        HasSMatrices = SMatrixEntries.Count > 0;
        if (notifyChanged)
        {
            RefreshEffectiveEntries();
            _onChanged?.Invoke();
        }
    }

    private void RefreshEffectiveEntries()
    {
        EffectiveEntries.Clear();
        if (_effectiveSMatrices == null || _effectivePins == null)
        {
            HasEffectiveEntries = false;
            return;
        }

        foreach (var kvp in _effectiveSMatrices.OrderBy(k => k.Key))
        {
            // A wavelength is "overridden" iff the active store has an entry
            // with the same wavelength key — a wavelength present in the
            // PDK default but not in the override is still PDK-driven.
            bool isOverridden =
                _storedSMatrices != null &&
                _storedSMatrices.TryGetValue(_smatrixKey, out var data) &&
                data.Wavelengths.ContainsKey(kvp.Key.ToString(CultureInfo.InvariantCulture));

            EffectiveEntries.Add(new EffectiveSMatrixEntryViewModel(
                kvp.Key, kvp.Value, _effectivePins, isOverridden));
        }

        HasEffectiveEntries = EffectiveEntries.Count > 0;
    }
}
