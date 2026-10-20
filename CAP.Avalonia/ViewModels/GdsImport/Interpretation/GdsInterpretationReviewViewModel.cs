using System.Collections.ObjectModel;
using System.ComponentModel;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.Localization;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

/// <summary>Editable interpretation of a parsed snapshot. No canvas or library mutation occurs here.</summary>
public sealed partial class GdsInterpretationReviewViewModel : ObservableObject
{
    private readonly GdsLibrary _library;
    private readonly string _topCell;
    private readonly string _sourceHash;
    private readonly GdsLayerGeometryScene _topScene;
    private readonly GdsReviewSceneBuilder _sceneBuilder;
    private readonly GdsLayerProfileStore _profiles;
    private sealed record UndoState(GdsImportInterpretation Interpretation, HashSet<string> EditedCells);
    private readonly Stack<UndoState> _undo = new();
    private UndoState? _lastSnapshot;
    private bool _restoring;
    private readonly bool[] _confirmed = new bool[6];
    private readonly List<GdsInterpretationGroup> _groups = new();
    private readonly CancellationTokenSource _lifetime = new();
    public ObservableCollection<GdsLayerReviewRow> Layers { get; } = new();
    public ObservableCollection<GdsCellReviewRow> Cells { get; } = new();
    public ObservableCollection<GdsReviewStep> Steps { get; } = new();
    public ObservableCollection<GdsLayerProfile> Profiles { get; } = new();
    public IReadOnlyList<GdsModelCandidate> Models { get; }
    [ObservableProperty] private int _stageIndex;
    [ObservableProperty] private GdsLayerReviewRow? _selectedLayer;
    [ObservableProperty] private GdsCellReviewRow? _selectedCell;
    [ObservableProperty] private GdsPortReviewRow? _selectedPort;
    [ObservableProperty] private bool _guideEnabled = true;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private GdsLayerProfile? _selectedProfile;
    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private bool _isPreparing;
    [ObservableProperty] private bool _isSceneLoading;
    [ObservableProperty] private bool _isPortEditing;
    public bool IsWorking => IsPreparing || IsSceneLoading;
    public Action? ApplyRequested { get; set; }
    public bool LayersConfirmed => _confirmed[1];
    public int SuggestedRouteLayerCount => Layers.Count(x => x.HasRouteSuggestion);
    public bool HasRouteSuggestions => SuggestedRouteLayerCount > 0;
    [RelayCommand]
    private void NextSuggestedRoute()
    {
        var candidates = Layers.Where(x => x.HasRouteSuggestion).ToArray();
        if (candidates.Length == 0) return;
        StageIndex = 1;
        SelectedLayer = candidates[(Array.IndexOf(candidates, SelectedLayer) + 1) % candidates.Length];
    }
    public bool CanApply => _confirmed.Take(5).All(x => x) && Validate() == null && !IsWorking;
    public bool IsLayoutStep => StageIndex == 0;
    public bool IsLayerStep => StageIndex == 1;
    public bool IsCellStep => StageIndex == 2;
    public bool IsPortStep => StageIndex == 3;
    public bool IsModelStep => StageIndex == 4;
    public bool IsSummaryStep => StageIndex == 5;
    public bool ShowCellSelector => StageIndex is 2 or 3 or 4;
    public bool HasSelectedPort => SelectedPort != null;
    public bool HasSelectedModel => SelectedCell?.SelectedModel != null;
    public IReadOnlyList<GdsModelCandidate> CompatibleModels => SelectedCell is not { } cell || cell.Ports.Count == 0
        ? [] : Models.Where(model => model.PinNames.Count == cell.Ports.Count &&
            model.PinNames.Select(name => model.PinDomains.GetValueOrDefault(name, "optical"))
                .Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(cell.Ports.Select(port => port.Kind == "electrical" ? "electrical" : "optical").Order(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase)).ToArray();
    public bool HasCompatibleModels => CompatibleModels.Count > 0;
    public string? HighlightCellName => IsCellStep ? SelectedCell?.CellName : null;
    public string StageTitle => Text("Step" + StageIndex);
    public string StageDescription => Text("Description" + StageIndex);
    public string NextText => Text(StageIndex == 5 ? "Apply" : "ConfirmNext");
    public string Summary => string.Format(Text("Summary"), Layers.Count, ActiveCells.Count, _groups.Count);
    public string ConnectionReadiness => string.Format(Text("ConnectionReadiness"),
        ActiveCells.Count(x => x.Ports.Count == 0), ActiveCells.Count(x => x.SelectedModel != null), ActiveCells.Count);
    public string RouteReadiness => Text(Layers.Any(x => (x.Role & (GdsLayerRole.OpticalCore | GdsLayerRole.Electrical | GdsLayerRole.Heater)) != 0)
        ? "RouteLayersChosen" : "NoRouteLayers");
    public string GroupSummary => string.Join(", ", _groups.Select(x => x.Name));
    public string GuideToggleText => Text(GuideEnabled ? "PauseGuide" : "ShowMe");
    private bool _hasPickedLayer;
    public object? GuideTargetItem => IsLayerStep ? (_hasPickedLayer ? (object?)SelectedLayer?.SelectedRole : SelectedLayer) : null;
    public void MarkLayerInteraction()
    {
        _hasPickedLayer = true;
        OnPropertyChanged(nameof(GuideTargetName));
        OnPropertyChanged(nameof(GuideTargetItem));
    }
    [RelayCommand]
    private void ToggleGuide()
    {
        if (GuideEnabled) PauseGuide();
        else RestartGuide();
    }
    [RelayCommand]
    private void ConfirmPortKind()
    {
        if (SelectedPort is not { NeedsKindReview: true } port) return;
        RecordEdit();
        port.NeedsKindReview = false;
        FinishEdit();
    }
    partial void OnGuideEnabledChanged(bool value) => OnPropertyChanged(nameof(GuideToggleText));
    public string GuideTargetName => StageIndex switch
    {
        0 => "GdsReviewNext",
        1 => _hasPickedLayer ? "GdsReviewRole" : "GdsReviewLayerList",
        2 => SelectedCell?.CanExpand == true ? "GdsReviewOpenAssembly" : "GdsReviewCellSelector",
        3 => SelectedPort != null ? "GdsReviewPortKind" : "GdsReviewAddPort",
        4 => HasCompatibleModels ? "GdsReviewModel" : "GdsReviewGeometryOnly",
        _ => "GdsReviewNext"
    };
    public GdsLayerGeometryScene Scene => StageIndex is 3 or 4 && SelectedCell != null
        ? _sceneCache.GetValueOrDefault(SelectedCell.CellName) ?? SelectedCell.Scene : _topScene;
    public GdsLayerPair? HighlightLayer => IsLayerStep ? SelectedLayer?.Pair : null;
    public IReadOnlyList<DetectedPin> PreviewPorts => IsPortStep && SelectedCell != null ? SelectedCell.Ports.Select(x => x.ToPin()).ToArray() : [];
    public int SelectedPortIndex => SelectedCell?.Ports.IndexOf(SelectedPort!) ?? -1;
    private static string Text(string key) => LocalizationService.Instance.Translate("GdsReview." + key);

    private GdsInterpretationReviewViewModel(GdsLibrary library, string topCell, string hash,
        GdsLayerGeometryScene scene, IReadOnlyList<GdsCellReviewRow> cells,
        IReadOnlyList<GdsLayerAssignment> suggestions, IReadOnlyList<GdsModelCandidate> models, GdsLayerProfileStore profiles,
        GdsReviewSceneBuilder sceneBuilder, GdsLayerGeometryScene firstScene,
        IReadOnlyList<GdsReviewRouteSuggestion> routeSuggestions)
    {
        _library = library; _topCell = topCell; _sourceHash = hash; _topScene = scene; _profiles = profiles; Models = models;
        _sceneBuilder = sceneBuilder; _sceneCache[firstScene.CellName] = firstScene;
        _guideEnabled = !profiles.GuideDismissed;
        foreach (var geometry in scene.Layers)
        {
            var row = new GdsLayerReviewRow(geometry, suggestions.FirstOrDefault(x => x.Layer == geometry.Pair.Layer && x.Datatype == geometry.Pair.Datatype)?.Role ?? GdsLayerRole.Unknown,
                routeSuggestions.FirstOrDefault(x => x.Layer == geometry.Pair.Layer && x.Datatype == geometry.Pair.Datatype));
            Layers.Add(row); row.PropertyChanged += OnRowChanged;
        }
        foreach (var cell in cells) { Cells.Add(cell); cell.PropertyChanged += OnRowChanged; }
        for (var i = 0; i < 6; i++) Steps.Add(new(i, Text("Step" + i)));
        SelectedLayer = Layers.FirstOrDefault(); SelectedCell = Cells.FirstOrDefault();
        _groupName = Text("DefaultGroup");
        try { foreach (var profile in profiles.Read()) Profiles.Add(profile); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { Message = ex.Message; }
        RefreshSteps(); _lastSnapshot = CaptureUndoState();
    }

    public static async Task<GdsInterpretationReviewViewModel> CreateAsync(GdsLibrary library, string topCell, string sourceHash,
        IReadOnlyList<GdsLayerAssignment> suggestions, IReadOnlyList<GdsModelCandidate> models,
        GdsLayerProfileStore? profiles = null, CancellationToken cancellationToken = default)
    {
        var prepared = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var builder = new GdsReviewSceneBuilder(library);
            var scene = builder.Build(topCell, cancellationToken);
            var reachable = new HashSet<string>();
            void Visit(string name)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reachable.Add(name)) return;
                foreach (var reference in library.Cells[name].Elements.OfType<GdsReference>()) Visit(reference.CellName);
            }
            Visit(topCell);
            var names = reachable.Where(x => x != topCell).ToArray();
            if (names.Length == 0) names = [topCell];
            var cells = new List<GdsCellReviewRow>();
            foreach (var name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cells.Add(new(name, library.Cells[name].Elements.OfType<GdsReference>().Any(),
                    new GdsLayerGeometryScene { CellName = name, Bounds = builder.GetBounds(name, cancellationToken) }));
            }
            var firstScene = names[0] == topCell ? scene : builder.Build(names[0], cancellationToken);
            var routeSuggestions = GdsReviewRouteEvidence.Suggest(library, topCell, cancellationToken);
            return (scene, cells, builder, firstScene, routeSuggestions);
        }, cancellationToken);
        return new(library, topCell, sourceHash, prepared.scene, prepared.cells, suggestions, models, profiles ?? new(), prepared.builder, prepared.firstScene, prepared.routeSuggestions);
    }

    partial void OnStageIndexChanged(int value) { IsPortEditing = false; RefreshCellSelection(); RefreshSteps(); RefreshPresentation(); }
    partial void OnSelectedLayerChanged(GdsLayerReviewRow? value)
    {
        OnPropertyChanged(nameof(HighlightLayer));
        OnPropertyChanged(nameof(GuideTargetItem));
    }
    partial void OnSelectedCellChanged(GdsCellReviewRow? value)
    {
        IsPortEditing = false;
        SelectedPort = value?.Ports.FirstOrDefault();
        RefreshPresentation();
        if (value != null) _ = PrepareSelectedSceneAsync(value);
    }
    partial void OnSelectedPortChanged(GdsPortReviewRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedPort));
        OnPropertyChanged(nameof(SelectedPortIndex));
        OnPropertyChanged(nameof(GuideTargetName));
    }
    partial void OnIsPreparingChanged(bool value) { OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(IsWorking)); }
    partial void OnIsSceneLoadingChanged(bool value) { OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(IsWorking)); }
    public void Close()
    {
        GuideEnabled = false;
        ApplyRequested = null;
        _lifetime.Cancel();
    }
    private void RefreshSteps() { foreach (var step in Steps) step.IsCurrent = step.Index == StageIndex; }
    private void RefreshPresentation()
    {
        RefreshHierarchyPresentation();
        foreach (var property in new[]{nameof(IsLayoutStep),nameof(IsLayerStep),nameof(IsCellStep),nameof(IsPortStep),
            nameof(IsModelStep),nameof(IsSummaryStep),nameof(ShowCellSelector),nameof(StageTitle),nameof(StageDescription),
            nameof(NextText),nameof(Summary),nameof(Scene),nameof(HighlightLayer),nameof(PreviewPorts),nameof(GuideTargetName),
            nameof(SelectedPortIndex),nameof(LayersConfirmed),nameof(CanApply),nameof(GroupSummary),nameof(HasSelectedModel),
            nameof(HighlightCellName),nameof(ConnectionReadiness),nameof(RouteReadiness),
            nameof(HasCombinedRouteLayers),nameof(CombinedRouteLayerWarning),
            nameof(CompatibleModels),nameof(HasCompatibleModels),nameof(GuideTargetItem),
            nameof(CanOpenAssembly),nameof(CellHierarchyHint)})
            OnPropertyChanged(property);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring || e.PropertyName is nameof(GdsLayerReviewRow.SelectedRole) or nameof(GdsPortReviewRow.NeedsKindReview)) return;
        if (e.PropertyName == nameof(GdsCellReviewRow.IncludeInGroup))
        {
            if (sender is GdsCellReviewRow { IncludeInGroup: true } cell) SelectedCell = cell;
            return;
        }
        if (sender is GdsPortReviewRow port)
        {
            var owner = Cells.FirstOrDefault(x => x.Ports.Contains(port));
            if (owner != null) owner.PortsEdited = true;
        }
        if (e.PropertyName == nameof(GdsCellReviewRow.ExpandChildren))
        {
            RefreshCellSelection();
            var activeNames = ActiveCells.Select(x => x.CellName).ToHashSet(StringComparer.Ordinal);
            _groups.RemoveAll(x => x.CellNames.Any(name => !activeNames.Contains(name)));
        }
        if (_lastSnapshot != null) PushUndo(_lastSnapshot);
        Array.Fill(_confirmed, false, sender is GdsLayerReviewRow ? 1 : 2, sender is GdsLayerReviewRow ? 5 : 4);
        _lastSnapshot = CaptureUndoState(); RefreshPresentation(); UndoCommand.NotifyCanExecuteChanged();
    }
}
