using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Creates the related PDK, instance-settings and component-editor window hosts.</summary>
internal sealed class ComponentEditorHostFactory(
    Func<Window, ErrorConsoleService, ComponentSettingsDialogFactory>? createSettings = null,
    IUserPdkLibrary? pdkStore = null, ITemplateSMatrixStore? overrides = null,
    ProcessEditorFactory? processEditors = null)
{
    public (PdkWindowHost Pdks, ComponentSettingsWindowHost Settings, ComponentEditorWindowHost Editors) Create(
        Window owner, LeftPanelViewModel library, DesignCanvasViewModel canvas, FileOperationsViewModel files,
        Func<Component, string?> resolvePdkSource, ErrorConsoleService errors, Action openSolverSettings)
    {
        var pdks = new PdkWindowHost(owner, library, canvas, resolvePdkSource, pdkStore, errors, processEditors);
        var settings = new ComponentSettingsWindowHost(owner, library, files, overrides,
            () => createSettings?.Invoke(owner, errors) ?? throw new InvalidOperationException(
                "Component settings require a configured dialog factory."), openSolverSettings, canvas.Document,
            component =>
            {
                var hierarchy = CAP_Core.Analysis.Hierarchy.DesignHierarchy.From(
                    canvas.Groups.RootComponents.Select(view => view.Component));
                return hierarchy.Leaves.Any(item => ReferenceEquals(item, component))
                    || hierarchy.Groups.Any(item => ReferenceEquals(item, component));
            });
        return (pdks, settings, new ComponentEditorWindowHost(owner, library, settings, pdks, pdkStore, openSolverSettings));
    }
}
