using System.Collections.ObjectModel;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;

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
    public GdsLayerReviewRow(GdsLayerGeometry geometry, GdsLayerRole role)
    {
        Layer = geometry.Pair.Layer; Datatype = geometry.Pair.Datatype;
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
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private bool _expandChildren;
    [ObservableProperty] private bool _includeInGroup;
    [ObservableProperty] private GdsModelCandidate? _selectedModel;
    [ObservableProperty] private bool _detectionLimited;
    public bool PortsEdited { get; set; }
    public GdsCellReviewRow(string name, bool canExpand, GdsLayerGeometryScene scene)
    {
        CellName = name; _displayName = name; CanExpand = canExpand; Scene = scene; Bounds = scene.Bounds;
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
