using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// ViewModel wrapper for a GroupTemplate to support UI interactions like hover state and delete button.
/// </summary>
public partial class GroupTemplateItemViewModel : ObservableObject
{
    private readonly ComponentLibraryViewModel _parentViewModel;

    /// <summary>
    /// The underlying GroupTemplate being displayed.
    /// </summary>
    public GroupTemplate Template { get; }

    /// <summary>
    /// Whether the mouse is currently hovering over this item (to show delete button).
    /// </summary>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>
    /// Initializes a new instance of the GroupTemplateItemViewModel.
    /// </summary>
    /// <param name="template">The underlying GroupTemplate.</param>
    /// <param name="parentViewModel">The parent ComponentLibraryViewModel for delete command.</param>
    public GroupTemplateItemViewModel(GroupTemplate template, ComponentLibraryViewModel parentViewModel)
    {
        Template = template;
        _parentViewModel = parentViewModel;
    }

    /// <summary>
    /// Deletes this group template from the library.
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        _parentViewModel.RemoveTemplateCommand.Execute(Template);
    }

    /// <summary>
    /// Renames this group template via a dialog prompt.
    /// </summary>
    [RelayCommand]
    private void Rename()
    {
        _parentViewModel.RenameTemplateCommand.Execute(Template);
    }
}
