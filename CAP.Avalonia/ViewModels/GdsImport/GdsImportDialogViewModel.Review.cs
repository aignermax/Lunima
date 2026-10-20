using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.GdsImport;

public partial class GdsImportDialogViewModel
{
    public bool EnableReview { get; set; }
    internal GdsLayerProfileStore? ReviewProfileStore { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReview))]
    private GdsInterpretationReviewViewModel? _review;
    public bool HasReview => Review != null;
    private byte[]? _reviewSourceBytes;
    private string _reviewSourceHash = "";
    private int _reviewGeneration;

    private async Task PrepareReviewAsync(CancellationToken token)
    {
        if (!EnableReview || _analyzedLibrary == null || SelectedTopCell == null) return;
        var generation = ++_reviewGeneration;
        if (Review != null)
        {
            Review.PropertyChanged -= OnReviewChanged;
            Review.Close();
            Review = null;
        }
        var selected = SelectedTopCell.CellName;
        var assignments = new List<GdsLayerAssignment>();
        void Add(string text, GdsLayerRole role)
        {
            foreach (var pair in ParseLayerPairs(text) ?? [])
            {
                var previous = assignments.FindIndex(x => x.Layer == pair.Layer && x.Datatype == pair.Datatype);
                if (previous >= 0) assignments[previous] = assignments[previous] with { Role = assignments[previous].Role | role };
                else assignments.Add(new(pair.Layer, pair.Datatype, role));
            }
        }
        Add(WaveguideLayersText, GdsLayerRole.OpticalCore);
        Add(MetalLayersText, GdsLayerRole.Electrical);
        Add(PortLayersText, GdsLayerRole.PortLabel | GdsLayerRole.PortMarker);
        var review = await GdsInterpretationReviewViewModel.CreateAsync(_analyzedLibrary, selected, _reviewSourceHash,
            assignments, _importService.GetModelCandidates(), profiles:ReviewProfileStore, cancellationToken: token);
        if (token.IsCancellationRequested || generation != _reviewGeneration || SelectedTopCell?.CellName != selected) return;
        if (Review != null) Review.PropertyChanged -= OnReviewChanged;
        Review = review;
        review.OfferSavedInterpretation(_importService.FindInterpretation(_reviewSourceHash, selected));
        review.PropertyChanged += OnReviewChanged;
        review.ApplyRequested = async () => await ImportCommand.ExecuteAsync(null);
        ImportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanImport));
    }
    private void OnReviewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GdsInterpretationReviewViewModel.CanApply)) return;
        ImportCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanImport));
    }
    private async Task RefreshSelectedReviewAsync()
    {
        if (!EnableReview || !AnalysisReady || IsBusy) return;
        IsBusy = true;
        try
        {
            var token = CurrentCts?.Token ?? CancellationToken.None;
            do
            {
                var selected = SelectedTopCell?.CellName;
                await PrepareReviewAsync(token);
                token.ThrowIfCancellationRequested();
                if (SelectedTopCell?.CellName == selected) break;
            } while (SelectedTopCell != null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorText = ex.Message; HasError = true; }
        finally { IsBusy = false; }
    }
}
