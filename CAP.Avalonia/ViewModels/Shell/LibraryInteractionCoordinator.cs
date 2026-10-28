using System.ComponentModel;
using CAP.Avalonia.Services.GroupTemplates;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Owns mutually exclusive library placement choices and saved-group loading.</summary>
internal sealed class LibraryInteractionCoordinator : IDisposable
{
    private readonly LeftPanelViewModel _library;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly Action<string> _status;
    private readonly Action<string, Exception> _error;

    public LibraryInteractionCoordinator(LeftPanelViewModel library, CanvasInteractionViewModel interaction,
        Action<string> status, Action<string, Exception> error)
    {
        (_library, _interaction, _status, _error) = (library, interaction, status, error);
        interaction.ClearLeftPanelGroupSelection = ClearGroup;
        interaction.ClearComponentTemplateSelection = ClearComponent;
        interaction.PropertyChanged += InteractionChanged;
        library.OnGroupTemplateSelected = SelectGroup;
    }

    private void ClearGroup() => _library.SelectedGroupTemplate = null;
    private void ClearComponent() => _interaction.SelectedTemplate = null;

    private void InteractionChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(CanvasInteractionViewModel.CurrentMode)
            && _interaction.CurrentMode is not (InteractionMode.PlaceComponent or InteractionMode.PlaceGroupTemplate))
            ClearGroup();
        else if (change.PropertyName == nameof(CanvasInteractionViewModel.SelectedTemplate)
            && _interaction.SelectedTemplate != null) ClearGroup();
    }

    private void SelectGroup(GroupTemplate template)
    {
        try
        {
            if (!SavedGroupTemplateLoader.LoadIfNeeded(template))
            {
                _status(string.Format(LocalizationService.Instance.Translate("Status.TemplateCorrupted"), template.Name));
                return;
            }
        }
        catch (Exception exception)
        {
            _status(string.Format(LocalizationService.Instance.Translate("Status.TemplateLoadFailed"), template.Name, exception.Message));
            _error($"Failed to load template '{template.Name}': {exception.Message}", exception);
            return;
        }
        _interaction.SelectedGroupTemplate = template;
    }

    public void Dispose()
    {
        _interaction.PropertyChanged -= InteractionChanged;
        if (_interaction.ClearLeftPanelGroupSelection == ClearGroup) _interaction.ClearLeftPanelGroupSelection = null;
        if (_interaction.ClearComponentTemplateSelection == ClearComponent) _interaction.ClearComponentTemplateSelection = null;
        if (_library.OnGroupTemplateSelected == SelectGroup) _library.OnGroupTemplateSelected = null;
    }
}
