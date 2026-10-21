using CAP_DataAccess.Import.Gds.Interpretation;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
    private GdsImportInterpretation? _savedInterpretation;
    public bool HasSavedInterpretation => _savedInterpretation != null;

    public void OfferSavedInterpretation(GdsImportInterpretation? saved)
    {
        _savedInterpretation = saved?.TopCellName == _topCell &&
            string.Equals(saved.SourceSha256, _sourceHash, StringComparison.OrdinalIgnoreCase) ? saved : null;
        OnPropertyChanged(nameof(HasSavedInterpretation));
    }

    [RelayCommand]
    private void ReuseSaved()
    {
        if (_savedInterpretation is not { } saved) return;
        RecordEdit();
        RestoreSnapshot(saved);
        StageIndex = 0;
        Message = Text("SavedReview");
    }
}
