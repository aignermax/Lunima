using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.ViewModels.AI;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP.Avalonia.ViewModels.Settings;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP.Avalonia.Views;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.Services.AiTools.GridTools;
using CAP_Contracts;
using CAP_Core.Helpers;
using CAP_Core.Export;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Register shared HttpClient with GitHub-compatible User-Agent
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ConnectAPICPro", "1.0"));
        services.AddSingleton(httpClient);

        // Register update services
        services.AddSingleton(sp => new UpdateChecker(
            sp.GetRequiredService<HttpClient>(),
            owner: "aignermax",
            repo: "Connect-A-PIC-Pro"));
        services.AddSingleton(sp => new UpdateDownloader(
            sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IUrlLauncher, SystemUrlLauncher>();
        services.AddSingleton<UpdateViewModel>();

        // Register AI assistant services
        services.AddSingleton<IAiService, AiService>(sp => new AiService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IAiGridService>(sp => new AiGridService(
            sp.GetRequiredService<DesignCanvasViewModel>(),
            sp.GetRequiredService<LeftPanelViewModel>(),
            sp.GetRequiredService<SimulationService>()));

        // Register AI tools — add new tools here without modifying any other file
        services.AddTransient<IAiTool, GetGridStateTool>();
        services.AddTransient<IAiTool, GetAvailableTypesTool>();
        services.AddTransient<IAiTool, PlaceComponentTool>();
        services.AddTransient<IAiTool, CreateConnectionTool>();
        services.AddTransient<IAiTool, RunSimulationTool>();
        services.AddTransient<IAiTool, GetLightValuesTool>();
        services.AddTransient<IAiTool, ClearGridTool>();
        services.AddTransient<IAiTool, CreateGroupTool>();
        services.AddTransient<IAiTool, UngroupTool>();
        services.AddTransient<IAiTool, SaveAsPrefabTool>();
        services.AddTransient<IAiTool, InspectGroupTool>();
        services.AddTransient<IAiTool, CopyComponentTool>();
        services.AddTransient<IAiTool, FitToViewTool>();
        services.AddSingleton<IAiToolRegistry, AiToolRegistry>();

        services.AddSingleton<AiAssistantViewModel>(sp => new AiAssistantViewModel(
            sp.GetRequiredService<IAiService>(),
            sp.GetRequiredService<UserPreferencesService>(),
            sp.GetRequiredService<IAiToolRegistry>()));

        // Register GdsExportViewModel as singleton so both FileOperations and
        // GdsExportSettingsPage share the same instance.
        services.AddSingleton<GdsExportViewModel>(sp =>
        {
            var vm = new GdsExportViewModel(
                sp.GetRequiredService<GdsExportService>(),
                sp.GetRequiredService<CAP_Core.ErrorConsoleService>());
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            vm.Initialize(prefs.GetCustomPythonPath());
            vm.OnPythonPathChanged = path => prefs.SetCustomPythonPath(path);
            return vm;
        });

        // PhotonTorchExportViewModel: singleton so the dialog and any other
        // consumer of FileOperations.PhotonTorchExport see the same state.
        services.AddSingleton<CAP_Core.Export.PhotonTorchExporter>();
        services.AddSingleton<PhotonTorchExportViewModel>();

        // Register core services
        services.AddSingleton<IDataAccessor, FileDataAccessor>();
        services.AddSingleton<CAP_Core.Components.Creation.GroupLibraryManager>();
        services.AddSingleton<GdsExportService>();
        services.AddSingleton<CAP_Core.Export.VerilogAExporter>();
        services.AddSingleton<VerilogAFileWriter>();
        services.AddSingleton<CAP_Core.ErrorConsoleService>();

        // Register application services
        services.AddSingleton<SimulationService>();
        services.AddSingleton<SimpleNazcaExporter>();
        services.AddSingleton<CAP_Core.Export.SaxExporter>();
        services.AddSingleton<PdkLoader>();
        services.AddSingleton<PdkImportService>();
        services.AddSingleton<Commands.CommandManager>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<ProjectPersistenceService>();
        // User-global PDK template S-matrix overrides — persists across projects
        // so editing a template's S-matrix isn't silently confined to the .lun
        // file the user happened to be in.
        services.AddSingleton<UserSMatrixOverrideStore>(sp =>
            new UserSMatrixOverrideStore(sp.GetService<CAP_Core.ErrorConsoleService>()));
        services.AddSingleton<Services.GroupPreviewGenerator>();
        services.AddSingleton<IInputDialogService, InputDialogService>();
        services.AddSingleton<IPortMappingDialogService, PortMappingDialogService>();

        // Register DesignCanvasViewModel as singleton (shared across all panel VMs)
        services.AddSingleton<DesignCanvasViewModel>();
        services.AddSingleton<ViewportControlViewModel>();

        // Register sub-ViewModels (Left panel)
        services.AddTransient<HierarchyPanelViewModel>();
        services.AddSingleton<PdkManagerViewModel>();
        services.AddTransient<ComponentLibraryViewModel>();

        // Register sub-ViewModels (Right panel)
        // Singleton so the Settings-window page and the RightPanel reference share state.
        services.AddSingleton<ChipSizeViewModel>();
        services.AddTransient<ParameterSweepViewModel>();
        services.AddTransient<RoutingDiagnosticsViewModel>();
        services.AddTransient<DesignValidationViewModel>();
        services.AddTransient<ComponentDimensionDiagnosticsViewModel>();
        services.AddTransient<ComponentDimensionViewModel>();
        services.AddTransient<ExportValidationViewModel>();
        services.AddTransient<SMatrixPerformanceViewModel>();
        services.AddTransient<CompressLayoutViewModel>();
        services.AddTransient<GroupSMatrixViewModel>();
        services.AddTransient<ArchitectureReportViewModel>();
        services.AddTransient<PdkConsistencyViewModel>();
        services.AddTransient<TimeDomainViewModel>();

        // VerilogAExportViewModel: singleton so the dialog and FileOperations
        // share the same state.
        services.AddSingleton<VerilogAExportViewModel>();

        // Register sub-ViewModels (Bottom panel)
        services.AddTransient<WaveguideLengthViewModel>();
        services.AddTransient<ElementLockViewModel>();
        services.AddTransient<ErrorConsoleViewModel>();

        // Register settings pages — each implements ISettingsPage; SettingsWindowViewModel enumerates them all.
        // To add a new settings page: add one line here + create the page class. Nothing else changes.
        services.AddTransient<ISettingsPage, GeneralSettingsPage>();
        services.AddTransient<ISettingsPage, GridSnapSettingsPage>();
        services.AddTransient<ISettingsPage, UpdateSettingsPage>();
        services.AddTransient<ISettingsPage, GdsExportSettingsPage>();
        services.AddTransient<ISettingsPage, ChipSizeSettingsPage>();
        services.AddTransient<ISettingsPage, AiAssistantSettingsPage>();
        services.AddTransient<SettingsWindowViewModel>();

        // Register panel ViewModels as singletons
        services.AddSingleton<LeftPanelViewModel>();
        services.AddSingleton<RightPanelViewModel>();
        services.AddSingleton<BottomPanelViewModel>();

        // Register PDK offset editor services and ViewModel.
        // The ViewModel is Singleton so that MainViewModel can hold it as a
        // property — this preserves editor state (loaded PDK, unsaved edits)
        // when the user re-opens the window. Transient here would give the
        // MainViewModel a different instance than any other DI resolution.
        services.AddSingleton<PdkJsonSaver>();
        services.AddSingleton(sp =>
        {
            var prefs = sp.GetRequiredService<UserPreferencesService>();
            var python = prefs.GetCustomPythonPath() ?? ResolvePythonExecutable();
            var script = FindPreviewScript();
            return new NazcaComponentPreviewService(python, script);
        });
        services.AddSingleton(sp => new PdkOffsetEditorViewModel(
            sp.GetRequiredService<PdkLoader>(),
            sp.GetRequiredService<PdkJsonSaver>(),
            sp.GetRequiredService<PdkManagerViewModel>(),
            sp.GetRequiredService<NazcaComponentPreviewService>()));

        // Register main ViewModel
        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            singleView.MainView = new MainView
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Searches for render_component_preview.py relative to the application base directory.
    /// Returns the first candidate path found, or the primary candidate when none exist
    /// (the service will return a graceful failure result at render time).
    /// </summary>
    private static string FindPreviewScript()
    {
        const string scriptName = "render_component_preview.py";
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // First: a "scripts" folder copied next to the binary (publish path).
        var local = Path.Combine(baseDir, "scripts", scriptName);
        if (File.Exists(local)) return local;

        // Otherwise walk up the directory tree looking for repo/scripts/<name>.
        // Hard-coded "..","..",".." chains break whenever the running configuration's
        // depth changes (debug vs release vs single-file publish vs net8.0 subfolder).
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "scripts", scriptName);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        // Best-effort fallback — NazcaComponentPreviewService returns a graceful
        // failure with a clear message when the path doesn't resolve.
        return local;
    }

    /// <summary>
    /// Picks the first runnable Python interpreter from a per-platform candidate
    /// list. Windows installers map differently (`python`, `py`-launcher) than
    /// most Linux distros (`python3`); falling back to a single hard-coded name
    /// caused the Nazca-preview to silently fail on default Windows installs.
    /// </summary>
    private static string ResolvePythonExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "python", "py", "python3" }
            : new[] { "python3", "python" };

        foreach (var candidate in candidates)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (p == null) continue;
                p.WaitForExit(2000);
                if (p.ExitCode == 0) return candidate;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Candidate not on PATH — try next
            }
            catch (Exception)
            {
                // Anything else: skip and try next
            }
        }

        // Best-effort fallback — preview service returns a clear error message
        // when this turns out not to be runnable.
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }
}
