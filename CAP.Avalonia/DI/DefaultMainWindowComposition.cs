using CAP.Avalonia.Views.Shell;
using CAP_DataAccess.Import;

namespace CAP.Avalonia.DI;

/// <summary>Basic settings composition retained by the public parameterless MainWindow constructor.</summary>
internal static class DefaultMainWindowComposition
{
    internal static ComponentEditorHostFactory CreateEditorHosts()
    {
        return new ComponentEditorHostFactory((owner, errors) =>
        {
            var imports = new SParameterImportCatalog(
                [new LumericalSParameterImporter(), new TouchstoneImporter()], errors);
            return new ComponentSettingsDialogFactory(
                imports, owner, errors, null, null, null, null, null, null, null, null);
        });
    }
}
