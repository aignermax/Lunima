using CAP_DataAccess.Import.Gds.LayerPicker;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
    [ObservableProperty] private bool _isolateSelectedLayer = true;
    [ObservableProperty] private int _previewDetailLevel;
    private readonly Dictionary<string, GdsLayerGeometryScene> _sceneCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sceneLock = new(1, 1);
    private int _sceneRequest;

    private async Task PrepareSelectedSceneAsync(GdsCellReviewRow cell)
    {
        var request = ++_sceneRequest;
        if (_sceneCache.ContainsKey(cell.CellName))
        {
            IsSceneLoading = false;
            OnPropertyChanged(nameof(Scene));
            return;
        }
        IsSceneLoading = true;
        try
        {
            var token = _lifetime.Token;
            await _sceneLock.WaitAsync(token);
            GdsLayerGeometryScene scene;
            try { scene = await Task.Run(() => _sceneBuilder.Build(cell.CellName, token), token); }
            finally { _sceneLock.Release(); }
            token.ThrowIfCancellationRequested();
            if (_sceneCache.Count >= 4) _sceneCache.Remove(_sceneCache.Keys.First());
            _sceneCache[cell.CellName] = scene;
            if (request == _sceneRequest) OnPropertyChanged(nameof(Scene));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Message = ex.Message; }
        finally { if (request == _sceneRequest) IsSceneLoading = false; }
    }
}
