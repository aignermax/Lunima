using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Creates process editors with the configured PDK import formats and persistence policy.</summary>
internal sealed class ProcessEditorFactory(
    IBundledPdkCatalog bundledPdks, IReadOnlyList<IProcessImporter> importers, IPdkWriter writer, IUserPdkLibrary library)
{
    public ProcessManagementViewModel Create(IFileDialogService dialogs) =>
        new(bundledPdks, dialogs, importers, writer, library);
}
