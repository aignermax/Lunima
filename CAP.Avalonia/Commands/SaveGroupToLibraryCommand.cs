using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Command to save a ComponentGroup to the library as a reusable template.
/// Generates preview thumbnail and adds to the component library ViewModel.
/// </summary>
public class SaveGroupToLibraryCommand : IUndoableCommand
{
    private readonly ComponentLibraryViewModel _libraryViewModel;
    private readonly GroupPreviewGenerator _previewGenerator;
    private readonly ComponentGroup _group;
    private readonly string _name;
    private readonly string? _description;

    /// <summary>
    /// The template that was created (null before execution).
    /// </summary>
    public CAP_Core.Components.Creation.GroupTemplate? CreatedTemplate { get; private set; }

    public SaveGroupToLibraryCommand(
        ComponentLibraryViewModel libraryViewModel,
        GroupPreviewGenerator previewGenerator,
        ComponentGroup group,
        string name,
        string? description = null)
    {
        _libraryViewModel = libraryViewModel;
        _previewGenerator = previewGenerator;
        _group = group;
        _name = name;
        _description = description;
    }

    public string Description => $"Save '{_name}' to library";

    public void Execute()
    {
        // Generate preview thumbnail
        var previewBase64 = _previewGenerator.GeneratePreview(_group);

        // Save to library
        var libraryManager = _libraryViewModel.GetLibraryManager();
        CreatedTemplate = libraryManager.SaveTemplate(_group, _name, _description, "User");

        // Set preview if generated
        if (previewBase64 != null)
        {
            CreatedTemplate.PreviewThumbnailBase64 = previewBase64;
        }

        // Add to ViewModel collection
        _libraryViewModel.AddTemplate(CreatedTemplate);
    }

    public void Undo()
    {
        if (CreatedTemplate == null)
            return;

        // Remove from library
        _libraryViewModel.RemoveTemplateCommand.Execute(CreatedTemplate);
        CreatedTemplate = null;
    }
}
