using System.Collections.ObjectModel;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.PinKinds;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed record GdsRoleChoice(GdsLayerRole Role, string Label)
{
    public string Description => LocalizationService.Instance.Translate("GdsReview.RoleHelp." +
        (Role == (GdsLayerRole.PortMarker | GdsLayerRole.PortLabel) ? "MarkerLabel" : Role.ToString()));
    public override string ToString() => Label;
}

public sealed partial class GdsLayerReviewRow : ObservableObject
{
    public int Layer { get; }
    public int Datatype { get; }
    public GdsLayerPair Pair => new(Layer, Datatype);
    public string Label => $"{Layer} / {Datatype}";
    public string Counts { get; }
    public IReadOnlyList<GdsRoleChoice> Choices { get; }
    public GdsReviewRouteSuggestion? RouteSuggestion { get; }
    public bool HasRouteSuggestion => RouteSuggestion != null;
    public string RouteSuggestionText => RouteSuggestion is { } suggestion
        ? string.Format(LocalizationService.Instance.Translate("GdsReview.RouteSuggestion"), suggestion.ShapeCount, suggestion.CellCount)
        : "";
    [RelayCommand]
    private void ApplyOpticalSuggestion() { if (HasRouteSuggestion) Role = GdsLayerRole.OpticalCore; }
    [RelayCommand]
    private void ApplyElectricalSuggestion() { if (HasRouteSuggestion) Role = GdsLayerRole.Electrical; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinEvidence))]
    [NotifyPropertyChangedFor(nameof(PinEvidenceText))]
    private int _suggestedPinCount;
    public bool HasPinEvidence => SuggestedPinCount > 0;
    public string PinEvidenceText => string.Format(
        LocalizationService.Instance.Translate("GdsReview.PinEvidence"), SuggestedPinCount);
    [ObservableProperty] private GdsLayerRole _role;
    public GdsRoleChoice SelectedRole
    {
        get => Choices.FirstOrDefault(x => x.Role == Role) ?? Choices[0];
        set { if (value != null) Role = value.Role; }
    }
    public GdsLayerReviewRow(GdsLayerGeometry geometry, GdsLayerRole role, GdsReviewRouteSuggestion? routeSuggestion = null)
    {
        Layer = geometry.Pair.Layer; Datatype = geometry.Pair.Datatype;
        RouteSuggestion = (role & (GdsLayerRole.PortMarker | GdsLayerRole.PortLabel)) == 0 ? routeSuggestion : null;
        Counts = $"{geometry.Polygons.Count} ◇  ·  {geometry.Texts.Count} T";
        Choices = new[]
        {
            (GdsLayerRole.Unknown,"Unknown"), (GdsLayerRole.OpticalCore,"Core"),
            (GdsLayerRole.Slab,"Slab"), (GdsLayerRole.Etch,"Etch"),
            (GdsLayerRole.Electrical,"Electrical"), (GdsLayerRole.Heater,"Heater"),
            (GdsLayerRole.PortMarker,"Marker"), (GdsLayerRole.PortLabel,"Label"),
            (GdsLayerRole.PortMarker | GdsLayerRole.PortLabel,"MarkerLabel"),
            (GdsLayerRole.Auxiliary,"Auxiliary")
        }.Select(x => new GdsRoleChoice(x.Item1, LocalizationService.Instance.Translate("GdsReview.Role." + x.Item2))).ToArray();
        _role = role;
    }
    partial void OnRoleChanged(GdsLayerRole value) => OnPropertyChanged(nameof(SelectedRole));
}

public sealed partial class GdsPortReviewRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _xUm;
    [ObservableProperty] private double _yUm;
    [ObservableProperty] private double _angleDegrees;
    [ObservableProperty] private double _widthUm;
    [ObservableProperty] private string _kind = "optical";
    [ObservableProperty] private bool _needsKindReview;
    [ObservableProperty] private string _modelPortName = "";
    public static IReadOnlyList<string> Kinds { get; } = ["optical", "electrical", "vertical_te", "vertical_tm"];
    public int? Layer { get; }
    public GdsPortReviewRow(DetectedPin pin)
    {
        _name = pin.Name; _xUm = pin.XUm; _yUm = pin.YUm; _angleDegrees = pin.AngleDegrees;
        _widthUm = pin.WidthUm; Layer = pin.Layer;
        _kind = pin.IsElectrical == true ? "electrical" : pin.PortType ?? "optical";
        _needsKindReview = pin.IsElectrical == null && pin.PortType == null;
    }
    partial void OnKindChanged(string value) => NeedsKindReview = false;
    public DetectedPin ToPin() => new()
    {
        Name = Name.Trim(),
        XUm = XUm,
        YUm = YUm,
        AngleDegrees = AngleDegrees,
        WidthUm = WidthUm,
        Layer = Layer,
        IsElectrical = NeedsKindReview ? null : Kind == "electrical",
        PortType = NeedsKindReview ? null : Kind,
        Source = DetectedPinSource.Label
    };
}

public sealed partial class GdsCellReviewRow : ObservableObject
{
    public string CellName { get; }
    public bool CanExpand { get; }
    public GdsBoundingBox Bounds { get; }
    public GdsLayerGeometryScene Scene { get; }
    public ObservableCollection<GdsPortReviewRow> Ports { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassThroughSummaryText))]
    [NotifyPropertyChangedFor(nameof(GeometryOnlySummaryText))]
    private string _displayName;
    [ObservableProperty] private bool _expandChildren;
    [ObservableProperty] private bool _includeInGroup;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPassThroughAssumed))]
    [NotifyPropertyChangedFor(nameof(PassThroughSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsShapeOnly))]
    [NotifyPropertyChangedFor(nameof(GeometryOnlySummaryText))]
    private GdsModelCandidate? _selectedModel;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPassThroughAssumed))]
    [NotifyPropertyChangedFor(nameof(PassThroughSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsShapeOnly))]
    [NotifyPropertyChangedFor(nameof(GeometryOnlySummaryText))]
    private bool _geometryOnly;
    [ObservableProperty] private bool _detectionLimited;
    public bool PortsEdited { get; set; }
    public GdsCellReviewRow(string name, bool canExpand, GdsLayerGeometryScene scene)
    {
        CellName = name; _displayName = name; CanExpand = canExpand; Scene = scene; Bounds = scene.Bounds;
    }
    /// <summary>Choosing a model retracts the explicit shape-only decision — the model wins.</summary>
    partial void OnSelectedModelChanged(GdsModelCandidate? value)
    {
        if (value != null) GeometryOnly = false;
    }
    /// <summary>
    /// True when this cell keeps no mapped model and its ports qualify for the
    /// lossless 2-pin pass-through default — the assumed model the review must
    /// surface instead of applying it silently. Ports feed the import's own rule
    /// (<see cref="GdsImportService.IsPassThroughEligible(IEnumerable{string?})"/>)
    /// through the same review→draft conversion (<see cref="GdsPortReviewRow.ToPin"/>)
    /// that <c>BuildInterpretation</c> persists, so hint and import can never drift.
    /// An explicit shape-only decision (<see cref="GeometryOnly"/>) opts out.
    /// </summary>
    public bool IsPassThroughAssumed => !GeometryOnly && SelectedModel == null &&
        GdsImportService.IsPassThroughEligible(Ports.Select(p =>
            p.ToPin().IsElectrical == true ? PinKindHelper.ElectricalKindName : (string?)null));
    /// <summary>True when the reviewer explicitly declined every model, pass-through included.</summary>
    public bool IsShapeOnly => GeometryOnly && SelectedModel == null;
    /// <summary>Summary-stage line naming this cell's assumed pass-through model; empty when mapped or ineligible.</summary>
    public string PassThroughSummaryText => IsPassThroughAssumed
        ? string.Format(LocalizationService.Instance.Translate("GdsReview.PassThroughSummaryRow"), DisplayName)
        : "";
    /// <summary>Summary-stage line naming this cell's explicit shape-only decision; empty otherwise.</summary>
    public string GeometryOnlySummaryText => IsShapeOnly
        ? $"{DisplayName}: {LocalizationService.Instance.Translate("GdsReview.GeometryOnly")}"
        : "";
    /// <summary>Re-raises the model-decision hints after port edits; collection and kind changes don't notify the row.</summary>
    public void NotifyPassThroughChanged()
    {
        OnPropertyChanged(nameof(IsPassThroughAssumed));
        OnPropertyChanged(nameof(PassThroughSummaryText));
        OnPropertyChanged(nameof(IsShapeOnly));
        OnPropertyChanged(nameof(GeometryOnlySummaryText));
    }
    public override string ToString() => CellName;
}

public sealed partial class GdsReviewStep : ObservableObject
{
    public int Index { get; }
    public string Title { get; }
    public string Number => (Index + 1).ToString();
    [ObservableProperty] private bool _isCurrent;
    public GdsReviewStep(int index, string title) { Index = index; Title = title; }
}
