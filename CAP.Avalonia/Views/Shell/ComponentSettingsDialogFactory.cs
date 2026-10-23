using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Notifications;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Solvers;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Builds settings dialogs with the optional solver and backend-picker capabilities.</summary>
internal sealed class ComponentSettingsDialogFactory(
    Window owner, CAP_Core.ErrorConsoleService? errorConsole, IPortMappingDialogService? portMappingDialog,
    CAP_Core.Solvers.Fdtd.IFdtdSMatrixService? fdtdService,
    CAP_Core.Export.NazcaComponentPreviewService? previewService,
    CAP_Core.Export.GdsFactoryComponentPreviewService? gdsFactoryGeometryService,
    CAP.Avalonia.Services.Solvers.IDockerSetupDialogService? dockerSetupDialog,
    INotificationService? notificationService,
    CAP.Avalonia.Services.Solvers.FdtdBackendRegistry? fdtdBackendRegistry, IUrlLauncher? urlLauncher)
{
    public (ComponentSettingsDialogViewModel Dialog, FdtdBackendSelectionViewModel? Backend) Create(
        Func<Task<ComponentTemplate?>>? resetToPdkOriginal, Action openSolverSettings)
    {
        // FDTD "Recalculate S-matrix": wire the solver service and a factory that
        // renders the component's geometry/pins into an FDTD request. Both are
        // optional — the dialog hides the recompute button when they're absent.
        Func<CAP_Core.Components.Core.Component, CancellationToken, Task<CAP_Core.Solvers.Fdtd.FdtdSMatrixRequest?>>? fdtdRequestFactory = null;
        if (fdtdService != null && previewService != null)
        {
            var requestFactory = new CAP.Avalonia.Services.Solvers.ComponentFdtdRequestFactory(
                previewService, gdsFactoryGeometryService);
            fdtdRequestFactory = (component, ct) => requestFactory.BuildAsync(component, ct);
        }

        // Guided Docker setup (issue #649): shown when the availability probe
        // reports Docker missing or its engine stopped.
        // FDTD backend picker (Meep local / Tidy3D cloud): persisted choice, shared
        // with other FDTD flows. Optional — the dialog falls back to the fixed
        // service when the registry isn't wired.
        CAP.Avalonia.ViewModels.Solvers.FdtdBackendSelectionViewModel? backendSelection = null;
        if (fdtdBackendRegistry != null)
        {
            backendSelection = new CAP.Avalonia.ViewModels.Solvers.FdtdBackendSelectionViewModel(
                fdtdBackendRegistry,
                urlLauncher);
            // Deep-link for the missing-key hint: opens Settings on the Tidy3D Cloud page.
            backendSelection.OpenTidy3dSettingsPage = openSolverSettings;
        }

        var dialogVm = new ComponentSettingsDialogViewModel(
            new FileDialogService(owner),
            errorConsole,
            importers: null,
            portMappingDialog: portMappingDialog,
            fdtdService: fdtdService,
            fdtdRequestFactory: fdtdRequestFactory,
            notificationService: notificationService,
            dockerSetupDialog: dockerSetupDialog,
            backendSelection: backendSelection,
            resetToPdkOriginal: resetToPdkOriginal);

        return (dialogVm, backendSelection);
    }
}
