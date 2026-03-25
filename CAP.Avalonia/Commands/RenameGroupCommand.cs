using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to rename a ComponentGroup both on the canvas and in the saved library.
/// Updates the group's name, description, and corresponding template.
/// </summary>
public class RenameGroupCommand : IUndoableCommand
{
    private readonly ComponentGroup _group;
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly string _newName;
    private readonly string? _newDescription;
    private readonly string _oldName;
    private readonly string _oldDescription;
    private GroupTemplate? _existingTemplate;
    private GroupTemplate? _newTemplate;

    public RenameGroupCommand(
        ComponentGroup group,
        ComponentLibraryViewModel libraryViewModel,
        string newName,
        string? newDescription = null)
    {
        _group = group;
        _libraryViewModel = libraryViewModel;
        _newName = newName;
        _newDescription = newDescription;
        _oldName = group.GroupName;
        _oldDescription = group.Description ?? "";
    }

    public string Description => $"Rename group to '{_newName}'";

    public void Execute()
    {
        if (string.IsNullOrWhiteSpace(_newName))
            return;

        // Find existing template for this group
        var libraryManager = _libraryViewModel.GetLibraryManager();
        _existingTemplate = _libraryViewModel.UserGroups
            .FirstOrDefault(t => t.Template.Name == _oldName)?.Template;

        // Update group properties
        _group.GroupName = _newName;
        if (_newDescription != null)
        {
            _group.Description = _newDescription;
        }

        // Remove old template if it exists
        if (_existingTemplate != null)
        {
            libraryManager.RemoveTemplate(_existingTemplate);
            var itemToRemove = _libraryViewModel.UserGroups
                .FirstOrDefault(vm => vm.Template == _existingTemplate);
            if (itemToRemove != null)
            {
                _libraryViewModel.UserGroups.Remove(itemToRemove);
            }
        }

        // Save new template with updated name
        _newTemplate = libraryManager.SaveTemplate(
            _group,
            _newName,
            _newDescription ?? _group.Description,
            "User");

        // Copy preview if it existed
        if (_existingTemplate?.PreviewThumbnailBase64 != null)
        {
            _newTemplate.PreviewThumbnailBase64 = _existingTemplate.PreviewThumbnailBase64;
        }

        // Add to ViewModel collection
        _libraryViewModel.AddTemplate(_newTemplate);
    }

    public void Undo()
    {
        // Restore original name and description
        _group.GroupName = _oldName;
        _group.Description = _oldDescription;

        // Remove new template
        if (_newTemplate != null)
        {
            _libraryViewModel.RemoveTemplateCommand.Execute(_newTemplate);
        }

        // Restore original template if it existed
        if (_existingTemplate != null)
        {
            var libraryManager = _libraryViewModel.GetLibraryManager();
            var restoredTemplate = libraryManager.SaveTemplate(
                _group,
                _oldName,
                _oldDescription,
                "User");

            if (_existingTemplate.PreviewThumbnailBase64 != null)
            {
                restoredTemplate.PreviewThumbnailBase64 = _existingTemplate.PreviewThumbnailBase64;
            }

            _libraryViewModel.AddTemplate(restoredTemplate);
        }
    }
}
