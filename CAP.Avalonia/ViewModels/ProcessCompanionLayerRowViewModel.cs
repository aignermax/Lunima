using System.Globalization;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// One editable row of the process editor's "Waveguide companion layers" table: a cladding,
/// trench or keep-out band drawn along every routed waveguide. The offset / total-width
/// fields are text so an empty cell means "not declared" (exactly one of both is used);
/// numbers are parsed culture-invariantly (a "," is accepted as decimal separator) and an
/// unparsable entry keeps the last valid value.
/// </summary>
public partial class ProcessCompanionLayerRowViewModel : ObservableObject
{
    /// <summary>Creates a row editing <paramref name="model"/> in place.</summary>
    public ProcessCompanionLayerRowViewModel(ProcessCompanionLayer model)
    {
        Model = model;
        _offsetText = Format(model.OffsetUm);
        _widthText = Format(model.WidthUm);
    }

    /// <summary>The DTO this row edits (written to the PDK JSON on save).</summary>
    public ProcessCompanionLayer Model { get; }

    /// <summary>Band name.</summary>
    public string Name
    {
        get => Model.Name;
        set => SetProperty(Model.Name, value, Model, (m, v) => m.Name = v);
    }

    /// <summary>GDS layer number.</summary>
    public int Layer
    {
        get => Model.Layer;
        set => SetProperty(Model.Layer, value, Model, (m, v) => m.Layer = v);
    }

    /// <summary>GDS datatype.</summary>
    public int Datatype
    {
        get => Model.Datatype;
        set => SetProperty(Model.Datatype, value, Model, (m, v) => m.Datatype = v);
    }

    /// <summary>Foundry meaning of the band (only KeepOut affects DRC).</summary>
    public WaveguideCompanionRole Role
    {
        get => Model.Role;
        set => SetProperty(Model.Role, value, Model, (m, v) => m.Role = v);
    }

    /// <summary>Description / provenance.</summary>
    public string? Description
    {
        get => Model.Description;
        set => SetProperty(Model.Description, value, Model, (m, v) => m.Description = string.IsNullOrWhiteSpace(v) ? null : v);
    }

    [ObservableProperty]
    private string _offsetText;

    [ObservableProperty]
    private string _widthText;

    partial void OnOffsetTextChanged(string value)
    {
        if (TryParse(value, out var parsed))
            Model.OffsetUm = parsed;
    }

    partial void OnWidthTextChanged(string value)
    {
        if (TryParse(value, out var parsed))
            Model.WidthUm = parsed;
    }

    private static bool TryParse(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed) || parsed < 0)
            return false;
        value = parsed;
        return true;
    }

    private static string Format(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
}
