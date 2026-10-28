using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Owns group library dialogs and their undoable rename/save actions.</summary>
internal sealed class GroupLibraryActions
{
    private readonly CommandManager _commandManager;
    private readonly ComponentLibraryViewModel? _libraryViewModel;
    private readonly GroupPreviewGenerator? _previewGenerator;
    private readonly IInputDialogService? _inputDialogService;
    private readonly Action<string> _status;

    public GroupLibraryActions(CommandManager commands, ComponentLibraryViewModel? library,
        GroupPreviewGenerator? previews, IInputDialogService? dialogs, Action<string> status)
    {
        _commandManager = commands;
        _libraryViewModel = library;
        _previewGenerator = previews;
        _inputDialogService = dialogs;
        _status = status;
    }

    public bool IsAvailable => _libraryViewModel != null && _inputDialogService != null;

    private bool RefuseOnDie(ComponentGroup? group)
    {
        if (group is not { IsDie: true }) return false;
        _status(LocalizationService.Instance.Translate("Dies.Status.NotAGroup"));
        return true;
    }

    public async Task RenameAsync(ComponentGroup? selectedGroup)
    {
        if (_inputDialogService == null || _libraryViewModel == null)
        {
            _status("Rename not available (dialog service not configured)");
            return;
        }

        if (selectedGroup == null || RefuseOnDie(selectedGroup))
            return;

        var currentName = selectedGroup.GroupName;
        var currentDescription = selectedGroup.Description ?? "";

        var result = await _inputDialogService.ShowMultiInputDialogAsync(
            "Rename Group",
            ("Name", currentName),
            ("Description (optional)", currentDescription));

        if (result == null)
            return;

        var newName = result["Name"].Trim();
        var newDescription = result["Description (optional)"].Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            _status("Group name cannot be empty");
            return;
        }

        var cmd = new RenameGroupCommand(
            selectedGroup,
            _libraryViewModel,
            newName,
            string.IsNullOrWhiteSpace(newDescription) ? null : newDescription);

        _commandManager.ExecuteCommand(cmd);
        _status($"Renamed group to '{newName}' and updated library");
    }

    public async Task SaveAsAsync(ComponentGroup? selectedGroup)
    {
        if (_inputDialogService == null || _libraryViewModel == null)
        {
            _status("Save not available (dialog service not configured)");
            return;
        }

        if (selectedGroup == null)
            return;

        var currentName = selectedGroup.GroupName;
        var currentDescription = selectedGroup.Description ?? "";

        var result = await _inputDialogService.ShowMultiInputDialogAsync(
            "Save Group as Prefab",
            ("Name", currentName),
            ("Description (optional)", currentDescription));

        if (result == null)
            return;

        var newName = result["Name"].Trim();
        var newDescription = result["Description (optional)"].Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            _status("Group name cannot be empty");
            return;
        }

        var cmd = new SaveGroupAsPrefabCommand(
            _libraryViewModel,
            _previewGenerator ?? new GroupPreviewGenerator(),
            selectedGroup,
            newName,
            string.IsNullOrWhiteSpace(newDescription) ? null : newDescription);

        _commandManager.ExecuteCommand(cmd);
        _status($"Saved group '{newName}' as prefab to library");
    }

}
