using CAP.Avalonia.Services.GdsImport;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.GdsImport.Interpretation;

public sealed partial class GdsInterpretationReviewViewModel
{
    [RelayCommand]
    private void SaveProfile()
    {
        if (!LayersConfirmed) { Message = Text("ConfirmLayersFirst"); return; }
        if (string.IsNullOrWhiteSpace(ProfileName)) { Message = Text("NameRequired"); return; }
        try
        {
            var profile = new GdsLayerProfile(ProfileName.Trim(), BuildInterpretation().Layers);
            _profiles.Save(profile);
            var previous = Profiles.FirstOrDefault(x => string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (previous != null) Profiles.Remove(previous);
            Profiles.Add(profile); SelectedProfile = profile; Message = Text("ProfileSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Message = ex.Message; }
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfile is not { } profile) return;
        RecordEdit(); _restoring = true;
        try
        {
            foreach (var row in Layers)
                row.Role = profile.Layers.FirstOrDefault(x => x.Layer == row.Layer && x.Datatype == row.Datatype)?.Role
                         ?? CAP_DataAccess.Import.Gds.GdsLayerRole.Unknown;
        }
        finally { _restoring = false; }
        Array.Fill(_confirmed, false, 1, 5); StageIndex = 1; _lastSnapshot = CaptureUndoState();
        RefreshPresentation(); UndoCommand.NotifyCanExecuteChanged(); Message = Text("ProfileReview");
    }
}
