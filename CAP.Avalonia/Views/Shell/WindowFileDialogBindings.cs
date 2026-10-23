using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Installs one window's file picker into its panels and restores only unchanged slots.</summary>
internal sealed class WindowFileDialogBindings : IDisposable
{
    private readonly CallbackBindings _bindings = new();

    public WindowFileDialogBindings(Window owner, FileOperationsViewModel files, GdsFactoryExportViewModel gdsFactory,
        LeftPanelViewModel library, GdsImportButtonViewModel import, RightPanelViewModel right, BottomPanelViewModel bottom)
    {
        IFileDialogService dialogs = new FileDialogService(owner);
        _bindings.BindReference(() => files.FileDialogService, value => files.FileDialogService = value, dialogs);
        _bindings.BindReference(() => files.PhotonTorchExport.FileDialogService, value => files.PhotonTorchExport.FileDialogService = value, dialogs);
        _bindings.BindReference(() => files.VerilogAExport.FileDialogService, value => files.VerilogAExport.FileDialogService = value, dialogs);
        _bindings.BindReference(() => gdsFactory.FileDialogService, value => gdsFactory.FileDialogService = value, dialogs);
        _bindings.BindReference(() => library.FileDialogService, value => library.FileDialogService = value, dialogs);
        _bindings.BindReference(() => import.FileDialogService, value => import.FileDialogService = value, dialogs);
        _bindings.BindReference(() => right.Sweep.FileDialogService, value => right.Sweep.FileDialogService = value, dialogs);
        _bindings.BindReference(() => right.OnaAnalysis.FileDialogService, value => right.OnaAnalysis.FileDialogService = value, dialogs);
        _bindings.BindReference(() => right.RoutingDiagnostics.FileDialogService, value => right.RoutingDiagnostics.FileDialogService = value, dialogs);
        _bindings.BindReference(() => right.Netlist.FileDialogService, value => right.Netlist.FileDialogService = value, dialogs);
        _bindings.BindReference(() => bottom.Analysis.Transient.FileDialogService, value => bottom.Analysis.Transient.FileDialogService = value, dialogs);
        _bindings.BindReference(() => bottom.Analysis.Eye.FileDialogService, value => bottom.Analysis.Eye.FileDialogService = value, dialogs);
        _bindings.BindReference(() => bottom.Analysis.Spectrum.Overlay.FileDialogService, value => bottom.Analysis.Spectrum.Overlay.FileDialogService = value, dialogs);
    }

    public void Dispose() => _bindings.Dispose();
}
